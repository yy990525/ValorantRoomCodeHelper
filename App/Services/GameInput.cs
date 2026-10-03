using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ValorantHelper.Services;

public sealed class GameInput : IGameInputPlatform
{
    private IntPtr target;
    private uint pid;
    private long started;
    private int x, y, width, height;
    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size=32)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Value; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out RectI rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointI point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref PointI point);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref PointI point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PointI point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] input, int size);

    public string BindFromCursor()
    {
        target=IntPtr.Zero;
        var window=GetForegroundWindow();
        try
        {
            GetWindowThreadProcessId(window,out var processId);
            using var process=Process.GetProcessById((int)processId);
            if (!process.ProcessName.Equals("VALORANT-Win64-Shipping",StringComparison.OrdinalIgnoreCase))
                return "绑定失败：请切换到无畏契约游戏，鼠标放在房间码输入框内";
            if (!GetClientRect(window,out var rect)||!GetCursorPos(out var point))
                return "无法读取游戏输入位置，请重试";
            if(GetAncestor(WindowFromPoint(point),2)!=window||!ScreenToClient(window,ref point))
                return "请把鼠标放在未被其他窗口遮挡的游戏输入框内";
            int w=rect.Right-rect.Left,h=rect.Bottom-rect.Top;
            if (w<100||h<100||point.X<0||point.Y<0||point.X>=w||point.Y>=h) return "鼠标需要位于游戏窗口内";
            target=window;pid=processId;started=process.StartTime.ToUniversalTime().Ticks;
            x=point.X;y=point.Y;width=w;height=h;
            return "";
        }
        catch { return "无法访问游戏窗口，请重新绑定"; }
    }
    public string CheckTarget()
    {
        if(target==IntPtr.Zero||!IsWindow(target)) return "请先绑定游戏房间码输入框";
        try
        {
            GetWindowThreadProcessId(target,out var currentPid);
            if(currentPid!=pid) return "游戏窗口已更换，请重新绑定";
            using var process=Process.GetProcessById((int)pid);
            if(process.StartTime.ToUniversalTime().Ticks!=started||!process.ProcessName.Equals("VALORANT-Win64-Shipping",StringComparison.OrdinalIgnoreCase))
                return "游戏已重启，请重新绑定";
            if(!GetClientRect(target,out var rect)||rect.Right-rect.Left!=width||rect.Bottom-rect.Top!=height)
                return "游戏窗口尺寸已改变，请重新绑定输入框";
            return "";
        }
        catch { return "游戏窗口不可用，请重新绑定"; }
    }
    public bool IsForeground => target!=IntPtr.Zero&&GetForegroundWindow()==target;
    public bool ModifiersPressed => new[]{0x10,0x11,0x12,0x5B,0x5C}.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0);
    public bool Activate()
    {
        if(CheckTarget().Length>0)return false;
        if(IsIconic(target))ShowWindow(target,9);
        return IsForeground||SetForegroundWindow(target);
    }
    private bool Ready()=>CheckTarget().Length==0&&IsForeground&&!ModifiersPressed;
    public bool ClickInput()
    {
        if(!Ready())return false;
        var point=new PointI{X=x,Y=y};
        if(!ClientToScreen(target,ref point)||GetAncestor(WindowFromPoint(point),2)!=target)return false;
        int left=GetSystemMetrics(76),top=GetSystemMetrics(77),w=GetSystemMetrics(78),h=GetSystemMetrics(79);
        if(w<=1||h<=1||point.X<left||point.Y<top||point.X>=left+w||point.Y>=top+h)return false;
        var move=new Input{Value=new(){Mouse=new(){X=(int)Math.Round((point.X-left)*65535d/(w-1)),Y=(int)Math.Round((point.Y-top)*65535d/(h-1)),Flags=0xC001}}};
        return Send(new[]{move,new Input{Value=new(){Mouse=new(){Flags=2}}},new Input{Value=new(){Mouse=new(){Flags=4}}}});
    }
    private bool Chord(ushort key)
    {
        if(!Ready())return false;
        Input Key(ushort value,uint flags)=>new(){Type=1,Value=new(){Key=new(){Key=value,Flags=flags}}};
        return Send(new[]{Key(0x11,0),Key(key,0),Key(key,2),Key(0x11,2)});
    }
    private static bool Send(Input[] values)=>SendInput((uint)values.Length,values,Marshal.SizeOf<Input>())==values.Length;
    internal static int NativeInputSize=>Marshal.SizeOf<Input>();
    public bool SelectAll()=>Chord(0x41);
    public bool Paste()=>Chord(0x56);
}
