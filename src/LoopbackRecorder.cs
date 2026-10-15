using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;

// Keeps the last 25 seconds of everything each render device plays (WASAPI loopback) so a
// short clip can be cut out when an event ends. The clip is the whole device mix, not just
// the one app. One MTA thread per device, plus a manager thread that tracks which devices exist.
sealed class LoopbackRecorder
{
    const int RingSeconds = 25;
    const int MaxClipSeconds = 20;
    const int PollMs = 15;
    const int RefreshMs = 2000;
    const long MaxClipsBytes = 500L * 1024 * 1024;   // clip retention: total size cap
    const int MaxClipFiles = 500;                    // clip retention: file count cap

    // PCM16 stereo ring indexed by absolute frame number. Frame numbers follow the wall clock,
    // so stretches where the device delivers no packets are filled with silence.
    sealed class Ring
    {
        public readonly int Rate;
        readonly short[] buf;
        readonly int cap;
        long head;
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly DateTime anchorUtc = DateTime.UtcNow;
        readonly object lk = new object();

        public Ring(int rate)
        {
            Rate = rate;
            cap = rate * RingSeconds;
            buf = new short[cap * 2];
        }

        long NowFrames() { return (long)(sw.Elapsed.TotalSeconds * Rate); }

        void Zero(long frames)
        {
            if (frames >= cap) Array.Clear(buf, 0, buf.Length);
            else
            {
                int pos = (int)(head % cap);
                int first = (int)Math.Min(frames, cap - pos);
                Array.Clear(buf, pos * 2, first * 2);
                if (frames > first) Array.Clear(buf, 0, (int)(frames - first) * 2);
            }
            head += frames;
        }

        // Fill silence up to a frame number (never rewinds).
        public void AdvanceTo(long frame)
        {
            lock (lk) { if (frame - head > Rate / 50) Zero(frame - head); }
        }

        // Called from the capture loop; leaves 100 ms of slack so late packets land in place.
        public void Idle() { AdvanceTo(NowFrames() - Rate / 10); }

        public void Write(short[] stereo, int frames)
        {
            lock (lk)
            {
                long target = NowFrames() - frames;
                if (target - head > Rate / 50) Zero(target - head);
                int pos = (int)(head % cap);
                int first = Math.Min(frames, cap - pos);
                Array.Copy(stereo, 0, buf, pos * 2, first * 2);
                if (frames > first) Array.Copy(stereo, first * 2, buf, 0, (frames - first) * 2);
                head += frames;
            }
        }

        // Returns interleaved stereo samples for [startUtc, endUtc], or null if nothing is available.
        public short[] Extract(DateTime startUtc, DateTime endUtc, out int frames)
        {
            frames = 0;
            lock (lk)
            {
                AdvanceTo(NowFrames() - Rate / 10);
                long fs = (long)((startUtc - anchorUtc).TotalSeconds * Rate);
                long fe = (long)((endUtc - anchorUtc).TotalSeconds * Rate);
                fs = Math.Max(fs, Math.Max(0L, head - cap));
                fe = Math.Min(fe, head);
                if (fe - fs > (long)MaxClipSeconds * Rate) fs = fe - (long)MaxClipSeconds * Rate;
                if (fe <= fs) return null;
                int n = (int)(fe - fs);
                var outBuf = new short[n * 2];
                int pos = (int)(fs % cap);
                if (pos < 0) pos += cap;
                int first = Math.Min(n, cap - pos);
                Array.Copy(buf, pos * 2, outBuf, 0, first * 2);
                if (n > first) Array.Copy(buf, 0, outBuf, first * 2, (n - first) * 2);
                frames = n;
                return outBuf;
            }
        }
    }

    sealed class Capture
    {
        readonly string id;
        volatile bool stop;
        Thread thread;
        int backoffMs = 1000;
        public volatile Ring Ring;

        public Capture(string id) { this.id = id; }

