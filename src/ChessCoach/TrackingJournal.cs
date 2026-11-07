using System.Text.Json;

namespace ChessCoach;

public sealed class TrackingJournal
{
    private readonly string _file=Path.Combine(AppContext.BaseDirectory,"logs","tracking-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".jsonl");
    private string? _last;
    public void RecordAutomatic(string action,AutoPlaySession session,PositionTracker tracker,MoveClickPlan? plan=null,string? detail=null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var entry=new{time=DateTimeOffset.Now,state="automatic",action,session.Enabled,side=session.PlayRed?"red":"black",session.Message,detail,move=session.Pending?.Move,confirmed=tracker.Current?.ToFen(),ply=tracker.History.Count,clicks=plan==null?null:new[]{new{plan.From.X,plan.From.Y},new{plan.To.X,plan.To.Y}}};
            File.AppendAllText(_file,JsonSerializer.Serialize(entry)+Environment.NewLine);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){ }
    }
    public void Record(Detection detection,TrackingResult result,PositionTracker tracker)
    {
        if(result.State==TrackingState.Warming)return;
        string key=result.State+":"+detection.Position?.Key+":"+tracker.Current?.ToFen();
        if(key==_last)return;_last=key;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var entry=new{time=DateTimeOffset.Now,state=result.State.ToString(),message=result.Message,observed=detection.Position?.ToFen(),confirmed=tracker.Current?.ToFen(),recovered=result.RecoveredMoves,history=tracker.History.ToArray(),confidence=detection.Confidence};
            File.AppendAllText(_file,JsonSerializer.Serialize(entry)+Environment.NewLine);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){ }
    }
}
