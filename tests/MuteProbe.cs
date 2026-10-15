using System;
using System.Threading;

// Test helper: finds a sound from a powershell.exe process (the test tone), mutes only that
// session through SessionMonitor, checks the mute state, then unmutes it.
// Prints "MUTE OK" and exits 0 on success.
static class MuteProbe
{
    static int Main()
    {
        var monitor = new SessionMonitor();
        SoundEvent target = null;
        var found = new ManualResetEvent(false);
        monitor.EventStarted += delegate(SoundEvent e)
        {
            if (target == null && e.ExePath != null && e.ExePath.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                target = e;
                found.Set();
            }
        };
        monitor.Start();

        bool muted = false;
        int rc = 1;
        try
        {
            if (!found.WaitOne(15000))
            {
                Console.WriteLine("FAIL: no powershell.exe tone session appeared within 15 s");
                return 1;
            }
            Console.WriteLine("target: " + target.Label + " pid " + target.Pid + " on " + target.Device);

            bool ok; bool state; string err;
            Call(monitor, target.Key, true, true, out ok, out state, out err);
            if (!ok) { Console.WriteLine("FAIL: SetMute(true): " + err); return 1; }
            muted = true;
            Call(monitor, target.Key, false, false, out ok, out state, out err);
            if (!ok || !state) { Console.WriteLine("FAIL: GetMute after mute: ok=" + ok + " muted=" + state + " " + err); return 1; }
            Console.WriteLine("GetMute=True");

            Call(monitor, target.Key, true, false, out ok, out state, out err);
            if (!ok) { Console.WriteLine("FAIL: SetMute(false): " + err); return 1; }
            muted = false;
            Call(monitor, target.Key, false, false, out ok, out state, out err);
            if (!ok || state) { Console.WriteLine("FAIL: GetMute after unmute: ok=" + ok + " muted=" + state + " " + err); return 1; }
            Console.WriteLine("GetMute=False");
            Console.WriteLine("MUTE OK");
            rc = 0;
        }
        finally
        {
            if (muted && target != null)
            {
                bool ok; bool state; string err;
                Call(monitor, target.Key, true, false, out ok, out state, out err);
            }
            monitor.Stop();
        }
        return rc;
    }

    // set=true: SetMute(mute) ; set=false: GetMute
    static void Call(SessionMonitor m, string key, bool set, bool mute, out bool ok, out bool state, out string err)
    {
        bool rOk = false, rState = false; string rErr = "timeout";
        var done = new ManualResetEvent(false);
        Action<bool, bool, string> cb = delegate(bool a, bool b, string c) { rOk = a; rState = b; rErr = c; done.Set(); };
        if (set) m.SetMute(key, mute, cb); else m.GetMute(key, cb);
        done.WaitOne(5000);
        ok = rOk; state = rState; err = rErr;
    }
}
