using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ValorantHelper.Services;

public sealed class Hotkeys : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window,int id);
    private readonly HwndSource source;
    private readonly IntPtr handle;
    public event Action? Toggle;
    public event Action? Copy;
    public Hotkeys(Window window)
    {
        handle=new WindowInteropHelper(window).Handle;
        source=HwndSource.FromHwnd(handle);source.AddHook(Hook);
    }
    public string Register(int toggle,int copy)
    {
        UnregisterHotKey(handle,71);UnregisterHotKey(handle,72);
        bool t=RegisterHotKey(handle,71,0x4000,(uint)(0x70+toggle-1));
        bool c=RegisterHotKey(handle,72,0x4000,(uint)(0x70+copy-1));
        return t&&c ? "" : $"{(!t ? $"F{toggle} " : "")}{(!c ? $"F{copy} " : "")}已被占用，可在设置中更换快捷键";
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(message==0x0312){if(wp.ToInt32()==71)Toggle?.Invoke();else if(wp.ToInt32()==72)Copy?.Invoke();handled=true;}
        return IntPtr.Zero;
    }
    public void Dispose(){UnregisterHotKey(handle,71);UnregisterHotKey(handle,72);source.RemoveHook(Hook);}
}

public static class ClipboardWriter
{
    public static async Task<bool> CopyAsync(string code,Func<bool> stillCurrent)
    {
        if(!CodeExtractor.IsValid(code)) return false;
        for(int attempt=0;attempt<4;attempt++)
        {
            if(!stillCurrent()) return false;
            try { System.Windows.Clipboard.SetText(code); return true; }
            catch(ExternalException) { if(attempt<3)await Task.Delay(75*(attempt+1)); }
        }
        return false;
    }
}
