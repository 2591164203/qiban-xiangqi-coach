using System.Runtime.InteropServices;
using System.Text;

namespace ChessCoach;

public sealed record CapturedFrame(Bitmap Image,RectangleF Grid,Rectangle Bounds,Rectangle WindowBounds);
public sealed class BoardOccludedException(bool byCoach):InvalidOperationException(byCoach?"棋伴窗口遮挡棋盘，正在调整位置":"棋盘被其他窗口遮挡，等待移开后恢复")
{public bool ByCoach {get;}=byCoach;}

public sealed class CaptureTarget
{
    public IntPtr Handle {get;init;}
    public Rectangle WindowAtSelection {get;init;}
    public Rectangle GridAtSelection {get;init;}
    public string Title {get;init;}="";
    private RectangleF? _gridInWindow;

    public Rectangle CurrentGrid()
    {
        return Resolve().Grid;
    }
    public Rectangle GetCaptureBounds()
    {
        var grid=Resolve().Grid;
        int px=(int)Math.Ceiling(grid.Width/8d*.56),py=(int)Math.Ceiling(grid.Height/9d*.56);
        grid.Inflate(px,py);return grid;
    }

    private (Rectangle Grid,Rectangle Window) Resolve()
    {
        if(!Native.IsWindow(Handle))throw new InvalidOperationException("目标窗口已关闭，请重新选择棋盘");
        if(Native.IsIconic(Handle))throw new InvalidOperationException("浏览器已最小化，请恢复棋盘窗口");
        var current=Native.WindowRect(Handle);
        if(current.Size!=WindowAtSelection.Size)throw new InvalidOperationException("浏览器大小已改变，请重新选择棋盘");
        var grid=GridAtSelection;grid.Offset(current.X-WindowAtSelection.X,current.Y-WindowAtSelection.Y);
        return(grid,current);
    }

    public void ApplyCalibration(RectangleF grid,CapturedFrame frame)
    {
        _gridInWindow=new RectangleF(frame.Bounds.Left+grid.Left-frame.WindowBounds.Left,frame.Bounds.Top+grid.Top-frame.WindowBounds.Top,grid.Width,grid.Height);
    }

    public CapturedFrame Capture(bool verifyOcclusion=true)
    {
        var resolved=Resolve();var grid=resolved.Grid;
        var inspection=_gridInWindow.HasValue?new RectangleF(resolved.Window.Left+_gridInWindow.Value.Left,resolved.Window.Top+_gridInWindow.Value.Top,_gridInWindow.Value.Width,_gridInWindow.Value.Height):grid;
        // Foreground pixel capture deliberately avoids PrintWindow's black browser frames.
        if(verifyOcclusion)
        foreach(float fy in new[]{.05f,.5f,.95f})foreach(float fx in new[]{.05f,.5f,.95f})
        {
            var point=new Point((int)(inspection.Left+inspection.Width*fx),(int)(inspection.Top+inspection.Height*fy));
            var blocker=Native.RootAt(point);
            if(blocker!=Handle){Native.GetWindowThreadProcessId(blocker,out int process);throw new BoardOccludedException(process==Environment.ProcessId);}
        }
        int px=(int)Math.Ceiling(grid.Width/8d*.56),py=(int)Math.Ceiling(grid.Height/9d*.56);
        var capture=grid;capture.Inflate(px,py);
        if(!SystemInformation.VirtualScreen.Contains(capture))throw new InvalidOperationException("棋盘超出屏幕边缘，请完整显示棋盘");
        var image=Native.Capture(capture);
        return new CapturedFrame(image,new RectangleF(inspection.Left-capture.Left,inspection.Top-capture.Top,inspection.Width,inspection.Height),capture,resolved.Window);
    }

