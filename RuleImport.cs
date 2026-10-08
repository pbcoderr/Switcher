using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace Switcher
{
    public sealed class RuleImportResult
    {
        public readonly List<RoutingRule> Rules = new List<RoutingRule>();
        public readonly List<string> Errors = new List<string>();
        public int ErrorCount, Duplicates;
        public string EncodingName;
        public void Error(string text) { ErrorCount++; if (Errors.Count < 50) Errors.Add(text); }
    }

    public static class RuleImport
    {
        const int MaxBytes = 1024 * 1024;
        const int MaxEntries = 10000;
        public static RuleImportResult Read(string path, string target, IList<RoutingRule> existing)
        {
            byte[] bytes;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > MaxBytes) throw new InvalidDataException("Список слишком большой. Максимум — 1 МБ.");
                using (var buffer = new MemoryStream()) { file.CopyTo(buffer); bytes = buffer.ToArray(); }
            }
            string encodingName; string text = Decode(bytes, out encodingName);
            var result = Parse(text, Path.GetExtension(path), target, existing); result.EncodingName = encodingName; return result;
        }
        public static RuleImportResult ReadExceptions(string path, IList<RoutingRule> existing)
        {
            var result = Read(path, "direct", existing);
            foreach (var rule in result.Rules)
                if (rule.Target != "direct") result.Error("«" + rule.Value + "»: файл содержит направление через Tailscale. Для списка исключений укажи direct или убери поле target.");
            return result;
        }
        static string Decode(byte[] bytes, out string name)
        {
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) { name = "UTF-16 LE"; return new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2); }
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) { name = "UTF-16 BE"; return new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2); }
            int start = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
            try { name = "UTF-8"; return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
            catch (DecoderFallbackException) { if (start != 0) throw; name = "Windows-1251"; return Encoding.GetEncoding(1251).GetString(bytes); }
        }
        public static RuleImportResult Parse(string text, string extension, string target, IList<RoutingRule> existing)
        {
            if (target != "direct" && target != "tailscale") throw new ArgumentException("Неизвестное направление.");
            if (text == null || text.Length > MaxBytes) throw new InvalidDataException("Список отсутствует или превышает 1 МБ.");
            text = text.TrimStart('\uFEFF');
            var result = new RuleImportResult();
            var seen = new Dictionary<string, RoutingRule>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
                foreach (var original in existing)
                {
                    var rule = JsonStore.Clone(original); rule.Validate();
                    string key = rule.Kind + "|" + rule.Value;
                    // Retain the first active rule when the table already has duplicates.
                    if (!seen.ContainsKey(key) || (!seen[key].Enabled && rule.Enabled)) seen[key] = rule;
                }
            int entries = 0;
            Action<string, string, string, bool, string> add = (value, kind, direction, enabled, where) => {
                if (++entries > MaxEntries) throw new InvalidDataException("В одном файле допускается до 10 000 записей.");
                try
                {
                    value = (value ?? "").Trim();
                    if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        Uri uri;
                        if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || String.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0)
                            throw new InvalidDataException("Неверный адрес сайта.");
                        if (kind != null && kind != "domain") throw new InvalidDataException("URL допустим только для сайта.");
                        value = uri.DnsSafeHost.Trim('[', ']'); kind = null;
                    }
                    if (kind == null) { IPAddress address; kind = IPAddress.TryParse(value.Split('/')[0], out address) ? "ip" : "domain"; }
                    var rule = new RoutingRule { Kind = kind, Value = value, Target = direction ?? target, Enabled = enabled };
                    rule.Validate();
                    string key = rule.Kind + "|" + rule.Value;
                    RoutingRule previous;
                    if (seen.TryGetValue(key, out previous))
                    {
                        if (previous.Target == rule.Target && previous.Enabled == rule.Enabled) { result.Duplicates++; return; }
                        throw new InvalidDataException("Для «" + rule.Value + "» уже есть правило с другим направлением или состоянием. Исправь конфликт до импорта.");
                    }
                    seen.Add(key, rule); result.Rules.Add(rule);
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException) { result.Error(where + ": " + ex.Message); }
            };
            extension = (extension ?? "").ToLowerInvariant();
            if (extension == ".json") ParseJson(text, add, result);
            else if (extension == ".csv") ParseCsv(text, add, result);
            else if (extension == ".txt" || extension == ".list")
            {
                using (var reader = new StringReader(text))
                {
                    string line; int number = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        number++; line = line.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith(";")) continue;
                        line = Regex.Replace(line, @"\s+#.*$", "").Trim();
                        add(line, null, null, true, "Строка " + number);
                    }
                }
            }
            else throw new InvalidDataException("Поддерживаются TXT, LIST, JSON и CSV.");
            if ((existing == null ? 0 : existing.Count) + result.Rules.Count > 2000)
                result.Error("После импорта будет больше 2000 правил. Уменьши список или удали ненужные правила.");
            return result;
        }
        static Dictionary<string, object> Map(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map == null) throw new InvalidDataException("Ожидается объект правила.");
            var output = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map)
            {
                if (output.ContainsKey(pair.Key)) throw new InvalidDataException("Повтор поля: " + pair.Key);
                output.Add(pair.Key, pair.Value);
            }
            return output;
        }
        static string StringField(Dictionary<string, object> map, string key, string fallback)
        {
            object value; if (!map.TryGetValue(key, out value)) return fallback;
            if (!(value is string)) throw new InvalidDataException("Поле " + key + " должно быть строкой.");
            return (string)value;
        }
        static void ParseJson(string text, Action<string, string, string, bool, string> add, RuleImportResult result)
        {
            object root;
            try { root = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 16 }.DeserializeObject(text); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { throw new InvalidDataException("JSON повреждён: " + ex.Message); }
            if (root is Dictionary<string, object>)
            {
                var map = Map(root);
                bool rules = map.ContainsKey("rules"), domains = map.ContainsKey("domains");
                if (rules == domains) throw new InvalidDataException("В JSON нужен массив строк, либо объект с одним полем rules или domains.");
                root = map[rules ? "rules" : "domains"];
            }
            var array = root as object[];
            if (array == null) throw new InvalidDataException("В JSON ожидается массив сайтов или правил.");
            if (array.Length > MaxEntries) throw new InvalidDataException("В одном файле допускается до 10 000 записей.");
            for (int i = 0; i < array.Length; i++)
            {
                string where = "Запись " + (i + 1);
                try
                {
                    if (array[i] is string) { add((string)array[i], null, null, true, where); continue; }
                    var item = Map(array[i]);
                    foreach (string field in item.Keys)
                        if (!new List<string> { "value", "kind", "target", "enabled" }.Contains(field.ToLowerInvariant())) throw new InvalidDataException("Неизвестное поле правила: " + field);
                    object enabled; bool isEnabled = true;
                    if (item.TryGetValue("enabled", out enabled)) { if (!(enabled is bool)) throw new InvalidDataException("enabled должен быть true или false."); isEnabled = (bool)enabled; }
                    add(StringField(item, "value", ""), StringField(item, "kind", null), StringField(item, "target", null), isEnabled, where);
                }
                catch (InvalidDataException ex) { result.Error(where + ": " + ex.Message); }
            }
        }
        static void ParseCsv(string text, Action<string, string, string, bool, string> add, RuleImportResult result)
        {
            string first = "";
            using (var reader = new StringReader(text)) { string line; while ((line = reader.ReadLine()) != null) { if (String.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("//")) continue; first = line; break; } }
            int commas = 0, semicolons = 0; bool quoted = false;
            foreach (char c in first) { if (c == '"') quoted = !quoted; else if (!quoted && c == ',') commas++; else if (!quoted && c == ';') semicolons++; }
            using (var parser = new TextFieldParser(new StringReader(text)))
            {
                parser.TextFieldType = FieldType.Delimited; parser.SetDelimiters(semicolons > commas ? ";" : ",");
                parser.HasFieldsEnclosedInQuotes = true; parser.TrimWhiteSpace = true; parser.CommentTokens = new[] { "#", "//" };
                string[] headers = null; int records = 0;
                while (!parser.EndOfData)
                {
                    long line = parser.LineNumber; string[] fields;
                    try { fields = parser.ReadFields(); }
                    catch (MalformedLineException ex) { result.Error("Строка " + ex.LineNumber + ": неверный CSV."); continue; }
                    if (fields == null) continue;
                    if (++records > MaxEntries + 1) throw new InvalidDataException("В одном файле допускается до 10 000 записей.");
                    if (records == 1 && Array.Exists(fields, x => String.Equals(x, "value", StringComparison.OrdinalIgnoreCase)))
                    {
                        headers = fields;
                        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (string field in headers)
                            if (!unique.Add(field) || !new List<string> { "value", "kind", "target", "enabled" }.Contains(field.ToLowerInvariant())) throw new InvalidDataException("Неизвестный или повторяющийся столбец CSV: " + field);
                        continue;
                    }
                    string where = "Строка " + line;
                    if (headers == null) { if (fields.Length == 1) add(fields[0], null, null, true, where); else result.Error(where + ": для нескольких столбцов нужна строка заголовков value,kind,target,enabled."); continue; }
                    if (fields.Length != headers.Length) { result.Error(where + ": число полей не совпадает с заголовком."); continue; }
                    var item = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < fields.Length; i++) item.Add(headers[i], fields[i]);
                    string value, kind, target, flag;
                    item.TryGetValue("value", out value); item.TryGetValue("kind", out kind); item.TryGetValue("target", out target); item.TryGetValue("enabled", out flag);
                    bool enabled = true;
                    if (!String.IsNullOrWhiteSpace(flag) && !Boolean.TryParse(flag, out enabled)) { result.Error(where + ": enabled должен быть true или false."); continue; }
                    add(value, String.IsNullOrWhiteSpace(kind) ? null : kind, String.IsNullOrWhiteSpace(target) ? null : target, enabled, where);
                }
            }
        }
    }
}
