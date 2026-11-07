namespace ChessCoach;

public sealed partial class MainForm
{
    private const int StopHotkey=0x4348;
    private readonly AutoPlaySession _automatic=new();
    private readonly ComboBox _autoSide=Combo(["我执红方","我执黑方"],128);
    private readonly Button _autoToggle=Button("开启自动行棋");
    private readonly Label _autoInfo=new(){Text="自动已关闭 · F8 停止",AutoSize=true,ForeColor=Muted,Padding=new Padding(0,7,0,0)};
    private CancellationTokenSource? _automaticCancellation;
    private bool _automaticDispatching,_hotkeyRegistered;
    private Analysis? _readyAnalysis;
    private string? _readyFen;
    private VisualBoardSnapshot? _stableVisual;
    private readonly AutomaticRecovery _recovery=new();
    internal bool AutomaticEnabledForVerification=>_automatic.Enabled;
    internal bool AutomaticControlsVisibleForVerification=>_autoToggle.Visible&&_autoSide.Visible&&_autoInfo.Visible;
    internal bool AutomaticWaitingForVerification=>_recovery.Waiting;
    internal void ArmAutomaticForVerification()
    {
        if(!IsRenderingOffscreen)throw new InvalidOperationException("Only available to offscreen UI checks");
        _automatic.Enable(true);_recovery.Reset();RefreshAutomatic();
    }
    internal void StopHotkeyForVerification()
    {
        var message=Message.Create(Handle,0x0312,new IntPtr(StopHotkey),IntPtr.Zero);WndProc(ref message);
    }

    private void ToggleAutomatic()
    {
        if(_automatic.Enabled){StopAutomatic("自动已关闭");return;}
        if(IsRenderingOffscreen)return;
        if(!_running||_target==null||!ReliableAutomaticBoard())
        {SetStatus("请先连接棋盘，确认当前行棋方，等待棋局同步后再开启自动行棋",true);return;}
        if(_automaticDispatching){SetStatus("上一次操作正在退出，请稍后再开启",true);return;}
        if(!WindowsMoveInput.RegisterHotKey(Handle,StopHotkey,0x4000,0x77))
        {SetStatus("F8 停止键被其他程序占用，无法开启自动模式",true);return;}
        _hotkeyRegistered=true;_automaticCancellation=new CancellationTokenSource();
        _automatic.Enable(_autoSide.SelectedIndex==0);_recovery.Reset();
        _journal.RecordAutomatic("enabled",_automatic,_tracker);RefreshAutomatic();
        SetStatus($"自动执{(_automatic.PlayRed?"红":"黑")}已开启 · F8 随时停止");TryAutomaticStep();
    }

    private bool ReliableAutomaticBoard()=>_tracker.Current!=null&&_lastDetection?.Position?.Key==_tracker.Current.Key
        &&_lastDetection.Confidence>=AutoPlaySession.MinimumConfidence&&_lastDetection.Grid.HasValue
        &&_lastTracking?.State is TrackingState.Accepted or TrackingState.Unchanged or TrackingState.Recovered or TrackingState.TurnCorrected;

    private void RefreshAutomatic()
    {
        _autoToggle.Text=_automatic.Enabled?"停止自动 · F8":"开启自动行棋";
        _autoToggle.BackColor=_automatic.Enabled?Color.FromArgb(158,68,40):Color.White;
        _autoToggle.ForeColor=_automatic.Enabled?Color.White:Ink;_autoSide.Enabled=!_automatic.Enabled;
        _autoInfo.Text=!_automatic.Enabled?_automatic.Message:_recovery.Waiting?"自动等待 · 画面恢复后继续 · F8 停止":_automatic.ConfirmationDelayed?"等待落子确认 · 不会重复点击":$"自动中 · 已走 {_automatic.ConfirmedMoves} 步 · F8 停止";
        _tooltips.SetToolTip(_autoInfo,_recovery.Waiting?_recovery.Reason:_automatic.Message);
    }

