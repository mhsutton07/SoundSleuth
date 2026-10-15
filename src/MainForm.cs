using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows.Forms;

sealed class DoubleBufferedListView : ListView
{
    public DoubleBufferedListView() { DoubleBuffered = true; }
}

sealed class MainForm : Form
{
    const int MaxRows = 1000;

    // Column order matches the ListView columns below.
    const int ColTime = 0, ColApp = 1, ColPid = 2, ColPath = 3, ColDevice = 4, ColPeak = 5, ColDuration = 6, ColClip = 7, ColState = 8;

    readonly ListView list;
    readonly Button btnPlay, btnMute, btnKill;
    readonly CheckBox chkToasts;
    readonly StatusStrip statusStrip;
    readonly ToolStripStatusLabel stLeft, stMessage;
    readonly Label emptyHint;
    readonly Timer timer;
    readonly Dictionary<SoundEvent, ListViewItem> rows = new Dictionary<SoundEvent, ListViewItem>();

    readonly SessionMonitor monitor = new SessionMonitor();
    readonly LoopbackRecorder recorder = new LoopbackRecorder();
    Notifier notifier;
    SoundPlayer player;
    int totalEvents;
    string transientMessage;
    bool closing;

    int S(int px) { return (int)Math.Round(px * DeviceDpi / 96.0); }

    public MainForm()
    {
        Text = "SoundSleuth";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(1180), S(540));
        MinimumSize = new Size(S(760), S(320));
        Icon appIcon = LoadAppIcon();
        Icon = appIcon;

        // ---- list ----
        list = new DoubleBufferedListView();
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.MultiSelect = false;
        list.GridLines = false;
        list.Columns.Add("Time", S(110));
        list.Columns.Add("App", S(170));
        list.Columns.Add("PID", S(60), HorizontalAlignment.Right);
        list.Columns.Add("Path", S(260));
        list.Columns.Add("Device", S(170));
        list.Columns.Add("Peak", S(55), HorizontalAlignment.Right);
        list.Columns.Add("Duration", S(70), HorizontalAlignment.Right);
        list.Columns.Add("Clip", S(150));
        list.Columns.Add("State", S(140));
        list.SelectedIndexChanged += delegate { UpdateButtons(); };
        list.DoubleClick += delegate { if (btnPlay.Enabled) PlaySelected(); };

        emptyHint = new Label();
        emptyHint.Text = "Waiting for sound. Anything that plays audio will show up here, newest first.";
        emptyHint.AutoSize = true;
        emptyHint.ForeColor = SystemColors.GrayText;
        list.Controls.Add(emptyHint);
        list.Resize += delegate { CenterHint(); };

        // ---- toolbar ----
        var toolbar = new Panel();
        toolbar.Dock = DockStyle.Top;
        toolbar.Height = S(46);
        toolbar.Padding = new Padding(S(8), S(8), S(8), S(6));

        btnPlay = MakeButton("Play clip", 0);
        btnMute = MakeButton("Mute", 1);
        btnKill = MakeButton("Kill process", 2);
        btnPlay.Click += delegate { PlaySelected(); };
        btnMute.Click += delegate { ToggleMute(); };
        btnKill.Click += delegate { KillSelected(); };

        chkToasts = new CheckBox();
        chkToasts.Text = "Toasts";
        chkToasts.Checked = true;
        chkToasts.AutoSize = true;
        chkToasts.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chkToasts.Location = new Point(toolbar.Width - S(80), S(12));
        new ToolTip().SetToolTip(chkToasts, "Show a Windows notification the first time each app plays sound.");

        toolbar.Controls.Add(btnPlay);
        toolbar.Controls.Add(btnMute);
        toolbar.Controls.Add(btnKill);
        toolbar.Controls.Add(chkToasts);
        toolbar.Resize += delegate { chkToasts.Left = toolbar.ClientSize.Width - chkToasts.Width - S(12); };

        // ---- status line ----
        statusStrip = new StatusStrip();
        stLeft = new ToolStripStatusLabel("Starting...");
        stLeft.AutoSize = false;
        stLeft.Width = S(300);
        stLeft.TextAlign = ContentAlignment.MiddleLeft;
        stMessage = new ToolStripStatusLabel();
        stMessage.Spring = true;
        stMessage.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(stLeft);
        statusStrip.Items.Add(stMessage);

