using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class HistoryForm : Form
    {
        private readonly HistoryStore _store;
        private readonly LanguageManager _language;
        private readonly FastDataGridView _sessionGrid;
        private readonly FastDataGridView _itemGrid;
        private readonly Label _summary;
        private readonly Button _exportButton;
        private readonly Button _deleteButton;
        private readonly Button _clearButton;
        private List<HistorySession> _sessions = new List<HistorySession>();

        public HistoryForm(HistoryStore store, LanguageManager language, Font appFont)
        {
            _store = store;
            _language = language;

            Text = L("History.Title");
            ClientSize = new Size(1160, 720);
            MinimumSize = new Size(900, 560);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 6;
            root.ColumnCount = 1;
            root.Padding = new Padding(18, 16, 18, 14);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 188F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("History.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            header.Controls.Add(title);
            Label desc = new Label();
            desc.Text = L("History.Description");
            desc.ForeColor = UiStyle.Muted;
            desc.Location = new Point(0, 27);
            desc.Size = new Size(1050, 24);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(desc);
            root.Controls.Add(header, 0, 0);

            Label sessionTitle = new Label();
            sessionTitle.Text = L("History.SessionList");
            sessionTitle.Font = new Font(appFont, FontStyle.Bold);
            sessionTitle.Dock = DockStyle.Fill;
            sessionTitle.TextAlign = ContentAlignment.BottomLeft;
            root.Controls.Add(sessionTitle, 0, 1);

            _sessionGrid = CreateGrid();
            _sessionGrid.MultiSelect = false;
            DataGridViewTextBoxColumn timeColumn = NewColumn("Time", L("History.Time"), 150);
            DataGridViewTextBoxColumn totalColumn = NewColumn("Total", L("History.Total"), 64);
            DataGridViewTextBoxColumn successColumn = NewColumn("Success", L("History.Success"), 64);
            DataGridViewTextBoxColumn skippedColumn = NewColumn("Skipped", L("History.Skipped"), 64);
            DataGridViewTextBoxColumn failedColumn = NewColumn("Failed", L("History.Failed"), 64);
            DataGridViewTextBoxColumn sourceColumn = NewColumn("Source", L("History.Source"), 300);
            DataGridViewTextBoxColumn targetColumn = NewColumn("Target", L("History.Target"), 300);
            UiStyle.ConfigureShortColumn(timeColumn, 130);
            UiStyle.ConfigureShortColumn(totalColumn, 56);
            UiStyle.ConfigureShortColumn(successColumn, 56);
            UiStyle.ConfigureShortColumn(skippedColumn, 56);
            UiStyle.ConfigureShortColumn(failedColumn, 56);
            UiStyle.ConfigureFillColumn(sourceColumn, 50F, 240);
            UiStyle.ConfigureFillColumn(targetColumn, 50F, 240);
            _sessionGrid.Columns.Add(timeColumn);
            _sessionGrid.Columns.Add(totalColumn);
            _sessionGrid.Columns.Add(successColumn);
            _sessionGrid.Columns.Add(skippedColumn);
            _sessionGrid.Columns.Add(failedColumn);
            _sessionGrid.Columns.Add(sourceColumn);
            _sessionGrid.Columns.Add(targetColumn);
            GridInteraction.Apply(_sessionGrid, "History.Sessions", _language, true);
            _sessionGrid.SelectionChanged += delegate { RenderItems(); };
            root.Controls.Add(_sessionGrid, 0, 2);

            Panel summaryHost = new Panel();
            summaryHost.Dock = DockStyle.Fill;
            Label itemTitle = new Label();
            itemTitle.Text = L("History.ItemList");
            itemTitle.Font = new Font(appFont, FontStyle.Bold);
            itemTitle.AutoSize = true;
            itemTitle.Location = new Point(0, 8);
            summaryHost.Controls.Add(itemTitle);
            _summary = new Label();
            _summary.ForeColor = UiStyle.Muted;
            _summary.AutoEllipsis = true;
            _summary.TextAlign = ContentAlignment.MiddleRight;
            _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _summary.Location = new Point(120, 4);
            _summary.Size = new Size(930, 28);
            summaryHost.Controls.Add(_summary);
            root.Controls.Add(summaryHost, 0, 3);

            _itemGrid = CreateGrid();
            DataGridViewTextBoxColumn fileColumn = NewColumn("FileName", L("History.FileName"), 270);
            DataGridViewTextBoxColumn authorColumn = NewColumn("Author", L("History.Author"), 130);
            DataGridViewTextBoxColumn matchedColumn = NewColumn("Matched", L("History.Matched"), 130);
            DataGridViewTextBoxColumn statusColumn = NewColumn("Status", L("History.Status"), 115);
            DataGridViewTextBoxColumn resultColumn = NewColumn("Result", L("History.Result"), 100);
            DataGridViewTextBoxColumn messageColumn = NewColumn("ResultMessage", L("History.ResultMessage"), 240);
            DataGridViewTextBoxColumn sourcePathColumn = NewColumn("SourcePath", L("History.SourcePath"), 330);
            DataGridViewTextBoxColumn targetPathColumn = NewColumn("TargetPath", L("History.TargetPath"), 330);
            DataGridViewTextBoxColumn reasonColumn = NewColumn("Reason", L("History.Reason"), 300);
            UiStyle.ConfigureFillColumn(fileColumn, 25F, 220);
            UiStyle.ConfigureFillColumn(authorColumn, 10F, 110);
            UiStyle.ConfigureFillColumn(matchedColumn, 10F, 110);
            UiStyle.ConfigureShortColumn(statusColumn, 92);
            UiStyle.ConfigureShortColumn(resultColumn, 82);
            UiStyle.ConfigureFillColumn(messageColumn, 15F, 180);
            UiStyle.ConfigureFillColumn(sourcePathColumn, 16F, 240);
            UiStyle.ConfigureFillColumn(targetPathColumn, 16F, 240);
            UiStyle.ConfigureFillColumn(reasonColumn, 12F, 180);
            _itemGrid.Columns.Add(fileColumn);
            _itemGrid.Columns.Add(authorColumn);
            _itemGrid.Columns.Add(matchedColumn);
            _itemGrid.Columns.Add(statusColumn);
            _itemGrid.Columns.Add(resultColumn);
            _itemGrid.Columns.Add(messageColumn);
            _itemGrid.Columns.Add(sourcePathColumn);
            _itemGrid.Columns.Add(targetPathColumn);
            _itemGrid.Columns.Add(reasonColumn);
            GridInteraction.Apply(_itemGrid, "History.Items", _language, true);
            root.Controls.Add(_itemGrid, 0, 4);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.WrapContents = false;
            left.Padding = new Padding(0, 7, 0, 0);
            _exportButton = UiStyle.NewButton(L("History.ExportCsv"), 112, false);
            _exportButton.Click += delegate { ExportSelected(); };
            _deleteButton = UiStyle.NewButton(L("History.DeleteRecord"), 112, false);
            UiStyle.StyleButton(_deleteButton, false, true);
            _deleteButton.Click += delegate { DeleteSelected(); };
            _clearButton = UiStyle.NewButton(L("History.ClearAll"), 112, false);
            UiStyle.StyleButton(_clearButton, false, true);
            _clearButton.Click += delegate { ClearAll(); };
            left.Controls.Add(_exportButton);
            left.Controls.Add(_deleteButton);
            left.Controls.Add(_clearButton);
            footer.Controls.Add(left, 0, 0);
            FlowLayoutPanel right = new FlowLayoutPanel();
            right.AutoSize = true;
            right.Padding = new Padding(0, 7, 0, 0);
            Button close = UiStyle.NewButton(L("Common.Close"), 88, false);
            close.Click += delegate { Close(); };
            right.Controls.Add(close);
            footer.Controls.Add(right, 1, 0);
            root.Controls.Add(footer, 0, 5);

            LoadHistory();
            UiStyle.RelayoutLocalizedTree(this);
        }

        private string L(string key)
        {
            return _language.Get(key);
        }

        private string LF(string key, params object[] args)
        {
            return _language.Format(key, args);
        }

        private FastDataGridView CreateGrid()
        {
            FastDataGridView grid = new FastDataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            UiStyle.StyleGrid(grid);
            return grid;
        }

        private DataGridViewTextBoxColumn NewColumn(string name, string title, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = title;
            column.Width = width;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            return column;
        }

        private void LoadHistory()
        {
            try
            {
                _sessions = _store.LoadSessions();
                _sessionGrid.Rows.Clear();

                foreach (HistorySession session in _sessions)
                {
                    int rowIndex = _sessionGrid.Rows.Add(
                        FormatTime(session.ExecutedAt),
                        session.TotalCount,
                        session.SuccessCount,
                        session.SkippedCount,
                        session.FailedCount,
                        session.SourceRoot,
                        session.TargetRoot);
                    _sessionGrid.Rows[rowIndex].Tag = session;
                }

                if (_sessionGrid.Rows.Count > 0)
                {
                    _sessionGrid.Rows[0].Selected = true;
                    _sessionGrid.CurrentCell = _sessionGrid.Rows[0].Cells[0];
                }

                RenderItems();
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("History.LoadFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private HistorySession GetSelectedSession()
        {
            if (_sessionGrid.SelectedRows.Count <= 0)
                return null;
            return _sessionGrid.SelectedRows[0].Tag as HistorySession;
        }

        private void RenderItems()
        {
            HistorySession session = GetSelectedSession();
            _itemGrid.Rows.Clear();

            if (session == null)
            {
                _summary.Text = _sessions.Count == 0 ? L("History.Empty") : "";
                SetActionState(false);
                return;
            }

            _summary.Text = LF(
                "History.Summary",
                session.FileTypeProfileName,
                GetScanRangeText(session.ScanRange),
                session.TotalCount,
                session.SuccessCount,
                session.SkippedCount,
                session.FailedCount);

            foreach (HistoryItem item in session.Items ?? new List<HistoryItem>())
            {
                int rowIndex = _itemGrid.Rows.Add(
                    item.FileName,
                    item.Author,
                    item.MatchedAs,
                    GetRecognitionText(item.RecognitionCode),
                    GetResultText(item.ResultCode),
                    item.ResultMessage,
                    item.SourcePath,
                    item.TargetPath,
                    _language.TranslateSource(item.MatchWhy ?? ""));

                DataGridViewRow row = _itemGrid.Rows[rowIndex];
                row.Cells["Status"].Style.ForeColor = GetRecognitionColor(item.RecognitionCode);
                row.Cells["Status"].Style.Font = new Font(Font, FontStyle.Bold);

                if (String.Equals(item.ResultCode, "failed", StringComparison.OrdinalIgnoreCase))
                    row.Cells["Result"].Style.ForeColor = Color.FromArgb(220, 38, 38);
                else if (String.Equals(item.ResultCode, "skipped", StringComparison.OrdinalIgnoreCase))
                    row.Cells["Result"].Style.ForeColor = Color.FromArgb(234, 88, 12);
                else
                    row.Cells["Result"].Style.ForeColor = Color.FromArgb(22, 163, 74);
            }

            SetActionState(true);
        }

        private void SetActionState(bool hasSession)
        {
            _exportButton.Enabled = hasSession;
            _deleteButton.Enabled = hasSession;
            _clearButton.Enabled = _sessions.Count > 0;
        }

        private string GetRecognitionText(string code)
        {
            string value = (code ?? "").ToLowerInvariant();
            if (value == "direct") return L("Recognition.DirectMatch");
            if (value == "normalized") return L("Recognition.NormalizedShort");
            if (value == "alias") return L("Recognition.AliasMatch");
            if (value == "society") return L("Recognition.SocietyMatch");
            if (value == "author") return L("Recognition.AuthorMatch");
            if (value == "new") return L("Recognition.New");
            if (value == "new-reuse") return L("Recognition.NewReuse");
            if (value == "ambiguous") return L("Recognition.Ambiguous");
            if (value == "unrecognized") return L("Recognition.UnrecognizedShort");
            if (value == "manual") return L("Recognition.ManualAssigned");
            return code ?? "";
        }

        private Color GetRecognitionColor(string code)
        {
            string value = (code ?? "").ToLowerInvariant();
            if (value == "direct") return Color.FromArgb(21, 128, 61);
            if (value == "normalized") return Color.FromArgb(37, 99, 235);
            if (value == "alias") return Color.FromArgb(147, 51, 234);
            if (value == "society") return Color.FromArgb(8, 145, 178);
            if (value == "author") return Color.FromArgb(67, 56, 202);
            if (value == "new") return Color.FromArgb(0, 137, 123);
            if (value == "new-reuse") return Color.FromArgb(100, 116, 139);
            if (value == "ambiguous") return Color.FromArgb(234, 88, 12);
            if (value == "manual") return Color.FromArgb(161, 98, 7);
            if (value == "unrecognized") return Color.FromArgb(220, 38, 38);
            return Color.DimGray;
        }

        private string GetResultText(string code)
        {
            string value = (code ?? "").ToLowerInvariant();
            if (value == "moved") return L("History.ResultMoved");
            if (value == "skipped") return L("History.ResultSkipped");
            if (value == "failed") return L("History.ResultFailed");
            return code ?? "";
        }

        private string GetScanRangeText(string code)
        {
            ScanModeKind mode;
            if (Enum.TryParse<ScanModeKind>(code ?? "", true, out mode))
            {
                switch (mode)
                {
                    case ScanModeKind.Exact: return L("ScanMode.Exact");
                    case ScanModeKind.NewAuthor: return L("ScanMode.New");
                    case ScanModeKind.Ambiguous: return L("ScanMode.Ambiguous");
                    case ScanModeKind.Unrecognized: return L("ScanMode.Unrecognized");
                    default: return L("ScanMode.Global");
                }
            }
            return code ?? "";
        }

        private void ExportSelected()
        {
            HistorySession session = GetSelectedSession();
            if (session == null)
                return;

            SaveFileDialog save = new SaveFileDialog();
            save.Filter = "CSV (*.csv)|*.csv";
            save.DefaultExt = "csv";
            save.AddExtension = true;
            save.FileName = "History_" + SafeTimeForFileName(session.ExecutedAt) + ".csv";

            if (save.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                _store.ExportSessionCsv(session, save.FileName);
                UiMessageBox.Show(this, LF("History.ExportSuccess", save.FileName), L("History.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, LF("History.ExportFailed", ex.Message), L("History.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteSelected()
        {
            HistorySession session = GetSelectedSession();
            if (session == null)
                return;

            if (UiMessageBox.Show(this, L("History.DeleteConfirm"), L("History.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                _store.DeleteSession(session.Id);
                LoadHistory();
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("History.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearAll()
        {
            if (_sessions.Count == 0)
                return;

            if (UiMessageBox.Show(this, L("History.ClearConfirm"), L("History.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                _store.Clear();
                LoadHistory();
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("History.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FormatTime(string value)
        {
            DateTime time;
            if (DateTime.TryParse(value, out time))
                return time.ToString("yyyy-MM-dd HH:mm:ss");
            return value ?? "";
        }

        private static string SafeTimeForFileName(string value)
        {
            DateTime time;
            if (DateTime.TryParse(value, out time))
                return time.ToString("yyyyMMdd_HHmmss");
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
    }
}
