using System;
using Switcher;
class TunnelAddressTests
{
    static int count;
    static void Check(bool value) { if(!value) throw new Exception("Address selection failed"); count++; }
    static void Main()
    {
        Check(TunnelAddressPool.SelectIPv4(new string[0])=="172.30.255.1/30");
        Check(TunnelAddressPool.SelectIPv4(new[]{"172.30.255.2/30"})=="172.30.254.1/30");
        Check(TunnelAddressPool.SelectIPv4(new[]{"172.30.0.1/16"})=="172.29.255.1/30");
        Check(TunnelAddressPool.SelectIPv4(new[]{"172.16.0.1/12"})=="10.254.255.1/30");
        Check(TunnelAddressPool.SelectIPv4(new[]{"172.16.0.1/12","10.0.0.1/8"})=="192.168.255.1/30");
        Check(TunnelAddressPool.SelectIPv4(new[]{"0.0.0.0/0","192.168.1.2/24"})=="172.30.255.1/30");
        Check(TunnelAddressPool.SelectIPv4(new[]{"172.30.255.3/32"})=="172.30.254.1/30");
        bool rejected=false;try {TunnelAddressPool.SelectIPv4(new[]{"172.16.0.0/12","10.0.0.0/8","192.168.0.0/16"});}catch(InvalidOperationException){rejected=true;}Check(rejected);
        Console.WriteLine("PASS "+count+" tunnel address checks");
    }
}
