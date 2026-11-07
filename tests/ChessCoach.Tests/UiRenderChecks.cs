using ChessCoach;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

internal static class UiRenderChecks
{
    // Render only this application's controls; never capture or activate the desktop.
    public static void Run(string output)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
                Application.EnableVisualStyles();Directory.CreateDirectory(output);
                using var form=new MainForm{IsRenderingOffscreen=true,ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)};
                form.Show();form.PerformLayout();
                if(form.AutomaticEnabledForVerification||!form.AutomaticControlsVisibleForVerification)throw new Exception("Automatic controls must be visible and default off");
                using var source=new Bitmap(Path.Combine(AppContext.BaseDirectory,"assets","fixtures","vin-screen.png"));
                using var recognizer=new Recognizer(Path.Combine(AppContext.BaseDirectory,"assets","models","vin-accurate.onnx"));
                var detection=recognizer.Detect(source,null,true);
                form.SetPreviewForVerification((Bitmap)source.Clone());
                for(int i=0;i<3;i++)form.ObserveForVerification(detection);
                Console.WriteLine("UI initial position submitted");
                void WaitForAnalysis()
                {
                    var deadline=DateTime.UtcNow.AddSeconds(22);
                    while(form.AnalysisInFlight&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(25);}
                    Application.DoEvents();
                    if(form.AnalysisInFlight)throw new Exception("UI analysis did not finish: "+string.Join(" / ",form.CandidateTitles));
                    if(form.CandidateTitles.Any(t=>t.Contains("等待候选")))throw new Exception("UI left unfinished candidate cards: "+string.Join(" / ",form.CandidateTitles));
                    var problems=form.LayoutProblemsForVerification();if(problems.Length>0)throw new Exception(string.Join(" / ",problems));
                }
                WaitForAnalysis();
                Console.WriteLine("UI initial analysis complete");
                string move=detection.Position!.LegalMoves().First();
                var next=detection.Position.Apply(move);
                var nextDetection=detection with{Position=next};
                for(int i=0;i<3;i++)form.ObserveForVerification(nextDetection);
                Console.WriteLine("UI second position submitted");
                WaitForAnalysis();
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"full.png"),ImageFormat.Png);}
                void ScaleForCheck(float scale)
                {
                    var controls=new List<(Control Control,Font Font)>();
                    void Collect(Control control){controls.Add((control,control.Font));foreach(Control child in control.Controls)Collect(child);}
                    Collect(form);form.AutoScaleMode=AutoScaleMode.None;form.Scale(new SizeF(scale,scale));
                    foreach(var item in controls)item.Control.Font=new Font(item.Font.FontFamily,item.Font.Size*scale,item.Font.Style);
                    form.RemeasureForVerification();form.PerformLayout();Application.DoEvents();
                    var problems=form.LayoutProblemsForVerification();if(problems.Length>0)throw new Exception("Scaled layout: "+string.Join(" / ",problems));
                }
                ScaleForCheck(1.5f);
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"full-150.png"),ImageFormat.Png);}
                ScaleForCheck(1/1.5f);
                form.CompactForVerification();form.PerformLayout();Application.DoEvents();
                if(!form.AutomaticControlsVisibleForVerification)throw new Exception("Compact layout hid automatic controls");
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"compact.png"),ImageFormat.Png);}
                if(form.SessionTracker.Current?.Key!=next.Key||!form.SessionTracker.History.SequenceEqual(new[]{move}))throw new Exception("UI turn display reset the confirmed tracker");
                form.ArmAutomaticForVerification();Application.DoEvents();
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"compact-active.png"),ImageFormat.Png);}
                form.StopHotkeyForVerification();
                if(form.AutomaticEnabledForVerification)throw new Exception("F8 message did not stop automatic mode");
                ScaleForCheck(1.5f);form.MinimumSize=new Size(400,600);form.Height=1000;form.RemeasureForVerification();Application.DoEvents();
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"compact-150.png"),ImageFormat.Png);}
                form.Close();
                foreach(var replay in InterruptionReplays.Cases)
                {
                    using var recoveryForm=new MainForm{IsRenderingOffscreen=true,ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)};
                    recoveryForm.Show();recoveryForm.SetTurnForVerification(Position.ParseFen(replay.Before).RedToMove);
                    Detection Frame(string fen)=>new(Position.ParseFen(fen),true,"replay",1,0,24){Grid=new RectangleF(10,10,800,900)};
                    for(int i=0;i<3;i++)recoveryForm.ObserveForVerification(Frame(replay.Before));recoveryForm.ArmAutomaticForVerification();
                    for(int i=0;i<3;i++)recoveryForm.ObserveForVerification(Frame(replay.Noise));
                    if(!recoveryForm.AutomaticEnabledForVerification||!recoveryForm.AutomaticWaitingForVerification)throw new Exception("Transient rejection disabled automatic mode");
                    for(int i=0;i<6;i++){Thread.Sleep(220);recoveryForm.ObserveForVerification(Frame(replay.Recovered));Application.DoEvents();}
                    if(!recoveryForm.AutomaticEnabledForVerification||recoveryForm.AutomaticWaitingForVerification||recoveryForm.SessionTracker.Current?.ToFen()!=replay.Recovered)throw new Exception("UI did not resume after recovery");
                    recoveryForm.StopHotkeyForVerification();
                    for(int i=0;i<4;i++)recoveryForm.ObserveForVerification(Frame(replay.Recovered));
                    if(recoveryForm.AutomaticEnabledForVerification)throw new Exception("User stop was overridden by recovery");
                    recoveryForm.Close();
                }
                Console.WriteLine("PASS hidden UI rendering and changing displayed turn preserves move history");
                Console.WriteLine("PASS automatic controls in both layouts, default off, F8 stop message routing");
                Console.WriteLine("PASS 150% layout, two real interruption replays resume automatically, F8 remains stopped");
            }
            catch(Exception ex){failure=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)throw new Exception("UI render check failed",failure);
    }
}
