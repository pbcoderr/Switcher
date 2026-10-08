using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Switcher;
class LifecycleTests
{
    class Backend : IBackend
    {
        public bool T, Z, FailRestore;
        public State Read() { return new State(T, Z); }
        public void Validate() { }
        public void Tail(bool on) { if (on && FailRestore) throw new IOException("injected restore failure"); T = on; }
        public void Zap(bool on) { Z = on; }
    }
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    static int Main(string[] args)
    {
        string root = Path.Combine(args[0], "lifecycle-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var settings = new RoutingSettings { EnginePath = args[1], ExitNode = "100.64.0.10" };
        File.Delete(Path.Combine(Path.GetDirectoryName(args[1]), "fail"));
        using (var controller = new RoutingController(root)) {
            var loginBackend = new Backend { T = true };
            controller.StartLogin(settings, loginBackend); Check(controller.Alive && !controller.Tunnel && controller.HasRecovery && !loginBackend.T, "login stops normal tunnel before launching engine");
            var child = (Process)typeof(RoutingController).GetField("process", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            int pid = child.Id; controller.Stop(loginBackend);
            Check(loginBackend.T && !controller.HasRecovery, "login restores original tunnel");
            bool alive; try { using (var p = Process.GetProcessById(pid)) alive = !p.HasExited; } catch (ArgumentException) { alive = false; }
            Check(!alive && !controller.Active, "stop closes only owned process");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[1]), "fail"), "1");
            foreach (int mode in new[] { 0, 1, 2, 3 }) {
                var backend = new Backend { T = (mode & 1) != 0, Z = (mode & 2) != 0 };
                bool failed = false; try { controller.StartRouting(settings, backend); } catch (Exception) { failed = true; }
                Check(failed, "bad engine reported");
                Check(backend.T == ((mode & 1) != 0) && backend.Z == ((mode & 2) != 0), "previous mode restored " + mode);
                Check(!controller.Active && !controller.HasRecovery, "failed child and journal cleaned");
            }
            var failure = new Backend { T = true, FailRestore = true };
            try { controller.StartRouting(settings, failure); } catch (Exception) { }
            Check(controller.HasRecovery && !controller.Active, "failed rollback retains journal but stops engine");
            failure.FailRestore = false; controller.Stop(failure); Check(failure.T && !controller.HasRecovery, "rollback retry succeeds");
        }
        Console.WriteLine("PASS " + count + " lifecycle checks with fake backend and fake engine"); return 0;
    }
}
