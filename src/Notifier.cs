using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

// Shows one balloon notification the first time each app makes a sound in a run.
// Windows can swallow these (notifications off, Focus Assist). When that is detected the
// taskbar button flashes instead and a message is passed to the status line.
sealed class Notifier : IDisposable
{
    const int SuppressedUnderMs = 500;

    readonly Form owner;
    readonly NotifyIcon icon;
    readonly Action<string> status;
    readonly HashSet<string> notified = new HashSet<string>();
    readonly Stopwatch shown = new Stopwatch();
    string pendingLabel;
    bool pendingHandled;
    bool clicked;

    public Notifier(Form owner, Icon appIcon, Action<string> status)
    {
        this.owner = owner;
        this.status = status;
        icon = new NotifyIcon();
        icon.Icon = new Icon(appIcon, SystemInformation.SmallIconSize);
        icon.Text = "SoundSleuth";
        icon.Visible = true;
        icon.BalloonTipShown += delegate { shown.Restart(); };
        icon.BalloonTipClicked += delegate { clicked = true; };
        icon.BalloonTipClosed += OnClosed;
    }

    // Call on the UI thread. Only the first event per label per run produces a notification.
    public void Notify(SoundEvent e)
    {
        if (!notified.Add(e.Label)) return;

        string text = e.IsSystemSounds
            ? "A system sound played on " + e.Device + " (foreground app: " + e.Hint + ")."
            : e.Label + " (PID " + e.Pid + ") is playing sound on " + e.Device + ".";

        pendingLabel = e.Label;
        pendingHandled = false;
        clicked = false;
        shown.Reset();
        icon.ShowBalloonTip(5000, "Sound from " + e.Label, text, ToolTipIcon.Info);

        if (ToastsDisabledByWindows())
        {
            pendingHandled = true;
            Suppressed(e.Label, "Windows notifications are turned off");
        }
    }

    void OnClosed(object sender, EventArgs ev)
    {
        if (pendingHandled || clicked || !shown.IsRunning) return;
        pendingHandled = true;
        if (shown.ElapsedMilliseconds < SuppressedUnderMs) Suppressed(pendingLabel, "Windows hid it");
    }

    void Suppressed(string label, string why)
    {
        FlashTaskbar();
        status("Notification for " + label + " was suppressed (" + why + "), so the taskbar button is flashing instead.");
    }

    public void FlashTaskbar()
    {
        try
        {
            if (!owner.IsHandleCreated) return;
            var fi = new Win.FLASHWINFO();
            fi.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win.FLASHWINFO));
            fi.hwnd = owner.Handle;
            fi.dwFlags = Win.FLASHW_ALL | Win.FLASHW_TIMERNOFG;
            fi.uCount = 8;
            fi.dwTimeout = 0;
            Win.FlashWindowEx(ref fi);
        }
        catch (Exception) { }
    }

    // HKCU\...\PushNotifications\ToastEnabled = 0 means toasts are switched off for the user.
    static bool ToastsDisabledByWindows()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications"))
            {
                if (k == null) return false;
                object v = k.GetValue("ToastEnabled");
                return v is int && (int)v == 0;
            }
        }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
