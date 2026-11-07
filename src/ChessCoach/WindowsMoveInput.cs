using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ChessCoach;

public static class WindowsMoveInput
{
    [StructLayout(LayoutKind.Sequential)]private struct MouseInput{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Explicit)]private struct InputUnion{[FieldOffset(0)]public MouseInput Mouse;}
    [StructLayout(LayoutKind.Sequential)]private struct Input{public uint Type;public InputUnion Value;}
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)]public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]public static extern bool UnregisterHotKey(IntPtr hwnd,int id);

    public static int NativeInputSize=>Marshal.SizeOf<Input>();
    private static Input Mouse(uint flags)=>new(){Value=new InputUnion{Mouse=new MouseInput{Flags=flags}}};
    private static void NoUserInput()
    {
        foreach(int key in new[]{1,2,0x10,0x11,0x12,0x1B,0x77})
            if((GetAsyncKeyState(key)&0x8000)!=0)throw new InvalidOperationException("检测到手动操作，自动模式已暂停");
    }
    private static void Click(Point point,IntPtr target)
    {
        if(Native.RootAt(point)!=target)throw new InvalidOperationException("落子位置被遮挡，自动模式已暂停");
        if(!SetCursorPos(point.X,point.Y))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法移动鼠标");
        if(!GetCursorPos(out var actual)||Math.Abs(actual.X-point.X)>2||Math.Abs(actual.Y-point.Y)>2)
            throw new InvalidOperationException("鼠标位置未到达棋子中心，自动模式已暂停");
        var inputs=new[]{Mouse(0x0002),Mouse(0x0004)};
        // Standard foreground input; respect Windows' integrity and focus restrictions.
        if(SendInput(2,inputs,Marshal.SizeOf<Input>())!=2)
        {SendInput(1,[Mouse(0x0004)],Marshal.SizeOf<Input>());throw new InvalidOperationException("鼠标输入未完整发送，自动模式已暂停");}
    }
    public static async Task<MoveClickPlan> ExecuteAsync(CaptureTarget target,IntPtr coachWindow,Func<CancellationToken,Task<MoveClickPlan>> prepare,Action inputStarting,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();NoUserInput();
        var foreground=GetForegroundWindow();
        if(foreground!=target.Handle&&foreground!=coachWindow)throw new InvalidOperationException("你已切换到其他窗口，自动模式已暂停");
        GetCursorPos(out var originalCursor);
        if(foreground!=target.Handle&&!SetForegroundWindow(target.Handle))throw new InvalidOperationException("无法激活棋盘窗口，自动模式已暂停");
        await Task.Delay(100,ct);
        var plan=await prepare(ct);ct.ThrowIfCancellationRequested();
        void CheckWindow()
        {
            NoUserInput();
            if(GetForegroundWindow()!=target.Handle||Native.WindowRect(target.Handle)!=plan.WindowBounds)
                throw new InvalidOperationException("棋盘窗口或焦点已改变，自动模式已暂停");
            if(!SystemInformation.VirtualScreen.Contains(plan.From)||!SystemInformation.VirtualScreen.Contains(plan.To))
                throw new InvalidOperationException("落子位置超出屏幕");
            if(Native.RootAt(plan.From)!=target.Handle||Native.RootAt(plan.To)!=target.Handle)
                throw new InvalidOperationException("棋盘被遮挡，自动模式已暂停");
        }
        CheckWindow();GetCursorPos(out var currentCursor);
        if(Math.Abs(currentCursor.X-originalCursor.X)>8||Math.Abs(currentCursor.Y-originalCursor.Y)>8)
            throw new InvalidOperationException("鼠标正在由你操作，自动模式已暂停");
        ct.ThrowIfCancellationRequested();inputStarting();Click(plan.From,target.Handle);
        await Task.Delay(140,ct);CheckWindow();GetCursorPos(out currentCursor);
        if(Math.Abs(currentCursor.X-plan.From.X)>8||Math.Abs(currentCursor.Y-plan.From.Y)>8)
            throw new InvalidOperationException("你已接管鼠标，自动模式已暂停");
        ct.ThrowIfCancellationRequested();Click(plan.To,target.Handle);return plan;
    }
}
