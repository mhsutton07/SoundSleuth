// Core Audio COM declarations. Blocks below are the verified declarations (compiled clean at /warn:4).
using System; using System.Runtime.InteropServices; using System.Text;
static class CA {
  public static readonly Guid IID_ISessionMgr2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
  public const uint CLSCTX_ALL = 0x17, DEVICE_STATE_ACTIVE = 1; public const int eRender = 0;
  [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant pv);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint acc, bool inh, uint pid);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder sb, ref uint size);
  public static string ExePath(uint pid){
    IntPtr h=OpenProcess(0x1000,false,pid); if(h==IntPtr.Zero) return "(access denied)";
    try{ var sb=new StringBuilder(32768); uint n=(uint)sb.Capacity;
         return QueryFullProcessImageNameW(h,0,sb,ref n)?sb.ToString():"(access denied)"; }
    finally{ CloseHandle(h); } }
}
[StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public uint pid; }
[StructLayout(LayoutKind.Sequential)] struct PropVariant { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }
static class Keys { public static PropertyKey FriendlyName = new PropertyKey{ fmtid=new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid=14 }; }

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom {}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
  [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devs);
  [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice dev);
  [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
  [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
  [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint n); [PreserveSig] int Item(uint i, out IMMDevice dev); }
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
  [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
  [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore props);
  [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
  [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore {
  [PreserveSig] int GetCount(out uint c); [PreserveSig] int GetAt(uint i, out PropertyKey k);
  [PreserveSig] int GetValue(ref PropertyKey k, out PropVariant v);
  [PreserveSig] int SetValue(ref PropertyKey k, ref PropVariant v); [PreserveSig] int Commit();
}
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2 {
  [PreserveSig] int GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr c);  // placeholder, never call
  [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr v);    // placeholder
  [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
  [PreserveSig] int RegisterSessionNotification(IntPtr n);   [PreserveSig] int UnregisterSessionNotification(IntPtr n);
  [PreserveSig] int RegisterDuckNotification(IntPtr sessionId, IntPtr n); [PreserveSig] int UnregisterDuckNotification(IntPtr n);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s); }
[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2 {   // slots 1-9 = IAudioSessionControl, 10-14 = Control2
  [PreserveSig] int GetState(out int state);                       // 0 Inactive, 1 Active, 2 Expired
  [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string n);
  [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string n, ref Guid ctx);
  [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string p);
  [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string p, ref Guid ctx);
  [PreserveSig] int GetGroupingParam(out Guid g);
  [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
  [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
  [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
  [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
  [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
  [PreserveSig] int GetProcessId(out uint pid);
  [PreserveSig] int IsSystemSoundsSession();                         // 0 (S_OK) = IS system sounds
  [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation {
  [PreserveSig] int GetPeakValue(out float peak);
  [PreserveSig] int GetMeteringChannelCount(out uint n);
  [PreserveSig] int GetChannelsPeakValues(uint n, IntPtr floats);
  [PreserveSig] int QueryHardwareSupport(out uint mask);
}
[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISimpleAudioVolume {
  [PreserveSig] int SetMasterVolume(float level, ref Guid ctx); [PreserveSig] int GetMasterVolume(out float level);
  [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
  [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

static class Hr {
  public const int S_OK = 0, AUDCLNT_S_BUFFER_EMPTY = 0x08890001;
  public const int DEVICE_INVALIDATED = unchecked((int)0x88890004);
  public const int RESOURCES_INVALIDATED = unchecked((int)0x88890026);
}
static class AC {
  public const uint LOOPBACK = 0x00020000, EVENTCALLBACK = 0x00040000;
  public const int SHAREMODE_SHARED = 0;
  public const uint BUF_DISCONTINUITY = 1, BUF_SILENT = 2, BUF_TSERROR = 4;
}
[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient {            // vtable order is load-bearing
  [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity,
                               IntPtr pFormat, [In] ref Guid audioSessionGuid);   // pass Guid.Empty
  [PreserveSig] int GetBufferSize(out uint numBufferFrames);
  [PreserveSig] int GetStreamLatency(out long hnsLatency);
  [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
  [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
  [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);      // free with Marshal.FreeCoTaskMem
  [PreserveSig] int GetDevicePeriod(out long hnsDefaultDevicePeriod, out long hnsMinimumDevicePeriod);
  [PreserveSig] int Start();
  [PreserveSig] int Stop();
  [PreserveSig] int Reset();
  [PreserveSig] int SetEventHandle(IntPtr eventHandle);
  [PreserveSig] int GetService([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}
[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient {
  [PreserveSig] int GetBuffer(out IntPtr pData, out uint numFramesToRead, out uint flags,
                              out ulong devicePosition, out ulong qpcPosition100ns);
  [PreserveSig] int ReleaseBuffer(uint numFramesRead);
  [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
}
sealed class MixFmt {
  public int Channels, Rate, BlockAlign, Bits; public bool IsFloat;
  public static MixFmt Read(IntPtr p) {
    var f = new MixFmt(); ushort tag = (ushort)Marshal.ReadInt16(p, 0);
    f.Channels = (ushort)Marshal.ReadInt16(p, 2); f.Rate = Marshal.ReadInt32(p, 4);
    f.BlockAlign = (ushort)Marshal.ReadInt16(p, 12); f.Bits = (ushort)Marshal.ReadInt16(p, 14);
    if (tag == 3) f.IsFloat = true;
    else if (tag == 0xFFFE) {                       // EXTENSIBLE: SubFormat GUID at offset 24
      byte[] g = new byte[16]; Marshal.Copy(new IntPtr(p.ToInt64() + 24), g, 0, 16);
      f.IsFloat = new Guid(g) == new Guid("00000003-0000-0010-8000-00aa00389b71");
    }
    return f;
  }
}

// Extra Win32 imports used by the app.
static class Win {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

  [StructLayout(LayoutKind.Sequential)]
  public struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
  public const uint FLASHW_ALL = 3, FLASHW_TIMERNOFG = 0xC;
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

  [DllImport("kernel32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool AttachConsole(int pid);
  [DllImport("kernel32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool AllocConsole();
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
