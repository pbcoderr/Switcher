using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Switcher
{
    public sealed class RoutingRule
    {
        public bool Enabled { get; set; }
        public string Kind { get; set; }
        public string Value { get; set; }
        public string Target { get; set; }
        public RoutingRule() { Enabled = true; Kind = "domain"; Target = "direct"; Value = ""; }
        public void Validate()
        {
            if (Target != "direct" && Target != "tailscale") throw new InvalidDataException("Выбери направление правила.");
            Value = (Value ?? "").Trim();
            if (Kind == "domain")
            {
                if (Value.StartsWith("*.")) Value = Value.Substring(2);
                Value = Value.TrimEnd('.');
                try { Value = new IdnMapping().GetAscii(Value).ToLowerInvariant(); }
                catch { throw new InvalidDataException("Неверное имя сайта: " + Value); }
                if (Value.Length == 0 || Value.Length > 253 || !Regex.IsMatch(Value, @"^[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?$"))
                    throw new InvalidDataException("Укажи домен без https://, пути и порта: например example.ru.");
                IPAddress ip;
                if (IPAddress.TryParse(Value, out ip)) throw new InvalidDataException("Для IP выбери тип «IP / подсеть».");
                foreach (string label in Value.Split('.'))
                    if (label.Length == 0 || label.Length > 63 || label.StartsWith("-") || label.EndsWith("-")) throw new InvalidDataException("Неверное имя сайта: " + Value);
            }
            else if (Kind == "ip")
            {
                string[] pieces = Value.Split('/'); IPAddress address; int prefix;
                if (pieces.Length > 2 || !IPAddress.TryParse(pieces[0], out address) || address.ScopeIdSafe() != 0)
                    throw new InvalidDataException("Укажи IP или подсеть: 192.0.2.1 либо 192.0.2.0/24.");
                int bits = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
                if (pieces.Length == 1) prefix = bits;
                else if (!Int32.TryParse(pieces[1], out prefix) || prefix < 0 || prefix > bits) throw new InvalidDataException("Неверная длина подсети.");
                Value = address + "/" + prefix;
            }
            else if (Kind == "process")
            {
                if (!Regex.IsMatch(Value, @"^[^<>:""/\\|?*\x00-\x1f]+\.exe$", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Укажи имя программы, например Discord.exe, без пути.");
            }
            else throw new InvalidDataException("Неизвестный тип правила.");
        }
    }

    static class AddressExtensions
    {
        public static long ScopeIdSafe(this IPAddress address) { return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? address.ScopeId : 0; }
    }

    public sealed class RoutingSettings
    {
        public string EnginePath { get; set; }
        public string ExitNode { get; set; }
        public string DefaultTarget { get; set; }
        public List<RoutingRule> Rules { get; set; }
        public RoutingSettings() { EnginePath = ""; ExitNode = ""; DefaultTarget = "tailscale"; Rules = new List<RoutingRule>(); }
        public RoutingSettings Copy() { return JsonStore.Clone(this); }
        public void Validate(bool requireEngine)
        {
            EnginePath = (EnginePath ?? "").Trim().Trim('"');
            if (requireEngine && (!Path.IsPathRooted(EnginePath) || !File.Exists(EnginePath) || !String.Equals(Path.GetFileName(EnginePath), "sing-box.exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Выбери sing-box.exe из официального архива sing-box 1.14.x.");
            if (DefaultTarget != "direct" && DefaultTarget != "tailscale") throw new InvalidDataException("Выбери маршрут по умолчанию.");
            ExitNode = (ExitNode ?? "").Trim();
            IPAddress ip;
            if (ExitNode.Length == 0 || (!IPAddress.TryParse(ExitNode, out ip) && !Regex.IsMatch(ExitNode, @"^[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}$")))
                throw new InvalidDataException("Укажи имя или Tailscale IP своего exit node.");
            if (Rules == null || Rules.Count > 2000) throw new InvalidDataException("Допускается до 2000 правил.");
            foreach (var rule in Rules) { if (rule == null) throw new InvalidDataException("Пустое правило."); rule.Validate(); }
        }
    }

    public static class JsonStore
    {
        public static T Clone<T>(T value) { var j = new JavaScriptSerializer(); return j.Deserialize<T>(j.Serialize(value)); }
        public static T Read<T>(string path) where T : new() { return File.Exists(path) ? new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path)) : new T(); }
        public static void Save(string path, object value)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null, true); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static class RoutingConfig
    {
        public static Dictionary<string, object> Obj(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        public static Dictionary<string, object> Build(RoutingSettings value, string stateDirectory, int probePort, bool tunnel)
        {
            value = value.Copy(); value.Validate(false);
            var rules = new List<object> { Obj("inbound", new[] { "probe" }, "action", "route", "outbound", "tailscale") };
            var dnsRules = new List<object>();
            // DNS must be handled before private-address and user rules.
            rules.Add(Obj("port", 53, "action", "hijack-dns"));
            rules.Add(Obj("action", "sniff"));
            rules.Add(Obj("ip_is_private", true, "action", "route", "outbound", "direct"));
            foreach (var rule in value.Rules)
            {
                if (!rule.Enabled) continue;
                string field = rule.Kind == "domain" ? "domain_suffix" : rule.Kind == "process" ? "process_name" : "ip_cidr";
                rules.Add(Obj(field, new[] { rule.Value }, "action", "route", "outbound", rule.Target));
                if (rule.Kind == "domain") dnsRules.Add(Obj(field, new[] { rule.Value }, "action", "route", "server", "dns-" + rule.Target));
            }
            var inbounds = new List<object> { Obj("type", "mixed", "tag", "probe", "listen", "127.0.0.1", "listen_port", probePort) };
            if (tunnel) inbounds.Add(Obj("type", "tun", "tag", "switcher-tun", "interface_name", "Switcher-Routing", "address", new[] { "172.30.255.1/30", "fdfe:dcba:9876::1/126" }, "mtu", 1280, "auto_route", true, "strict_route", true));
            return Obj(
                "log", Obj("level", "info", "timestamp", true, "disabled", false),
                "dns", Obj("servers", new object[] {
                    Obj("type", "udp", "tag", "dns-direct", "server", "1.1.1.1"),
                    Obj("type", "udp", "tag", "dns-tailscale", "server", "1.1.1.1", "detour", "tailscale") },
                    "rules", dnsRules, "final", "dns-" + (tunnel ? value.DefaultTarget : "direct"), "reverse_mapping", true, "strategy", "ipv4_only"),
                "inbounds", inbounds,
                "outbounds", new object[] { Obj("type", "direct", "tag", "direct") },
                "endpoints", new object[] { Obj("type", "tailscale", "tag", "tailscale", "state_directory", stateDirectory,
                    "hostname", "switcher-routing", "exit_node", value.ExitNode, "accept_routes", false) },
                "route", Obj("auto_detect_interface", true, "default_domain_resolver", "dns-direct", "rules", rules, "final", value.DefaultTarget));
        }
    }
}

