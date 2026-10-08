using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Switcher
{
    public static class TunnelAddressPool
    {
        public static string SelectIPv4(IEnumerable<string> occupied)
        {
            var ranges = new List<Tuple<uint,uint>>();
            foreach (string cidr in occupied)
            {
                string[] parts = cidr.Split('/'); IPAddress ip;
                if (!IPAddress.TryParse(parts[0], out ip) || ip.AddressFamily != AddressFamily.InterNetwork) continue;
                int bits = parts.Length > 1 ? Int32.Parse(parts[1]) : 32;
                if (bits < 0 || bits > 32) throw new ArgumentException("Invalid IPv4 prefix");
                // A default route describes Internet access, not ownership of every address.
                if (bits == 0) continue;
                uint mask = UInt32.MaxValue << (32-bits), value = Number(ip);
                ranges.Add(Tuple.Create(value & mask, (value & mask) | ~mask));
            }
            foreach (string block in new[] { "172.30.", "172.29.", "10.254.", "10.253.", "192.168." })
                for (int subnet=255; subnet>=128; subnet--)
                {
                    string address=block+subnet+".1"; uint first=Number(IPAddress.Parse(address)) & 0xfffffffc;
                    bool used=false;
                    foreach (var range in ranges) if (first<=range.Item2 && first+3>=range.Item1) {used=true;break;}
                    if (!used) return address+"/30";
                }
            throw new InvalidOperationException("Не найдена свободная подсеть для временного адаптера Switcher. Проверь пересечение адресов с другими VPN.");
        }
        static uint Number(IPAddress ip) { byte[] b=ip.GetAddressBytes(); return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3]; }
        public static string DetectIPv4()
        {
            var networks=new List<string>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                    if(address.Address.AddressFamily==AddressFamily.InterNetwork)
                    {
                        var mask=address.IPv4Mask; int bits=0;
                        if(mask!=null) foreach(byte octet in mask.GetAddressBytes()) for(int bit=0;bit<8;bit++) if((octet & (1<<bit))!=0) bits++;
                        networks.Add(address.Address+"/"+(bits==0?32:bits));
                    }
            return SelectIPv4(networks);
        }
    }
}
