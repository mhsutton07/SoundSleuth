using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;

// Resolves a process id to an executable path and a human-readable label.
// All lookups are local; WMI is queried on the local machine only.
static class ProcessInfo
{
    public static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;

    const int CacheSeconds = 60;
    sealed class PathEntry { public string Path; public DateTime At; }
    sealed class LabelEntry { public string Label; public uint KillPid; public DateTime At; }

    static readonly Dictionary<uint, PathEntry> pathCache = new Dictionary<uint, PathEntry>();
    static readonly Dictionary<uint, LabelEntry> labelCache = new Dictionary<uint, LabelEntry>();

    // Full path of the process image, or "(access denied)".
    public static string ExePath(uint pid)
    {
        PathEntry e;
        DateTime now = DateTime.UtcNow;
        lock (pathCache)
        {
            if (pathCache.TryGetValue(pid, out e) && (now - e.At).TotalSeconds < CacheSeconds) return e.Path;
        }
        string p = CA.ExePath(pid);
        lock (pathCache)
        {
            pathCache[pid] = new PathEntry { Path = p, At = now };
        }
        return p;
    }

    // Display label for a process. For a Chromium audio-service or renderer child the label is
    // "<parent> (browser audio)" and killPid is the parent process, because ending only the
    // child makes the browser restart its audio service.
    public static string Label(uint pid, out uint killPid)
    {
        LabelEntry e;
        DateTime now = DateTime.UtcNow;
        lock (labelCache)
        {
            if (labelCache.TryGetValue(pid, out e) && (now - e.At).TotalSeconds < CacheSeconds)
            {
                killPid = e.KillPid;
                return e.Label;
            }
        }

        string path = ExePath(pid);
        string name = null;
        if (path != "(access denied)")
        {
            try { name = Path.GetFileNameWithoutExtension(path); } catch (ArgumentException) { }
        }
        if (string.IsNullOrEmpty(name)) name = ProcessName(pid);
        if (string.IsNullOrEmpty(name)) name = "pid " + pid;

        string label = name;
        uint kill = pid;

        if (path != "(access denied)")
        {
            string cmd = null;
            uint ppid = 0;
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine, ParentProcessId FROM Win32_Process WHERE ProcessId=" + pid))
                using (ManagementObjectCollection rows = searcher.Get())
                {
                    foreach (ManagementObject mo in rows)
                    {
                        cmd = mo["CommandLine"] as string;
                        if (mo["ParentProcessId"] != null) ppid = Convert.ToUInt32(mo["ParentProcessId"]);
                        mo.Dispose();
                    }
                }
            }
            catch (Exception) { }

            if (cmd != null && ppid != 0 && IsChromiumAudioChild(cmd))
            {
                string parent = ProcessName(ppid);
                if (!string.IsNullOrEmpty(parent))
                {
                    label = parent + " (browser audio)";
                    kill = ppid;
                }
            }
        }

        lock (labelCache)
        {
            labelCache[pid] = new LabelEntry { Label = label, KillPid = kill, At = now };
        }
        killPid = kill;
        return label;
    }

    static bool IsChromiumAudioChild(string cmd)
    {
        if (cmd.IndexOf("--type=renderer", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return cmd.IndexOf("--type=utility", StringComparison.OrdinalIgnoreCase) >= 0
            && cmd.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string ProcessName(uint pid)
    {
        try
        {
            using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName;
        }
        catch (Exception) { return null; }
    }

    // Name of the process that owns the foreground window, or "(unknown)".
    public static string ForegroundHint()
    {
        try
        {
            IntPtr h = Win.GetForegroundWindow();
            if (h == IntPtr.Zero) return "(unknown)";
            uint pid;
            Win.GetWindowThreadProcessId(h, out pid);
            if (pid == 0) return "(unknown)";
            string n = ProcessName(pid);
            return string.IsNullOrEmpty(n) ? "(unknown)" : n;
        }
        catch (Exception) { return "(unknown)"; }
    }
}
