using ChessCoach;
using System.Drawing;

if(args.Contains("--capture-smoke")){ScreenCaptureChecks.Run();return;}
if(args.Contains("--render-ui")){UiRenderChecks.Run(args[Array.IndexOf(args,"--render-ui")+1]);return;}
if(args.Contains("--probe"))
{
    using var source=new Bitmap(args[Array.IndexOf(args,"--probe")+1]);
    var models=Path.Combine(AppContext.BaseDirectory,"assets","models");
    foreach(var name in new[]{"vin-accurate.onnx","vin-rotated.onnx"})
    {
        using var scorer=new Yolov5Net.Scorer.YoloScorer<VinXiangQi.YoloXiangQiModel>(Path.Combine(models,name));
        foreach(var p in scorer.Predict(source))Console.WriteLine($"{name} {p.Label.Name} {p.Score:F3} {p.Rectangle}");
        using var recognizer=new Recognizer(Path.Combine(models,name));
        var inferred=recognizer.Detect(source,null,true);
        Console.WriteLine($"AUTO {name}: {inferred.Message}: {inferred.Position?.ToFen()}");
    }
    return;
}

int passed=0;
var results=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed++;results.Add(name);Console.WriteLine("PASS "+name);}
var start=Position.ParseFen(Position.StartFen);
Check(start.ToFen()==Position.StartFen,"FEN 完整往返");
Check(start.Validate(out _),"标准开局合法");
Check(start.IsLegal("h2e2"),"炮二平五合法");
Check(start.Describe("h2e2")=="炮二平五","中文走法映射");
Check(!start.IsLegal("a0a9"),"车不能穿子");
Check(!start.IsLegal("e0e2"),"帅不能走两格");
Check(!start.IsLegal("a3b3"),"未过河兵不能横走");
Check(start.IsLegal("b0c2"),"马走日");
Check(!start.IsLegal("b0d1"),"蹩马腿");
Check(!start.IsLegal("b7e7"),"不能替对方行棋");
Check(!start.IsLegal("j0a0")&&!start.IsLegal("a0a0"),"无效坐标拒绝");
var cannon=start.Apply("h2e2");
Check(!cannon.RedToMove&&cannon[4,7]=='C'&&cannon[7,7]=='.',"落子与轮次切换");
Check(start.TryTransition(cannon,out var move)&&move=="h2e2","图像局面还原合法一步");
Check(!start.TryTransition(cannon.Apply("b9c7"),out _),"多步跳转不自动接受");
Check(!Position.ParseFen("4k4/9/9/9/9/9/9/9/9/4K4 w - - 0 1").Validate(out _),"将帅照面拒绝");
var check=Position.ParseFen("4k4/9/9/9/4p4/9/9/4p4/4r4/4K4 w - - 0 1");
Check(check.InCheck(true)&&check.IsLegal("e0d0")&&!check.IsLegal("e0e1"),"将军、避将和不能送将");
var noKings=Position.ParseFen("9/9/9/9/9/9/9/9/9/9 w - - 0 1");
Check(!noKings.Validate(out _),"漏识别将帅拒绝");
var tracker=new PositionTracker(true);
Check(tracker.Observe(start).State==TrackingState.Warming&&tracker.Current==null,"首帧不算棋");
Check(tracker.Observe(start).State==TrackingState.Warming,"第二帧不算棋");
Check(tracker.Observe(start).State==TrackingState.Accepted,"三帧稳定才接受");
tracker.Observe(cannon);tracker.Invalidate();
Check(tracker.Observe(cannon).State==TrackingState.Warming,"失败帧清空稳定计数");
tracker.Observe(cannon);
Check(tracker.Observe(cannon).State==TrackingState.Accepted&&!tracker.Current!.RedToMove&&tracker.History.Single()=="h2e2","跟踪自动切换轮次并保留历史");
var jumped=cannon.Apply("b9c7").Apply("b0c2");
tracker.Observe(jumped);tracker.Observe(jumped);
var fastReply=tracker.Observe(jumped);
Check(fastReply.State==TrackingState.Recovered&&tracker.Current!.Key==jumped.Key&&tracker.History.SequenceEqual(new[]{"h2e2","b9c7","b0c2"}),"快速回棋跳过中间画面时补齐两步并保留完整历史");
Check(tracker.RootFen==Position.StartFen,"引擎历史使用原始根局面");
TrackingResult Stable(PositionTracker t,Position p){t.Observe(p);t.Observe(p);return t.Observe(p);}
var pairTracker=new PositionTracker(true);Stable(pairTracker,start);
var pairEnd=start.Apply("h2e2").Apply("b9c7");
var pairResult=Stable(pairTracker,pairEnd);
Check(pairResult.State==TrackingState.Recovered&&pairTracker.Current!.RedToMove&&pairResult.RecoveredMoves!.SequenceEqual(new[]{"h2e2","b9c7"}),"人机双方一来一回自动恢复正确轮次");
var undo=Stable(pairTracker,cannon);
Check(undo.State==TrackingState.Rewound&&pairTracker.History.SequenceEqual(new[]{"h2e2"})&&!pairTracker.Current!.RedToMove,"已记录的单步悔棋自动回退轮次和历史");
var wrongTurn=new PositionTracker(false);Stable(wrongTurn,start);
Check(Stable(wrongTurn,cannon).State==TrackingState.TurnCorrected&&wrongTurn.RootFen==Position.StartFen&&!wrongTurn.Current!.RedToMove,"首次轮次设错时由实际单步落子校正");
var tripleTracker=new PositionTracker(true);Stable(tripleTracker,start);
var triple=start.Apply("b0c2").Apply("c6c5").Apply("c2e1");
Check(Stable(tripleTracker,triple).State==TrackingState.Recovered&&tripleTracker.History.SequenceEqual(new[]{"b0c2","c6c5","c2e1"}),"三步漏采仅在走法顺序可唯一确定时补齐");
var ambiguousTracker=new PositionTracker(true);Stable(ambiguousTracker,start);
var ambiguous=start.Apply("h2e2").Apply("b9c7").Apply("b0c2");
Check(Stable(ambiguousTracker,ambiguous).State==TrackingState.Rejected&&ambiguousTracker.History.Count==0&&ambiguousTracker.Current!.Key==start.Key,"多种三步顺序得到同一棋盘时不猜测历史");
var badCells=(char[])pairEnd.Cells.Clone();badCells[4*9+4]='R';
var badFrame=new Position(badCells,true);
var noiseTracker=new PositionTracker(true);Stable(noiseTracker,start);Stable(noiseTracker,pairEnd);
Check(Stable(noiseTracker,badFrame).State==TrackingState.Rejected&&noiseTracker.Current!.Key==pairEnd.Key,"持续错识别不覆盖已确认棋局");
Check(Stable(noiseTracker,pairEnd).State==TrackingState.Unchanged&&noiseTracker.History.Count==2,"识别波动恢复后自动继续，无需手动重连");
var repetitionTracker=new PositionTracker(true);Stable(repetitionTracker,start);Stable(repetitionTracker,pairEnd);
Check(Stable(repetitionTracker,start).State==TrackingState.Recovered&&repetitionTracker.History.Count==4,"双方合法回走时保留重复历史，避免误当悔棋清除");
Check(start.LegalMoves().Distinct().Count()==start.LegalMoves().Count()&&start.LegalMoves().All(start.IsLegal),"补步搜索生成的走法均合法且不重复");
var info=EngineClient.ParseInfo("info depth 17 multipv 2 score cp -46 nodes 999 pv h2e2 b9c7");
Check(info is {Rank:2,Depth:17,Score:-46,IsMate:false}&&info.Moves[0]=="h2e2","UCI 候选与分数解析");
Check(EngineClient.ParseInfo("info string arbitrary message")==null&&EngineClient.ParseInfo("info depth 9 score cp 42 lowerbound pv h2e2")==null,"忽略非走法信息与边界分");
Check(EngineClient.ParseInfo("info depth 3 score mate -2 pv e0d0") is {IsMate:true,Score:-2},"终局分保留类型");
var sparseCenters=new[]{new PointF(300,250),new PointF(500,450),new PointF(700,650),new PointF(500,850)};
var sparseGrid=GridCalibration.Fit(new RectangleF(100,50,800,900),sparseCenters);
Check(Math.Abs(sparseGrid.Width-800)<1&&Math.Abs(sparseGrid.Height-900)<1&&Math.Abs(sparseGrid.Left-100)<1,"残局缺少边线棋子时不缩小九路十行网格");
var beside=WindowPlacement.Beside(new Rectangle(0,0,1920,1080),new Rectangle(100,80,700,900),new Size(430,850));
Check(beside.HasValue&&beside.Value.Left>=816&&!beside.Value.IntersectsWith(new Rectangle(100,80,700,900)),"提示窗口放在棋盘右侧且不遮挡");
var negativeMonitor=WindowPlacement.Beside(new Rectangle(-1920,0,1920,1080),new Rectangle(-900,0,900,1080),new Size(430,850));
Check(negativeMonitor.HasValue&&negativeMonitor.Value.Right<=-916,"副屏负坐标时提示窗口正确放在左侧");
Check(WindowPlacement.Beside(new Rectangle(0,0,1920,1080),new Rectangle(0,0,1920,1080),new Size(430,850))==null,"没有空余区域时不伪造可用窗口位置");
AutomaticChecks.Run(Check);

