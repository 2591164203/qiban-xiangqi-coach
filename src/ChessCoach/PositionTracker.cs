using System.Diagnostics;

namespace ChessCoach;

public enum TrackingState { Warming, Accepted, Unchanged, Recovered, Rewound, TurnCorrected, Rejected }
public sealed record TrackingResult(TrackingState State,string Message,string? Move=null,string[]? RecoveredMoves=null);
public sealed record RecoveryResult(string[][] Paths,bool Exhausted);

public static class MoveRecovery
{
    // Search for an EXACT board match. A partial/ambiguous/timed-out search never
    // authorizes a new position. The final ply is matched directly, not brute-forced.
    public static RecoveryResult Find(Position start,Position target,int maxPlies=3,int budgetMilliseconds=140,int minPlies=2)
    {
        var watch=Stopwatch.StartNew();
        bool exhausted=false;int nodes=0;
        for(int depth=minPlies;depth<=maxPlies;depth++)
        {
            if(start.Differences(target)>2*depth)continue;
            var paths=new List<string[]>();
            void Search(Position current,int remaining,List<string> path)
            {
                if(paths.Count>1||exhausted)return;
                if(++nodes>20000||watch.ElapsedMilliseconds>budgetMilliseconds){exhausted=true;return;}
                if(current.Differences(target)>2*remaining)return;
                if(remaining==1)
                {
                    if(current.TryTransition(target,out var last))paths.Add([..path,last]);
                    return;
                }
                foreach(string move in current.LegalMoves())
                {
                    var next=current.Apply(move);
                    if(next.Differences(target)>2*(remaining-1))continue;
                    path.Add(move);Search(next,remaining-1,path);path.RemoveAt(path.Count-1);
                    if(paths.Count>1||exhausted)return;
                }
            }
            Search(start,depth,[]);
            if(paths.Count>0||exhausted)return new(paths.ToArray(),exhausted);
        }
        return new([],exhausted);
    }
}

public sealed class PositionTracker(bool initialRedTurn,int stableFrames=3)
{
    public Position? Current {get;private set;}
    public string? RootFen {get;private set;}
    public List<string> History {get;}=[];
    public int RecoveredPlies {get;private set;}
    private readonly List<Position> _timeline=[];
    private string? _pending,_attempted;
    private int _count;
    private bool _turnAnchored;
    private TrackingResult? _previousFailure;
    public void Invalidate(){_pending=null;_count=0;_attempted=null;_previousFailure=null;}

    public TrackingResult Observe(Position observed)
    {
        if(_pending!=observed.Key){_pending=observed.Key;_count=1;_attempted=null;_previousFailure=null;}else _count++;
        if(_count<stableFrames)return new(TrackingState.Warming,"等待棋子落稳，正在同步画面");
        if(Current==null)
        {
            var root=observed.WithTurn(initialRedTurn);
            if(!root.Validate(out var error))return new(TrackingState.Rejected,error);
            Current=root;RootFen=root.ToFen();_timeline.Add(root);
            return new(TrackingState.Accepted,"棋局已连接");
        }
        if(Current.Key==observed.Key)return new(TrackingState.Unchanged,"局面已同步");
        if(_attempted==observed.Key&&_previousFailure!=null)return _previousFailure;
        if(Current.TryTransition(observed,out var move))
        {
            Append([move]);_turnAnchored=true;
            return new(TrackingState.Accepted,"已跟随最新走法",move);
        }
        bool plausible=observed.WithTurn(true).Validate(out _)||observed.WithTurn(false).Validate(out _);
        if(!plausible)return Failure("识别结果尚不可靠，等待画面恢复；请对照采集画面",observed);
        // Prefer a legal forward pair over an older matching board, so a real
        // repetition cycle is not mistaken for an undo and erased from history.
        var pair=MoveRecovery.Find(Current,observed,maxPlies:2);
        if(pair.Paths.Length==1&&!pair.Exhausted)return AcceptRecovery(pair.Paths[0]);
        if(pair.Paths.Length>1||pair.Exhausted)return Failure("暂时无法唯一还原走法，请核对行棋方后采用当前局面",observed);
        // A known earlier board is a deliberate undo/replay step, not an illegal move.
        for(int i=_timeline.Count-2;i>=0;i--)
            if(_timeline[i].Key==observed.Key)
            {
                int undone=_timeline.Count-1-i;
                Current=_timeline[i];_timeline.RemoveRange(i+1,_timeline.Count-i-1);
                History.RemoveRange(i,History.Count-i);
                return new(TrackingState.Rewound,$"已跟随悔棋，回退 {undone} 步");
            }
        // Before a directly observed move anchors the turn, an exact one-ply move
        // by the other color can correct an initial manual turn assumption.
        var other=Current.WithTurn(!Current.RedToMove);
        if(!_turnAnchored&&other.TryTransition(observed,out var corrected))
        {
            Current=other;RootFen=other.ToFen();History.Clear();_timeline.Clear();_timeline.Add(other);
            Append([corrected]);_turnAnchored=true;
            return new(TrackingState.TurnCorrected,"已根据实际落子校正行棋方",corrected);
        }
        var recovered=MoveRecovery.Find(Current,observed,minPlies:3);
        if(recovered.Paths.Length==1&&!recovered.Exhausted)
        {
            return AcceptRecovery(recovered.Paths[0]);
        }
        return Failure(recovered.Paths.Length>1?"存在多种走法顺序，请核对行棋方后采用当前局面":
            "画面变化超出可还原范围；核对棋盘和行棋方后，点击采用当前局面",observed);
    }
    private TrackingResult Failure(string message,Position observed)
    {
        _attempted=observed.Key;return _previousFailure=new(TrackingState.Rejected,message);
    }
    private TrackingResult AcceptRecovery(string[] path)
    {
        Append(path);RecoveredPlies+=path.Length;
        return new(TrackingState.Recovered,$"已自动补齐 {path.Length} 步，继续分析",path[^1],path);
    }
    private void Append(IEnumerable<string> moves)
    {
        foreach(string move in moves){Current=Current!.Apply(move);History.Add(move);_timeline.Add(Current);}
        _attempted=null;_previousFailure=null;
    }
}
