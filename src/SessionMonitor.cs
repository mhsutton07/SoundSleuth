using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// One audible stretch of sound from one audio session.
sealed class SoundEvent
{
    public string Key;          // device id + session instance id
    public string DeviceId;
    public string Device;       // friendly device name
    public uint Pid;            // process that owns the session (0 for System Sounds)
    public uint KillPid;        // process that Kill would end
    public string Label;
    public string ExePath;
    public string Hint = "-";   // foreground app, for System Sounds only
    public bool IsSystemSounds;
    public DateTime StartUtc;
    public DateTime EndUtc;
    public float MaxPeak;
    public double Duration;
    public bool Open;
    public bool Muted;
    public string ClipFile = "(unavailable)";   // file name inside the clips folder, or "(unavailable)"
}

// Watches every active render endpoint on one dedicated MTA thread. All session COM objects
// are created, used and released on that thread; other threads talk to it through a queue.
sealed class SessionMonitor
{
    const float Threshold = 0.01f;
    const double CloseAfterSeconds = 1.0;
    const int PollMs = 100;
    const int RefreshMs = 2000;

    sealed class DevCtx
    {
        public string Id, Name;
        public IMMDevice Dev;
        public IAudioSessionManager2 Mgr;
    }

    sealed class Track
    {
        public SoundEvent Ev;
        public DateTime LastAudible;
        public bool Seen;
    }

    sealed class Cmd
    {
        public string Key;
        public bool Get;
        public bool Mute;
        public Action<bool, bool, string> Done;   // ok, muted, error
    }

    public event Action<SoundEvent> EventStarted;
    public event Action<SoundEvent> EventEnded;

    readonly Dictionary<string, DevCtx> devices = new Dictionary<string, DevCtx>();
    readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();
    readonly Queue<Cmd> cmds = new Queue<Cmd>();
    readonly ManualResetEvent wake = new ManualResetEvent(false);
    Thread thread;
    volatile bool stop;
    IMMDeviceEnumerator enumerator;
    volatile int deviceCount;
    volatile string failure;

    public int DeviceCount { get { return deviceCount; } }

    // Non-null when the monitor thread died unexpectedly (monitoring has stopped).
    public string Failure { get { return failure; } }

    public void Start()
    {
        thread = new Thread(Run);
        thread.IsBackground = true;
        thread.Name = "SessionMonitor";
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    // Stops the thread and finalizes any open events (EventEnded fires for each).
    public void Stop()
    {
        stop = true;
        wake.Set();
        if (thread != null) thread.Join(8000);
    }

    // Queue a mute change for the session behind an event key. done(ok, muted, error) runs on the monitor thread.
    public void SetMute(string key, bool mute, Action<bool, bool, string> done)
    {
        Enqueue(new Cmd { Key = key, Mute = mute, Done = done });
    }

    public void GetMute(string key, Action<bool, bool, string> done)
    {
        Enqueue(new Cmd { Key = key, Get = true, Done = done });
    }

    void Enqueue(Cmd c)
    {
        lock (cmds) cmds.Enqueue(c);
        wake.Set();
    }

    // ---- thread body ----

    void Run()
    {
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            try { RefreshDevices(); } catch (Exception) { }
            Stopwatch refresh = Stopwatch.StartNew();
            Stopwatch tick = new Stopwatch();
            while (!stop)
            {
                tick.Restart();
                RunCommands();
                if (refresh.ElapsedMilliseconds >= RefreshMs)
                {
                    try { RefreshDevices(); } catch (Exception) { }
                    refresh.Restart();
                }
                try { Poll(); } catch (Exception) { }
                int wait = PollMs - (int)tick.ElapsedMilliseconds;
                if (wait < 1) wait = 1;
                wake.WaitOne(wait);
                wake.Reset();
            }
        }
        catch (Exception ex) { failure = ex.Message; }
        finally
        {
            if (!stop && failure == null) failure = "monitor thread ended unexpectedly";
            CloseAll();
            FailPendingCommands();
            foreach (DevCtx d in devices.Values) ReleaseCtx(d);
            devices.Clear();
            if (enumerator != null) { Marshal.FinalReleaseComObject(enumerator); enumerator = null; }
        }
    }

