using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Web.Script.Serialization;
namespace Switcher
{
    public interface INativeRoutingBackend : IBackend { NativeRoutingPlan ReadRoutingPlan(); }
    public sealed class NativeRoutingPlan
    {
        public string PhysicalInterface, TailscaleInterface, PhysicalId, TailscaleId, NodeId, ExitNodeId;
        public uint PhysicalIndex;
        public static NativeRoutingPlan Detect(string statusJson)
        {
            var status=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(statusJson);
            if(Convert.ToString(status["BackendState"])!="Running") throw new InvalidOperationException("Сначала подключи обычный Tailscale.");
            object exit;
            if(!status.TryGetValue("ExitNodeStatus",out exit) || exit==null) throw new InvalidOperationException("Выбери exit node в обычном приложении Tailscale.");
            var exitStatus=(Dictionary<string,object>)exit;
            if(!exitStatus.ContainsKey("Online") || !Object.Equals(exitStatus["Online"],true)) throw new InvalidOperationException("Выбранный exit node не в сети.");
            var self=(Dictionary<string,object>)status["Self"];
            var addresses=new HashSet<string>(((IEnumerable)self["TailscaleIPs"]).Cast<object>().Select(Convert.ToString));
            var adapters=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).ToArray();
            var tailscale=adapters.SingleOrDefault(n=>n.GetIPProperties().UnicastAddresses.Any(a=>addresses.Contains(a.Address.ToString())));
            if(tailscale==null) throw new InvalidOperationException("Сетевой интерфейс Tailscale не найден.");
            var physical=adapters.Where(n=>n.Id!=tailscale.Id && n.Name!="Switcher-Routing" &&
                (n.NetworkInterfaceType==NetworkInterfaceType.Ethernet || n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211) &&
                n.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork && g.Address.ToString()!="0.0.0.0")).ToArray();
            if(physical.Length!=1) throw new InvalidOperationException("Нужен один активный интернет-интерфейс с IPv4-шлюзом. Отключи лишнее подключение или VPN перед включением правил.");
            return new NativeRoutingPlan { PhysicalInterface=physical[0].Name, PhysicalId=physical[0].Id,
                PhysicalIndex=(uint)physical[0].GetIPProperties().GetIPv4Properties().Index, TailscaleInterface=tailscale.Name,
                TailscaleId=tailscale.Id, NodeId=Convert.ToString(self["ID"]), ExitNodeId=Convert.ToString(exitStatus["ID"]) };
        }
        public bool SameConnection(NativeRoutingPlan other)
        { return other!=null && PhysicalId==other.PhysicalId && PhysicalIndex==other.PhysicalIndex && TailscaleId==other.TailscaleId && NodeId==other.NodeId && ExitNodeId==other.ExitNodeId; }
    }
}
