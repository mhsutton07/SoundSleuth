using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

static class Program
{
    const string UsageText = "Usage: SoundSleuth.exe [--console <seconds>]\r\n"
        + "  (no arguments)        open the window\r\n"
        + "  --console <seconds>   print finished sound events to this console for the given time, then exit";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            bool createdNew;
            using (var single = new Mutex(true, "Local\\SoundSleuth.Gui", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("SoundSleuth is already running.", "SoundSleuth", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                GC.KeepAlive(single);
            }
            return 0;
        }

        int seconds;
        if (args.Length == 2 && args[0] == "--console" && int.TryParse(args[1], out seconds) && seconds > 0)
        {
            int rc = RunConsole(seconds);
            Environment.Exit(rc);
            return rc;
        }

        using (TextWriter w = OpenConsoleWriter())
        {
            w.WriteLine(UsageText);
        }
        Environment.Exit(2);
        return 2;
    }

    // The stdout stream is opened BEFORE AttachConsole: after attaching, a redirected handle
    // (cmd "> file") would otherwise be lost. A console is attached or allocated only when
    // no stdout handle was inherited.
    static TextWriter OpenConsoleWriter()
    {
        Stream so = Console.OpenStandardOutput();
        bool attached = Win.AttachConsole(-1);
        if (so == Stream.Null)
        {
            if (!attached) attached = Win.AllocConsole();
            if (attached)
            {
                // GENERIC_WRITE, FILE_SHARE_WRITE, OPEN_EXISTING
                IntPtr h = Win.CreateFile("CONOUT$", 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h != IntPtr.Zero && h != new IntPtr(-1))
                    so = new FileStream(new SafeFileHandle(h, true), FileAccess.Write);
            }
        }
        var writer = new StreamWriter(so, new UTF8Encoding(false));
        writer.AutoFlush = true;
        return writer;
    }

    static int RunConsole(int seconds)
    {
        using (TextWriter output = OpenConsoleWriter())
        {
            var gate = new object();
            Action<string> print = delegate(string line) { lock (gate) output.WriteLine(line); };

            string logError = EventLog.Check();
            if (logError != null) print("error: cannot use the log folder, events will not be saved: " + logError);

            var monitor = new SessionMonitor();
            var recorder = new LoopbackRecorder();
            monitor.EventEnded += delegate(SoundEvent e)
            {
                string clip = recorder.SaveClip(e);
                e.ClipFile = clip ?? "(unavailable)";
                EventLog.Append(e);
                print(EventLog.FormatLine(e));
                if (EventLog.LastError != null) print("error: could not write the log: " + EventLog.LastError);
            };

            var done = new ManualResetEvent(false);
            ConsoleCancelEventHandler cancel = delegate(object s, ConsoleCancelEventArgs a) { a.Cancel = true; done.Set(); };
            Console.CancelKeyPress += cancel;

            print("Listening for " + seconds + " s. Press Ctrl+C to stop early.");
            recorder.Start();
            monitor.Start();
            done.WaitOne((int)Math.Min((long)seconds * 1000, int.MaxValue));
            monitor.Stop();     // finalizes open events (clip + log + print) before the recorder goes away
            recorder.Stop();
            print("Done.");
            return 0;
        }
    }
}