    private void StopAutomatic(string reason)
    {
        bool active=_automatic.Enabled||_hotkeyRegistered;
        _automaticCancellation?.Cancel();_automaticCancellation?.Dispose();_automaticCancellation=null;
        _automatic.Stop(reason);_recovery.Reset();
        if(_hotkeyRegistered){WindowsMoveInput.UnregisterHotKey(Handle,StopHotkey);_hotkeyRegistered=false;}
        if(active)_journal.RecordAutomatic("stopped",_automatic,_tracker);
        RefreshAutomatic();if(active&&!_closing)SetStatus(reason,true);
    }

    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312&&m.WParam.ToInt32()==StopHotkey){StopAutomatic("F8 已停止自动行棋");return;}
        base.WndProc(ref m);
    }

    private void HoldAutomatic(string reason)
    {
        if(!_automatic.Enabled)return;
        bool wasWaiting=_recovery.Waiting;_recovery.Hold(reason,DateTimeOffset.UtcNow);RefreshAutomatic();
        if(!wasWaiting)_journal.RecordAutomatic("waiting-for-recovery",_automatic,_tracker,detail:reason);
    }

    private void ObserveAutomatic(TrackingResult tracked,Position position)
    {
        if(!_automatic.Enabled)return;
        if(tracked.State==TrackingState.Rewound||tracked.State==TrackingState.TurnCorrected&&_automatic.Pending!=null)
        {StopAutomatic("局面或行棋方已校正，请核对后重新开启自动");return;}
        if(_lastDetection!.Confidence<AutoPlaySession.MinimumConfidence){HoldAutomatic("识别置信度不足");return;}
        bool wasWaiting=_recovery.Waiting;
        if(!_recovery.ObserveReliable(position.ToFen()+"|"+_lastDetection.RedAtBottom,DateTimeOffset.UtcNow)){RefreshAutomatic();return;}
        if(wasWaiting)_journal.RecordAutomatic("recovered",_automatic,_tracker);
        var result=_automatic.Observe(position,DateTimeOffset.UtcNow);
        if(result==AutomaticObservation.Paused){StopAutomatic(_automatic.Message);return;}
        if(result is AutomaticObservation.Confirmed or AutomaticObservation.ConfirmedWithReply)
            _journal.RecordAutomatic("confirmed",_automatic,_tracker);
        RefreshAutomatic();
    }

    private void RememberStableVisual(Detection detection,Position position)
    {
        if(detection.Grid is not RectangleF grid||_preview.Image is not Bitmap image)return;
        // Refresh from confirmed frames so a selection highlight never freezes
        // the baseline from before an interrupted source click.
        _stableVisual?.Dispose();_stableVisual=new(image,grid,position,detection.RedAtBottom);
    }

    private void TryAutomaticStep()
    {
        if(!_automatic.Enabled||_recovery.Waiting||_automaticDispatching||_automatic.Pending!=null||!_running||_target==null||IsRenderingOffscreen
            ||!ReliableAutomaticBoard()||_stableVisual==null||_readyAnalysis==null||_readyFen!=_tracker.Current!.ToFen())return;
        if(_readyAnalysis.BestMove is "(none)" or "0000"){StopAutomatic("本局已无合法走法，自动已停止");return;}
        var action=_automatic.Reserve(_tracker.Current,_readyAnalysis.BestMove,_tracker.History.Count,_lastDetection!.Confidence);
        if(action==null)return;
        var baseline=_stableVisual.Clone();
        _ = ExecuteAutomaticAsync(action,baseline,_target,_analysisRevision,_automaticCancellation!.Token);
    }

    private async Task ExecuteAutomaticAsync(AutomaticMove action,VisualBoardSnapshot baseline,CaptureTarget target,int revision,CancellationToken ct)
    {
        _automaticDispatching=true;RefreshAutomatic();
        bool SameSession()=>_automatic.Enabled&&_automatic.Generation==action.Generation;
        void EnsureCurrent()
        {
            ct.ThrowIfCancellationRequested();
            if(!SameSession()||_closing||!_running||!ReferenceEquals(_target,target)||revision!=_analysisRevision
                ||!ReliableAutomaticBoard()||_tracker.Current?.ToFen()!=action.Before.ToFen())
                throw new AutomaticRetryException("复核期间画面或分析已更新，等待重新确认");
        }
        async Task<MoveClickPlan> Prepare(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();EnsureCurrent();
            var frame=target.Capture();
            using(frame.Image)
            {
                var seen=await DetectAsync(frame.Image,frame.Grid,ModelPath,action.Before.RedToMove,Orientation);
                token.ThrowIfCancellationRequested();EnsureCurrent();
                if(seen.Position?.Key!=action.Before.Key||seen.Confidence<AutoPlaySession.MinimumConfidence||seen.Grid is not RectangleF grid
                    ||!baseline.Matches(frame.Image,grid,seen.Position,action.Move,seen.RedAtBottom))
                    throw new AutomaticRetryException($"落子前复核等待：棋局匹配 {seen.Position?.Key==action.Before.Key}，置信度 {seen.Confidence:P0}，网格 {seen.Grid.HasValue}，朝向 {seen.RedAtBottom}，画面需稳定");
                var screenGrid=new RectangleF(frame.Bounds.Left+grid.Left,frame.Bounds.Top+grid.Top,grid.Width,grid.Height);
                return MoveClickPlan.Create(action.Before,action.Move,screenGrid,seen.RedAtBottom,frame.WindowBounds);
            }
        }
        try
        {
            // Recheck once before focusing, then again after the target is foreground.
            await Prepare(ct);
            var plan=await WindowsMoveInput.ExecuteAsync(target,Handle,Prepare,()=>
            {
                EnsureCurrent();
                if(!_automatic.InputStarting(action.Id,DateTimeOffset.UtcNow))throw new OperationCanceledException(ct);
                _journal.RecordAutomatic("input-started",_automatic,_tracker);RefreshAutomatic();
            },ct);
            if(SameSession()){_journal.RecordAutomatic("input-sent",_automatic,_tracker,plan);RefreshAutomatic();}
        }
        catch(OperationCanceledException){if(SameSession())StopAutomatic("自动操作已取消");}
        catch(Exception ex)
        {
            if(SameSession())
            {
                if(_automatic.Pending?.InputStarted==null&&ex is AutomaticRetryException or BoardOccludedException)
                {_automatic.ReleaseUnsent(action.Id);_tracker.Invalidate();ClearAdvice();HoldAutomatic(ex.Message);}
                else StopAutomatic(ex.Message);
                Log(ex);
            }
        }
        finally{baseline.Dispose();_automaticDispatching=false;}
    }
}
