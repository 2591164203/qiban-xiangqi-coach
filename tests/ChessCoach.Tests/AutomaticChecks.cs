using ChessCoach;
using System.Drawing;

internal static class AutomaticChecks
{
    public static void Run(Action<bool,string> check)
    {
        var start=Position.ParseFen(Position.StartFen);
        var bounds=new Rectangle(0,0,1200,1200);var grid=new RectangleF(100,100,800,900);
        var plan=MoveClickPlan.Create(start,"h2e2",grid,true,bounds);
        check(plan.From==new Point(800,800)&&plan.To==new Point(500,800),"自动落子坐标由实际交叉点映射，红方在下");
        var rotated=MoveClickPlan.Create(start,"h2e2",grid,false,bounds);
        check(rotated.From==new Point(200,300)&&rotated.To==new Point(500,300),"自动落子坐标支持黑方在下和上下翻转");
        var negative=MoveClickPlan.Create(start,"b0c2",new RectangleF(-1000,50,800,900),true,new Rectangle(-1100,0,1100,1100));
        check(negative.From==new Point(-900,950)&&negative.To==new Point(-800,750),"自动行棋支持负坐标副屏");
        bool illegal=false,invalidGrid=false;
        try{MoveClickPlan.Create(start,"b7e7",grid,true,bounds);}catch(InvalidOperationException){illegal=true;}
        try{MoveClickPlan.Create(start,"h2e2",new RectangleF(float.NaN,0,800,900),true,bounds);}catch(InvalidOperationException){invalidGrid=true;}
        check(illegal&&invalidGrid,"非法走法和无效网格不能生成点击计划");
        check(WindowsMoveInput.NativeInputSize==(Environment.Is64BitProcess?40:28),"Windows SendInput 原生结构尺寸正确");

        var auto=new AutoPlaySession();var now=DateTimeOffset.UtcNow;
        check(!auto.Enabled&&auto.Reserve(start,"h2e2",0,1)==null,"自动模式默认关闭，未启用不会安排落子");
        auto.Enable(false);
        check(auto.Reserve(start,"h2e2",0,1)==null,"只允许所选执棋颜色在自己回合落子");
        auto.Enable(true);
        check(auto.Reserve(start,"h2e2",0,.5f)==null&&auto.Reserve(start,"h2e2",0,float.NaN)==null&&auto.Reserve(start,"a0a9",0,1)==null,"置信度不足、无效置信度和非法建议不能落子");
        var first=auto.Reserve(start,"h2e2",0,1)!;
        check(first!=null&&auto.Reserve(start,"b0c2",0,1)==null,"每次只允许一个待执行动作");
        check(!auto.InputStarting(Guid.NewGuid(),now)&&auto.InputStarting(first!.Id,now)&&!auto.InputStarting(first.Id,now),"发送前核对动作标识，同一动作最多发送一次");
        check(auto.Observe(start,now)==AutomaticObservation.Waiting&&auto.Pending!=null,"点击后画面尚未变化时等待确认，不重复点击");
        var expected=start.Apply("h2e2");
        check(auto.Observe(expected,now)==AutomaticObservation.Confirmed&&auto.Pending==null&&auto.ConfirmedMoves==1,"实际棋盘与预期完全相同才确认我方落子");
        check(auto.Reserve(start,"h2e2",0,1)==null,"已发送的同一历史回合不能再次执行");
        check(auto.Reserve(expected,"b9c7",1,1)==null,"我方落子后等待对方，不能代走另一方");

        auto.Enable(true);first=auto.Reserve(start,"h2e2",0,1)!;auto.InputStarting(first.Id,now);
        check(auto.Observe(expected.Apply("b9c7"),now)==AutomaticObservation.ConfirmedWithReply&&auto.ConfirmedMoves==1,"跳过我方落子中间帧时可确认落子加对方快速回棋");
        auto.Enable(true);first=auto.Reserve(start,"h2e2",0,1)!;auto.InputStarting(first.Id,now);
        check(auto.Observe(start.Apply("b0c2"),now)==AutomaticObservation.Paused&&!auto.Enabled,"棋盘实际变化与所发走法不符时立即暂停");
        auto.Enable(true);first=auto.Reserve(start,"h2e2",0,1)!;auto.InputStarting(first.Id,now);
        check(auto.CheckTimeout(now.AddSeconds(9))&&auto.Enabled&&auto.Pending!=null&&auto.ConfirmationDelayed,"超过八秒未识别到结果时保留待确认动作，不重复落子");
        check(auto.Reserve(start,"h2e2",0,1)==null&&auto.Observe(expected,now.AddSeconds(20))==AutomaticObservation.Confirmed&&!auto.ConfirmationDelayed,"长时间遮挡后恢复识别仍可确认原动作，无需重新开启");
        auto.Enable(true);first=auto.Reserve(start,"h2e2",0,1)!;long old=auto.Generation;auto.Stop("F8");
        check(!auto.Enabled&&auto.Pending==null&&auto.Generation!=old&&!auto.InputStarting(first.Id,now),"停止键使排队动作和旧会话动作失效");
        auto.Enable(true);first=auto.Reserve(start,"h2e2",0,1)!;auto.ReleaseUnsent(first.Id);
        check(auto.Enabled&&auto.Pending==null&&auto.Reserve(start,"h2e2",0,1)!=null,"未发送的预检取消不计为已落子");
        auto.Enable(true);first=auto.Reserve(start,"h2e2",0,1)!;auto.InputStarting(first.Id,now);auto.ReleaseUnsent(first.Id);
        check(auto.Pending!=null,"已开始发送的动作不能当作未发送而清除");
        auto.Observe(expected.Apply("b9c7"),now);
        check(auto.Reserve(start,"h2e2",4,1)!=null,"同一棋盘合法重复但历史步数不同，允许新回合落子");
        auto.Enable(false);first=auto.Reserve(expected,"b9c7",1,1)!;
        check(first!=null&&auto.InputStarting(first.Id,now)&&auto.Observe(expected.Apply("b9c7"),now)==AutomaticObservation.Confirmed,"执黑自动模式能正确安排并确认黑方落子");

        using var image=new Bitmap(1000,1100);
        using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.BurlyWood);
        using var visual=new VisualBoardSnapshot(image,grid,start,true);
        check(visual.Matches(image,grid,start,"h2e2",true)&&!visual.Matches(image,grid,start,"h2e2",false),"落子前像素复核允许原图并拒绝意外棋盘翻转");
        using var overlay=(Bitmap)image.Clone();
        using(var graphics=Graphics.FromImage(overlay))graphics.FillRectangle(Brushes.Black,750,750,100,100);
        check(!visual.Matches(overlay,grid,start,"h2e2",true),"源棋子被同窗口弹窗遮挡时像素复核拒绝点击");
        using var copied=visual.Clone();
        check(copied.Matches(image,grid,start,"h2e2",true),"动作持有独立画面快照，不受采集预览替换影响");
        int callbacks=0;bool canceled=false;
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        try{WindowsMoveInput.ExecuteAsync(new CaptureTarget(),IntPtr.Zero,_=>{callbacks++;return Task.FromResult(plan);},()=>callbacks++,cancel.Token).GetAwaiter().GetResult();}
        catch(OperationCanceledException){canceled=true;}
        check(canceled&&callbacks==0,"已停止的原生输入任务在预检和鼠标输入之前退出");
        var recovery=new AutomaticRecovery();
        recovery.Hold("漏识别",now);
        check(!recovery.ObserveReliable(start.ToFen(),now.AddMilliseconds(700))&&!recovery.ObserveReliable(start.ToFen(),now.AddMilliseconds(900))&&recovery.Waiting,"识别恢复一两帧时仍等待，避免立即重复预检");
        check(recovery.ObserveReliable(start.ToFen(),now.AddMilliseconds(1100))&&!recovery.Waiting,"连续三次合法稳定观察后自动恢复");
        recovery.Hold("遮挡",now);recovery.ObserveReliable("a",now.AddSeconds(10));recovery.ObserveReliable("a",now.AddSeconds(11));
        check(!recovery.ObserveReliable("b",now.AddSeconds(12))&&recovery.Waiting,"恢复过程中棋局再变动时重新累计可靠帧");
        recovery.Hold("长时间遮挡",now.AddMinutes(2));
        check(recovery.Waiting&&!recovery.ObserveReliable("b",now.AddMinutes(2)),"持续遮挡只等待，不因计时直接放行");
        InterruptionReplays.Run(check);
    }

    public static async Task ReplayAsync(Bitmap source,Detection detected,Recognizer recognizer,EngineClient engine,Action<bool,string> check)
    {
        var before=detected.Position!;var grid=detected.Grid!.Value;
        using var visual=new VisualBoardSnapshot(source,grid,before,detected.RedAtBottom);
        check(visual.Matches(source,grid,before,"b0c2",detected.RedAtBottom),"真实 QQ H5 样本可通过落子前图像复核");
        var session=new AutoPlaySession();session.Enable(true);
        var action=session.Reserve(before,"b0c2",0,detected.Confidence)!;
        var plan=MoveClickPlan.Create(before,action.Move,grid,detected.RedAtBottom,new Rectangle(0,0,source.Width,source.Height));
        Point Cell(Point p)=>new((int)Math.Round((p.X-grid.Left)*8/grid.Width),(int)Math.Round((p.Y-grid.Top)*9/grid.Height));
        var from=Cell(plan.From);var to=Cell(plan.To);
        string clickMove=$"{(char)('a'+from.X)}{9-from.Y}{(char)('a'+to.X)}{9-to.Y}";
        check(clickMove==action.Move,"识别网格生成的点击坐标反算后准确落在目标交叉点");
        session.InputStarting(action.Id,DateTimeOffset.UtcNow);
        using var ownFrame=ImageReplay.Move(source,grid,clickMove);
        using var replyFrame=ImageReplay.Move(ownFrame,grid,"a6a5");
        var seen=recognizer.Detect(replyFrame,grid,true);
        var tracker=new PositionTracker(true);
        for(int i=0;i<3;i++)tracker.Observe(before);
        TrackingResult result=null!;for(int i=0;i<3;i++)result=tracker.Observe(seen.Position!);
        check(result.State==TrackingState.Recovered&&session.Observe(tracker.Current!,DateTimeOffset.UtcNow)==AutomaticObservation.ConfirmedWithReply,"点击坐标 → 棋子像素回放 → 真实识别 → 快回棋补步 → 自动落子确认");
        var advice=await engine.AnalyzeAsync(tracker.Current!,350,CancellationToken.None,tracker.RootFen,tracker.History);
        check(session.Reserve(tracker.Current!,advice.BestMove,tracker.History.Count,seen.Confidence)!=null,"快回棋确认后真实皮卡鱼建议可安排下一回合动作");
    }
}
