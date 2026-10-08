using System;
using System.IO;
using System.Threading;
class MockEngine
{
    static int Main(string[] args)
    {
        if (args[0] == "version") { Console.WriteLine("sing-box version 1.14.2\nTags: with_tailscale"); return 0; }
        if (args[0] == "check") return 0;
        Console.WriteLine("sing-box started"); Console.Out.Flush();
        if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fail"))) { Thread.Sleep(250); Console.Error.WriteLine("injected engine failure"); return 9; }
        while (true) Thread.Sleep(500);
    }
}
