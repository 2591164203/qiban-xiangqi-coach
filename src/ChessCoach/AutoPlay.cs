namespace ChessCoach;

public sealed record MoveClickPlan(string Move,Point From,Point To,RectangleF Grid,Rectangle WindowBounds)
{
    public static MoveClickPlan Create(Position position,string move,RectangleF grid,bool redAtBottom,Rectangle windowBounds)
    {
        if(!position.Validate(out var reason)||!position.IsLegal(move))throw new InvalidOperationException("不能执行此走法："+reason);
        if(!float.IsFinite(grid.X)||!float.IsFinite(grid.Y)||!float.IsFinite(grid.Width)||!float.IsFinite(grid.Height)||grid.Width<80||grid.Height<90)
            throw new InvalidOperationException("棋盘坐标无效，请重新连接棋盘");
        Position.TryCoordinates(move,out var from,out var to);
        Point Screen(Point p)=>new((int)Math.Round(grid.Left+(redAtBottom?p.X:8-p.X)*grid.Width/8),(int)Math.Round(grid.Top+(redAtBottom?p.Y:9-p.Y)*grid.Height/9));
        return new(move,Screen(from),Screen(to),grid,windowBounds);
    }
}

public sealed record AutomaticMove(Guid Id,long Generation,string TurnKey,Position Before,Position Expected,string Move,DateTimeOffset? InputStarted=null);
public enum AutomaticObservation { None,Waiting,Confirmed,ConfirmedWithReply,Paused }

public sealed class AutomaticRetryException(string message):InvalidOperationException(message);

// A failed frame suspends dispatch without revoking the user's enabled mode.
// Recovery requires consecutive legal observations, not just elapsed time.
public sealed class AutomaticRecovery
{
    public bool Waiting {get;private set;}
    public string Reason {get;private set;}="";
    private DateTimeOffset _retryAfter;
    private string? _key;
    private int _frames;
    public void Reset(){Waiting=false;Reason="";_key=null;_frames=0;_retryAfter=default;}
    public void Hold(string reason,DateTimeOffset now)
    {Waiting=true;Reason=reason;_retryAfter=now.AddMilliseconds(600);_key=null;_frames=0;}
    public bool ObserveReliable(string key,DateTimeOffset now)
    {
        if(!Waiting)return true;
        if(_key!=key){_key=key;_frames=1;}else _frames++;
        if(_frames<3||now<_retryAfter)return false;
        Reset();return true;
    }
}

// No mouse effects here. The session reserves one action per confirmed turn and
// requires visual acknowledgement before another action can be reserved.
public sealed class AutoPlaySession
{
    public const float MinimumConfidence=.80f;
    public bool Enabled {get;private set;}
    public bool PlayRed {get;private set;}
    public long Generation {get;private set;}
    public AutomaticMove? Pending {get;private set;}
    public string Message {get;private set;}="手动提示";
    public int ConfirmedMoves {get;private set;}
    public bool ConfirmationDelayed {get;private set;}
    private readonly HashSet<string> _attempted=[];