        public void Start()
        {
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "Loopback";
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Stop()
        {
            stop = true;
            if (thread != null) thread.Join(3000);
        }

        void Loop()
        {
            while (!stop)
            {
                bool started = false;
                try { started = RunOnce(); } catch (Exception) { }
                if (stop) break;
                backoffMs = started ? 1000 : Math.Min(backoffMs * 2, 10000);
                for (int waited = 0; waited < backoffMs && !stop; waited += 100) Thread.Sleep(100);
            }
        }

        // Runs one capture session until it fails or is stopped. Returns true if capture started.
        bool RunOnce()
        {
            IMMDeviceEnumerator en = null;
            IMMDevice dev = null;
            IAudioClient client = null;
            IAudioCaptureClient cap = null;
            IntPtr pf = IntPtr.Zero;
            bool started = false;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (en.GetDevice(id, out dev) < 0 || dev == null) return false;
                object o;
                Guid iidClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
                if (dev.Activate(ref iidClient, CA.CLSCTX_ALL, IntPtr.Zero, out o) < 0 || o == null) return false;
                client = (IAudioClient)o;
                if (client.GetMixFormat(out pf) < 0 || pf == IntPtr.Zero) return false;
                MixFmt fmt = MixFmt.Read(pf);
                Guid sg = Guid.Empty;
                int hr = client.Initialize(AC.SHAREMODE_SHARED, AC.LOOPBACK, 10000000, 0, pf, ref sg);
                Marshal.FreeCoTaskMem(pf);
                pf = IntPtr.Zero;
                if (hr < 0) return false;
                if (!IsSupported(fmt)) return false;
                Guid iidCap = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
                object oc;
                if (client.GetService(ref iidCap, out oc) < 0 || oc == null) return false;
                cap = (IAudioCaptureClient)oc;

                var ring = new Ring(fmt.Rate);
                Ring = ring;
                if (client.Start() < 0) return false;
                started = true;

                byte[] raw = new byte[0];
                short[] stereo = new short[0];
                while (!stop)
                {
                    uint n;
                    int r = cap.GetNextPacketSize(out n);
                    while (r == 0 && n != 0)
                    {
                        IntPtr data;
                        uint frames, flags;
                        ulong dp, qpc;
                        r = cap.GetBuffer(out data, out frames, out flags, out dp, out qpc);
                        if (r != 0) break;
                        int nf = (int)frames;
                        if (stereo.Length < nf * 2) stereo = new short[nf * 2];
                        if ((flags & AC.BUF_SILENT) != 0 || data == IntPtr.Zero)
                        {
                            Array.Clear(stereo, 0, nf * 2);
                        }
                        else
                        {
                            int bytes = nf * fmt.BlockAlign;
                            if (raw.Length < bytes) raw = new byte[bytes];
                            Marshal.Copy(data, raw, 0, bytes);
                            Convert(raw, nf, fmt, stereo);
                        }
                        ring.Write(stereo, nf);
                        cap.ReleaseBuffer(frames);
                        r = cap.GetNextPacketSize(out n);
                    }
                    if (r < 0) break;   // device invalidated or removed: leave and re-init after a backoff
                    ring.Idle();
                    Thread.Sleep(PollMs);
                }
                client.Stop();
                return started;
            }
            finally
            {
                if (pf != IntPtr.Zero) Marshal.FreeCoTaskMem(pf);
                if (cap != null) Marshal.FinalReleaseComObject(cap);
                if (client != null) Marshal.FinalReleaseComObject(client);
                if (dev != null) Marshal.FinalReleaseComObject(dev);
                if (en != null) Marshal.FinalReleaseComObject(en);
            }
        }

        static bool IsSupported(MixFmt f)
        {
            if (f.Channels < 1 || f.Rate < 8000) return false;
            if (f.IsFloat) return f.Bits == 32;
            return f.Bits == 16 || f.Bits == 24 || f.Bits == 32;
        }

        // Takes channels 0 and 1 (mono is duplicated) and converts to 16-bit.
        static void Convert(byte[] raw, int frames, MixFmt f, short[] stereo)
        {
            int bps = f.Bits / 8;
            for (int i = 0; i < frames; i++)
            {
                int baseOff = i * f.BlockAlign;
                short l = Sample(raw, baseOff, f);
                short r = f.Channels > 1 ? Sample(raw, baseOff + bps, f) : l;
                stereo[i * 2] = l;
                stereo[i * 2 + 1] = r;
            }
        }

        static short Sample(byte[] b, int off, MixFmt f)
        {
            if (f.IsFloat)
            {
                float v = BitConverter.ToSingle(b, off);
                if (v > 1f) v = 1f;
                if (v < -1f) v = -1f;
                return (short)(v * 32767f);
            }
            if (f.Bits == 16) return BitConverter.ToInt16(b, off);
            if (f.Bits == 24) return (short)((b[off + 2] << 8) | b[off + 1]);
            return (short)(BitConverter.ToInt32(b, off) >> 16);
        }
    }

    readonly Dictionary<string, Capture> captures = new Dictionary<string, Capture>();
    Thread manager;
    volatile bool stop;

    public void Start()
    {
        manager = new Thread(ManagerLoop);
        manager.IsBackground = true;
        manager.Name = "LoopbackManager";
        manager.SetApartmentState(ApartmentState.MTA);
        manager.Start();
    }

    public void Stop()
    {
        stop = true;
        if (manager != null) manager.Join(5000);
        List<Capture> all;
        lock (captures) { all = new List<Capture>(captures.Values); captures.Clear(); }
        foreach (Capture c in all) c.Stop();
    }

