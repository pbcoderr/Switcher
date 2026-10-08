using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class MockEngine
{
    static void Main(string[] args)
    {
        if(args[0]=="version"){Console.WriteLine("sing-box version 1.14.2\nTags: with_tailscale");return;}
        if(args[0]=="check")return;
        if(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fail"))){Console.WriteLine("sing-box started");return;}
        var json=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));
        int port=0;foreach(Dictionary<string,object> inbound in (IEnumerable)json["inbounds"])if(inbound.ContainsKey("listen_port"))port=Convert.ToInt32(inbound["listen_port"]);
        var listener=new TcpListener(IPAddress.Loopback,port); listener.Start();Console.WriteLine("sing-box started");Console.Out.Flush();
        while(true)using(var c=listener.AcceptTcpClient()) {
            var s=c.GetStream();s.ReadTimeout=3000;
            for(int i=0;i<3;i++)if(s.ReadByte()<0)break;
            s.Write(new byte[]{5,0},0,2);
            for(int i=0;i<10;i++)if(s.ReadByte()<0)break;
            s.Write(new byte[]{5,0,0,1,127,0,0,1,0,0},0,10);
        }
    }
}
