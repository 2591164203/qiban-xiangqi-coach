using System.Drawing.Drawing2D;

namespace ChessCoach;

public sealed class BoardView:Control
{
    public Position? Position {get;set;}
    public bool RedAtBottom {get;set;}=true;
    public string? BestMove {get;set;}
    public string EmptyMessage {get;set;}="先选择浏览器中的棋盘";
    public BoardView(){DoubleBuffered=true;BackColor=Color.FromArgb(250,247,240);ResizeRedraw=true;}

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        float cell=Math.Min((Width-100)/8f,(Height-100)/9f);
        if(cell<10)return;
        float left=(Width-cell*8)/2,top=(Height-cell*9)/2;
        var area=new RectangleF(left-27,top-27,cell*8+54,cell*9+54);
        using(var shadow=new SolidBrush(Color.FromArgb(20,64,44,24)))g.FillRectangle(shadow,area.X+4,area.Y+5,area.Width,area.Height);
        using(var wood=new LinearGradientBrush(area,Color.FromArgb(245,226,189),Color.FromArgb(235,208,164),30))g.FillRectangle(wood,area);
        using var line=new Pen(Color.FromArgb(145,111,72),1.1f);
        for(int y=0;y<10;y++)g.DrawLine(line,left,top+y*cell,left+cell*8,top+y*cell);
        for(int x=0;x<9;x++)
        {
            if(x is 0 or 8)g.DrawLine(line,left+x*cell,top,left+x*cell,top+cell*9);
            else{g.DrawLine(line,left+x*cell,top,left+x*cell,top+cell*4);g.DrawLine(line,left+x*cell,top+cell*5,left+x*cell,top+cell*9);}
        }
        foreach(int row in new[]{0,7})
        {
            g.DrawLine(line,left+3*cell,top+row*cell,left+5*cell,top+(row+2)*cell);
            g.DrawLine(line,left+5*cell,top+row*cell,left+3*cell,top+(row+2)*cell);
        }
        using var riverFont=new Font("Microsoft YaHei UI",Math.Max(10,cell*.28f),FontStyle.Regular,GraphicsUnit.Pixel);
        using var riverBrush=new SolidBrush(Color.FromArgb(154,118,77));
        var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
        g.DrawString("楚 河",riverFont,riverBrush,new RectangleF(left+cell,top+cell*4,cell*2,cell),format);
        g.DrawString("汉 界",riverFont,riverBrush,new RectangleF(left+cell*5,top+cell*4,cell*2,cell),format);
        using var numberFont=new Font("Microsoft YaHei UI",10,FontStyle.Regular,GraphicsUnit.Pixel);
        for(int x=0;x<9;x++)g.DrawString(RedAtBottom?"九八七六五四三二一"[x].ToString():(x+1).ToString(),numberFont,Brushes.Gray,new RectangleF(left+x*cell-12,top+cell*9+cell*.43f,24,16),format);
        if(Position!=null)
        {
            for(int y=0;y<10;y++)for(int x=0;x<9;x++)
            {
                char p=Position[x,y];if(p=='.')continue;
                int sx=RedAtBottom?x:8-x,sy=RedAtBottom?y:9-y;
                float diameter=cell*.81f,cx=left+sx*cell,cy=top+sy*cell;
                var r=new RectangleF(cx-diameter/2,cy-diameter/2,diameter,diameter);
                using(var shade=new SolidBrush(Color.FromArgb(42,72,43,24)))g.FillEllipse(shade,r.X+1,r.Y+3,r.Width,r.Height);
                using(var fill=new LinearGradientBrush(r,Color.FromArgb(255,250,228),Color.FromArgb(244,222,178),90))g.FillEllipse(fill,r);
                Color ink=ChessCoach.Position.IsRed(p)?Color.FromArgb(172,50,41):Color.FromArgb(39,57,63);
                using var edge=new Pen(ink,1.4f);g.DrawEllipse(edge,r);
                r.Inflate(-4,-4);g.DrawEllipse(edge,r);
                using var font=new Font("Microsoft YaHei UI",diameter*.55f,FontStyle.Bold,GraphicsUnit.Pixel);
                using var brush=new SolidBrush(ink);g.DrawString(ChessCoach.Position.PieceName(p),font,brush,new RectangleF(cx-diameter/2,cy-diameter/2,diameter,diameter),format);
            }
            if(BestMove!=null&&ChessCoach.Position.TryCoordinates(BestMove,out var from,out var to))
            {
                PointF ScreenPoint(Point p)=>new(left+(RedAtBottom?p.X:8-p.X)*cell,top+(RedAtBottom?p.Y:9-p.Y)*cell);
                var a=ScreenPoint(from);var b=ScreenPoint(to);
                using var arrow=new Pen(Color.FromArgb(225,19,133,106),Math.Max(4,cell*.09f));
                using var cap=new AdjustableArrowCap(4,5,true);arrow.CustomEndCap=cap;g.DrawLine(arrow,a,b);
                using var origin=new SolidBrush(Color.FromArgb(235,19,133,106));g.FillEllipse(origin,a.X-5,a.Y-5,10,10);
            }
        }
        else
        {
            using var wash=new SolidBrush(Color.FromArgb(210,250,247,240));g.FillRectangle(wash,area);
            using var font=new Font("Microsoft YaHei UI",13,FontStyle.Bold);
            g.DrawString(EmptyMessage,font,Brushes.DimGray,new RectangleF(left,top+cell*3,cell*8,cell*3),format);
        }
    }
}
