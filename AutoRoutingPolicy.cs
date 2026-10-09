using System;

namespace Switcher
{
    // A manual stop lasts until the next connection or an explicit Tailscale selection.
    public sealed class AutoRoutingPolicy
    {
        bool paused, connected;
        DateTime retryAfter, reachableSince; bool waitingForNetwork; int failures;
        public bool ShouldStart(State state, bool active, bool recovery, bool hasRules, DateTime now)
        {
            bool ready = state.Tailscale && !state.Zapret;
            if (!ready) { connected = false; paused = false; return false; }
            if (!connected) { connected = true; }
            return !paused && !active && !recovery && hasRules && now >= retryAfter;
        }
        public void Pause() { paused = true; connected = true; }
        public void Resume() { paused = false; retryAfter = DateTime.MinValue; }
        public void Failed(DateTime now) { failures = Math.Min(failures + 1, 4); retryAfter = now.AddSeconds(Math.Min(300, 60 * Math.Pow(2, failures - 1))); waitingForNetwork = true; reachableSince = DateTime.MinValue; }
        public void Succeeded() { failures = 0; waitingForNetwork = false; reachableSince = DateTime.MinValue; retryAfter = DateTime.MinValue; }
        public bool NetworkReady(bool reachable, DateTime now)
        {
            if (!reachable) { waitingForNetwork = true; reachableSince = DateTime.MinValue; return false; }
            if (!waitingForNetwork) return true;
            if (reachableSince == DateTime.MinValue) reachableSince = now;
            if ((now - reachableSince).TotalSeconds < 10) return false;
            waitingForNetwork = false; return true;
        }
    }
}
