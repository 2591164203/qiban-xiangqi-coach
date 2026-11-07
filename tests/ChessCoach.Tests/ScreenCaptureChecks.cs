using ChessCoach;
using System.Drawing;
using System.Windows.Forms;

internal static class ScreenCaptureChecks
{
    public static void Run()
    {
        Exception? failure=null;
        var checks=new List<string>();
        var thread=new Thread(()=>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            string assets=Path.Combine(AppContext.BaseDirectory,"assets");
            using var fixture=new Bitmap(Path.Combine(assets,"fixtures","vin-screen.png"));
            using var blank=new Bitmap(fixture.Width,fixture.Height);
            using var form=new Form{Text="棋伴自动读屏测试 · 固定样本",StartPosition=FormStartPosition.Manual,Location=new Point(40,40),ClientSize=new Size(694,716),TopMost=true};
            var picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.StretchImage,Image=fixture};
            form.Controls.Add(picture);
            void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
            form.Shown+=async(_,_)=>
            {
                try
                {
                    await Task.Delay(450);
                    var origin=picture.PointToScreen(Point.Empty);
                    float sx=picture.ClientSize.Width/(float)fixture.Width,sy=picture.ClientSize.Height/(float)fixture.Height;
                    var grid=new Rectangle(origin.X+(int)(61*sx),origin.Y+(int)(63*sy),(int)(232*sx),(int)(252*sy));
                    var target=new CaptureTarget{Handle=form.Handle,WindowAtSelection=Native.WindowRect(form.Handle),GridAtSelection=grid};
                    using var recognizer=new Recognizer(Path.Combine(assets,"models","vin-accurate.onnx"));
                    var tracker=new PositionTracker(true);
                    const string expected="2bakabnr/7c1/2n4c1/p1p1p1p1p/1r7/2P6/P3P1P1P/1CN1C1N2/9/1RBAKAB1R w - - 0 1";
                    for(int i=0;i<3;i++)
                    {
                        var captured=target.Capture();
                        using(captured.Image)
                        {
                            var detected=await Task.Run(()=>recognizer.Detect(captured.Image,captured.Grid,true));
                            Check(detected.Position?.ToFen()==expected,"真实屏幕像素识别正确 / 帧 "+(i+1));
                            tracker.Observe(detected.Position!);
                        }
                        await Task.Delay(150);
                    }
                    Check(tracker.Current!=null,"屏幕连续三帧稳定后自动接受");
                    var engine=new EngineClient(Path.Combine(assets,"engine","Pikafish-Windows-x86-64-universal.exe"));
                    var advice=await engine.AnalyzeAsync(tracker.Current!,500,CancellationToken.None);
                    Check(tracker.Current!.IsLegal(advice.BestMove),"实际读屏 → 识别 → 稳定跟踪 → 皮卡鱼提示");
                    form.Location=new Point(70,65);await Task.Delay(200);
                    var moved=target.Capture();using(moved.Image)
                    {
                        var detected=await Task.Run(()=>recognizer.Detect(moved.Image,moved.Grid,true));
                        Check(detected.Position?.ToFen()==expected,"窗口移动后框选区域自动跟随");
                    }
                    picture.Image=blank;picture.Refresh();await Task.Delay(150);
                    var empty=target.Capture();using(empty.Image)
                    {
                        var detected=await Task.Run(()=>recognizer.Detect(empty.Image,empty.Grid,true));
                        Check(!detected.Success,"屏幕变为空白时拒绝局面");
                    }
                    form.Width+=20;bool resized=false;
                    try{using var ignored=target.Capture().Image;}catch(InvalidOperationException){resized=true;}
                    Check(resized,"窗口缩放后要求重新选择，避免坐标错位");
                }
                catch(Exception ex){failure=ex;}
                finally{form.Close();}
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)throw new Exception("Screen capture smoke failed",failure);
        Console.WriteLine("SCREEN CAPTURE PASSED: "+checks.Count);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"screen-capture-results.json"),System.Text.Json.JsonSerializer.Serialize(new{passed=checks.Count,checks},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
