using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;

namespace Switcher
{
    public interface IExitNodeSource { List<ExitNodeInfo> GetExitNodes(); }
    public sealed class ExitNodeInfo
    {
        public string Name, HostName, DnsName, Address;
        public bool Online;
        public bool Matches(string value)
        {
            value = (value ?? "").Trim().TrimEnd('.');
            return String.Equals(value, Address, StringComparison.OrdinalIgnoreCase) || String.Equals(value, HostName, StringComparison.OrdinalIgnoreCase) || String.Equals(value, Name, StringComparison.OrdinalIgnoreCase) || String.Equals(value, DnsName.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        }
        public override string ToString() { return Name + " — " + Address + (Online ? "" : " (не в сети)"); }
    }
    public static class ExitNodes
    {
        public static List<ExitNodeInfo> Parse(string json)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var result = new List<ExitNodeInfo>(); object peers;
            if (root == null || !root.TryGetValue("Peer", out peers) || peers == null) return result;
            foreach (var pair in (Dictionary<string, object>)peers)
            {
                var peer = (Dictionary<string, object>)pair.Value; object option, ips, online;
                if (!peer.TryGetValue("ExitNodeOption", out option) || !Object.Equals(option, true) || !peer.TryGetValue("TailscaleIPs", out ips)) continue;
                string address = "";
                foreach (object ip in (IEnumerable)ips) { IPAddress parsed; if (IPAddress.TryParse(Convert.ToString(ip), out parsed) && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { address = parsed.ToString(); break; } }
                if (address.Length == 0) continue;
                string dns = peer.ContainsKey("DNSName") ? Convert.ToString(peer["DNSName"]) : "";
                string host = peer.ContainsKey("HostName") ? Convert.ToString(peer["HostName"]) : "";
                result.Add(new ExitNodeInfo { Address = address, DnsName = dns, HostName = host, Name = dns.Length == 0 ? host : dns.Split('.')[0], Online = peer.TryGetValue("Online", out online) && Object.Equals(online, true) });
            }
            return result;
        }
        public static RoutingSettings Resolve(RoutingSettings original, IBackend backend)
        {
            var settings = original.Copy(); var source = backend as IExitNodeSource;
            if (source == null) return settings;
            var matches = source.GetExitNodes().FindAll(node => node.Matches(settings.ExitNode));
            if (matches.Count > 1) throw new InvalidOperationException("Имя exit node неоднозначно. Выбери сервер через «Найти сервер».");
            if (matches.Count == 1)
            {
                if (!matches[0].Online) throw new InvalidOperationException("Выбранный exit node сейчас не в сети.");
                settings.ExitNode = matches[0].Address;
            }
            else { IPAddress ip; if (!IPAddress.TryParse(settings.ExitNode, out ip)) throw new InvalidOperationException("Сервер не найден. Нажми «Найти сервер» или укажи его Tailscale IP."); }
            return settings;
        }
    }
}
