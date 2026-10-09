using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Native WinForms replacement for the three selected AuthorDbBuilder source cards.
    /// Keeps downloaded raw files outside the shipping directory and reuses the unified
    /// evidence/index merge path. Does not load WPF or a .NET 8 runtime.
    /// </summary>
    internal sealed class AdvancedAuthorBuilderForm : Form
    {
        private readonly LanguageManager _language;
        private readonly FlowLayoutPanel _sourceList;
        private readonly Dictionary<string, SourceCard> _cards = new Dictionary<string, SourceCard>();
        private readonly Label _task;
        private readonly Label _stats;
        private readonly ProgressBar _progress;
        private readonly Button _cancel;
        private readonly Button _pause;
        private AdvancedOperationControl _operationControl;
        private readonly CheckBox _autoFilter;
        private readonly CheckBox _deleteRaw;
        private CancellationTokenSource _cts;
        private bool _working;
        private readonly string _dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AuthorDbSources");
        private sealed class SourceCard
        {
            public AdvancedSourceDefinition Source;
            public Panel Panel;
            public Label Status;
            public Label Remote;
            public Button Check;
            public Button Download;
            public Button Choose;
            public Button Filter;
            public Button Delete;
            public string FileName = "";
            public AuthorReferenceRemoteInfo RemoteInfo;
        }

        public AdvancedAuthorBuilderForm(LanguageManager language, Font appFont)
        {
            _language = language;
            Text = T("AdvancedBuilder.Title", "高级作者数据库构建");
            ClientSize = new Size(1050, 735);
            MinimumSize = new Size(965, 615);
            StartPosition = FormStartPosition.CenterParent;
            UiStyle.ApplyDialog(this, appFont);
            Directory.CreateDirectory(_dataDirectory);

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 12), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);
            Panel intro = new Panel { Dock = DockStyle.Fill };
            Label heading = new Label { Text = T("AdvancedBuilder.Heading", "归归作者数据库构建工具"), AutoSize = true, Font = new Font(appFont.FontFamily, 15F, FontStyle.Bold) };
            Label subtitle = new Label { Text = T("AdvancedBuilder.Subtitle", "逐个处理来源，仅提取作者、社团、名称及关系；不下载图片或作品文件。"), ForeColor = UiStyle.Muted, Top = 34, AutoSize = true };
            intro.Controls.Add(heading); intro.Controls.Add(subtitle);
            layout.Controls.Add(intro, 0, 0);

            _sourceList = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2) };
            layout.Controls.Add(_sourceList, 0, 1);
            foreach (AdvancedSourceDefinition definition in AdvancedSourceService.Sources) AddCard(definition);
            _sourceList.SizeChanged += delegate { RelayoutCards(); };
            Shown += async delegate
            {
                RelayoutCards();
                try
                {
                    AdvancedSourceDefinition[] configured=await AdvancedSourceService.GetConfiguredSourcesAsync();
                    foreach(AdvancedSourceDefinition src in configured)
                    {
                        SourceCard card;
                        if (_cards.TryGetValue(src.Id,out card)) card.Source=src;
                    }
                }
                catch { /* Keep validated built-in source endpoints when offline. */ }
            };

            GroupBox tasks = new GroupBox { Text = T("AdvancedBuilder.Task", "当前任务"), Dock = DockStyle.Fill, Padding = new Padding(14, 17, 14, 8) };
            TableLayoutPanel progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _task = new Label { Dock = DockStyle.Fill, Text = T("AdvancedBuilder.Idle", "空闲 · 选择来源后检查或下载"), AutoEllipsis = true };
            _progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Value = 0, Height = 19 };
            _stats = new Label { Dock = DockStyle.Fill, ForeColor = UiStyle.Muted, Text = T("AdvancedBuilder.NoIndex", "当前公共库：等待检查"), AutoEllipsis = true };
            progressLayout.Controls.Add(_task, 0, 0); progressLayout.Controls.Add(_progress, 0, 1); progressLayout.Controls.Add(_stats, 0, 2);
            tasks.Controls.Add(progressLayout); layout.Controls.Add(tasks, 0, 2);

            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _autoFilter = new CheckBox { AutoSize = true, Text = T("AdvancedBuilder.AutoFilter", "下载完成后自动开始过滤并整合到公共库") };
            _deleteRaw = new CheckBox { AutoSize = true, Text = T("AdvancedBuilder.DeleteAfter", "过滤完成后自动删除原始数据库（不影响公共库和证据）") };
            options.Controls.Add(_autoFilter); options.Controls.Add(_deleteRaw); layout.Controls.Add(options, 0, 3);

            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button close = UiStyle.NewButton(T("Common.Close", "关闭"), 88, false); close.Click += delegate { Close(); };
            Button fresh = UiStyle.NewButton(T("AdvancedBuilder.FreshBase", "新建空库"), 102, false);
            fresh.Click += delegate
            {
                if (_working) return;
                if (UiMessageBox.Show(this,
                    "新建 Schema v4 空白基线会在下次重启后替换当前公共索引！旧库会另存备份，现有作者实体不会自动迁移。\n\n"+
                    "通常应优先下载开发者公共库；只有需要从零构建或当前库格式不兼容时才使用。继续？",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    AuthorReferenceLibraryService.Current.EnsureEvidenceStorage();
                    PublicAuthorIndexMergeService.PrepareFreshBase();
                    PublicAuthorIndexMergeService.BuildPending(AuthorReferenceLibraryService.Current.Path);
                    _task.Text="已建立 Schema v4 空白基线并生成待激活数据库，重启后生效。";
                    UpdateStats();
                }
                catch(Exception ex) { UiMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            Button rebuild = UiStyle.NewButton(T("AdvancedBuilder.RebuildMerge", "重新合并"), 100, false);
            rebuild.Click += async delegate
            {
                if (_working) return;
                await RunActionAsync(null, ct => AuthorReferenceLibraryService.Current.RebuildPublicIndexAsync(
                    new Progress<AdvancedSourceProgress>(OnProgress), ct, _operationControl), true);
                UpdateStats();
            };
            Button config = UiStyle.NewButton(T("AdvancedBuilder.Configuration", "打开配置"), 110, false);
            config.Click += delegate { string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SourcesConfig.json");
                if (File.Exists(configFile)) Process.Start(configFile); else UiMessageBox.Show(this, configFile, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); };
            Button open = UiStyle.NewButton(T("AdvancedBuilder.OpenDirectory", "打开输出目录"), 125, false);
            open.Click += delegate { Process.Start(_dataDirectory); };
            _pause = UiStyle.NewButton(T("AdvancedBuilder.Pause", "暂停"), 78, false);
            _pause.Enabled = false;
            _pause.Click += delegate
            {
                if (_operationControl==null) return;
                _operationControl.Toggle();
                _pause.Text = _operationControl.IsPaused ? T("AdvancedBuilder.Resume", "继续") : T("AdvancedBuilder.Pause", "暂停");
                _task.Text = _operationControl.IsPaused ? T("AdvancedBuilder.Paused", "任务已暂停") : T("AdvancedBuilder.Working", "继续处理中...");
            };
            _cancel = UiStyle.NewButton(T("AdvancedBuilder.Cancel", "取消任务"), 110, false);
            _cancel.Enabled = false; _cancel.Click += delegate { if (_cts != null) { _cts.Cancel(); if(_operationControl!=null) _operationControl.Resume(); } };
            bottom.Controls.Add(close); bottom.Controls.Add(config); bottom.Controls.Add(open); bottom.Controls.Add(fresh); bottom.Controls.Add(rebuild); bottom.Controls.Add(_cancel); bottom.Controls.Add(_pause);
            layout.Controls.Add(bottom, 0, 4);
            UpdateStats();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (_working) { e.Cancel = true; UiMessageBox.Show(this, T("AdvancedBuilder.Wait", "请先取消当前任务，等待操作结束。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); } };
        }

        private void AddCard(AdvancedSourceDefinition definition)
        {
            Panel card = new Panel { Height = 139, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window, Margin = new Padding(0, 0, 0, 12) };
            Label heading = new Label { Text = definition.Name, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), Location = new Point(18, 12), AutoSize = true };
            Label badge = new Label { Text = definition.Kind == "csv" ? T("AdvancedBuilder.Continuous", "持续更新") : T("AdvancedBuilder.Archive", "大型来源"), ForeColor = Color.FromArgb(80, 62, 153), Location = new Point(20, 39), AutoSize = true };
            Label status = new Label { Text = T("AdvancedBuilder.NotChecked", "状态：尚未检查"), Location = new Point(18, 62), AutoSize = false, Width = 350, Height = 24, AutoEllipsis = true };
            Label remote = new Label { Text = T("AdvancedBuilder.Local", "本地：未下载"), Location = new Point(18, 91), AutoSize = false, Width = 500, Height = 35, AutoEllipsis = true, ForeColor = UiStyle.Muted };
            Button source = UiStyle.NewButton(T("AdvancedBuilder.Source", "来源"), 66, false);
            Button check = UiStyle.NewButton(T("AuthorReference.Check", "检查"), 66, false);
            Button download = UiStyle.NewButton(T("AdvancedBuilder.Download", "下载"), 66, false);
            Button choose = UiStyle.NewButton(T("AdvancedBuilder.ChooseFile", "选择文件"), 90, false);
            Button filter = UiStyle.NewButton(T("AdvancedBuilder.Filter", "过滤"), 66, true);
            Button delete = UiStyle.NewButton(T("AdvancedBuilder.DeleteRaw", "删除原始库"), 104, false);
            foreach (Button b in new[] { source, check, download, choose, filter, delete }) { b.Height = 31; b.Top = 46; card.Controls.Add(b); }
            card.Controls.Add(heading); card.Controls.Add(badge); card.Controls.Add(status); card.Controls.Add(remote);
            SourceCard state = new SourceCard { Source = definition, Panel = card, Status = status, Remote = remote, Check = check, Download = download, Choose = choose, Filter = filter, Delete = delete };
            _cards[definition.Id] = state;
            source.Click += delegate { UiMessageBox.Show(this, state.Source.Name + "\n" + state.Source.Description + "\n\n" + state.Source.Url, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); };
            check.Click += async delegate { await RunActionAsync(state, async ct => { AdvancedRemoteMetadata info = await AdvancedSourceService.CheckAsync(state.Source, ct); status.Text = "远程：" + info.Details + (info.UpdatedUtc.Year>2000?" · "+info.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd"):""); }, false); };
            download.Click += async delegate { await DownloadAsync(state); };
            choose.Click += delegate { using (OpenFileDialog picker = new OpenFileDialog { Title = definition.Name, Filter = definition.Kind == "csv" ? "CSV (*.csv)|*.csv|所有文件 (*.*)|*.*" : "SQLite 或压缩数据库 (*.db;*.sqlite;*.gz;*.zstd;*.zst)|*.db;*.sqlite;*.gz;*.zstd;*.zst|所有文件 (*.*)|*.*" })
                { if (picker.ShowDialog(this) == DialogResult.OK) { state.FileName = picker.FileName; UpdateCard(state); } } };
            filter.Click += async delegate { await FilterAsync(state); };
            delete.Click += delegate { DeleteRaw(state); };
            _sourceList.Controls.Add(card); UpdateCard(state);
        }
        private void RelayoutCards()
        {
            int width = Math.Max(700, _sourceList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
            foreach (SourceCard state in _cards.Values)
            {
                state.Panel.Width = width;
                Button[] buttons = { state.Check, state.Download, state.Choose, state.Filter, state.Delete };
                Button sourceButton = state.Panel.Controls.OfType<Button>().First(b => b != state.Check && b != state.Download && b != state.Choose && b != state.Filter && b != state.Delete);
                Button[] ordered = { sourceButton, state.Check, state.Download, state.Choose, state.Filter, state.Delete };
                int widths = ordered.Sum(b => b.Width) + (ordered.Length - 1) * 6;
                int left = Math.Max(325, width - widths - 14);
                foreach (Button b in ordered) { b.Left = left; left += b.Width + 6; }
            }
        }
        private async Task DownloadAsync(SourceCard state)
        {
            await RunActionAsync(state, async ct =>
            {
                if (state.Source.Kind == "csv")
                {
                    _task.Text = "检查并下载 " + state.Source.Name + " 历史 CSV";
                    AuthorReferenceRemoteInfo all = await AuthorReferenceLibraryService.Current.GetRemoteInfoAsync(true);
                    // The standard service only tracks the selected CSV source in its manifest.
                    if (all.ChangedFiles == 0) { _task.Text = "所有 CSV 已导入"; return; }
                    if (UiMessageBox.Show(this, "需要处理 " + all.ChangedFiles + " 个 CSV，预计 " + FormatSize(all.Size) + "。继续？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    IProgress<AdvancedSourceProgress> progress = new Progress<AdvancedSourceProgress>(OnProgress);
                    string directory=await AdvancedSourceService.DownloadCsvAsync(all, _dataDirectory, progress, ct, _operationControl);
                    state.RemoteInfo=all;
                    state.FileName=directory;UpdateCard(state);
                    state.Status.Text="状态：下载完成，待过滤";
                    _task.Text=state.Source.Name+"：已下载原始 CSV";
                    if(_autoFilter.Checked) await FilterSourceAsync(state,ct);
                }
                else
                {
                    IProgress<AdvancedSourceProgress> progress = new Progress<AdvancedSourceProgress>(OnProgress);
                    AdvancedRemoteMetadata meta = await AdvancedSourceService.CheckAsync(state.Source, ct);
                    if (UiMessageBox.Show(this, "下载 " + state.Source.Name + "，约 " + FormatSize(meta.Size) +
                        "。\n大型来源可能需要额外数 GB 磁盘空间用于解压和过滤，确定继续？", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    string path = await AdvancedSourceService.DownloadAsync(state.Source, _dataDirectory, progress, ct, _operationControl);
                    state.FileName = path; UpdateCard(state);
                    _task.Text = state.Source.Name + " 下载完成";
                    if (_autoFilter.Checked) await FilterSourceAsync(state, ct);
                }
            }, true);
        }
        private async Task FilterAsync(SourceCard state)
        {
            await RunActionAsync(state, ct => FilterSourceAsync(state, ct), true);
        }
        private async Task FilterSourceAsync(SourceCard state, CancellationToken token)
        {
            if (state.Source.Kind == "csv")
            {
                if (Directory.Exists(state.FileName))
                    await AuthorReferenceLibraryService.Current.ImportLocalCsvDirectoryAsync(state.FileName,
                        state.RemoteInfo, new Progress<AdvancedSourceProgress>(OnProgress), token, _operationControl);
                else if (File.Exists(state.FileName))
                    await AuthorReferenceLibraryService.Current.ImportLocalCsvAsync(state.FileName, token, _operationControl);
                else { _task.Text="请先下载 CSV 或选择一个本地 CSV 文件。";return; }
            }
            else
            {
                if (String.IsNullOrWhiteSpace(state.FileName) || !File.Exists(state.FileName))
                {
                    UiMessageBox.Show(this, "请先下载或选择原始数据库。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                IProgress<AdvancedSourceProgress> progress = new Progress<AdvancedSourceProgress>(OnProgress);
                await AuthorReferenceLibraryService.Current.ImportExternalSourceAsync(state.Source.Id, state.FileName, progress, token, _operationControl);
            }
            _task.Text = state.Source.Name + "：已过滤并合并，重启归归后生效";
            state.Status.Text = "状态：过滤完成";
            UpdateStats();
            if (_deleteRaw.Checked && Path.GetFullPath(state.FileName).StartsWith(Path.GetFullPath(_dataDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if(Directory.Exists(state.FileName))Directory.Delete(state.FileName,true);
                else if(File.Exists(state.FileName))File.Delete(state.FileName);
                state.FileName="";UpdateCard(state);
            }
        }
        private async Task RunActionAsync(SourceCard state, Func<CancellationToken,Task> operation, bool resetProgress)
        {
            if (_working) return;
            _working = true; _cts = new CancellationTokenSource(); _operationControl=new AdvancedOperationControl();
            foreach (SourceCard card in _cards.Values)
                foreach (Button button in card.Panel.Controls.OfType<Button>()) button.Enabled = false;
            _cancel.Enabled = true; _pause.Enabled=true;_pause.Text=T("AdvancedBuilder.Pause", "暂停");
            _progress.Style = ProgressBarStyle.Blocks;
            if (resetProgress) _progress.Value = 0;
            _task.Text = (state==null ? "公共数据库" : state.Source.Name) + " · " + T("AdvancedBuilder.Working", "处理中...");
            try { await operation(_cts.Token); }
            catch (OperationCanceledException) { _task.Text = T("AdvancedBuilder.Cancelled", "已取消，之前已完成的步骤仍然保留。"); }
            catch (Exception ex) { _task.Text = "操作中断：" + ex.Message; UiMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { _progress.Style=ProgressBarStyle.Blocks; _operationControl.Resume();_operationControl.Dispose();_operationControl=null; _cts.Dispose(); _cts = null; _working = false; _cancel.Enabled = false;_pause.Enabled=false;
                foreach (SourceCard card in _cards.Values) foreach (Button button in card.Panel.Controls.OfType<Button>()) button.Enabled = true; }
        }
        private void OnProgress(AdvancedSourceProgress info)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new MethodInvoker(delegate { OnProgress(info); })); } catch { } return; }
            _task.Text = info.Message;
            if (info.Total > 0)
            {
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.Value = (int)Math.Min(100, info.Current * 100 / info.Total);
            }
            else _progress.Style = ProgressBarStyle.Marquee;
        }
        private void UpdateCard(SourceCard card)
        {
            string file = card.FileName;
            if (String.IsNullOrEmpty(file))
            {
                string likely = card.Source.Kind=="csv" ? Path.Combine(_dataDirectory,"nh-metadata-archive") :
                    Path.Combine(_dataDirectory, card.Source.FileName);
                if (File.Exists(likely) || Directory.Exists(likely)) file = card.FileName = likely;
            }
            card.Remote.Text = Directory.Exists(file ?? "") ? "本地："+Directory.GetFiles(file,"*.csv").Length+" 个 CSV 待过滤" :
                File.Exists(file ?? "") ? "本地：" + Path.GetFileName(file) + " (" + FormatSize(new FileInfo(file).Length) + ")" : "本地：尚未下载 / 未选择文件";
        }
        private void DeleteRaw(SourceCard card)
        {
            if (!File.Exists(card.FileName) && !Directory.Exists(card.FileName)) return;
            if (!Path.GetFullPath(card.FileName).StartsWith(Path.GetFullPath(_dataDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { UiMessageBox.Show(this, "只允许从程序的来源下载目录删除原始文件；外部选择的文件请自行管理。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (UiMessageBox.Show(this, "确认删除原始文件？这不会删除公共库或已导入的证据。", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if(Directory.Exists(card.FileName))Directory.Delete(card.FileName,true);
            else File.Delete(card.FileName);
            card.FileName = ""; UpdateCard(card);
        }
        private void UpdateStats()
        {
            try
            {
                AuthorReferenceLocalInfo info = AuthorReferenceLibraryService.Current.GetLocalInfo();
                _stats.Text = (PublicAuthorIndexMergeService.HasActiveIndex && !PublicAuthorIndexMergeService.HasBuilderSchemaIndex
                    ? "当前公共库不是 Schema v4，请先导入官方库或备份后新建空库。  |  " : "") +
                    "作品 " + info.WorkCount.ToString("N0") + " | 证据 " + info.EvidenceCount.ToString("N0") +
                    " | 已匹配 " + info.MatchedCount.ToString("N0") + " | 歧义 " + info.AmbiguousCount.ToString("N0") +
                    " | 候选 " + info.CandidateCount.ToString("N0") + " | 待激活 " + (PublicAuthorIndexMergeService.HasPendingIndex ? "是" : "否");
            }
            catch (Exception ex) { _stats.Text = ex.Message; }
        }
        private string T(string key, string fallback) { string value = _language.Get(key); return String.IsNullOrWhiteSpace(value) || value == key ? fallback : value; }
        private static string FormatSize(long size) { if (size < 0) return "—"; double v=size; string[] suffix={"B","KB","MB","GB"}; int u=0; while(v >=1024 && u < 3) {v/=1024;u++;} return v.ToString("0.##")+" "+suffix[u]; }
    }
}