    public static CaptureTarget FromSelection(Rectangle grid)
    {
        var handle=Native.RootAt(new Point(grid.Left+grid.Width/2,grid.Top+grid.Height/2));
        if(handle==IntPtr.Zero)throw new InvalidOperationException("没有找到目标窗口");
        Native.GetWindowThreadProcessId(handle,out var processId);
        if(processId==Environment.ProcessId)throw new InvalidOperationException("请框选浏览器中的棋盘");
        return new CaptureTarget{Handle=handle,GridAtSelection=grid,WindowAtSelection=Native.WindowRect(handle),Title=Native.Title(handle)};
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]private struct RECT {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hWnd,out RECT rect);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")]public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int processId);
    public static IntPtr RootAt(Point point)=>GetAncestor(WindowFromPoint(point),2);
    public static Rectangle WindowRect(IntPtr hwnd){GetWindowRect(hwnd,out var r);return Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);}
    public static string Title(IntPtr hwnd){var text=new StringBuilder(512);GetWindowText(hwnd,text,text.Capacity);return text.ToString();}
    public static Bitmap Capture(Rectangle rectangle)
    {
        var image=new Bitmap(rectangle.Width,rectangle.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try{using var graphics=Graphics.FromImage(image);graphics.CopyFromScreen(rectangle.Location,Point.Empty,rectangle.Size,CopyPixelOperation.SourceCopy);return image;}
        catch{image.Dispose();throw;}
    }
}

public sealed class RegionSelector:Form
{
    private readonly Bitmap _desktop;
    private readonly Rectangle _desktopBounds;
    private Point _origin;
    private Rectangle _selection;
    private bool _dragging;
    public Rectangle SelectedGrid=>new(_selection.X+_desktopBounds.X,_selection.Y+_desktopBounds.Y,_selection.Width,_selection.Height);
    public RegionSelector()
    {
        _desktopBounds=SystemInformation.VirtualScreen;_desktop=Native.Capture(_desktopBounds);
        FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=_desktopBounds;
        TopMost=true;DoubleBuffered=true;KeyPreview=true;Cursor=Cursors.Cross;ShowInTaskbar=false;
        KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();}if(e.KeyCode==Keys.Enter)Confirm();};
        MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){_origin=e.Location;_dragging=true;Capture=true;}};
        MouseMove+=(_,e)=>{if(!_dragging)return;_selection=Rectangle.FromLTRB(Math.Min(_origin.X,e.X),Math.Min(_origin.Y,e.Y),Math.Max(_origin.X,e.X),Math.Max(_origin.Y,e.Y));Invalidate();};
        MouseUp+=(_,_)=>{_dragging=false;Capture=false;Invalidate();};
        MouseDoubleClick+=(_,_)=>Confirm();
    }
    private void Confirm(){if(_selection.Width>=160&&_selection.Height>=180){DialogResult=DialogResult.OK;Close();}}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.DrawImageUnscaled(_desktop,Point.Empty);
        using(var mask=new Region(ClientRectangle)){if(!_selection.IsEmpty)mask.Exclude(_selection);using var shade=new SolidBrush(Color.FromArgb(155,12,22,28));g.FillRegion(shade,mask);}
        using var pen=new Pen(Color.FromArgb(86,222,173),2);
        if(!_selection.IsEmpty)
        {
            g.DrawRectangle(pen,_selection);
            using var guide=new Pen(Color.FromArgb(140,86,222,173),1);
            for(int x=1;x<8;x++)g.DrawLine(guide,_selection.X+_selection.Width*x/8f,_selection.Top,_selection.X+_selection.Width*x/8f,_selection.Bottom);
            for(int y=1;y<9;y++)g.DrawLine(guide,_selection.Left,_selection.Y+_selection.Height*y/9f,_selection.Right,_selection.Y+_selection.Height*y/9f);
        }
        var screen=Screen.FromPoint(Cursor.Position).Bounds;
        int left=screen.X-_desktopBounds.X+30,top=screen.Y-_desktopBounds.Y+30;
        using var bg=new SolidBrush(Color.FromArgb(235,21,40,45));g.FillRectangle(bg,left,top,800,104);
        using var title=new Font("Microsoft YaHei UI",16,FontStyle.Bold);using var body=new Font("Microsoft YaHei UI",11);
        g.DrawString("选择完整棋盘 · 自动定位网格",title,Brushes.White,left+20,top+15);
        g.DrawString("框入全部棋子和棋盘，可包含外边框；程序会自动校准交叉点。",body,Brushes.WhiteSmoke,left+20,top+49);
        g.DrawString("Enter / 双击确认     Esc 取消",body,Brushes.PaleGreen,left+20,top+76);
    }
    protected override void Dispose(bool disposing){if(disposing)_desktop.Dispose();base.Dispose(disposing);}
}
