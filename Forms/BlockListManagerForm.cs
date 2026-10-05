using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class BlockListManagerForm : Form
    {
        private readonly BlockListStore _store;
        private readonly LanguageManager _language;
        private readonly FastDataGridView _grid;
        private readonly TextBox _search;
        private readonly Label _summary;
        private Button _removeButton;
        private Button _removeMissingButton;
        private List<string> _items = new List<string>();

        public BlockListManagerForm(BlockListStore store, LanguageManager language, Font appFont)
        {
            _store = store;
            _language = language;
            Text = L("Dialog.BlockList.Title");
            ClientSize = new Size(960, 610);
            MinimumSize = new Size(760, 470);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 16, 18, 14);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("Dialog.BlockList.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            header.Controls.Add(title);
            Label desc = new Label();
            desc.Text = L("Dialog.BlockList.Info");
            desc.ForeColor = UiStyle.Muted;
            desc.Location = new Point(0, 27);
            desc.Size = new Size(860, 25);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(desc);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel toolbar = new TableLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.ColumnCount = 3;
            toolbar.RowCount = 1;
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label searchLabel = UiStyle.NewCaption(L("Common.Search"));
            searchLabel.Anchor = AnchorStyles.Left;
            searchLabel.Margin = new Padding(0, 0, 8, 0);
            toolbar.Controls.Add(searchLabel, 0, 0);
            _search = new TextBox();
            _search.Dock = DockStyle.Fill;
            _search.Margin = new Padding(0, 5, 12, 5);
            _search.TextChanged += delegate { Render(); };
            toolbar.Controls.Add(_search, 1, 0);
            _summary = new Label();
            _summary.ForeColor = UiStyle.Muted;
            _summary.TextAlign = ContentAlignment.MiddleRight;
            _summary.Dock = DockStyle.Fill;
            _summary.AutoEllipsis = true;
            toolbar.Controls.Add(_summary, 2, 0);
            root.Controls.Add(toolbar, 0, 1);

            _grid = new FastDataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AllowUserToResizeColumns = true;
            _grid.ReadOnly = true;
            _grid.MultiSelect = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            DataGridViewTextBoxColumn nameColumn = NewColumn("Name", L("Dialog.BlockList.FileName"), 300);
            DataGridViewTextBoxColumn pathColumn = NewColumn("Path", L("Dialog.BlockList.FullPath"), 470);
            DataGridViewTextBoxColumn statusColumn = NewColumn("Exists", L("Dialog.BlockList.Status"), 100);
            UiStyle.ConfigureFillColumn(nameColumn, 35F, 220);
            UiStyle.ConfigureFillColumn(pathColumn, 65F, 320);
            UiStyle.ConfigureShortColumn(statusColumn, 80);
            _grid.Columns.Add(nameColumn);
            _grid.Columns.Add(pathColumn);
            _grid.Columns.Add(statusColumn);
            _grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete)
                {
                    RemoveSelected();
                    e.SuppressKeyPress = true;
                }
            };
            _grid.SelectionChanged += delegate { UpdateActionButtons(); };
            UiStyle.StyleGrid(_grid);
            GridInteraction.Apply(_grid, "BlockListManager", _language, false);
            root.Controls.Add(_grid, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.WrapContents = false;
            left.Padding = new Padding(0, 7, 0, 0);
            _removeButton = UiStyle.NewButton(L("Dialog.BlockList.DeleteSelected"), 120, false);
            UiStyle.StyleButton(_removeButton, false, true);
            _removeButton.Click += delegate { RemoveSelected(); };
            _removeMissingButton = UiStyle.NewButton(L("BlockList.RemoveMissing"), 120, false);
            _removeMissingButton.Click += delegate { RemoveMissing(); };
            left.Controls.Add(_removeButton);
            left.Controls.Add(_removeMissingButton);
            footer.Controls.Add(left, 0, 0);
            FlowLayoutPanel right = new FlowLayoutPanel();
            right.AutoSize = true;
            right.Padding = new Padding(0, 7, 0, 0);
            Button close = UiStyle.NewButton(L("Common.Close"), 88, false);
            close.Click += delegate { Close(); };
            right.Controls.Add(close);
            footer.Controls.Add(right, 1, 0);
            root.Controls.Add(footer, 0, 3);
            LoadItems();
            UpdateActionButtons();
            UiStyle.RelayoutLocalizedTree(this);
        }

        private string L(string key) { return _language.Get(key); }
        private string LF(string key, params object[] args) { return _language.Format(key, args); }
        private DataGridViewTextBoxColumn NewColumn(string name, string title, int width)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = title;
            c.Width = width;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }
        private void LoadItems()
        {
            _items = _store.Load();
            Render();
        }
        private void Render()
        {
            string q = (_search.Text ?? "").Trim();
            _grid.Rows.Clear();
            int shown = 0;
            foreach (string path in _items)
            {
                string name;
                try { name = Path.GetFileName(path); } catch { name = path; }
                if (q.Length > 0 && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int row = _grid.Rows.Add(name, path, File.Exists(path) ? L("Dialog.BlockList.FileExists") : L("Dialog.BlockList.FileMissing"));
                _grid.Rows[row].Tag = path;
                shown++;
            }
            _summary.Text = LF("BlockList.Summary", shown, _items.Count);
            UpdateActionButtons();
        }
        private void UpdateActionButtons()
        {
            if (_removeButton != null)
                _removeButton.Enabled = _grid != null && _grid.SelectedRows.Count > 0;
            if (_removeMissingButton != null)
                _removeMissingButton.Enabled = _items.Any(delegate(string p) { return !File.Exists(p); });
        }

        private void RemoveSelected()
        {
            List<string> remove = new List<string>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                string path = row.Tag as string;
                if (!String.IsNullOrWhiteSpace(path)) remove.Add(path);
            }
            if (remove.Count == 0) return;
            _store.RemovePaths(remove);
            LoadItems();
        }
        private void RemoveMissing()
        {
            List<string> missing = _items.Where(delegate(string p) { return !File.Exists(p); }).ToList();
            if (missing.Count == 0) return;
            _store.RemovePaths(missing);
            LoadItems();
        }
    }
}
