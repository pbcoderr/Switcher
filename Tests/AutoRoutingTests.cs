using System;
using Switcher;
class AutoRoutingTests
{
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
    static void Main()
    {
        var p = new AutoRoutingPolicy(); var t = new State(true, false); var now = DateTime.UtcNow;
        Check(p.ShouldStart(t, false, false, true, now), "Startup with connected Tailscale");
        Check(!p.ShouldStart(t, true, false, true, now), "No duplicate start");
        Check(!p.ShouldStart(t, false, true, true, now), "Do not bypass recovery");
        Check(!p.ShouldStart(t, false, false, false, now), "No rules means no tunnel");
        p.Failed(now);
        Check(!p.ShouldStart(t, false, false, true, now.AddSeconds(59)), "Backoff");
        Check(p.ShouldStart(t, false, false, true, now.AddSeconds(60)), "Retry");
        p.Pause();
        Check(!p.ShouldStart(t, false, false, true, now.AddMinutes(5)), "Manual stop is respected");
        p.Resume();
        Check(p.ShouldStart(t, false, false, true, now), "Explicit Tailscale resumes");
        p.Pause();
        Check(!p.ShouldStart(new State(false, true), false, false, true, now), "Zapret is not routed");
        Check(p.ShouldStart(t, false, false, true, now), "Reconnect resets pause");
        Check(!p.ShouldStart(new State(true, true), false, false, true, now), "Conflict is not routed");
        Check(!p.ShouldStart(new State(false, false), false, false, true, now), "Disconnected is not routed");
        Console.WriteLine("PASS " + count + " auto-routing checks");
    }
}