    public void Enable(bool playRed)
    {
        Generation++;Enabled=true;PlayRed=playRed;Pending=null;_attempted.Clear();ConfirmedMoves=0;ConfirmationDelayed=false;
        Message=$"自动执{(playRed?"红":"黑")}，等待可靠局面";
    }
    public void Stop(string reason)
    {
        Generation++;Enabled=false;Pending=null;ConfirmationDelayed=false;Message=reason;
    }
    public AutomaticMove? Reserve(Position position,string move,int plyCount,float confidence)
    {
        if(!Enabled||Pending!=null||position.RedToMove!=PlayRed||!float.IsFinite(confidence)||confidence<MinimumConfidence||!position.IsLegal(move)||!position.Validate(out _))return null;
        string key=position.ToFen()+"|"+plyCount;
        if(_attempted.Contains(key))return null;
        Pending=new(Guid.NewGuid(),Generation,key,position,position.Apply(move),move);
        Message="正在复核棋局";return Pending;
    }
    public bool InputStarting(Guid id,DateTimeOffset now)
    {
        if(!Enabled||Pending?.Id!=id||Pending.Generation!=Generation||Pending.InputStarted!=null)return false;
        _attempted.Add(Pending.TurnKey);Pending=Pending with{InputStarted=now};ConfirmationDelayed=false;Message="正在落子";return true;
    }
    public void ReleaseUnsent(Guid id)
    {
        if(Pending?.Id==id&&Pending.InputStarted==null){Pending=null;Message="等待稳定棋局";}
    }
    public bool CheckTimeout(DateTimeOffset now)
    {
        if(Enabled&&!ConfirmationDelayed&&Pending?.InputStarted is DateTimeOffset sent&&(now-sent).TotalSeconds>8)
        {ConfirmationDelayed=true;Message="等待落子结果恢复识别 · 不会重复点击";return true;}
        return false;
    }
    public AutomaticObservation Observe(Position confirmed,DateTimeOffset now)
    {
        if(!Enabled||Pending?.InputStarted==null)return AutomaticObservation.None;
        CheckTimeout(now);
        var pending=Pending!;
        if(confirmed.ToFen()==pending.Before.ToFen()){if(!ConfirmationDelayed)Message="等待落子确认";return AutomaticObservation.Waiting;}
        if(confirmed.ToFen()==pending.Expected.ToFen())
        {Pending=null;ConfirmationDelayed=false;ConfirmedMoves++;Message="落子已确认，等待对方";return AutomaticObservation.Confirmed;}
        if(pending.Expected.TryTransition(confirmed,out var reply)&&pending.Expected.Apply(reply).RedToMove==confirmed.RedToMove)
        {Pending=null;ConfirmationDelayed=false;ConfirmedMoves++;Message="我方落子和对方回棋均已确认";return AutomaticObservation.ConfirmedWithReply;}
        Stop("实际落子与预期不一致，已暂停；请核对局面");return AutomaticObservation.Paused;
    }
}

public sealed class VisualBoardSnapshot:IDisposable
{
    private readonly Bitmap _image;
    private readonly RectangleF _grid;
    public string PositionKey {get;}
    public bool RedAtBottom {get;}
    public VisualBoardSnapshot(Bitmap image,RectangleF grid,Position position,bool redAtBottom)
    {_image=(Bitmap)image.Clone();_grid=grid;PositionKey=position.Key;RedAtBottom=redAtBottom;}
    public VisualBoardSnapshot Clone()=>new(_image,_grid,new Position(PositionKey.ToCharArray(),true),RedAtBottom);

    public bool Matches(Bitmap image,RectangleF grid,Position position,string move,bool redAtBottom)
    {
        if(position.Key!=PositionKey||redAtBottom!=RedAtBottom||!Position.TryCoordinates(move,out var from,out var to)
            ||!new RectangleF(0,0,image.Width,image.Height).Contains(grid)||grid.Width<80||grid.Height<90)return false;
        foreach(var p in new[]{from,to})
        {
            int x=redAtBottom?p.X:8-p.X,y=redAtBottom?p.Y:9-p.Y;
            double difference=0;int count=0;
            for(int sy=-4;sy<=4;sy++)for(int sx=-4;sx<=4;sx++)
            {
                Color Sample(Bitmap bmp,RectangleF g)
                {
                    int px=(int)Math.Round(g.Left+(x+sx*.055f)*g.Width/8),py=(int)Math.Round(g.Top+(y+sy*.055f)*g.Height/9);
                    return bmp.GetPixel(Math.Clamp(px,0,bmp.Width-1),Math.Clamp(py,0,bmp.Height-1));
                }
                var a=Sample(_image,_grid);var b=Sample(image,grid);
                difference+=(Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B))/(3d*255);count++;
            }
            if(difference/count>.10)return false;
        }
        return true;
    }
    public void Dispose()=>_image.Dispose();
}
