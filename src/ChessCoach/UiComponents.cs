using System.Drawing.Drawing2D;

namespace ChessCoach;

public class SurfacePanel:Panel
{
    public SurfacePanel(){DoubleBuffered=true;Padding=new Padding(16);BackColor=Color.White;}
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor??Color.FromArgb(242,245,243));
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=new GraphicsPath();int r=14,w=ClientSize.Width-1,h=ClientSize.Height-1;
        if(w<r*2||h<r*2)return;
        path.AddArc(0,0,r*2,r*2,180,90);path.AddArc(w-r*2,0,r*2,r*2,270,90);
        path.AddArc(w-r*2,h-r*2,r*2,r*2,0,90);path.AddArc(0,h-r*2,r*2,r*2,90,90);path.CloseFigure();
        using var fill=new SolidBrush(BackColor);using var line=new Pen(Color.FromArgb(223,231,225));
        e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(line,path);
    }
}

public sealed class CandidateCard:SurfacePanel
{
    public string ContentTitle=>_name.Text;
    public int ContentHeight=>Padding.Vertical+_name.Font.Height+_line.Font.Height*2+12;
    internal bool ContentFits
    {
        get
        {
            var bounds=_score.Bounds;
            for(Control? parent=_score.Parent;parent!=null&&parent!=this;parent=parent.Parent)bounds.Offset(parent.Location);
            return bounds.Left>=0&&bounds.Right<=ClientSize.Width&&_score.Height>=_score.Font.Height;
        }
    }
    private readonly Label _name=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold),AutoEllipsis=true};
    private readonly Label _score=new(){Dock=DockStyle.Right,Width=64,TextAlign=ContentAlignment.MiddleRight,Font=new Font("Segoe UI",11,FontStyle.Bold)};
    private readonly Label _line=new(){Dock=DockStyle.Fill,ForeColor=Color.FromArgb(108,122,116),Font=new Font("Microsoft YaHei UI",9),AutoEllipsis=true};
    public CandidateCard()
    {
        Height=88;Dock=DockStyle.Fill;Margin=new Padding(0,0,0,6);Padding=new Padding(12,6,12,6);
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,BackColor=Color.Transparent,Padding=Padding.Empty,Margin=Padding.Empty};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var headline=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=1,BackColor=Color.Transparent,Margin=Padding.Empty};
        headline.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));headline.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _name.AutoSize=true;_score.AutoSize=true;headline.Controls.Add(_name,0,0);headline.Controls.Add(_score,1,0);
        _name.Margin=Padding.Empty;_score.Margin=Padding.Empty;_line.Margin=new Padding(0,2,0,0);
        grid.Controls.Add(headline,0,0);grid.Controls.Add(_line,0,1);Controls.Add(grid);
    }
    public void SetContent(string name,string score,string line,bool primary=false)
    {
        _name.Text=name;_score.Text=score;_line.Text=line;
        BackColor=primary?Color.FromArgb(235,246,239):Color.FromArgb(249,250,248);
        _score.ForeColor=Color.FromArgb(22,117,91);Invalidate();
    }
    public void SetWaiting(int rank)=>SetContent($"{rank:00}  等待候选走法","—","棋局稳定后会自动更新");
}
