using System.Diagnostics;

namespace ChessCoach;

public sealed partial class MainForm
{
    private readonly Label _badge=new(){Text="未连接",AutoSize=false,Width=160,Height=30,TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.FromArgb(228,239,232),ForeColor=Accent};
    private readonly Label _metrics=new(){Dock=DockStyle.Right,Width=270,TextAlign=ContentAlignment.MiddleRight,ForeColor=Muted,AutoEllipsis=true};
    private readonly Label _historyText=new(){Dock=DockStyle.Fill,Text="连接后会自动跟随对方走棋",ForeColor=Muted,AutoEllipsis=true};
    private readonly CheckBox _topmost=new(){Text="窗口置顶",Checked=true,AutoSize=true,Margin=new Padding(12,9,0,0)};
    private FlowLayoutPanel _advancedPanel=null!;
    private Button _settingsButton=null!,_moreButton=null!;
    private readonly ToolTip _tooltips=new(){AutoPopDelay=12000,InitialDelay=500,ReshowDelay=200};
    private bool _showSettings;
    private TableLayoutPanel _hintStack=null!;
    private bool _measuringHints;
    private readonly Label _turnCaption=new(){Text="当前行棋",AutoSize=true,Padding=new Padding(0,5,6,0)};
    private readonly Label _thinkCaption=new(){Text="思考时间",AutoSize=true,Padding=new Padding(0,5,6,0)};

