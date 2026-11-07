using System.Diagnostics;
using System.Text.Json;

namespace ChessCoach;

public sealed partial class MainForm:Form
{
    private static readonly Color Ink=Color.FromArgb(30,58,57),Accent=Color.FromArgb(20,119,94),Muted=Color.FromArgb(109,124,120);
    private readonly BoardView _board=new(){Dock=DockStyle.Fill};
    private readonly PictureBox _preview=new(){Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(32,42,43)};
    private readonly Label _status=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(16,0,0,0),Text="就绪 · 请先打开网页版棋盘"};
    private readonly Label _mainMove=new(){Text="等待棋局",AutoSize=false,Height=54,Dock=DockStyle.Top,Font=new Font("Microsoft YaHei UI",25,FontStyle.Bold),ForeColor=Ink};
    private readonly Label _evaluation=new(){Text="框选一次，后续自动识别与分析",Height=34,Dock=DockStyle.Top,ForeColor=Muted};
    private readonly Label _detail=new(){Text="尚未开始读屏",Height=64,Dock=DockStyle.Top,ForeColor=Muted};
    private readonly CandidateCard[] _candidateLabels=new CandidateCard[3];
    private readonly Label _targetInfo=new(){Text="未选择棋盘",Dock=DockStyle.Bottom,Height=48,ForeColor=Muted};
    private readonly Button _start=Button("开始读屏",true),_select=Button("选择棋盘"),_sync=Button("重新同步");
    private readonly ComboBox _turn=Combo(["轮到红方走","轮到黑方走"],132),_orientation=Combo(["自动判断朝向","红方在下","黑方在下"],140),_model=Combo(["精准识别","快速识别","旋转兼容"],112);
    private readonly NumericUpDown _think=new(){Minimum=.3m,Maximum=5,Increment=.5m,DecimalPlaces=1,Value=1,Width=62};
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=250};
    private readonly SemaphoreSlim _recognitionGate=new(1,1);
    private readonly EngineClient _engine=new(Path.Combine(AppContext.BaseDirectory,"assets","engine","Pikafish-Windows-x86-64-universal.exe"));
    private CaptureTarget? _target;
    private PositionTracker _tracker=new(true);
    private Recognizer? _recognizer;
    private string? _loadedModel,_lastDetectionKey,_analysisKey;
    private CancellationTokenSource? _analysisCancellation;
    private bool _running,_busy,_closing;
    private int _generation,_analysisRevision;
    private Detection? _lastDetection;
    private readonly bool _demo;
    private TableLayoutPanel _rootLayout=null!,_bodyLayout=null!;
    private FlowLayoutPanel _actionBar=null!,_optionBar=null!;
    private TabControl _boardTabs=null!;
    private Panel _header=null!;
    private Control[] _fullOnly=[];
    private Button _compactButton=null!;
    private bool _compact;
    private Rectangle? _captureBounds;
    private DateTimeOffset _lastPlacementAttempt;
    private static string? _lastLoggedError;
    private static DateTime _lastLogTime;
    private bool _updatingTurn,_adviceActive;
    private TrackingResult? _lastTracking;
    private readonly TrackingJournal _journal=new();
    internal PositionTracker SessionTracker=>_tracker;
    internal void SetPreviewForVerification(Bitmap image)=>SetPreview(image);
    internal void ObserveForVerification(Detection detection)=>ProcessDetection(detection);
    internal void CompactForVerification()=>SetCompact(true);
    internal bool IsRenderingOffscreen {get;set;}
    protected override bool ShowWithoutActivation=>IsRenderingOffscreen||base.ShowWithoutActivation;
    internal bool AnalysisInFlight=>_analysisCancellation!=null;
    internal string[] CandidateTitles=>_candidateLabels.Select(c=>c.ContentTitle).ToArray();
    internal void SetTurnForVerification(bool red)=>_turn.SelectedIndex=red?0:1;
    internal void RemeasureForVerification()=>UpdateContentHeights();
    internal string[] LayoutProblemsForVerification()
    {
        var problems=new List<string>();
        foreach(var label in new[]{_mainMove,_evaluation,_detail})
            if(label.Height<label.Font.Height)problems.Add($"{label.Text}: height {label.Height} below font {label.Font.Height}");
        if(_mainMove.Bottom>_evaluation.Top||_evaluation.Bottom>_detail.Top)problems.Add("Advice labels overlap");
        if(_autoToggle.Width<TextRenderer.MeasureText(_autoToggle.Text,_autoToggle.Font).Width+8)problems.Add("Automatic button is clipped");
        if(_candidateLabels.Any(c=>!c.ContentFits))problems.Add("Candidate score is clipped");
        var brand=_header.Controls[1];
        if(brand.Visible&&brand.Controls.Count>=2&&brand.Controls[0].Bounds.IntersectsWith(brand.Controls[1].Bounds))problems.Add("Brand labels overlap");
        return problems.ToArray();
    }

    public MainForm(bool demo=false)
    {
        _demo=demo;Text="棋伴 · 象棋辅导 v0.5";
        BuildInterface();
        _select.Click+=async(_,_)=>await SelectRegionAsync();
        _start.Click+=async(_,_)=>{if(_running)StopMonitoring();else{if(_target==null){await SelectRegionAsync();return;}if(_captureBounds.HasValue)MoveBesideCapture(_captureBounds.Value);StartMonitoring();}};
        _sync.Click+=(_,_)=>{ResetTracking();SetStatus("已重置同步 · 请确认轮次；等待稳定棋盘");if(!_running&&_lastDetection?.Position!=null)AnalyzeStill(_lastDetection);};
        _compactButton.Click+=(_,_)=>{SetCompact(!_compact);if(_compact&&_captureBounds.HasValue)MoveBesideCapture(_captureBounds.Value);};
        _preview.Paint+=(_,e)=>DrawCalibratedGrid(e.Graphics);
        foreach(var combo in new[]{_turn,_orientation,_model})combo.SelectedIndexChanged+=(_,_)=>{if(_updatingTurn)return;ResetTracking();SetStatus("设置已更新，正在重新确认局面");};
        _think.ValueChanged+=(_,_)=>ClearAdvice();
        _timer.Tick+=async(_,_)=>await TickAsync();
        Shown+=async(_,_)=>{if(!IsRenderingOffscreen){var area=Screen.FromControl(this).WorkingArea;if(Width>area.Width||Height>area.Height)SetCompact(true);}if(_demo)await LoadDemoAsync();};
        FormClosing+=(_,_)=>{_closing=true;StopAutomatic("程序已关闭");_timer.Stop();_running=false;_generation++;ClearAdvice();};
        FormClosed+=async(_,_)=>{await _recognitionGate.WaitAsync();try{_recognizer?.Dispose();}finally{_recognitionGate.Release();}_preview.Image?.Dispose();_stableVisual?.Dispose();};
    }

    private static Button Button(string text,bool primary=false)=>new(){Text=text,AutoSize=false,Width=116,Height=36,Margin=new Padding(0,0,9,0),FlatStyle=FlatStyle.Flat,BackColor=primary?Accent:Color.White,ForeColor=primary?Color.White:Ink,FlatAppearance={BorderColor=primary?Accent:Color.FromArgb(211,221,215)}};
    private static ComboBox Combo(string[] items,int width){var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=width,Margin=new Padding(0,0,9,0)};c.Items.AddRange(items);c.SelectedIndex=0;return c;}
    private bool InitialRed=>_turn.SelectedIndex==0;
    private bool? Orientation=>_orientation.SelectedIndex switch{1=>true,2=>false,_=>null};
    private string ModelPath=>Path.Combine(AppContext.BaseDirectory,"assets","models",_model.SelectedIndex switch{1=>"vin-fast.onnx",2=>"vin-rotated.onnx",_=>"vin-accurate.onnx"});
    private void SetStatus(string message,bool warning=false){if(_closing)return;if(_status.Text!=message)_status.Text=message;_status.ForeColor=warning?Color.FromArgb(164,87,27):Muted;_tooltips.SetToolTip(_status,message);}
    private void SetPreview(Bitmap image){var previous=_preview.Image;_preview.Image=image;previous?.Dispose();}
    private void ClearAdvice()
    {
        _analysisRevision++;_analysisKey=null;_readyAnalysis=null;_readyFen=null;_analysisCancellation?.Cancel();
        _board.BestMove=null;_board.Invalidate();
        if(!_adviceActive)return;
        _adviceActive=false;_mainMove.Text="等待落稳";_evaluation.Text="画面稳定后自动分析";
        for(int i=0;i<3;i++)_candidateLabels[i].SetWaiting(i+1);
    }
    private void ResetTracking(){StopAutomatic("设置或局面已重置 · 自动关闭");_generation++;_tracker=new PositionTracker(InitialRed);_stableVisual?.Dispose();_stableVisual=null;ClearAdvice();}
    private void StartMonitoring()
    {
        ResetTracking();_running=true;_start.Text=_compact?"暂停":"暂停读屏";_timer.Start();_detail.Text="正在确认棋盘，快速回棋会自动补步";_badge.Text="●  读屏中";SetStatus("读屏已开始，等待棋局稳定");
    }
    private void StopMonitoring()
    {
        StopAutomatic("读屏已暂停 · 自动关闭");_running=false;_timer.Stop();_generation++;_start.Text=_compact?"继续":"开始读屏";ClearAdvice();_badge.Text="●  已暂停";SetStatus("读屏已暂停");
    }

    private async Task SelectRegionAsync()
    {
        StopMonitoring();_select.Enabled=false;bool selected=false;
        try
        {
            Hide();await Task.Delay(250);
            using var selector=new RegionSelector();
            if(selector.ShowDialog()==DialogResult.OK)
            {
                await Task.Delay(100);_target=CaptureTarget.FromSelection(selector.SelectedGrid);
                var frame=_target.Capture(verifyOcclusion:false);
                try
                {
                    var detection=await DetectAsync(frame.Image,frame.Grid,ModelPath,InitialRed,Orientation);
                    if(detection.Grid.HasValue)_target.ApplyCalibration(detection.Grid.Value,frame);
                    _captureBounds=frame.Bounds;SetPreview(frame.Image);
                    _lastDetection=detection;_lastDetectionKey=detection.Position?.Key;
                    _board.Position=detection.Position;_board.RedAtBottom=detection.RedAtBottom;_board.Invalidate();
                    _detail.Text=detection.Message;selected=true;
                }
                catch{frame.Image.Dispose();throw;}
                _targetInfo.Text=$"已选择棋盘 · 自动校准网格\n确认当前行棋方";
            }
        }
        catch(Exception ex){SetStatus(ex.Message,true);Log(ex);}
        finally{if(selected&&_captureBounds.HasValue)MoveBesideCapture(_captureBounds.Value);Show();_select.Enabled=true;if(selected)StartMonitoring();}
    }

    private void SetCompact(bool compact)
    {
        if(_compact==compact)return;
        _compact=compact;SuspendLayout();float scale=DeviceDpi/96f;
        _boardTabs.Visible=!compact;
        _bodyLayout.ColumnStyles[0].SizeType=compact?SizeType.Absolute:SizeType.Percent;
        _bodyLayout.ColumnStyles[0].Width=compact?0:56;
        _bodyLayout.ColumnStyles[1].Width=compact?100:44;
        foreach(var c in _fullOnly)c.Visible=!compact;
        foreach(Control c in _header.Controls)c.Visible=!compact||c==_header.Controls[0]||c==_badge.Parent;
        _actionBar.WrapContents=true;_optionBar.WrapContents=true;
        _select.Text=compact?"换棋盘":"连接棋盘";_select.Width=(int)((compact?96:140)*scale);
        _start.Text=compact?(_running?"暂停":"继续"):(_running?"暂停读屏":"开始读屏");_start.Width=(int)((compact?78:112)*scale);
        _compactButton.Text=compact?"展开":"浮窗模式";_compactButton.Width=(int)((compact?78:112)*scale);
        _moreButton.Text=compact?"···":"更多 ···";_moreButton.Width=(int)((compact?48:88)*scale);
        _turnCaption.Text=compact?"轮次":"当前行棋";_thinkCaption.Text=compact?"思考":"思考时间";_turn.Width=(int)((compact?132:144)*scale);
        _advancedPanel.Visible=!compact&&_showSettings;
        _metrics.Visible=!compact;
        var area=Screen.FromControl(this).WorkingArea;
        MinimumSize=new Size(Math.Min(area.Width,(int)((compact?390:900)*scale)),Math.Min(area.Height,(int)(600*scale)));
        Size=new Size(Math.Min(area.Width,(int)((compact?450:1160)*scale)),Math.Min(area.Height,(int)((compact?866:954)*scale)));
        ResumeLayout(true);UpdateContentHeights();
    }

    private void MoveBesideCapture(Rectangle capture)
    {
        if(WindowState!=FormWindowState.Normal)WindowState=FormWindowState.Normal;
        var area=Screen.FromRectangle(capture).WorkingArea;
        var placement=WindowPlacement.Beside(area,capture,Size);
        if(placement==null&&!_compact){SetCompact(true);placement=WindowPlacement.Beside(area,capture,Size);}
        if(placement.HasValue){WindowState=FormWindowState.Normal;Bounds=placement.Value;}
    }

    private void DrawCalibratedGrid(Graphics graphics)
    {
        if(_preview.Image==null||_lastDetection?.Grid is not RectangleF grid)return;
        float scale=Math.Min(_preview.ClientSize.Width/(float)_preview.Image.Width,_preview.ClientSize.Height/(float)_preview.Image.Height);
        float ox=(_preview.ClientSize.Width-_preview.Image.Width*scale)/2,oy=(_preview.ClientSize.Height-_preview.Image.Height*scale)/2;
        using var pen=new Pen(Color.FromArgb(160,50,225,156),1);
        for(int x=0;x<9;x++)graphics.DrawLine(pen,ox+(grid.Left+grid.Width*x/8)*scale,oy+grid.Top*scale,ox+(grid.Left+grid.Width*x/8)*scale,oy+grid.Bottom*scale);
        for(int y=0;y<10;y++)graphics.DrawLine(pen,ox+grid.Left*scale,oy+(grid.Top+grid.Height*y/9)*scale,ox+grid.Right*scale,oy+(grid.Top+grid.Height*y/9)*scale);
    }

    private void SaveDiagnostic()
    {
        if(_preview.Image==null){SetStatus("暂无采集画面，请先选择棋盘",true);return;}
        try
        {
            var directory=Path.Combine(AppContext.BaseDirectory,"diagnostics");Directory.CreateDirectory(directory);
            string name=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            _preview.Image.Save(name+".png",System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText(name+".json",JsonSerializer.Serialize(new{version="0.5",model=Path.GetFileName(ModelPath),turn=InitialRed?"red":"black",detection=_lastDetection?.Message,observedFen=_lastDetection?.Position?.ToFen(),fen=_tracker.Current?.ToFen(),history=_tracker.History,tracking=_lastTracking?.Message,grid=_lastDetection?.Grid,automatic=new{_automatic.Enabled,_automatic.PlayRed,_automatic.Message,pending=_automatic.Pending?.Move,confirmedMoves=_automatic.ConfirmedMoves,recovering=_recovery.Waiting,recoveryReason=_recovery.Reason,confirmationDelayed=_automatic.ConfirmationDelayed}},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
            SetStatus("诊断已保存："+name);
        }
        catch(Exception ex){SetStatus(ex.Message,true);Log(ex);}
    }

    private async Task<Detection> DetectAsync(Bitmap image,RectangleF? grid,string model,bool turn,bool? orientation)
    {
        return await Task.Run(async()=>
        {
            await _recognitionGate.WaitAsync();
            try
            {
                if(_loadedModel!=model){_recognizer?.Dispose();_loadedModel=null;_recognizer=new Recognizer(model);_loadedModel=model;}
                return _recognizer!.Detect(image,grid,turn,orientation);
            }
            finally{_recognitionGate.Release();}
        });
    }

    private async Task TickAsync()
    {
        if(_busy||!_running||_target==null||_closing)return;
        if(_automatic.CheckTimeout(DateTimeOffset.UtcNow)){_journal.RecordAutomatic("confirmation-delayed",_automatic,_tracker);RefreshAutomatic();}
        _busy=true;int generation=_generation;Bitmap? image=null;
        try
        {
            var frame=_target.Capture();image=frame.Image;
            var detection=await DetectAsync(image,frame.Grid,ModelPath,InitialRed,Orientation);
            if(_closing||generation!=_generation)return;
            if(detection.Grid.HasValue)_target.ApplyCalibration(detection.Grid.Value,frame);
            _captureBounds=frame.Bounds;SetPreview(image);image=null;ProcessDetection(detection);
        }
        catch(Exception ex)
        {
            if(ex is BoardOccludedException{ByCoach:true}&&!_closing&&generation==_generation&&(DateTimeOffset.UtcNow-_lastPlacementAttempt).TotalSeconds>2)
            {
                _lastPlacementAttempt=DateTimeOffset.UtcNow;
                try{_captureBounds=_target.GetCaptureBounds();MoveBesideCapture(_captureBounds.Value);}catch(InvalidOperationException){ }
            }
            if(!_closing&&generation==_generation){_tracker.Invalidate();_lastDetection=null;_lastDetectionKey=null;ClearAdvice();HoldAutomatic(ex.Message);_board.EmptyMessage="等待完整可见的棋盘";_board.Position=null;_board.Invalidate();SetStatus(ex.Message,true);_detail.Text=ex.Message;Log(ex);}
        }
        finally{image?.Dispose();_busy=false;}
    }

    private void ProcessDetection(Detection detection)
    {
        _lastDetection=detection;_lastDetectionKey=detection.Position?.Key;
        _metrics.Text=$"{detection.PieceCount} 子 · {detection.Milliseconds} ms · {detection.Confidence:P0}";
        if(detection.Position==null)
        {
            _tracker.Invalidate();ClearAdvice();HoldAutomatic(detection.Message);_board.Position=null;_board.EmptyMessage="等待清晰、稳定的棋盘";_board.Invalidate();_detail.Text=detection.Message;_badge.Text="●  等待画面";SetStatus(detection.Message,true);return;
        }
        _board.Position=detection.Position;_board.RedAtBottom=detection.RedAtBottom;_board.Invalidate();
        if(_tracker.Current?.Key!=detection.Position.Key)ClearAdvice();
        var tracked=_tracker.Observe(detection.Position);
        _lastTracking=tracked;_journal.Record(detection,tracked,_tracker);
        if(tracked.State==TrackingState.Rejected){HoldAutomatic("等待恢复合法局面："+tracked.Message);ClearAdvice();_mainMove.Text="等待局面恢复";_evaluation.Text="恢复识别后自动继续";_detail.Text=tracked.Message;_badge.Text="●  待确认";_sync.Text="采用当前局面";_sync.BackColor=Color.FromArgb(255,241,215);SetStatus(tracked.Message,true);return;}
        if(tracked.State==TrackingState.Warming){HoldAutomatic(tracked.Message);_badge.Text="●  同步中";if(!_adviceActive){_mainMove.Text="等待落稳";_evaluation.Text="正在确认最新棋局";}SetStatus(tracked.Message);return;}
        var position=_tracker.Current!;
        _updatingTurn=true;try{_turn.SelectedIndex=position.RedToMove?0:1;}finally{_updatingTurn=false;}
        _sync.Text="校准局面";_sync.BackColor=Color.White;_badge.Text="●  已同步";
        if(tracked.State!=TrackingState.Unchanged)_historyText.Text=tracked.Message;
        _targetInfo.Text=$"{(position.RedToMove?"红方":"黑方")}行棋 · 本次记录 {_tracker.History.Count} 步 · 自动补步 {_tracker.RecoveredPlies} 步";
        SetStatus(tracked.State==TrackingState.Unchanged?$"局面已同步，等待{(position.RedToMove?"红方":"黑方")}走棋":tracked.Message);
        RememberStableVisual(detection,position);ObserveAutomatic(tracked,position);
        if(_analysisKey!=position.ToFen())_ = RunAnalysisAsync(position);
        TryAutomaticStep();
    }

    private async Task RunAnalysisAsync(Position position)
    {
        _analysisCancellation?.Cancel();
        var cancel=new CancellationTokenSource();_analysisCancellation=cancel;int revision=++_analysisRevision;
        _analysisKey=position.ToFen();_adviceActive=true;_mainMove.Text="正在思考…";_evaluation.Text=$"{(position.RedToMove?"红方":"黑方")}行棋 · 正在计算";_detail.Text="棋局已确认，正在比较候选走法";
        var root=_tracker.RootFen;var history=_tracker.History.ToArray();
        try
        {
            var analysis=await _engine.AnalyzeAsync(position,(int)(_think.Value*1000),cancel.Token,root,history);
            if(_closing||cancel.IsCancellationRequested||revision!=_analysisRevision||_lastDetectionKey!=position.Key)return;
            _mainMove.Text=analysis.BestMove is "(none)" or "0000"?"无合法走法":position.Describe(analysis.BestMove);
            _board.BestMove=position.IsLegal(analysis.BestMove)?analysis.BestMove:null;_board.Invalidate();
            var primary=analysis.Candidates.FirstOrDefault(c=>c.Moves[0]==analysis.BestMove);
            _evaluation.Text=$"{(position.RedToMove?"红方":"黑方")}视角"+(primary!=null?$" · 评估 {primary.Evaluation} · 深度 {primary.Depth}":"");
            _detail.Text="绿色箭头表示推荐走法；候选卡片展示后续变化";
            for(int i=0;i<3;i++)
            {
                if(i>=analysis.Candidates.Length){_candidateLabels[i].SetContent($"{i+1:00}  暂无更多候选","—","");continue;}
                var c=analysis.Candidates[i];_candidateLabels[i].SetContent($"{i+1:00}  {position.Describe(c.Moves[0])}",c.Evaluation,position.DescribeLine(c.Moves,4),i==0);
            }
            _readyAnalysis=analysis;_readyFen=position.ToFen();TryAutomaticStep();
            if(!_running)SetStatus("图片分析完成 · 选择浏览器棋盘可开始自动读屏");
        }
        catch(OperationCanceledException){ }
        catch(Exception ex){if(!_closing&&revision==_analysisRevision){_mainMove.Text="分析未完成";_evaluation.Text=ex.Message;SetStatus(ex.Message,true);Log(ex);}}
        finally{if(ReferenceEquals(_analysisCancellation,cancel))_analysisCancellation=null;cancel.Dispose();}
    }

    private async Task ImportImageAsync()
    {
        using var picker=new OpenFileDialog{Filter="棋盘图片|*.png;*.jpg;*.jpeg;*.bmp",Title="导入棋盘截图"};
        if(picker.ShowDialog()!=DialogResult.OK)return;
        StopMonitoring();_target=null;await AnalyzeImageAsync(picker.FileName,null);
    }
    public async Task AnalyzeImageAsync(string path,RectangleF? grid)
    {
        Bitmap? image=null;int generation=_generation;
        try
        {
            using(var source=Image.FromFile(path))image=new Bitmap(source);
            SetStatus("正在识别导入图片…");
            var detection=await DetectAsync(image,grid,ModelPath,InitialRed,Orientation);
            if(_closing||generation!=_generation)return;
            SetPreview(image);image=null;_targetInfo.Text="图片分析模式\n自动读屏需重新选择浏览器棋盘";
            _lastDetection=detection;AnalyzeStill(detection);
        }
        catch(Exception ex){SetStatus(ex.Message,true);Log(ex);}
        finally{image?.Dispose();}
    }
    private void AnalyzeStill(Detection detection)
    {
        ResetTracking();for(int i=0;i<3;i++)ProcessDetection(detection);
        SetStatus(detection.Success?"图片模式 · 正在分析导入的棋局":detection.Message,!detection.Success);
    }
    private async Task LoadDemoAsync()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"assets","fixtures","vin-screen.png");
        if(File.Exists(path))await AnalyzeImageAsync(path,new RectangleF(61,63,232,252));
    }
    private static void Log(Exception ex)
    {
        string signature=ex.GetType().FullName+ex.Message;
        if(signature==_lastLoggedError&&(DateTime.UtcNow-_lastLogTime).TotalSeconds<15)return;
        _lastLoggedError=signature;_lastLogTime=DateTime.UtcNow;
        try{var dir=Path.Combine(AppContext.BaseDirectory,"logs");Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,DateTime.Now.ToString("yyyyMMdd")+".log"),DateTime.Now.ToString("O")+" "+ex+Environment.NewLine);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){ }
    }
}