    // Re-enumerate render endpoints: add new ones, drop ones that went away.
    void RefreshDevices()
    {
        IMMDeviceCollection col;
        if (enumerator.EnumAudioEndpoints(CA.eRender, CA.DEVICE_STATE_ACTIVE, out col) < 0 || col == null) return;
        var seen = new HashSet<string>();
        try
        {
            uint n;
            if (col.GetCount(out n) < 0) return;
            for (uint i = 0; i < n; i++)
            {
                IMMDevice dev;
                if (col.Item(i, out dev) < 0 || dev == null) continue;
                string id;
                if (dev.GetId(out id) < 0 || id == null) { Marshal.FinalReleaseComObject(dev); continue; }
                seen.Add(id);
                if (devices.ContainsKey(id)) { Marshal.FinalReleaseComObject(dev); continue; }

                DevCtx ctx = CreateCtx(id, dev);
                if (ctx == null) { Marshal.FinalReleaseComObject(dev); seen.Remove(id); continue; }
                devices[id] = ctx;
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(col);
        }

        var gone = new List<string>();
        foreach (string id in devices.Keys) if (!seen.Contains(id)) gone.Add(id);
        foreach (string id in gone) DropDevice(id);
        deviceCount = devices.Count;
    }

    DevCtx CreateCtx(string id, IMMDevice dev)
    {
        object o;
        Guid iid = CA.IID_ISessionMgr2;
        if (dev.Activate(ref iid, CA.CLSCTX_ALL, IntPtr.Zero, out o) < 0 || o == null) return null;
        return new DevCtx { Id = id, Dev = dev, Mgr = (IAudioSessionManager2)o, Name = DeviceName(dev, id) };
    }

    static string DeviceName(IMMDevice dev, string fallback)
    {
        IPropertyStore ps = null;
        try
        {
            if (dev.OpenPropertyStore(0, out ps) < 0 || ps == null) return fallback;
            PropertyKey k = Keys.FriendlyName;
            PropVariant pv;
            if (ps.GetValue(ref k, out pv) < 0) return fallback;
            string name = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null;
            CA.PropVariantClear(ref pv);
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
        catch (Exception) { return fallback; }
        finally { if (ps != null) Marshal.FinalReleaseComObject(ps); }
    }

    void DropDevice(string id)
    {
        DevCtx ctx;
        if (!devices.TryGetValue(id, out ctx)) return;
        var keys = new List<string>();
        foreach (var kv in tracks) if (kv.Value.Ev.DeviceId == id) keys.Add(kv.Key);
        foreach (string k in keys) CloseTrack(k);
        devices.Remove(id);
        ReleaseCtx(ctx);
        deviceCount = devices.Count;
    }

    static void ReleaseCtx(DevCtx ctx)
    {
        try { if (ctx.Mgr != null) Marshal.FinalReleaseComObject(ctx.Mgr); } catch (Exception) { }
        try { if (ctx.Dev != null) Marshal.FinalReleaseComObject(ctx.Dev); } catch (Exception) { }
    }

    // One pass over every session on every device.
    void Poll()
    {
        foreach (Track t in tracks.Values) t.Seen = false;
        List<string> bad = null;

        foreach (DevCtx ctx in devices.Values)
        {
            IAudioSessionEnumerator en;
            if (ctx.Mgr.GetSessionEnumerator(out en) < 0 || en == null)
            {
                if (bad == null) bad = new List<string>();
                bad.Add(ctx.Id);
                continue;
            }
            try
            {
                int n;
                if (en.GetCount(out n) < 0) continue;
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s;
                    if (en.GetSession(i, out s) < 0 || s == null) continue;
                    try { PollSession(ctx, s); }
                    catch (Exception) { }
                    finally { Marshal.FinalReleaseComObject(s); }
                }
            }
            finally { Marshal.FinalReleaseComObject(en); }
        }

        if (bad != null) foreach (string id in bad) DropDevice(id);

        // sessions that disappeared end their event
        List<string> vanished = null;
        foreach (var kv in tracks)
        {
            if (!kv.Value.Seen)
            {
                if (vanished == null) vanished = new List<string>();
                vanished.Add(kv.Key);
            }
        }
        if (vanished != null) foreach (string k in vanished) CloseTrack(k);
    }

    // Gate on the session peak (not on session state) to decide whether a session is audible.
    void PollSession(DevCtx ctx, IAudioSessionControl2 s)
    {
        float peak;
        var meter = (IAudioMeterInformation)s;
        if (meter.GetPeakValue(out peak) < 0) return;
        bool audible = peak > Threshold;
        if (!audible && tracks.Count == 0) return;

        string inst;
        if (s.GetSessionInstanceIdentifier(out inst) < 0 || inst == null) return;
        string key = ctx.Id + "|" + inst;

        Track t;
        tracks.TryGetValue(key, out t);
        if (t == null && !audible) return;

        DateTime now = DateTime.UtcNow;
        if (t == null)
        {
            bool sys = s.IsSystemSoundsSession() == 0;
            uint pid = 0;
            int hr = s.GetProcessId(out pid);
            if (!sys && (hr < 0 || pid == 0)) return;
            if (!sys && pid == ProcessInfo.OwnPid) return;

            var ev = new SoundEvent
            {
                Key = key, DeviceId = ctx.Id, Device = ctx.Name, Pid = sys ? 0 : pid,
                StartUtc = now, MaxPeak = peak, Open = true, IsSystemSounds = sys
            };
            if (sys)
            {
                ev.Label = "Windows System Sounds";
                ev.ExePath = "(system)";
                ev.KillPid = 0;
                ev.Hint = ProcessInfo.ForegroundHint();
            }
            else
            {
                uint kp;
                ev.ExePath = ProcessInfo.ExePath(pid);
                ev.Label = ProcessInfo.Label(pid, out kp);
                ev.KillPid = kp;
            }
            ReadMute(s, ev);
            t = new Track { Ev = ev, LastAudible = now, Seen = true };
            tracks[key] = t;
            Raise(EventStarted, ev);
            return;
        }

        t.Seen = true;
        if (audible)
        {
            t.LastAudible = now;
            if (peak > t.Ev.MaxPeak) t.Ev.MaxPeak = peak;
            ReadMute(s, t.Ev);
        }
        else if ((now - t.LastAudible).TotalSeconds >= CloseAfterSeconds)
        {
            CloseTrack(key);
        }
    }

    static void ReadMute(IAudioSessionControl2 s, SoundEvent ev)
    {
        try
        {
            bool m;
            if (((ISimpleAudioVolume)s).GetMute(out m) >= 0) ev.Muted = m;
        }
        catch (Exception) { }
    }

    void CloseTrack(string key)
    {
        Track t;
        if (!tracks.TryGetValue(key, out t)) return;
        tracks.Remove(key);
        SoundEvent ev = t.Ev;
        ev.EndUtc = t.LastAudible;
        ev.Duration = Math.Max(0.1, (ev.EndUtc - ev.StartUtc).TotalSeconds);
        ev.Open = false;
        Raise(EventEnded, ev);
    }

    void CloseAll()
    {
        var keys = new List<string>(tracks.Keys);
        foreach (string k in keys) CloseTrack(k);
    }

    static void Raise(Action<SoundEvent> handler, SoundEvent ev)
    {
        if (handler == null) return;
        try { handler(ev); } catch (Exception) { }
    }

    // ---- mute commands ----

    void RunCommands()
    {
        while (true)
        {
            Cmd c;
            lock (cmds)
            {
                if (cmds.Count == 0) return;
                c = cmds.Dequeue();
            }
            bool ok = false, muted = false;
            string err;
            try { err = Execute(c, out ok, out muted); }
            catch (Exception ex) { err = ex.Message; }
            Complete(c, ok, muted, err);
        }
    }

    void FailPendingCommands()
    {
        while (true)
        {
            Cmd c;
            lock (cmds)
            {
                if (cmds.Count == 0) return;
                c = cmds.Dequeue();
            }
            Complete(c, false, false, "The monitor has stopped.");
        }
    }

    static void Complete(Cmd c, bool ok, bool muted, string err)
    {
        if (c.Done == null) return;
        try { c.Done(ok, muted, err); } catch (Exception) { }
    }

    string Execute(Cmd c, out bool ok, out bool muted)
    {
        ok = false; muted = false;
        int bar = c.Key == null ? -1 : c.Key.IndexOf('|');
        if (bar < 0) return "Invalid session key.";
        DevCtx ctx;
        if (!devices.TryGetValue(c.Key.Substring(0, bar), out ctx))
            return "The audio device for this sound is no longer available.";

        IAudioSessionEnumerator en;
        if (ctx.Mgr.GetSessionEnumerator(out en) < 0 || en == null)
            return "Could not list the audio sessions on this device.";
        try
        {
            int n;
            if (en.GetCount(out n) < 0) return "Could not list the audio sessions on this device.";
            for (int i = 0; i < n; i++)
            {
                IAudioSessionControl2 s;
                if (en.GetSession(i, out s) < 0 || s == null) continue;
                try
                {
                    string inst;
                    if (s.GetSessionInstanceIdentifier(out inst) < 0 || inst == null) continue;
                    if (!string.Equals(ctx.Id + "|" + inst, c.Key, StringComparison.Ordinal)) continue;

                    var vol = (ISimpleAudioVolume)s;
                    if (!c.Get)
                    {
                        Guid g = Guid.Empty;
                        int hr = vol.SetMute(c.Mute, ref g);
                        if (hr < 0) return "Windows refused the mute request (0x" + hr.ToString("X8") + ").";
                    }
                    bool m;
                    int hr2 = vol.GetMute(out m);
                    if (hr2 < 0) return "Could not read the mute state (0x" + hr2.ToString("X8") + ").";
                    muted = m;
                    ok = true;
                    Track t;
                    if (tracks.TryGetValue(c.Key, out t)) t.Ev.Muted = m;
                    return null;
                }
                finally { Marshal.FinalReleaseComObject(s); }
            }
        }
        finally { Marshal.FinalReleaseComObject(en); }
        return "That sound's audio session has ended, so there is nothing left to mute.";
    }
}
