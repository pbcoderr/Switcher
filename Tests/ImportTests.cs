using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Switcher;

class ImportTests
{
    static int count;
    static void Check(bool test, string name) { if (!test) throw new Exception(name); count++; }
    static void Reject(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
    static object Field(object obj, string name) { return obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj); }
    static RuleImportResult Parse(string text, string ext) { return RuleImport.Parse(text, ext, "direct", new List<RoutingRule>()); }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string root = Path.Combine(args[0], "import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var txt = Parse("# comment\r\nEXAMPLE.ru\n*.example.ru\n\n// note\n; note\nпример.рф # note\nhttps://other.example/a?b=1#fragment\n203.0.113.1\n", ".txt");
        Check(txt.ErrorCount == 0 && txt.Rules.Count == 4 && txt.Duplicates == 1, "text comments, blanks and duplicates");
        Check(txt.Rules[1].Value == "xn--e1afmkfd.xn--p1ai", "IDN normalized");
        Check(txt.Rules[2].Value == "other.example", "URL becomes domain");
        Check(txt.Rules[3].Kind == "ip" && txt.Rules[3].Value == "203.0.113.1/32", "IP detection");
        Check(txt.Rules.TrueForAll(r => r.Target == "direct"), "default direction direct");
        Check(Parse("bad..ru\nhttps://user:pass@site.ru\nhttps://\nhttps://good.example/x", ".txt").ErrorCount == 3, "invalid lines reported");
        Check(Parse("\uFEFFexample.ru", ".list").Rules.Count == 1, "BOM and list extension");
        Check(Parse("[\"example.ru\",\"пример.рф\"]", ".json").Rules.Count == 2, "JSON strings");
        Check(Parse("{\"domains\":[\"example.ru\"]}", ".json").Rules.Count == 1, "JSON domains wrapper");
        var rules = Parse("{\"Rules\":[{\"Value\":\"App.exe\",\"Kind\":\"process\",\"Enabled\":false,\"Target\":\"direct\"},{\"value\":\"2001:db8::/32\",\"kind\":\"ip\"}]}", ".json");
        Check(rules.ErrorCount == 0 && rules.Rules.Count == 2 && !rules.Rules[0].Enabled && rules.Rules[0].Kind == "process", "own settings JSON fields supported");
        Check(Parse("[{\"value\":\"example.ru\",\"enabled\":\"false\"},null,3,{\"value\":\"x.ru\",\"action\":\"run\"}]", ".json").ErrorCount == 4, "invalid types and unknown fields rejected");
        foreach (string malformed in new[] { "{", "{}", "null", "{\"rules\":[],\"domains\":[]}", "{\"domains\":\"example.ru\"}", "{\"rules\":[],\"Rules\":[]}" }) Reject(() => Parse(malformed, ".json"), "bad JSON structure " + malformed);
        var csv = Parse("value,kind,target,enabled\r\n\"https://example.ru/a,b\",domain,direct,true\r\nApp.exe,process,,false\r\n", ".csv");
        Check(csv.ErrorCount == 0 && csv.Rules.Count == 2 && csv.Rules[0].Value == "example.ru" && !csv.Rules[1].Enabled, "quoted CSV with comma URL");
        Check(Parse("value;kind;enabled\r\nпример.рф;domain;true", ".csv").Rules.Count == 1, "semicolon CSV");
        Check(Parse("# note\nexample.ru\nother.ru", ".csv").Rules.Count == 2, "CSV one column without header");
        Check(Parse("value,kind,enabled\nx.ru,domain,maybe\na.ru\n", ".csv").ErrorCount == 2, "bad CSV values and field counts");
        Reject(() => Parse("value,command\nx.ru,run", ".csv"), "unknown CSV header rejected");
        Reject(() => Parse("value,value\nx.ru,x.ru", ".csv"), "duplicate CSV header rejected");
        Check(Parse("x.ru,direct", ".csv").ErrorCount == 1, "multi-column CSV requires header");
        Check(Parse("value\n\"broken", ".csv").ErrorCount == 1, "broken quoting reported");
        var existing = new List<RoutingRule> { new RoutingRule { Value = "EXAMPLE.ru" } };
        var merged = RuleImport.Parse("example.ru\nnew.example", ".txt", "direct", existing);
        Check(merged.Duplicates == 1 && merged.Rules.Count == 1 && existing[0].Value == "EXAMPLE.ru", "merge does not mutate existing rules");
        existing[0].Target = "tailscale";
        Check(RuleImport.Parse("example.ru", ".txt", "direct", existing).ErrorCount == 1, "opposite existing route is a conflict");
        Check(Parse("[{\"value\":\"x.ru\",\"target\":\"direct\"},{\"value\":\"x.ru\",\"target\":\"tailscale\"}]", ".json").ErrorCount == 1, "conflict within list");
        string path = Path.Combine(root, "list.txt");
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.GetEncoding(1251) }) {
            File.WriteAllText(path, "пример.рф", encoding);
            var decoded = RuleImport.ReadExceptions(path, new List<RoutingRule>());
            Check(decoded.ErrorCount == 0 && decoded.Rules[0].Value == "xn--e1afmkfd.xn--p1ai", "encoding " + encoding.WebName);
        }
        string json = Path.Combine(root, "rules.json");
        File.WriteAllText(json, "[{\"value\":\"example.ru\",\"target\":\"tailscale\"}]");
        Check(RuleImport.ReadExceptions(json, new List<RoutingRule>()).ErrorCount == 1, "exception import rejects tunnel target");
        var many = new StringBuilder(); for (int i = 0; i < 2001; i++) many.Append("site" + i + ".example\n");
        Check(Parse(many.ToString(), ".txt").ErrorCount == 1, "2000 rule cap");
        File.WriteAllBytes(path, new byte[1024 * 1024 + 1]); Reject(() => RuleImport.ReadExceptions(path, existing), "file size cap");
        Reject(() => Parse("x.ru", ".exe"), "unsupported type rejected");
        var bad = Parse(String.Join("\n", new string[100]).Replace("\n", "bad domain\n"), ".txt");
        Check(bad.ErrorCount == 99 && bad.Errors.Count == 50, "errors bounded but full count retained");
        File.WriteAllText(path, "example.ru\nпример.рф\n203.0.113.0/24");
        using (var controller = new RoutingController(root))
        using (var main = new RoutingForm(controller, null)) {
            Check(main.GetType().GetField("defaultRoute", BindingFlags.Instance | BindingFlags.NonPublic) == null, "no direction chooser");
            var grid = (DataGridView)Field(main, "grid"); grid.Rows.Add(true, "Сайт + поддомены", "existing.example");
            var apply = main.GetType().GetMethod("ApplyImport", BindingFlags.Instance | BindingFlags.NonPublic);
            var plan = RuleImport.ReadExceptions(path, new List<RoutingRule>()); apply.Invoke(main, new object[] { plan });
            Check(grid.Rows.Count == 4 && (string)grid.Rows[0].Cells[2].Value == "existing.example", "UI appends and sets fallback Tailscale");
            Check(!File.Exists(controller.SettingsPath) && !controller.Active, "import does not save or change network");
            var rejected = new RuleImportResult(); rejected.Error("bad");
            Reject(() => apply.Invoke(main, new object[] { rejected }), "failed plan cannot apply");
            Check(grid.Rows.Count == 4, "failed plan leaves grid unchanged");
            main.StartPosition = FormStartPosition.Manual; main.Location = new Point(-20000, -20000); main.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size)); bitmap.Save(Path.Combine(args[0], "Routing-import-preview.png")); } main.Close();
        }
        using (var dialog = new RuleImportForm(path, new List<RoutingRule>()))
        using (var timer = new Timer { Interval = 50 }) {
            int ticks = 0;
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            timer.Tick += delegate {
                if (++ticks > 200) throw new Exception("Import dialog timeout");
                if (dialog.Result == null || (bool)Field(dialog, "loading")) return;
                timer.Stop();
                Check(((Button)Field(dialog, "import")).Enabled, "valid preview permits import");
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(args[0], "Import-preview.png")); }
                ((Button)Field(dialog, "cancel")).PerformClick();
            };
            timer.Start(); Check(dialog.ShowDialog() == DialogResult.Cancel, "cancel does not approve import");
        }
        Console.WriteLine("PASS " + count + " import checks; no live network changes");
    }
}