if(args.Contains("--integration"))
{
    var assets=Path.Combine(AppContext.BaseDirectory,"assets");
    var engine=new EngineClient(Path.Combine(assets,"engine","Pikafish-Windows-x86-64-universal.exe"));
    var analysis=await engine.AnalyzeAsync(start,700,CancellationToken.None);
    Check(start.IsLegal(analysis.BestMove),"真实皮卡鱼标准开局返回合法走法");
    Check(analysis.Candidates.Length>0&&analysis.Candidates.All(c=>start.IsLegal(c.Moves[0])),"真实 MultiPV 候选全部合法");
    Check(analysis.Candidates.Select(c=>c.Moves[0]).Distinct().Count()==analysis.Candidates.Length,"超时截断 MultiPV 后候选走法不重复");
    Console.WriteLine("ENGINE "+analysis.BestMove+" / "+start.Describe(analysis.BestMove));
    using(var cancel=new CancellationTokenSource(70))
    {
        bool canceled=false;
        try{await engine.AnalyzeAsync(start,5000,cancel.Token);}catch(OperationCanceledException){canceled=true;}
        Check(canceled,"过期分析可以取消并终止引擎");
    }
    var afterCancel=await engine.AnalyzeAsync(cannon,400,CancellationToken.None,Position.StartFen,["h2e2"]);
    Check(cannon.IsLegal(afterCancel.BestMove),"取消后新局面可正常分析，含历史");

    using var image=new Bitmap(Path.Combine(assets,"fixtures","vin-screen.png"));
    using var recognizer=new Recognizer(Path.Combine(assets,"models","vin-accurate.onnx"));
    var grid=new RectangleF(61,63,232,252);
    var detected=recognizer.Detect(image,grid,true);
    Console.WriteLine("VISION "+detected.Message+" / "+detected.Milliseconds+" ms / "+detected.Position?.ToFen());
    Check(detected.Success,"真实 Vin 模型识别上游截图");
    const string fixtureFen="2bakabnr/7c1/2n4c1/p1p1p1p1p/1r7/2P6/P3P1P1P/1CN1C1N2/9/1RBAKAB1R w - - 0 1";
    Check(detected.Position!.ToFen()==fixtureFen,"棋盘 90 个交叉点与人工核对 FEN 完全一致");
    Check(detected.RedAtBottom&&detected.PieceCount==32,"朝向与棋子总数正确");
    var position=detected.Position;
    Check(position.Validate(out _),"识别局面通过结构与轮次校验");
    var advice=await engine.AnalyzeAsync(position,700,CancellationToken.None);
    Check(position.IsLegal(advice.BestMove),"截图 → Vin 识别 → 皮卡鱼 → 合法建议全链路");
    using var rotated=(Bitmap)image.Clone();rotated.RotateFlip(RotateFlipType.Rotate180FlipNone);
    using var rotationRecognizer=new Recognizer(Path.Combine(assets,"models","vin-rotated.onnx"));
    var flipped=rotationRecognizer.Detect(rotated,new RectangleF(image.Width-grid.Right,image.Height-grid.Bottom,grid.Width,grid.Height),true);
    Console.WriteLine("ROTATED "+flipped.Message+" / "+flipped.Position?.ToFen());
    Check(flipped.Success&&!flipped.RedAtBottom&&flipped.Position!.Key==position.Key,"红方在上时旋转回标准坐标");
    using var blank=new Bitmap(360,400);
    var missing=recognizer.Detect(blank,new RectangleF(30,30,280,315),true);
    Check(!missing.Success,"空白或遮挡画面不产生棋局建议");

    using var qq=new Bitmap(Path.Combine(AppContext.BaseDirectory,"fixtures","qq-h5-board.png"));
    var qqResult=recognizer.Detect(qq,new RectangleF(68,68,960,1064),true);
    const string qqFen="r1bakabnr/9/1cn4c1/p1p1p1p1p/9/9/P1P1P1P1P/4C2C1/9/RNBAKABNR w - - 0 1";
    Console.WriteLine("QQ H5 "+qqResult.Message+" / "+qqResult.Position?.ToFen());
    Check(qqResult.Position?.ToFen()==qqFen&&qqResult.PieceCount==32,"实际 QQ H5 画面包含外框和按钮时正确识别全部 32 子");
    Check(qqResult.Grid.HasValue&&Math.Abs(qqResult.Grid.Value.Left-136)<10&&Math.Abs(qqResult.Grid.Value.Width-828)<12,"自动网格覆盖手工框选的错误边界");
    var fullFrame=recognizer.Detect(qq,new RectangleF(0,0,qq.Width,qq.Height),true);
    Check(fullFrame.Position?.ToFen()==qqFen,"把整个棋盘外框当作框选区域仍能自动校准");
    var qqAdvice=await engine.AnalyzeAsync(qqResult.Position!,700,CancellationToken.None);
    Check(qqResult.Position!.IsLegal(qqAdvice.BestMove),"实际 QQ H5 截图直接得到合法下一步建议");
    Console.WriteLine("QQ ADVICE "+qqResult.Position.Describe(qqAdvice.BestMove));
    using var playerFrame=ImageReplay.Move(qq,qqResult.Grid!.Value,"b0c2");
    var playerSeen=recognizer.Detect(playerFrame,qqResult.Grid,true);
    var expectedPlayer=qqResult.Position.Apply("b0c2");
    Check(playerSeen.Position?.Key==expectedPlayer.Key,"QQ 棋子像素回放可识别我方落子画面");
    using var replyFrame=ImageReplay.Move(playerFrame,qqResult.Grid.Value,"a6a5");
    var replySeen=recognizer.Detect(replyFrame,qqResult.Grid,true);
    var expectedReply=expectedPlayer.Apply("a6a5");
    Check(replySeen.Position?.Key==expectedReply.Key,"QQ 棋子像素回放可识别快速回棋后的画面");
    var visualTracker=new PositionTracker(true);Stable(visualTracker,qqResult.Position);
    var visualRecovery=Stable(visualTracker,replySeen.Position!);
    Check(visualRecovery.State==TrackingState.Recovered&&visualTracker.History.SequenceEqual(new[]{"b0c2","a6a5"}),"真实模型输出跳过中间帧后自动补齐两步");
    var recoveredAdvice=await engine.AnalyzeAsync(visualTracker.Current!,500,CancellationToken.None,visualTracker.RootFen,visualTracker.History);
    Check(visualTracker.Current!.IsLegal(recoveredAdvice.BestMove),"QQ 像素回放 → 模型 → 补步 → 皮卡鱼下一步全链路");
    await AutomaticChecks.ReplayAsync(qq,qqResult,recognizer,engine,Check);
}
Console.WriteLine($"ALL PASSED: {passed}");
if(args.Contains("--report"))
{
    int index=Array.IndexOf(args,"--report");
    File.WriteAllText(args[index+1],System.Text.Json.JsonSerializer.Serialize(new{passed,checks=results},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
}
