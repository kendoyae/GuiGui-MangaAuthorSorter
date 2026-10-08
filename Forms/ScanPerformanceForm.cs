using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class ScanPerformanceForm : Form
    {
        private readonly LanguageManager _language;
        private readonly string _logPath;
        private readonly Action<bool, bool, bool> _saveSettings;
        private readonly Action _showSimulation;
        private readonly CheckBox _enabled, _warmup, _everything;
        private readonly Label _state;
        private readonly DataGridView _grid;
        private readonly Dictionary<string, Label> _values = new Dictionary<string, Label>(StringComparer.Ordinal);
        private ScanWarmupStatusEntry _warmupStatus;

        public ScanPerformanceForm(
            LanguageManager language,
            string logPath,
            Action<bool, bool, bool> saveSettings,
            Action showSimulation)
        {
            _language = language; _logPath = logPath; _saveSettings = saveSettings; _showSimulation = showSimulation;
            Text = L("Performance.Title"); Size = new Size(1000, 790); MinimumSize = new Size(820, 650); ShowInTaskbar = true;
            UiStyle.ApplyDialog(this, SystemFonts.MessageBoxFont); StartPosition = FormStartPosition.Manual;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label title = new Label { AutoSize = true, Text = L("Performance.Title"), Font = new Font(Font.FontFamily, 16F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
            Label description = new Label { AutoSize = true, MaximumSize = new Size(940, 0), Text = L("Performance.Description"), ForeColor = UiStyle.Muted, Margin = new Padding(0, 0, 0, 14) };

            Panel switchCard = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = UiStyle.Soft, Padding = new Padding(16, 10, 16, 8), Margin = new Padding(0, 0, 0, 14) };
            TableLayoutPanel switchLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            switchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            switchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            switchLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            switchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel switches = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 30, WrapContents = false, Margin = Padding.Empty };
            _enabled = NewSwitch(L("Performance.Toggle"), ScanPerformanceDiagnostics.Enabled);
            _warmup = NewSwitch(L("Performance.WarmupToggle"), ScanPerformanceDiagnostics.WarmupEnabled);
            _everything = NewSwitch(L("Performance.EverythingToggle"), ScanPerformanceDiagnostics.EverythingEnabled);
            switches.Controls.Add(_enabled); switches.Controls.Add(_warmup); switches.Controls.Add(_everything);
            _state = new Label { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = UiStyle.Muted, Margin = new Padding(0, 6, 0, 0) };
            Button simulation = UiStyle.NewButton(L("Performance.SimulationButton"), 170, true);
            simulation.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            simulation.Click += delegate { if (_showSimulation != null) _showSimulation(); };
            _enabled.CheckedChanged += SettingsChanged; _warmup.CheckedChanged += SettingsChanged; _everything.CheckedChanged += SettingsChanged;
            switchLayout.Controls.Add(switches, 0, 0);
            switchLayout.Controls.Add(simulation, 1, 0);
            switchLayout.SetRowSpan(simulation, 2);
            switchLayout.Controls.Add(_state, 0, 1);
            switchCard.Controls.Add(switchLayout); UpdateState();

            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            TableLayoutPanel historyPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
            historyPane.RowStyles.Add(new RowStyle(SizeType.AutoSize)); historyPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            historyPane.Controls.Add(SectionHeading(L("Performance.History")), 0, 0);
            _grid = BuildHistoryGrid(); historyPane.Controls.Add(_grid, 0, 1);

            TableLayoutPanel dataPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(12, 0, 0, 0) };
            dataPane.RowStyles.Add(new RowStyle(SizeType.AutoSize)); dataPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            dataPane.Controls.Add(SectionHeading(L("Performance.DataRecord")), 0, 0);
            TableLayoutPanel data = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 5, 8, 5) };
            data.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); data.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            AddDataRow(data, "node", L("Performance.BackgroundNode"));
            AddDataRow(data, "stage", L("Performance.CurrentStage"));
            AddDataRow(data, "error", L("Performance.FailureDetail"));
            AddDataRow(data, "date", L("Performance.Date"));
            AddDataRow(data, "provider", L("Performance.Provider"));
            AddDataRow(data, "results", L("Performance.FoundResults"));
            AddDataRow(data, "target", L("Performance.DirectoryIndex"));
            AddDataRow(data, "discovery", L("Performance.FileDiscovery"));
            AddDataRow(data, "providerQuery", L("Performance.ProviderQuery"));
            AddDataRow(data, "indexSync", L("Performance.IndexReconcile"));
            AddDataRow(data, "planRead", L("Performance.PlanCacheRead"));
            AddDataRow(data, "planWrite", L("Performance.PlanCacheWrite"));
            AddDataRow(data, "finalize", L("Performance.ScanFinalize"));
            AddDataRow(data, "snapshot", L("Performance.Snapshot"));
            AddDataRow(data, "indexDelta", L("Performance.IndexDelta"));
            AddDataRow(data, "planCache", L("Performance.PlanCache"));
            AddDataRow(data, "recalculated", L("Performance.Recalculated"));
            AddDataRow(data, "parsedCache", L("Performance.ParsedCache"));
            AddDataRow(data, "recognitionCache", L("Performance.RecognitionCache"));
            AddDataRow(data, "destinationCache", L("Performance.DestinationCache"));
            AddDataRow(data, "warmup", L("Performance.Warmup"));
            AddDataRow(data, "wait", L("Performance.WarmupWait"));
            AddDataRow(data, "author", L("Performance.AuthorMatch"));
            AddDataRow(data, "identities", L("Performance.IdentitySources"));
            AddDataRow(data, "targetDirs", L("Performance.TargetFolderDiscovery"));
            AddDataRow(data, "initialIndex", L("Performance.TargetIndexBuild"));
            AddDataRow(data, "incrementalIndex", L("Performance.TargetIndexIncrement"));
            AddDataRow(data, "prepared", L("Performance.RecognitionPreparation"));
            AddDataRow(data, "planning", L("Performance.RecognitionPlanning"));
            AddDataRow(data, "online", L("Performance.OnlineResolution"));
            AddDataRow(data, "other", L("Performance.OtherRecognition"));
            AddDataRow(data, "authors", L("Performance.UniqueAuthors"));
            AddDataRow(data, "ui", L("Performance.UiApply"));
            AddDataRow(data, "total", L("Performance.TotalResponse"));
            Panel dataScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            dataScroll.Controls.Add(data);
            dataPane.Controls.Add(dataScroll, 0, 1);
            content.Controls.Add(historyPane, 0, 0); content.Controls.Add(dataPane, 1, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            Button copy = UiStyle.NewButton(L("Performance.Copy"), 150, true); copy.Click += delegate { CopySelected(); };
            Button clear = UiStyle.NewButton(L("Performance.Clear"), 110, false); clear.Click += delegate
            {
                string error;
                if (!ScanPerformanceDiagnostics.TryClear(out error))
                {
                    MessageBox.Show(this, error, L("Performance.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _grid.Rows.Clear(); ShowSelectedDetail();
            };
            Button open = UiStyle.NewButton(L("Performance.OpenLog"), 140, false); open.Click += delegate { OpenLogLocation(); };
            actions.Controls.Add(copy); actions.Controls.Add(clear); actions.Controls.Add(open);
            root.Controls.Add(title, 0, 0); root.Controls.Add(description, 0, 1); root.Controls.Add(switchCard, 0, 2); root.Controls.Add(content, 0, 3); root.Controls.Add(actions, 0, 4); Controls.Add(root);

            _warmupStatus = ScanPerformanceDiagnostics.LastWarmupStatus();
            foreach (ScanPerformanceEntry entry in ScanPerformanceDiagnostics.Snapshot()) AddEntry(entry);
            if (_grid.Rows.Count > 0) { _grid.ClearSelection(); _grid.Rows[_grid.Rows.Count - 1].Selected = true; }
            ScanPerformanceDiagnostics.EntryAdded += OnEntryAdded; ScanPerformanceDiagnostics.WarmupStatusChanged += OnWarmupStatusChanged;
            FormClosed += delegate { ScanPerformanceDiagnostics.EntryAdded -= OnEntryAdded; ScanPerformanceDiagnostics.WarmupStatusChanged -= OnWarmupStatusChanged; };
            ShowSelectedDetail();
        }

        private string L(string key) { return _language.Get(key); }
        private Label SectionHeading(string text) { return new Label { AutoSize = true, Text = text, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) }; }
        private CheckBox NewSwitch(string text, bool value) { return new CheckBox { AutoSize = true, Text = text, Checked = value, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 3, 24, 0) }; }
        private void AddDataRow(TableLayoutPanel panel, string id, string name)
        {
            int row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            panel.Controls.Add(new Label { Dock = DockStyle.Fill, Text = name, ForeColor = UiStyle.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, row);
            Label value = new Label { Dock = DockStyle.Fill, Text = "—", Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            panel.Controls.Add(value, 1, row); _values[id] = value;
        }
        private DataGridView BuildHistoryGrid()
        {
            DataGridView grid = new FastDataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText };
            grid.Columns.Add("Date", L("Performance.Date")); grid.Columns.Add("Provider", L("Performance.Column.Provider")); grid.Columns.Add("Count", L("Performance.Column.Count"));
            grid.Columns.Add("Warmup", L("Performance.Column.Warmup")); grid.Columns.Add("Total", L("Performance.Column.Total")); UiStyle.StyleGrid(grid);
            UiStyle.ConfigureFillColumn(grid.Columns[0], 135, 165); UiStyle.ConfigureFillColumn(grid.Columns[1], 100, 130); UiStyle.ConfigureShortColumn(grid.Columns[2], 75);
            UiStyle.ConfigureShortColumn(grid.Columns[3], 75); UiStyle.ConfigureShortColumn(grid.Columns[4], 90); GridInteraction.Apply(grid, "ScanPerformance", _language, false);
            grid.SelectionChanged += delegate { ShowSelectedDetail(); };
            ContextMenuStrip menu = new ContextMenuStrip(); ToolStripMenuItem copy = new ToolStripMenuItem(L("Performance.CopySelected")); copy.Click += delegate { CopySelected(); };
            menu.Items.Add(copy); UiStyle.StyleMenu(menu); grid.ContextMenuStrip = menu; return grid;
        }
        private void SettingsChanged(object sender, EventArgs e)
        { ScanPerformanceDiagnostics.Enabled = _enabled.Checked; ScanPerformanceDiagnostics.WarmupEnabled = _warmup.Checked; ScanPerformanceDiagnostics.EverythingEnabled = _everything.Checked; if (_saveSettings != null) _saveSettings(_enabled.Checked, _warmup.Checked, _everything.Checked); UpdateState(); }
        private void UpdateState() { _state.Text = L(_enabled.Checked ? "Performance.State.On" : "Performance.State.Off"); }
        private void OnEntryAdded(ScanPerformanceEntry entry)
        { if (IsDisposed || Disposing) return; if (InvokeRequired) { try { BeginInvoke(new Action<ScanPerformanceEntry>(OnEntryAdded), entry); } catch { } return; } AddEntry(entry); _grid.ClearSelection(); _grid.Rows[_grid.Rows.Count - 1].Selected = true; }
        private void OnWarmupStatusChanged(ScanWarmupStatusEntry status)
        { if (IsDisposed || Disposing) return; if (InvokeRequired) { try { BeginInvoke(new Action<ScanWarmupStatusEntry>(OnWarmupStatusChanged), status); } catch { } return; } _warmupStatus = status; ShowSelectedDetail(); }
        private void AddEntry(ScanPerformanceEntry e)
        {
            int index = _grid.Rows.Add(e.Time.ToString("yyyy-MM-dd HH:mm:ss"), Provider(e.Provider), e.ResultCount,
                HitStatus(e), e.TotalResponseMs + " ms");
            _grid.Rows[index].Tag = e; if (_grid.Rows.Count == 1) _grid.Rows[0].Selected = true;
        }
        private ScanPerformanceEntry SelectedEntry() { return _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as ScanPerformanceEntry; }
        private void ShowSelectedDetail()
        {
            ScanPerformanceEntry e = SelectedEntry(); foreach (Label label in _values.Values) label.Text = "—";
            if (e != null)
            {
                _values["node"].Text = e.SnapshotHit ? L("Performance.Node.Ready") : L("Performance.Status.Direct");
                _values["date"].Text = e.Time.ToString("yyyy-MM-dd HH:mm:ss"); _values["provider"].Text = Provider(e.Provider); _values["results"].Text = e.ResultCount.ToString("N0");
                SetMs("discovery", e.FileDiscoveryMs, true);
                SetMs("providerQuery", e.ProviderQueryMs, true);
                SetMs("indexSync", e.IndexReconcileMs, true);
                SetMs("planRead", e.PlanCacheReadMs, true);
                SetMs("planWrite", e.PlanCacheWriteMs, true);
                SetMs("finalize", e.FinalizeMs, true);
                SetMs("snapshot", e.SnapshotPrepareMs, e.WarmupHit); _values["warmup"].Text = HitStatus(e);
                _values["indexDelta"].Text = IndexSummary(e);
                _values["planCache"].Text = e.PlanCacheHits.ToString("N0");
                _values["recalculated"].Text = e.RecalculatedFiles.ToString("N0");
                _values["parsedCache"].Text = CacheRatio(e.ParsedCacheHits, e.ParsedCacheMisses);
                _values["recognitionCache"].Text = CacheRatio(e.RecognitionCacheHits, e.RecognitionCacheMisses);
                _values["destinationCache"].Text = CacheRatio(e.DestinationCacheHits, e.DestinationCacheMisses);
                SetMs("wait", e.WarmupWaitMs, true); SetMs("author", e.AuthorMatchMs, true);
                SetMs("identities", e.IdentitySourceMs, e.InitialIndexBuilds > 0);
                SetMs("targetDirs", e.TargetDirectoryMs, e.InitialIndexBuilds > 0);
                _values["initialIndex"].Text = e.InitialIndexBuilds > 0 ? e.InitialIndexMs + " ms / " + e.InitialIndexBuilds : "—";
                _values["incrementalIndex"].Text = e.InitialIndexBuilds > 0 ? e.IncrementalIndexMs + " ms / " + e.IncrementalIndexAdds : "—";
                SetMs("prepared", e.PrepareRecognitionMs, e.InitialIndexBuilds > 0);
                SetMs("planning", e.PlanningLoopMs, e.InitialIndexBuilds > 0);
                SetMs("online", e.OnlineLookupMs, e.InitialIndexBuilds > 0);
                SetMs("other", OtherRecognitionMs(e), e.InitialIndexBuilds > 0);
                _values["authors"].Text = e.InitialIndexBuilds > 0 ? e.UniqueAuthors + " / " + e.NewAuthorFolders + " (" + L("Performance.NewFoldersShort") + ")" : "—";
                SetMs("ui", e.UiApplyMs, true); SetMs("total", e.TotalResponseMs, true);
            }
            // Historical scan details always take priority over live preparation status.
            // After restart a new warmup event is newer than every historical scan;
            // comparing timestamps used to erase the selected scan's visible metrics.
            // Live preparation information is shown only when no scan is selected.
            if (ShouldShowWarmupForSelection(e, _warmupStatus))
            {
                _values["node"].Text = WarmupState(_warmupStatus.State); _values["date"].Text = _warmupStatus.Time.ToString("yyyy-MM-dd HH:mm:ss");
                _values["stage"].Text = String.IsNullOrWhiteSpace(_warmupStatus.Stage) ? "—" : _warmupStatus.Stage;
                _values["error"].Text = String.IsNullOrWhiteSpace(_warmupStatus.Error) ? "—" : _warmupStatus.Error;
                if (!String.IsNullOrWhiteSpace(_warmupStatus.Provider)) { _values["provider"].Text = Provider(_warmupStatus.Provider); _values["results"].Text = _warmupStatus.CandidateCount.ToString("N0"); }
                bool discoveryReady = _warmupStatus.State == ScanWarmupState.PartiallyReady || _warmupStatus.State == ScanWarmupState.Ready;
                SetMs("target", _warmupStatus.TargetIndexMs, true); SetMs("discovery", _warmupStatus.FileDiscoveryMs, discoveryReady);
                SetMs("snapshot", _warmupStatus.SnapshotPrepareMs, _warmupStatus.State == ScanWarmupState.Ready);
                _values["parsedCache"].Text = CacheRatio(_warmupStatus.ParsedCacheHits, _warmupStatus.ParsedCacheMisses);
                _values["recognitionCache"].Text = CacheRatio(_warmupStatus.RecognitionCacheHits, _warmupStatus.RecognitionCacheMisses);
                _values["destinationCache"].Text = CacheRatio(_warmupStatus.DestinationCacheHits, _warmupStatus.DestinationCacheMisses);
                _values["indexDelta"].Text = "—"; _values["planCache"].Text = "—"; _values["recalculated"].Text = "—";
                foreach (string detail in new string[] { "identities", "targetDirs", "initialIndex",
                    "incrementalIndex", "prepared", "planning", "online", "other", "authors" })
                    _values[detail].Text = "—";
                _values["warmup"].Text = "—"; _values["wait"].Text = "—"; _values["author"].Text = "—"; _values["ui"].Text = "—"; _values["total"].Text = "—";
            }
        }
        internal static bool ShouldShowWarmupForSelection(ScanPerformanceEntry selected, ScanWarmupStatusEntry warmup)
        { return selected == null && warmup != null; }
        private void SetMs(string id, long value, bool available) { _values[id].Text = available ? value + " ms" : "—"; }
        private string CacheRatio(long hits, long misses)
        {
            long total = hits + misses;
            if (total <= 0) return "—";
            return String.Format("{0:N0} / {1:N0} ({2:0.0}%)", hits, misses, hits * 100.0 / total);
        }
        private string WarmupState(ScanWarmupState state)
        { return L(state == ScanWarmupState.Preparing || state == ScanWarmupState.PartiallyReady ? "Performance.Node.Preparing" : state == ScanWarmupState.Ready ? "Performance.Node.Ready" : state == ScanWarmupState.Failed ? "Performance.Node.Failed" : "Performance.Node.Invalidated"); }
        private void CopySelected()
        {
            ScanPerformanceEntry entry = SelectedEntry();
            // Copy the selected historical report, even if background warmup
            // (including a failed warmup) happened after that scan.
            if (entry == null && _warmupStatus != null && _warmupStatus.State == ScanWarmupState.Failed)
            {
                Clipboard.SetText(
                    "后台准备节点: " + WarmupState(_warmupStatus.State) + Environment.NewLine +
                    "日期: " + _warmupStatus.Time.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    "阶段: " + (_warmupStatus.Stage ?? "") + Environment.NewLine +
                    "错误: " + (_warmupStatus.Error ?? ""));
                return;
            }
            if (entry != null) Clipboard.SetText(Format(entry));
        }
        private string Format(ScanPerformanceEntry e)
        {
            StringBuilder s = new StringBuilder(); s.AppendLine(L("Performance.ReportTitle")); Add(s, "Performance.Date", e.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            Add(s, "Performance.Provider", Provider(e.Provider)); Add(s, "Performance.Results", e.ResultCount.ToString()); Add(s, "Performance.Warmup", HitStatus(e));
            Add(s, "Performance.IndexDelta", IndexSummary(e));
            Add(s, "Performance.PlanCache", e.PlanCacheHits.ToString());
            Add(s, "Performance.Recalculated", e.RecalculatedFiles.ToString());
            Metric(s, "Performance.Snapshot", e.SnapshotPrepareMs); Metric(s, "Performance.WarmupWait", e.WarmupWaitMs); Metric(s, "Performance.FileDiscovery", e.FileDiscoveryMs);
            Metric(s, "Performance.ProviderQuery", e.ProviderQueryMs);
            Metric(s, "Performance.IndexReconcile", e.IndexReconcileMs);
            Add(s, "Performance.PlanCacheRead", e.PlanCacheReadMs + " ms");
            Add(s, "Performance.PlanCacheWrite", e.PlanCacheWriteMs + " ms");
            Add(s, "Performance.ScanFinalize", e.FinalizeMs + " ms");
            Metric(s, "Performance.AuthorMatch", e.AuthorMatchMs);
            if (e.InitialIndexBuilds > 0)
            {
                Metric(s, "Performance.IdentitySources", e.IdentitySourceMs);
                Metric(s, "Performance.TargetFolderDiscovery", e.TargetDirectoryMs);
                Add(s, "Performance.TargetIndexBuild", e.InitialIndexMs + " ms / " + e.InitialIndexBuilds);
                Add(s, "Performance.TargetIndexIncrement", e.IncrementalIndexMs + " ms / " + e.IncrementalIndexAdds);
                Metric(s, "Performance.RecognitionPreparation", e.PrepareRecognitionMs);
                Metric(s, "Performance.RecognitionPlanning", e.PlanningLoopMs);
                Metric(s, "Performance.OnlineResolution", e.OnlineLookupMs);
                Add(s, "Performance.OtherRecognition", OtherRecognitionMs(e) + " ms");
                Add(s, "Performance.UniqueAuthors", e.UniqueAuthors + " / " + e.NewAuthorFolders + " (" + L("Performance.NewFoldersShort") + ")");
            }
            Metric(s, "Performance.UiApply", e.UiApplyMs); Metric(s, "Performance.TotalResponse", e.TotalResponseMs); return s.ToString().TrimEnd();
        }
        private void Add(StringBuilder s, string key, string value) { s.AppendLine(L(key) + ": " + value); }
        private void Metric(StringBuilder s, string key, long value) { if (value > 0) Add(s, key, value + " ms"); }
        // This residual includes durable plan cache I/O, group preparation and
        // any work outside ArchiveEngine, not an additional independently timed stage.
        // Incremental index updates are nested in PlanningLoopMs: never add them twice.
        private static long OtherRecognitionMs(ScanPerformanceEntry e)
        {
            return Math.Max(0, e.AuthorMatchMs -
                e.IdentitySourceMs - e.TargetDirectoryMs - e.InitialIndexMs -
                e.PrepareRecognitionMs - e.PlanningLoopMs - e.OnlineLookupMs);
        }

        private string IndexSummary(ScanPerformanceEntry e)
        {
            return String.Format(
                "{0:N0} / +{1:N0} ~{2:N0} >{3:N0} R{4:N0} -{5:N0}",
                e.IndexCacheHits, e.IndexAdded, e.IndexModified,
                e.IndexMoved, e.IndexRenamed, e.IndexRemoved);
        }
        private string HitStatus(ScanPerformanceEntry e) { return e.SnapshotHit ? L("Performance.Status.HitShort") : (e.WarmupHit ? L("Performance.Status.PartialHit") : L("Performance.Status.Direct")); }
        private string Provider(string value) { return String.Equals(value, "Everything SDK", StringComparison.OrdinalIgnoreCase) ? L("Performance.Provider.Everything") : L("Performance.Provider.System"); }
        private void OpenLogLocation() { try { Process.Start("explorer.exe", "/select,\"" + _logPath + "\""); } catch { } }
    }
}