        // The fill control goes in first so the docked bars take their edges before it.
        Controls.Add(list);
        Controls.Add(toolbar);
        Controls.Add(statusStrip);

        timer = new Timer();
        timer.Interval = 250;
        timer.Tick += delegate { Tick(); };

        notifier = new Notifier(this, appIcon, ShowMessage);

        monitor.EventStarted += OnEventStarted;
        monitor.EventEnded += OnEventEnded;

        Shown += delegate
        {
            CenterHint();
            recorder.Start();
            monitor.Start();
            timer.Start();
        };
        FormClosing += OnFormClosing;
        UpdateButtons();
    }

    Button MakeButton(string text, int index)
    {
        var b = new Button();
        b.Text = text;
        b.Size = new Size(S(104), S(30));
        b.Location = new Point(S(8) + index * S(112), S(8));
        b.Enabled = false;
        b.FlatStyle = FlatStyle.System;
        return b;
    }

    static Icon LoadAppIcon()
    {
        try
        {
            Icon ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (ic != null) return ic;
        }
        catch (Exception) { }
        return SystemIcons.Application;
    }

    void CenterHint()
    {
        emptyHint.Left = Math.Max(0, (list.ClientSize.Width - emptyHint.Width) / 2);
        emptyHint.Top = Math.Max(S(40), list.ClientSize.Height / 3);
    }

    // ---- monitor callbacks (run on the monitor thread; marshal to the UI thread) ----

    void OnEventStarted(SoundEvent e)
    {
        PostToUi(delegate
        {
            ListViewItem it = new ListViewItem(new string[9]);
            it.Tag = e;
            Fill(it, e);
            rows[e] = it;
            list.Items.Insert(0, it);
            totalEvents++;
            emptyHint.Visible = false;
            while (list.Items.Count > MaxRows)
            {
                ListViewItem old = list.Items[list.Items.Count - 1];
                var oe = (SoundEvent)old.Tag;
                if (oe.Open) break;
                rows.Remove(oe);
                list.Items.RemoveAt(list.Items.Count - 1);
            }
            if (chkToasts.Checked) notifier.Notify(e);
        });
    }

    void OnEventEnded(SoundEvent e)
    {
        // Still on the monitor thread: cut the clip and write the log line, then refresh the row.
        string clip = recorder.SaveClip(e);
        e.ClipFile = clip ?? "(unavailable)";
        EventLog.Append(e);
        PostToUi(delegate
        {
            ListViewItem it;
            if (rows.TryGetValue(e, out it)) Fill(it, e);
            UpdateButtons();
            UpdateStatus();
        });
    }

    void PostToUi(MethodInvoker action)
    {
        if (closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }   // includes ObjectDisposedException
    }

    // ---- rows ----

    static string StateText(SoundEvent e)
    {
        if (e.Open) return e.Muted ? "Muted (still playing)" : "Playing";
        return e.Muted ? "Ended (muted)" : "Ended";
    }

    static void SetCell(ListViewItem it, int col, string text)
    {
        if (it.SubItems[col].Text != text) it.SubItems[col].Text = text;
    }

    static void Fill(ListViewItem it, SoundEvent e)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        double dur = e.Open ? Math.Max(0.1, (DateTime.UtcNow - e.StartUtc).TotalSeconds) : e.Duration;
        SetCell(it, ColTime, e.StartUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", inv));
        SetCell(it, ColApp, e.Label);
        SetCell(it, ColPid, e.Pid.ToString(inv));
        SetCell(it, ColPath, e.IsSystemSounds ? "(system) - foreground app: " + e.Hint : e.ExePath);
        SetCell(it, ColDevice, e.Device);
        SetCell(it, ColPeak, Math.Min(e.MaxPeak, 0.999f).ToString("0.000", inv));
        SetCell(it, ColDuration, dur.ToString("0.0", inv) + " s");
        SetCell(it, ColClip, e.Open ? "(recording)" : (string.IsNullOrEmpty(e.ClipFile) ? "(unavailable)" : e.ClipFile));
        SetCell(it, ColState, StateText(e));
    }

    SoundEvent Selected()
    {
        if (list.SelectedItems.Count == 0) return null;
        return list.SelectedItems[0].Tag as SoundEvent;
    }

    string ClipPath(SoundEvent e)
    {
        if (e == null || string.IsNullOrEmpty(e.ClipFile) || e.ClipFile == "(unavailable)") return null;
        string p = Path.Combine(EventLog.ClipsDir, e.ClipFile);
        return File.Exists(p) ? p : null;
    }

    void UpdateButtons()
    {
        SoundEvent e = Selected();
        btnPlay.Enabled = e != null && !e.Open && ClipPath(e) != null;
        btnMute.Enabled = e != null;
        btnMute.Text = (e != null && e.Muted) ? "Unmute" : "Mute";
        btnKill.Enabled = e != null && !e.IsSystemSounds && e.Pid != 0 && e.KillPid != 0;
    }

    void Tick()
    {
        foreach (var kv in rows)
        {
            if (kv.Key.Open) Fill(kv.Value, kv.Key);
        }
        UpdateButtons();
        UpdateStatus();
    }

    void UpdateStatus()
    {
        int n = monitor.DeviceCount;
        string dead = monitor.Failure;
        if (dead != null) stLeft.Text = "Monitoring stopped (" + dead + "). Restart SoundSleuth.";
        else stLeft.Text = "Listening on " + n + (n == 1 ? " audio device" : " audio devices") + "  |  "
            + totalEvents + (totalEvents == 1 ? " event" : " events");
        string err = EventLog.LastError;
        string msg = transientMessage ?? "";
        if (err != null) msg = (msg.Length > 0 ? msg + "   |   " : "") + "Log error: " + err + " (events are shown here but not saved)";
        string pe = LoopbackRecorder.PruneError;
        if (pe != null) msg = (msg.Length > 0 ? msg + "   |   " : "") + "Clip cleanup failed: " + pe;
        stMessage.Text = msg;
    }

    void ShowMessage(string text)
    {
        transientMessage = text;
        UpdateStatus();
    }

    // ---- buttons ----

    void PlaySelected()
    {
        SoundEvent e = Selected();
        string path = ClipPath(e);
        if (path == null) return;
        try
        {
            if (player != null) player.Stop();
            player = new SoundPlayer(path);
            player.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not play the clip:\n\n" + ex.Message, "SoundSleuth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void ToggleMute()
    {
        SoundEvent e = Selected();
        if (e == null) return;
        bool want = !e.Muted;
        string label = e.Label;
        btnMute.Enabled = false;
        monitor.SetMute(e.Key, want, delegate(bool ok, bool muted, string err)
        {
            PostToUi(delegate
            {
                if (ok)
                {
                    e.Muted = muted;
                    ListViewItem it;
                    if (rows.TryGetValue(e, out it)) Fill(it, e);
                    ShowMessage((muted ? "Muted " : "Unmuted ") + label + ".");
                }
                else
                {
                    MessageBox.Show(this, "Could not " + (want ? "mute " : "unmute ") + label + ":\n\n" + err,
                        "SoundSleuth", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                UpdateButtons();
            });
        });
    }

    void KillSelected()
    {
        SoundEvent e = Selected();
        if (e == null || e.IsSystemSounds || e.KillPid == 0) return;
        uint target = e.KillPid;
        string name = ProcessInfo.ProcessName(target);
        if (name == null)
        {
            MessageBox.Show(this, "That process (PID " + target + ") is no longer running.", "SoundSleuth",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (target == ProcessInfo.OwnPid) return;

        string extra = target != e.Pid
            ? "\n\nThe sound comes from a helper process of this app (PID " + e.Pid + "). Ending only the helper would just restart it, so the whole app is ended instead."
            : "";
        DialogResult r = MessageBox.Show(this,
            "End " + name + " (PID " + target + ")?" + extra + "\n\nUnsaved work in that program will be lost.",
            "Kill process", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (r != DialogResult.Yes) return;

        try
        {
            using (Process p = Process.GetProcessById((int)target))
            {
                p.Kill();
                p.WaitForExit(3000);
            }
            ShowMessage("Ended " + name + " (PID " + target + ").");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not end " + name + " (PID " + target + "):\n\n" + ex.Message, "SoundSleuth",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- shutdown ----

    void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        closing = true;
        timer.Stop();
        if (player != null) player.Stop();
        monitor.Stop();     // writes the clip and log line for anything still open
        recorder.Stop();
        notifier.Dispose();
    }
}
