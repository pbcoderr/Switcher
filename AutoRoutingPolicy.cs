using System;

namespace Switcher
{
    // A manual stop lasts until the next connection or an explicit Tailscale selection.
    public sealed class AutoRoutingPolicy
    {
        bool paused, connected;
        DateTime retryAfter;
        public bool ShouldStart(State state, bool active, bool recovery, bool hasRules, DateTime now)
        {
            bool ready = state.Tailscale && !state.Zapret;
            if (!ready) { connected = false; paused = false; retryAfter = DateTime.MinValue; return false; }
            if (!connected) { connected = true; retryAfter = DateTime.MinValue; }
            return !paused && !active && !recovery && hasRules && now >= retryAfter;
        }
        public void Pause() { paused = true; connected = true; }
        public void Resume() { paused = false; retryAfter = DateTime.MinValue; }
        public void Failed(DateTime now) { retryAfter = now.AddSeconds(60); }
    }
}
