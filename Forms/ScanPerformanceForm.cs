using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class ScanPerformanceForm : Form
    {
        private readonly LanguageManager _language;
        private readonly string _logPath;
        private readonly Action<bool, bool, bool> _saveSettings;
        private readonly CheckBox _enabled;
        private readonly CheckBox _warmup;
        private readonly CheckBox _everything;
        private readonly Label _state;
        private readonly DataGridView _grid;
        private readonly TextBox _detail;

        public ScanPerformanceForm(LanguageManager language, string logPath, Action<bool, bool, bool> saveSettings)
        {
            _language = language; _logPath = logPath; _saveSettings = saveSettings;
            Text = L("Performance.Title");
            Size = new Size(980, 700); MinimumSize = new Size(760, 520); ShowInTaskbar = true;
            UiStyle.ApplyDialog(this, SystemFonts.MessageBoxFont);
            // The owner re-centers this form after DPI auto-scaling has completed.
            // CenterParent can use stale pre-scale bounds for a non-modal window.
            StartPosition = FormStartPosition.Manual;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label title = new Label { AutoSize = true, Text = L("Performance.Title"), Font = new Font(Font.FontFamily, 16F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
            Label description = new Label { AutoSize = true, MaximumSize = new Size(900, 0), Text = L("Performance.Description"), ForeColor = UiStyle.Muted, Margin = new Padding(0, 0, 0, 18) };
            Panel switchCard = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = UiStyle.Soft, Padding = new Padding(16, 10, 16, 8), Margin = new Padding(0, 0, 0, 16) };
            FlowLayoutPanel switches = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false, AutoSize = false };
            _enabled = NewSwitch(L("Performance.Toggle"), ScanPerformanceDiagnostics.Enabled);
            _warmup = NewSwitch(L("Performance.WarmupToggle"), ScanPerformanceDiagnostics.WarmupEnabled);
            _everything = NewSwitch(L("Performance.EverythingToggle"), ScanPerformanceDiagnostics.EverythingEnabled);
            switches.Controls.Add(_enabled); switches.Controls.Add(_warmup); switches.Controls.Add(_everything);
            _state = new Label { AutoSize = true, MaximumSize = new Size(880, 0), ForeColor = UiStyle.Muted, Location = new Point(17, 47) };
            _enabled.CheckedChanged += SettingsChanged; _warmup.CheckedChanged += SettingsChanged; _everything.CheckedChanged += SettingsChanged;
            switchCard.Controls.Add(switches); switchCard.Controls.Add(_state); UpdateState();

            _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText, Margin = new Padding(0, 0, 0, 12) };
            _grid.Columns.Add("Time", L("Performance.Column.Time")); _grid.Columns.Add("Provider", L("Performance.Column.Provider"));
            _grid.Columns.Add("Warmup", L("Performance.Column.Warmup")); _grid.Columns.Add("Everything", L("Performance.Column.Everything"));
            _grid.Columns.Add("Count", L("Performance.Column.Count")); _grid.Columns.Add("Total", L("Performance.Column.Total"));
            _grid.Columns.Add("Status", L("Performance.Column.Status"));
            UiStyle.StyleGrid(_grid); UiStyle.ConfigureShortColumn(_grid.Columns[0], 115); UiStyle.ConfigureFillColumn(_grid.Columns[1], 130, 150);
            UiStyle.ConfigureShortColumn(_grid.Columns[2], 75); UiStyle.ConfigureShortColumn(_grid.Columns[3], 95);
            UiStyle.ConfigureShortColumn(_grid.Columns[4], 80); UiStyle.ConfigureShortColumn(_grid.Columns[5], 105); UiStyle.ConfigureFillColumn(_grid.Columns[6], 90, 120);
            _grid.SelectionChanged += delegate { ShowSelectedDetail(); };
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem copyMenu = new ToolStripMenuItem(L("Performance.CopySelected")); copyMenu.Click += delegate { CopySelected(); }; menu.Items.Add(copyMenu); UiStyle.StyleMenu(menu); _grid.ContextMenuStrip = menu;

            _detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = Font, Margin = new Padding(0, 0, 0, 12) };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button copy = UiStyle.NewButton(L("Performance.Copy"), 150, true); copy.Click += delegate { CopySelected(); };
            Button clear = UiStyle.NewButton(L("Performance.Clear"), 110, false); clear.Click += delegate { ScanPerformanceDiagnostics.Clear(); _grid.Rows.Clear(); _detail.Clear(); };
            Button open = UiStyle.NewButton(L("Performance.OpenLog"), 140, false); open.Click += delegate { OpenLogLocation(); };
            actions.Controls.Add(copy); actions.Controls.Add(clear); actions.Controls.Add(open);
            root.Controls.Add(title, 0, 0); root.Controls.Add(description, 0, 1); root.Controls.Add(switchCard, 0, 2);
            root.Controls.Add(_grid, 0, 3); root.Controls.Add(_detail, 0, 4); root.Controls.Add(actions, 0, 5); Controls.Add(root);

            foreach (ScanPerformanceEntry entry in ScanPerformanceDiagnostics.Snapshot()) AddEntry(entry);
            ScanPerformanceDiagnostics.EntryAdded += OnEntryAdded;
            FormClosed += delegate { ScanPerformanceDiagnostics.EntryAdded -= OnEntryAdded; };
        }

        private string L(string key) { return _language.Get(key); }
        private CheckBox NewSwitch(string text, bool value)
        { return new CheckBox { AutoSize = true, Text = text, Checked = value, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 3, 24, 0) }; }
        private void SettingsChanged(object sender, EventArgs e)
        {
            ScanPerformanceDiagnostics.Enabled = _enabled.Checked;
            ScanPerformanceDiagnostics.WarmupEnabled = _warmup.Checked;
            ScanPerformanceDiagnostics.EverythingEnabled = _everything.Checked;
            if (_saveSettings != null) _saveSettings(_enabled.Checked, _warmup.Checked, _everything.Checked);
            UpdateState();
        }
        private void UpdateState()
        {
            _state.Text = L(_enabled.Checked ? "Performance.State.On" : "Performance.State.Off") + Environment.NewLine +
                L("Performance.RecommendedHint");
        }
        private void OnEntryAdded(ScanPerformanceEntry entry)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<ScanPerformanceEntry>(OnEntryAdded), entry); } catch { } return; }
            AddEntry(entry);
        }
        private void AddEntry(ScanPerformanceEntry e)
        {
            int index = _grid.Rows.Add(e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"), Provider(e.Provider), YesNo(e.WarmupEnabled),
                YesNo(e.EverythingEnabled), e.ResultCount, e.TotalResponseMs + " ms",
                e.WarmupHit ? L("Performance.Status.Hit") : L("Performance.Status.Direct"));
            _grid.Rows[index].Tag = e; if (_grid.Rows.Count == 1) _grid.Rows[0].Selected = true;
        }
        private void ShowSelectedDetail()
        {
            if (_grid.SelectedRows.Count == 0) { _detail.Clear(); return; }
            List<ScanPerformanceEntry> entries = SelectedEntries();
            StringBuilder text = new StringBuilder();
            foreach (ScanPerformanceEntry entry in entries) { if (text.Length > 0) text.AppendLine().AppendLine(); text.Append(Format(entry)); }
            _detail.Text = text.ToString();
        }
        private List<ScanPerformanceEntry> SelectedEntries()
        {
            List<DataGridViewRow> rows = new List<DataGridViewRow>(); foreach (DataGridViewRow row in _grid.SelectedRows) rows.Add(row);
            rows.Sort(delegate(DataGridViewRow a, DataGridViewRow b) { return a.Index.CompareTo(b.Index); });
            List<ScanPerformanceEntry> result = new List<ScanPerformanceEntry>(); foreach (DataGridViewRow row in rows) if (row.Tag is ScanPerformanceEntry) result.Add((ScanPerformanceEntry)row.Tag); return result;
        }
        private void CopySelected() { ShowSelectedDetail(); if (_detail.TextLength > 0) Clipboard.SetText(_detail.Text); }
        private string Format(ScanPerformanceEntry e)
        {
            StringBuilder s = new StringBuilder(); s.AppendLine(L("Performance.ReportTitle"));
            s.AppendLine(L("Performance.Version") + ": V" + Application.ProductVersion); s.AppendLine(L("Performance.Time") + ": " + e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff")); s.AppendLine();
            Add(s, "Performance.Provider", Provider(e.Provider)); Add(s, "Performance.Warmup", YesNo(e.WarmupEnabled)); Add(s, "Performance.Everything", YesNo(e.EverythingEnabled));
            Add(s, "Performance.WarmupHit", YesNo(e.WarmupHit)); Add(s, "Performance.ReadyState", e.ReadyBeforeRequest ? L("Performance.Ready") : L("Performance.NotReady"));
            Add(s, "Performance.HitType", e.ReadyBeforeRequest ? L("Performance.HitType.Full") : (e.WarmupHit ? L("Performance.HitType.InFlight") : L("Performance.HitType.None"))); Add(s, "Performance.SnapshotHit", YesNo(e.SnapshotHit)); s.AppendLine();
            Metric(s, "Performance.WarmupWait", e.WarmupWaitMs); Metric(s, "Performance.SnapshotPrepare", e.SnapshotPrepareMs); Metric(s, "Performance.FileDiscovery", e.FileDiscoveryMs);
            Metric(s, "Performance.TargetIndex", e.TargetIndexMs); Metric(s, "Performance.CandidatePrepare", e.CandidatePrepareMs); Metric(s, "Performance.AuthorMatch", e.AuthorMatchMs);
            Metric(s, "Performance.Sort", e.SortMs); Metric(s, "Performance.RowBuild", e.RowBuildMs); Metric(s, "Performance.AddRows", e.AddRowsMs); Metric(s, "Performance.Layout", e.LayoutMs);
            Metric(s, "Performance.Finalize", e.FinalizeMs); Metric(s, "Performance.UiApply", e.UiApplyMs); Metric(s, "Performance.TotalResponse", e.TotalResponseMs); s.AppendLine();
            Add(s, "Performance.Candidates", e.CandidateCount.ToString()); Add(s, "Performance.Results", e.ResultCount.ToString()); return s.ToString().TrimEnd();
        }
        private void Add(StringBuilder s, string key, string value) { s.AppendLine(L(key) + ": " + value); }
        private void Metric(StringBuilder s, string key, long value) { Add(s, key, value + " ms"); }
        private string YesNo(bool value) { return L(value ? "Common.Yes" : "Common.No"); }
        private string Provider(string value) { return String.Equals(value, "Everything SDK", StringComparison.OrdinalIgnoreCase) ? L("Performance.Provider.Everything") : L("Performance.Provider.System"); }
        private void OpenLogLocation() { try { string dir = Path.GetDirectoryName(_logPath); Process.Start("explorer.exe", "/select,\"" + _logPath + "\""); } catch { } }
    }
}
