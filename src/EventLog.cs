using System;
using System.Globalization;
using System.IO;
using System.Text;

// Appends one line per finished event to %LOCALAPPDATA%\SoundSleuth\sounds.log.
// Failures never throw; the most recent one is exposed in LastError (null after a success).
static class EventLog
{
    static readonly object gate = new object();
    static volatile string lastError;

    public static string LastError { get { return lastError; } }

    public static string Root
    {
        get
        {
            string root = Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\SoundSleuth");
            if (root.IndexOf('%') >= 0) root = Path.Combine(Path.GetTempPath(), "SoundSleuth");
            return root;
        }
    }

    public static string LogPath { get { return Path.Combine(Root, "sounds.log"); } }
    public static string ClipsDir { get { return Path.Combine(Root, "clips"); } }

    public static string FormatLine(SoundEvent e)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        float peak = Math.Min(e.MaxPeak, 0.999f);
        string clip = string.IsNullOrEmpty(e.ClipFile) ? "(unavailable)" : e.ClipFile;
        return e.StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", inv)
            + " | " + e.Label
            + " | pid " + e.Pid
            + " | " + e.ExePath
            + " | " + e.Device
            + " | peak " + peak.ToString("0.000", inv)
            + " | " + e.Duration.ToString("0.0", inv) + " s"
            + " | hint: " + (string.IsNullOrEmpty(e.Hint) ? "-" : e.Hint)
            + " | clip: " + clip;
    }

    public static void Append(SoundEvent e)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Root);
                File.AppendAllText(LogPath, FormatLine(e) + Environment.NewLine, new UTF8Encoding(false));
            }
            lastError = null;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
        }
    }

    // Checks that the log folder can be created; returns the error text or null.
    public static string Check()
    {
        try
        {
            lock (gate) Directory.CreateDirectory(Root);
            if (File.Exists(Root)) throw new IOException("log folder is a file");
            lastError = null;
            return null;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            return ex.Message;
        }
    }
}