    private void BuildInterface()
    {
        SuspendLayout();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(1140,914);MinimumSize=new Size(900,640);Font=new Font("Microsoft YaHei UI",10);
        StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(242,245,243);ForeColor=Ink;TopMost=true;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(20,4,20,8)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        _rootLayout=root;
        for(int i=0;i<5;i++)root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,RowCount=1,Margin=new Padding(0,8,0,12)};_header=header;
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var logo=new Label{Text="棋",Size=new Size(46,46),Anchor=AnchorStyles.Left,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Microsoft YaHei UI",21,FontStyle.Bold),BackColor=Accent,ForeColor=Color.White,Margin=new Padding(0,0,14,0)};
        var brand=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        brand.RowStyles.Add(new RowStyle(SizeType.AutoSize));brand.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var name=new Label{Text="棋伴",AutoSize=true,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",23,FontStyle.Bold),Margin=Padding.Empty};
        var subtitle=new Label{Text="看清局面，想好下一步",AutoSize=true,Dock=DockStyle.Fill,ForeColor=Muted,Font=new Font("Microsoft YaHei UI",9),Margin=Padding.Empty};
        brand.Controls.Add(name,0,0);brand.Controls.Add(subtitle,0,1);
        var badgeHolder=new Panel{Width=160,Height=32,Anchor=AnchorStyles.Right,Margin=Padding.Empty};
        _badge.Dock=DockStyle.Fill;badgeHolder.Controls.Add(_badge);
        header.Controls.Add(logo,0,0);header.Controls.Add(brand,1,0);header.Controls.Add(badgeHolder,2,0);root.Controls.Add(header,0,0);
        _actionBar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,12)};
        _select.Text="连接棋盘";_select.Width=140;_select.BackColor=Accent;_select.ForeColor=Color.White;
        _start.Width=112;_start.BackColor=Color.White;_start.ForeColor=Ink;
        _compactButton=Button("浮窗模式");_compactButton.Width=112;
        _settingsButton=Button("识别设置");_settingsButton.Width=100;
        _moreButton=Button("更多 ···");_moreButton.Width=88;
        _actionBar.Controls.AddRange([_select,_start,_compactButton,_settingsButton,_moreButton,_topmost]);root.Controls.Add(_actionBar,0,1);
        _optionBar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,10),Padding=Padding.Empty};
        var turnGroup=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        var thinkGroup=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(14,0,0,0)};
        _turn.Width=144;turnGroup.Controls.AddRange([_turnCaption,_turn]);thinkGroup.Controls.AddRange([_thinkCaption,_think,new Label{Text="秒",AutoSize=true,Padding=new Padding(2,5,0,0)}]);_optionBar.Controls.AddRange([turnGroup,thinkGroup]);
        root.Controls.Add(_optionBar,0,2);
        var autoRow=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=2,Margin=new Padding(0,0,0,10),Padding=Padding.Empty};
        autoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        autoRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));autoRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var autoButtons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=Padding.Empty};
        _autoSide.Margin=new Padding(0,3,10,0);_autoToggle.Width=180;_autoToggle.Height=33;_autoToggle.MinimumSize=new Size(160,33);
        _autoInfo.AutoSize=true;_autoInfo.AutoEllipsis=true;_autoInfo.Dock=DockStyle.Fill;_autoInfo.Padding=new Padding(0,4,0,0);_autoInfo.Margin=Padding.Empty;
        autoButtons.Controls.AddRange([_autoSide,_autoToggle]);autoRow.Controls.Add(autoButtons,0,0);autoRow.Controls.Add(_autoInfo,0,1);
        root.Controls.Add(autoRow,0,3);_autoToggle.Click+=(_,_)=>ToggleAutomatic();
        _advancedPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Visible=false,Margin=new Padding(0,0,0,10),Padding=Padding.Empty};
        _advancedPanel.Controls.AddRange([_orientation,_model,new Label{Text="识别和计算均在本机完成",AutoSize=true,ForeColor=Muted,Padding=new Padding(12,5,0,0)}]);root.Controls.Add(_advancedPanel,0,4);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,4,0,0)};_bodyLayout=body;
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,56));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,44));
        var viewport=new SurfacePanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,14,0),Padding=new Padding(10)};
        _boardTabs=new TabControl{Dock=DockStyle.Fill,Appearance=TabAppearance.FlatButtons,SizeMode=TabSizeMode.Fixed,ItemSize=new Size(118,32),Font=new Font("Microsoft YaHei UI",10)};
        var boardTab=new TabPage("识别棋盘"){BackColor=Color.FromArgb(250,247,240)};boardTab.Controls.Add(_board);
        var sourceTab=new TabPage("采集画面"){BackColor=Color.FromArgb(28,42,38)};sourceTab.Controls.Add(_preview);
        _boardTabs.TabPages.AddRange([boardTab,sourceTab]);viewport.Controls.Add(_boardTabs);body.Controls.Add(viewport,0,0);
        var hints=new SurfacePanel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(20),Margin=Padding.Empty};
        var stack=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=false,ColumnCount=1,RowCount=9,Padding=Padding.Empty,Margin=Padding.Empty};_hintStack=stack;
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<4;i++)stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for(int i=0;i<3;i++)stack.RowStyles.Add(new RowStyle(SizeType.Absolute,88));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(new Label{Text="推荐走法",AutoSize=true,Dock=DockStyle.Fill,ForeColor=Muted,Margin=new Padding(0,0,0,6)},0,0);
        _mainMove.Dock=DockStyle.Fill;_mainMove.AutoSize=true;_mainMove.Font=new Font("Microsoft YaHei UI",29,FontStyle.Bold);_mainMove.TextAlign=ContentAlignment.MiddleLeft;_mainMove.Margin=Padding.Empty;
        _evaluation.Dock=DockStyle.Fill;_evaluation.AutoSize=true;_evaluation.Font=new Font("Microsoft YaHei UI",10);_evaluation.ForeColor=Accent;_evaluation.Margin=new Padding(0,4,0,6);
        _detail.Dock=DockStyle.Fill;_detail.AutoSize=true;_detail.Font=new Font("Microsoft YaHei UI",9);_detail.AutoEllipsis=true;_detail.Margin=new Padding(0,0,0,16);
        stack.Controls.Add(_mainMove,0,1);stack.Controls.Add(_evaluation,0,2);stack.Controls.Add(_detail,0,3);
        for(int i=0;i<3;i++){var card=new CandidateCard();card.SetWaiting(i+1);_candidateLabels[i]=card;stack.Controls.Add(card,0,i+4);}
        var recoveryRow=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=1,Margin=new Padding(0,6,0,0)};
        recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _sync.Anchor=AnchorStyles.Right;_sync.Width=130;_sync.Text="校准局面";_sync.Height=34;_sync.Margin=Padding.Empty;
        _historyText.AutoSize=true;_historyText.Padding=new Padding(0,7,12,0);recoveryRow.Controls.Add(_historyText,0,0);recoveryRow.Controls.Add(_sync,1,0);stack.Controls.Add(recoveryRow,0,7);
        _targetInfo.Dock=DockStyle.Fill;_targetInfo.AutoSize=true;_targetInfo.Font=new Font("Microsoft YaHei UI",9);_targetInfo.Padding=new Padding(0,10,0,0);stack.Controls.Add(_targetInfo,0,8);
        hints.Controls.Add(stack);body.Controls.Add(hints,1,0);root.Controls.Add(body,0,5);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=1,Margin=new Padding(0,10,0,0)};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Padding=Padding.Empty;_status.AutoSize=true;_status.AutoEllipsis=true;_status.Font=new Font("Microsoft YaHei UI",9);_metrics.Font=new Font("Microsoft YaHei UI",9);_metrics.AutoSize=true;
        footer.Controls.Add(_status,0,0);footer.Controls.Add(_metrics,1,0);root.Controls.Add(footer,0,6);
        _fullOnly=[_settingsButton,_topmost];
        _settingsButton.Click+=(_,_)=>ToggleSettings();
        _topmost.CheckedChanged+=(_,_)=>TopMost=_topmost.Checked;
        var menu=new ContextMenuStrip{Font=new Font("Microsoft YaHei UI",10)};
        menu.Items.Add("打开象棋网页版",null,(_,_)=>Process.Start(new ProcessStartInfo("https://h5login.qqchess.qq.com/"){UseShellExecute=true}));
        menu.Items.Add("导入棋盘截图…",null,async(_,_)=>await ImportImageAsync());
        menu.Items.Add("保存诊断",null,(_,_)=>SaveDiagnostic());
        menu.Items.Add("复制当前 FEN",null,(_,_)=>{if(_tracker.Current!=null){Clipboard.SetText(_tracker.Current.ToFen());SetStatus("已复制当前局面");}});
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("识别设置",null,(_,_)=>ToggleSettings());
        var pin=new ToolStripMenuItem("窗口置顶"){CheckOnClick=true,Checked=true};pin.CheckedChanged+=(_,_)=>_topmost.Checked=pin.Checked;menu.Items.Add(pin);
        menu.Opening+=(_,_)=>pin.Checked=_topmost.Checked;_moreButton.Click+=(_,_)=>menu.Show(_moreButton,new Point(0,_moreButton.Height));
        _tooltips.SetToolTip(_turn,"首次连接时确认轮次，之后会根据实际走棋自动更新。");
        _tooltips.SetToolTip(_sync,"棋局跳转或识别异常时，核对棋盘与行棋方后采用当前局面。");
        _tooltips.SetToolTip(_compactButton,"只显示走法建议，方便放在棋盘旁边。");
        _tooltips.SetToolTip(_autoSide,"选择你在网页中的执棋颜色，与当前行棋方分别设置。");
        _tooltips.SetToolTip(_autoToggle,"只操作当前棋局。开启前核对棋子、行棋方与执棋颜色；点击停止或按 F8 接管。");
        foreach(var label in new[]{_mainMove,_evaluation,_detail,_historyText,_targetInfo})label.TextChanged+=(_,_)=>UpdateContentHeights();
        hints.SizeChanged+=(_,_)=>UpdateContentHeights();Shown+=(_,_)=>UpdateContentHeights();DpiChanged+=(_,_)=>BeginInvoke(UpdateContentHeights);
        ResumeLayout(true);
    }

    private void UpdateContentHeights()
    {
        if(_hintStack==null||_candidateLabels.Any(c=>c==null)||_measuringHints)return;
        _measuringHints=true;
        try
        {
            int width=Math.Max(100,_hintStack.ClientSize.Width),height=0;
            if(_hintStack.Parent is ScrollableControl scroll)
            {
                width=Math.Max(100,scroll.ClientSize.Width-scroll.Padding.Horizontal-(scroll.VerticalScroll.Visible?SystemInformation.VerticalScrollBarWidth:0));
                if(_hintStack.MaximumSize.Width!=width)_hintStack.MaximumSize=new Size(width,0);
                if(_hintStack.Width!=width)_hintStack.Width=width;
            }
            for(int i=0;i<3;i++)
            {
                int cardHeight=_candidateLabels[i].ContentHeight+_candidateLabels[i].Margin.Vertical;
                if(Math.Abs(_hintStack.RowStyles[i+4].Height-cardHeight)>1)_hintStack.RowStyles[i+4].Height=cardHeight;
                height+=cardHeight;
            }
            foreach(var control in new[]{_hintStack.GetControlFromPosition(0,0)!,_mainMove,_evaluation,_detail,_targetInfo})
                height+=control.GetPreferredSize(new Size(width,0)).Height+control.Margin.Vertical;
            height+=Math.Max(_sync.Height,_historyText.GetPreferredSize(new Size(Math.Max(100,width-_sync.Width),0)).Height)+12;
            if(_hintStack.Height!=height)_hintStack.Height=height;
        }
        finally{_measuringHints=false;}
    }

    private void ToggleSettings()
    {
        _showSettings=!_showSettings;if(_showSettings&&_compact)SetCompact(false);_advancedPanel.Visible=_showSettings;
    }
}
