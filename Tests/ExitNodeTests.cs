using System;
using System.Collections.Generic;
using Switcher;
class ExitNodeTests
{
    class Backend : IBackend, IExitNodeSource
    {
        public List<ExitNodeInfo> Nodes;
        public int Writes;
        public List<ExitNodeInfo> GetExitNodes() { return Nodes; }
        public State Read() { return new State(true, false); }
        public void Validate() {}
        public void Tail(bool on) { Writes++; }
        public void Zap(bool on) { Writes++; }
    }
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, name); }
    static void Main()
    {
        var nodes = ExitNodes.Parse("{\"Peer\":{\"a\":{\"HostName\":\"old-host\",\"DNSName\":\"new-server.tailnet.ts.net.\",\"TailscaleIPs\":[\"fd7a::1\",\"100.64.0.10\"],\"ExitNodeOption\":true,\"Online\":true},\"b\":{\"ExitNodeOption\":false}}}");
        Check(nodes.Count == 1 && nodes[0].Address == "100.64.0.10", "only IPv4 exit nodes");
        var backend = new Backend { Nodes = nodes };
        foreach (string name in new[] {"old-host", "NEW-SERVER", "new-server.tailnet.ts.net.", "100.64.0.10"})
        {
            var original = new RoutingSettings { ExitNode = name };
            var resolved = ExitNodes.Resolve(original, backend);
            Check(resolved.ExitNode == "100.64.0.10" && original.ExitNode == name, "resolve without mutation: " + name);
        }
        Reject(() => ExitNodes.Resolve(new RoutingSettings { ExitNode = "missing" }, backend), "unknown name");
        Check(ExitNodes.Resolve(new RoutingSettings { ExitNode = "100.64.0.20" }, backend).ExitNode == "100.64.0.20", "explicit IP for other account");
        nodes[0].Online = false;
        Reject(() => ExitNodes.Resolve(new RoutingSettings { ExitNode = "new-server" }, backend), "offline server");
        nodes[0].Online = true; nodes.Add(nodes[0]);
        Reject(() => ExitNodes.Resolve(new RoutingSettings { ExitNode = "new-server" }, backend), "ambiguous server");
        Check(backend.Writes == 0, "discovery does not change services");
        Check(ExitNodes.Parse("{\"Peer\":null}").Count == 0, "empty peer list");
        Console.WriteLine("PASS " + count + " exit-node checks; no live network changes");
    }
}
