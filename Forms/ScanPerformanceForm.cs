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
        private readonly CheckBox _enabled, _warmup, _everything;
        private readonly Label _state;
        private readonly DataGridView _grid;
        private readonly Dictionary<string, Label> _values = new Dictionary<string, Label>(StringComparer.Ordinal);
        private ScanWarmupStatusEntry _warmupStatus;

        public ScanPerformanceForm(LanguageManager language, string logPath, Action<bool, bool, bool> saveSettings)
        {
            _language = language; _logPath = logPath; _saveSettings = saveSettings;
            Text = L("Performance.Title"); Size = new Size(1000, 790); MinimumSize = new Size(820, 650); ShowInTaskbar = true;
            UiStyle.ApplyDialog(this, SystemFonts.MessageBoxFont); StartPosition = FormStartPosition.Manual;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label title = new Label { AutoSize = true, Text = L("Performance.Title"), Font = new Font(Font.FontFamily, 16F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
            Label description = new Label { AutoSize = true, MaximumSize = new Size(940, 0), Text = L("Performance.Description"), ForeColor = UiStyle.Muted, Margin = new Padding(0, 0, 0, 14) };

            Panel switchCard = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = UiStyle.Soft, Padding = new Padding(16, 10, 16, 8), Margin = new Padding(0, 0, 0, 14) };
            FlowLayoutPanel switches = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, WrapContents = false };
            _enabled = NewSwitch(L("Performance.Toggle"), ScanPerformanceDiagnostics.Enabled);
            _warmup = NewSwitch(L("Performance.WarmupToggle"), ScanPerformanceDiagnostics.WarmupEnabled);
            _everything = NewSwitch(L("Performance.EverythingToggle"), ScanPerformanceDiagnostics.EverythingEnabled);
            switches.Controls.Add(_enabled); switches.Controls.Add(_warmup); switches.Controls.Add(_everything);
            _state = new Label { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = UiStyle.Muted, Location = new Point(17, 45) };
            _enabled.CheckedChanged += SettingsChanged; _warmup.CheckedChanged += SettingsChanged; _everything.CheckedChanged += SettingsChanged;
            switchCard.Controls.Add(switches); switchCard.Controls.Add(_state); UpdateState();

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
            AddDataRow(data, "date", L("Performance.Date"));
            AddDataRow(data, "provider", L("Performance.Provider"));
            AddDataRow(data, "results", L("Performance.FoundResults"));
            AddDataRow(data, "target", L("Performance.DirectoryIndex"));
            AddDataRow(data, "discovery", L("Performance.FileDiscovery"));
            AddDataRow(data, "snapshot", L("Performance.Snapshot"));
            AddDataRow(data, "warmup", L("Performance.Warmup"));
            AddDataRow(data, "wait", L("Performance.WarmupWait"));
            AddDataRow(data, "author", L("Performance.AuthorMatch"));
            AddDataRow(data, "ui", L("Performance.UiApply"));
            AddDataRow(data, "total", L("Performance.TotalResponse"));
            dataPane.Controls.Add(data, 0, 1);
            content.Controls.Add(historyPane, 0, 0); content.Controls.Add(dataPane, 1, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
            Button copy = UiStyle.NewButton(L("Performance.Copy"), 150, true); copy.Click += delegate { CopySelected(); };
            Button clear = UiStyle.NewButton(L("Performance.Clear"), 110, false); clear.Click += delegate { ScanPerformanceDiagnostics.Clear(); _grid.Rows.Clear(); ShowSelectedDetail(); };
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
            int row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
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
                SetMs("discovery", e.FileDiscoveryMs, true); SetMs("snapshot", e.SnapshotPrepareMs, e.WarmupHit); _values["warmup"].Text = HitStatus(e);
                SetMs("wait", e.WarmupWaitMs, true); SetMs("author", e.AuthorMatchMs, true); SetMs("ui", e.UiApplyMs, true); SetMs("total", e.TotalResponseMs, true);
            }
            // A newer background preparation belongs to the user's latest action.
            // It must take precedence over whichever historical scan is selected.
            if (_warmupStatus != null && (e == null || _warmupStatus.Time > e.Time))
            {
                _values["node"].Text = WarmupState(_warmupStatus.State); _values["date"].Text = _warmupStatus.Time.ToString("yyyy-MM-dd HH:mm:ss");
                if (!String.IsNullOrWhiteSpace(_warmupStatus.Provider)) { _values["provider"].Text = Provider(_warmupStatus.Provider); _values["results"].Text = _warmupStatus.CandidateCount.ToString("N0"); }
                bool discoveryReady = _warmupStatus.State == ScanWarmupState.PartiallyReady || _warmupStatus.State == ScanWarmupState.Ready;
                SetMs("target", _warmupStatus.TargetIndexMs, true); SetMs("discovery", _warmupStatus.FileDiscoveryMs, discoveryReady);
                SetMs("snapshot", _warmupStatus.SnapshotPrepareMs, _warmupStatus.State == ScanWarmupState.Ready);
                _values["warmup"].Text = "—"; _values["wait"].Text = "—"; _values["author"].Text = "—"; _values["ui"].Text = "—"; _values["total"].Text = "—";
            }
        }
        private void SetMs(string id, long value, bool available) { _values[id].Text = available ? value + " ms" : "—"; }
        private string WarmupState(ScanWarmupState state)
        { return L(state == ScanWarmupState.Preparing || state == ScanWarmupState.PartiallyReady ? "Performance.Node.Preparing" : state == ScanWarmupState.Ready ? "Performance.Node.Ready" : state == ScanWarmupState.Failed ? "Performance.Node.Failed" : "Performance.Node.Invalidated"); }
        private void CopySelected() { ScanPerformanceEntry entry = SelectedEntry(); if (entry != null) Clipboard.SetText(Format(entry)); }
        private string Format(ScanPerformanceEntry e)
        {
            StringBuilder s = new StringBuilder(); s.AppendLine(L("Performance.ReportTitle")); Add(s, "Performance.Date", e.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            Add(s, "Performance.Provider", Provider(e.Provider)); Add(s, "Performance.Results", e.ResultCount.ToString()); Add(s, "Performance.Warmup", HitStatus(e));
            Metric(s, "Performance.Snapshot", e.SnapshotPrepareMs); Metric(s, "Performance.WarmupWait", e.WarmupWaitMs); Metric(s, "Performance.FileDiscovery", e.FileDiscoveryMs);
            Metric(s, "Performance.AuthorMatch", e.AuthorMatchMs); Metric(s, "Performance.UiApply", e.UiApplyMs); Metric(s, "Performance.TotalResponse", e.TotalResponseMs); return s.ToString().TrimEnd();
        }
        private void Add(StringBuilder s, string key, string value) { s.AppendLine(L(key) + ": " + value); }
        private void Metric(StringBuilder s, string key, long value) { if (value > 0) Add(s, key, value + " ms"); }
        private string HitStatus(ScanPerformanceEntry e) { return e.SnapshotHit ? L("Performance.Status.HitShort") : (e.WarmupHit ? L("Performance.Status.PartialHit") : L("Performance.Status.Direct")); }
        private string Provider(string value) { return String.Equals(value, "Everything SDK", StringComparison.OrdinalIgnoreCase) ? L("Performance.Provider.Everything") : L("Performance.Provider.System"); }
        private void OpenLogLocation() { try { Process.Start("explorer.exe", "/select,\"" + _logPath + "\""); } catch { } }
    }
}
