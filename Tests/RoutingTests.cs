using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Switcher;

class RoutingTests
{
    static int count;
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
    static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
    static object Field(object obj, string key) { return obj.GetType().GetField(key, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj); }
    sealed class Backend : INativeRoutingBackend
    {
        public NativeRoutingPlan ReadRoutingPlan() { return new NativeRoutingPlan { PhysicalInterface = "Ethernet", TailscaleInterface = "Tailscale", PhysicalIndex = 5 }; }
        public bool TailOn, ZapOn, FailTail;
        public State Read() { return new State(TailOn, ZapOn); }
        public void Validate() { }
        public void Tail(bool on) { if (FailTail && on) throw new IOException("injected restore failure"); TailOn = on; }
        public void Zap(bool on) { ZapOn = on; }
    }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        BundledEngine.EnsureAvailable();
        Check(File.Exists(BundledEngine.FilePath), "embedded engine unpacked without download");
        BundledEngine.ValidateFiles(Path.GetDirectoryName(BundledEngine.FilePath)); count++;
        DateTime engineWrite = File.GetLastWriteTimeUtc(BundledEngine.FilePath);
        BundledEngine.EnsureAvailable();
        Check(File.GetLastWriteTimeUtc(BundledEngine.FilePath) == engineWrite, "valid embedded engine is reused");
        File.WriteAllBytes(BundledEngine.FilePath, new byte[] { 1, 2, 3 });
        Reject(() => BundledEngine.ValidateFiles(Path.GetDirectoryName(BundledEngine.FilePath)), "damaged engine rejected");
        BundledEngine.EnsureAvailable();
        BundledEngine.ValidateFiles(Path.GetDirectoryName(BundledEngine.FilePath)); count++;
        Check(Directory.GetFiles(Path.GetDirectoryName(BundledEngine.FilePath), "*.tmp").Length == 0, "repair leaves no temporary binaries");
        string root = Path.Combine(args[0], "test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var settings = new RoutingSettings { EnginePath = args[1], ExitNode = "100.64.0.10" };
        Check(settings.Rules.Count == 0 && settings.DefaultTarget == "tailscale", "universal defaults, no personal rules");
        foreach (string value in new[] { "example.ru", "*.example.ru", "рф", "пример.рф", "RU", "example.ru." }) {
            var rule = new RoutingRule { Value = value }; rule.Validate(); Check(rule.Value.Length > 0, "valid domain " + value);
        }
        var idn = new RoutingRule { Value = "рф" }; idn.Validate(); Check(idn.Value == "xn--p1ai", "IDN normalized");
        foreach (string value in new[] { "", "https://example.ru", "example.ru/path", "bad..ru", "-bad.ru", "bad-.ru", "a b.ru", "127.0.0.1", "a:443", "*." })
            Reject(() => new RoutingRule { Value = value }.Validate(), "reject malformed domain " + value);
        foreach (string value in new[] { "192.0.2.1", "192.0.2.0/24", "2001:db8::/32", "::1" }) { new RoutingRule { Kind = "ip", Value = value }.Validate(); count++; }
        foreach (string value in new[] { "1.1.1.1/33", "1.1.1.1/-1", "2001:db8::/129", "invalid", "1.1.1.1/24/2" })
            Reject(() => new RoutingRule { Kind = "ip", Value = value }.Validate(), "reject bad IP " + value);
        new RoutingRule { Kind = "process", Value = "Discord.exe" }.Validate(); count++;
        foreach (string value in new[] { "C:\\Discord.exe", "../bad.exe", "cmd.exe & whoami", "*.exe", "chrome" })
            Reject(() => new RoutingRule { Kind = "process", Value = value }.Validate(), "reject process " + value);
        settings.Rules.Add(new RoutingRule { Value = "example.ru" });
        settings.Rules.Add(new RoutingRule { Kind = "process", Value = "Discord.exe", Target = "tailscale" });
        settings.Rules.Add(new RoutingRule { Kind = "ip", Value = "203.0.113.0/24" });
        settings.Rules.Add(new RoutingRule { Value = "disabled.example", Enabled = false });
        settings.Validate(true);
        string serialized = new JavaScriptSerializer().Serialize(RoutingConfig.Build(settings, new Backend().ReadRoutingPlan(), 23456, true));
        Check(serialized.Contains("xn--") == false && serialized.Contains("domain_suffix"), "domain rules emitted");
        Check(serialized.IndexOf("example.ru") < serialized.IndexOf("Discord.exe"), "priority retained");
        Check(!serialized.Contains("disabled.example"), "disabled rules excluded");
        Check(serialized.Contains("ip_cidr") && serialized.Contains("process_name"), "IP and process rules");
        Check(serialized.Contains("\"strategy\":\"ipv4_only\""), "avoid unusable IPv6 answers in an IPv4 tunnel");
        Check(serialized.Contains("hijack-dns") && serialized.Contains("reverse_mapping"), "DNS domain matching enabled");
        Check(serialized.Contains("strict_route") && serialized.Contains("fdfe:"), "dual-stack route and DNS protection");
        string auth = new JavaScriptSerializer().Serialize(RoutingConfig.Build(settings, new Backend().ReadRoutingPlan(), 23456, false));
        Check(!serialized.Contains("endpoints") && !serialized.Contains("state_directory"), "no second Tailscale client or node state");
        Check(!auth.Contains("auto_route") && !auth.Contains("switcher-tun"), "login creates no tunnel");
        Check(auth.Contains("127.0.0.1"), "probe listens on loopback only");
        using (var controller = new RoutingController(root)) {
            controller.Validate(settings, new Backend()); count++; // real sing-box check, no run or networking
            settings.DefaultTarget = "direct"; controller.Validate(settings, new Backend()); count++;
            JsonStore.Save(controller.SettingsPath, settings);
            var loaded = JsonStore.Read<RoutingSettings>(controller.SettingsPath);
            Check(loaded.Rules.Count == 4 && loaded.DefaultTarget == "direct", "settings round trip");
            JsonStore.Save(controller.SettingsPath, new RoutingSettings { EnginePath = @"Z:\old-installation\sing-box.exe" });
            using (var form = new RoutingForm(controller, new Backend())) {
                Check(((Label)Field(form, "engine")).Text.Contains("Встроен"), "bundled engine shown without file picker");
                var collected = (RoutingSettings)form.GetType().GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                Check(collected.EnginePath == BundledEngine.FilePath, "engine selected automatically");
                var grid = (DataGridView)Field(form, "grid"); Check(grid.Rows.Count == 0, "no rules selected implicitly");
                grid.Rows.Add(true, "Сайт + поддомены", "ru");
                grid.Rows.Add(true, "Сайт + поддомены", "рф");
                grid.Rows.Add(true, "Программа (.exe)", "Discord.exe");
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.Show(); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(args[0], "Routing-preview.png")); }
                form.Close(); Check(!controller.Active, "UI does not launch engine automatically");
            }
            var backend = new Backend();
            JsonStore.Save(Path.Combine(root, "routing-recovery.json"), new RoutingRecovery { Tailscale = true });
            controller.Stop(backend); Check(backend.TailOn && !backend.ZapOn && !controller.HasRecovery, "restore old Tailscale");
            JsonStore.Save(Path.Combine(root, "routing-recovery.json"), new RoutingRecovery { Zapret = true });
            controller.Stop(backend); Check(!backend.TailOn && backend.ZapOn && !controller.HasRecovery, "restore old zapret");
            JsonStore.Save(Path.Combine(root, "routing-recovery.json"), new RoutingRecovery());
            controller.Stop(backend); Check(!backend.TailOn && !backend.ZapOn, "restore both off");
            JsonStore.Save(Path.Combine(root, "routing-recovery.json"), new RoutingRecovery { Tailscale = true });
            backend.FailTail = true; Reject(() => controller.Stop(backend), "restore failure surfaces");
            Check(controller.HasRecovery, "journal retained for retry"); backend.FailTail = false; controller.Stop(backend);
            Check(!controller.HasRecovery && backend.TailOn, "retry recovery");
        }
        Console.WriteLine("PASS " + count + " routing checks (no live VPN changes)");
    }
}