    void ManagerLoop()
    {
        IMMDeviceEnumerator en = null;
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            while (!stop)
            {
                try { Refresh(en); } catch (Exception) { }
                for (int waited = 0; waited < RefreshMs && !stop; waited += 100) Thread.Sleep(100);
            }
        }
        catch (Exception) { }
        finally { if (en != null) Marshal.FinalReleaseComObject(en); }
    }

    void Refresh(IMMDeviceEnumerator en)
    {
        IMMDeviceCollection col;
        if (en.EnumAudioEndpoints(CA.eRender, CA.DEVICE_STATE_ACTIVE, out col) < 0 || col == null) return;
        var ids = new HashSet<string>();
        try
        {
            uint n;
            if (col.GetCount(out n) < 0) return;
            for (uint i = 0; i < n; i++)
            {
                IMMDevice dev;
                if (col.Item(i, out dev) < 0 || dev == null) continue;
                string id;
                if (dev.GetId(out id) >= 0 && id != null) ids.Add(id);
                Marshal.FinalReleaseComObject(dev);
            }
        }
        finally { Marshal.FinalReleaseComObject(col); }

        var removed = new List<Capture>();
        lock (captures)
        {
            foreach (string id in ids)
            {
                if (captures.ContainsKey(id)) continue;
                var c = new Capture(id);
                captures[id] = c;
                c.Start();
            }
            var gone = new List<string>();
            foreach (string id in captures.Keys) if (!ids.Contains(id)) gone.Add(id);
            foreach (string id in gone) { removed.Add(captures[id]); captures.Remove(id); }
        }
        foreach (Capture c in removed) c.Stop();
    }

    // Cuts the clip for a finished event out of its device's ring and writes it as a WAV.
    // Returns the file name inside the clips folder, or null when no clip could be made.
    public string SaveClip(SoundEvent e)
    {
        try
        {
            Capture c;
            lock (captures) captures.TryGetValue(e.DeviceId, out c);
            if (c == null) return null;
            Ring ring = c.Ring;
            if (ring == null) return null;

            int frames;
            short[] data = ring.Extract(e.StartUtc.AddSeconds(-0.5), e.EndUtc.AddSeconds(0.3), out frames);
            if (data == null || frames == 0) return null;

            string dir = EventLog.ClipsDir;
            Directory.CreateDirectory(dir);
            string stem = e.StartUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss") + "_" + Sanitize(e.Label) + "_" + e.Pid;
            string name = stem + ".wav";
            for (int k = 2; File.Exists(Path.Combine(dir, name)); k++) name = stem + "-" + k + ".wav";
            WavWriter.WritePcm16(Path.Combine(dir, name), data, frames * 2, 2, ring.Rate);
            Prune(dir, name);
            return name;
        }
        catch (Exception) { return null; }
    }

    // Set when the last prune failed, null otherwise. Shown in the window's status line.
    public static volatile string PruneError;

    static readonly Regex ClipName = new Regex(@"^\d{8}-\d{6}_.+_\d+(-\d+)?\.wav$", RegexOptions.IgnoreCase);

    // Deletes the oldest of the app's own clips until the folder is within both caps.
    // Never touches the clip just written or files that do not match the clip naming pattern.
    static void Prune(string dir, string keep)
    {
        try
        {
            var files = new List<FileInfo>();
            long total = 0;
            foreach (string f in Directory.GetFiles(dir, "*.wav"))
            {
                if (!ClipName.IsMatch(Path.GetFileName(f))) continue;
                var fi = new FileInfo(f);
                files.Add(fi);
                total += fi.Length;
            }
            if (files.Count <= MaxClipFiles && total <= MaxClipsBytes) { PruneError = null; return; }
            files.Sort(delegate(FileInfo a, FileInfo b)
            {
                int c = a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            int count = files.Count;
            foreach (FileInfo fi in files)
            {
                if (count <= MaxClipFiles && total <= MaxClipsBytes) break;
                if (string.Equals(fi.Name, keep, StringComparison.OrdinalIgnoreCase)) continue;
                long len = fi.Length;
                fi.Delete();
                count--;
                total -= len;
            }
            PruneError = null;
        }
        catch (Exception ex) { PruneError = ex.Message; }
    }

    static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        char[] bad = Path.GetInvalidFileNameChars();
        foreach (char ch in s)
        {
            if (sb.Length >= 40) break;
            if (Array.IndexOf(bad, ch) >= 0 || char.IsWhiteSpace(ch) || ch == '(' || ch == ')') sb.Append('_');
            else sb.Append(ch);
        }
        return sb.Length == 0 ? "sound" : sb.ToString();
    }
}
