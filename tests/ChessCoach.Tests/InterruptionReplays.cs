using ChessCoach;

internal static class InterruptionReplays
{
    // Minimal positions from the user's two interruptions; no account or screen data.
    public static readonly (string Before,string Noise,string Recovered,string Move)[] Cases=
    [
        ("2bak4/3Ra1r2/2c1N4/p3Ccn1p/9/2p6/P3P1p1P/9/9/2BAKABN1 w - - 0 1",
         "2bak4/3Ra1N2/2c6/p5n1p/9/2p6/P3P1p1P/9/9/2BAKABN1 w - - 0 1",
         "2bak4/3Ra1N2/2c6/p3Ccn1p/9/2p6/P3P1p1P/9/9/2BAKABN1 b - - 0 1","e7g8"),
        ("2ba1k3/3Ra1N2/2c6/p4cC1p/9/2p6/P3P1p1P/9/9/2BAKABN1 b - - 0 1",
         "2ba1k3/3Ra1N2/4c4/p4cC1p/9/2p6/P5p1P/9/9/2BAKABN1 b - - 0 1",
         "2ba1k3/3Ra1N2/4c4/p4cC1p/9/2p6/P3P1p1P/9/9/2BAKABN1 w - - 0 1","c7e7")
    ];
    public static void Run(Action<bool,string> check)
    {
        for(int n=0;n<Cases.Length;n++)
        {
            var c=Cases[n];var before=Position.ParseFen(c.Before);var noise=Position.ParseFen(c.Noise);var recovered=Position.ParseFen(c.Recovered);
            var tracker=new PositionTracker(before.RedToMove);var gate=new AutomaticRecovery();var session=new AutoPlaySession();session.Enable(before.RedToMove);
            TrackingResult Stable(Position position){tracker.Observe(position);tracker.Observe(position);return tracker.Observe(position);}
            Stable(before);var pending=session.Reserve(before,c.Move,0,1)!;session.InputStarting(pending.Id,DateTimeOffset.UtcNow);
            var failed=Stable(noise);gate.Hold(failed.Message,DateTimeOffset.UtcNow);
            check(failed.State==TrackingState.Rejected&&session.Enabled&&tracker.Current!.Key==before.Key,$"实测中断 {n+1}：漏识别不会覆盖棋局或关闭自动");
            var result=Stable(recovered);
            check(result.State==TrackingState.Accepted&&tracker.Current!.ToFen()==recovered.ToFen()&&session.Observe(tracker.Current,DateTimeOffset.UtcNow)==AutomaticObservation.Confirmed,$"实测中断 {n+1}：画面恢复后正确确认原落子");
        }
    }
}
