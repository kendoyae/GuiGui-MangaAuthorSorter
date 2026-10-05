using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class AliasManagerForm : Form
    {
        private readonly AliasLibrary _library;
        private readonly LanguageManager _language;
        private readonly FastDataGridView _grid;
        private readonly TextBox _search;
        private readonly Label _summary;
        private Button _editButton;
        private Button _deleteButton;
        private List<AliasGroup> _groups = new List<AliasGroup>();

        public AliasManagerForm(AliasLibrary library, LanguageManager language, Font appFont)
        {
            _library = library;
            _language = language;

            Text = L("AliasManager.Title");
            ClientSize = new Size(920, 610);
            MinimumSize = new Size(760, 500);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Padding = new Padding(18, 16, 18, 14);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("AliasManager.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(0, 0);
            header.Controls.Add(title);
            Label desc = new Label();
            desc.Text = L("AliasManager.Description");
            desc.ForeColor = UiStyle.Muted;
            desc.Location = new Point(0, 27);
            desc.Size = new Size(820, 25);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(desc);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel toolbar = new TableLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.ColumnCount = 3;
            toolbar.RowCount = 1;
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
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
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            DataGridViewTextBoxColumn canonicalColumn = NewColumn("Canonical", L("AliasManager.Canonical"), 230);
            DataGridViewTextBoxColumn aliasesColumn = NewColumn("Aliases", L("AliasManager.Aliases"), 510);
            DataGridViewTextBoxColumn countColumn = NewColumn("Count", L("AliasManager.Count"), 80);
            UiStyle.ConfigureFillColumn(canonicalColumn, 32F, 180);
            UiStyle.ConfigureFillColumn(aliasesColumn, 68F, 280);
            UiStyle.ConfigureShortColumn(countColumn, 62);
            _grid.Columns.Add(canonicalColumn);
            _grid.Columns.Add(aliasesColumn);
            _grid.Columns.Add(countColumn);
            _grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) EditSelected();
            };
            _grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete)
                {
                    DeleteSelected();
                    e.SuppressKeyPress = true;
                }
            };
            _grid.SelectionChanged += delegate { UpdateActionButtons(); };
            UiStyle.StyleGrid(_grid);
            GridInteraction.Apply(_grid, "AliasManager", _language, false);
            root.Controls.Add(_grid, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel leftButtons = new FlowLayoutPanel();
            leftButtons.Dock = DockStyle.Fill;
            leftButtons.WrapContents = false;
            leftButtons.Padding = new Padding(0, 7, 0, 0);
            Button add = UiStyle.NewButton(L("AliasManager.Add"), 100, true);
            add.Click += delegate { AddGroup(); };
            _editButton = UiStyle.NewButton(L("AliasManager.Edit"), 100, false);
            _editButton.Click += delegate { EditSelected(); };
            _deleteButton = UiStyle.NewButton(L("AliasManager.Delete"), 100, false);
            UiStyle.StyleButton(_deleteButton, false, true);
            _deleteButton.Click += delegate { DeleteSelected(); };
            leftButtons.Controls.Add(add);
            leftButtons.Controls.Add(_editButton);
            leftButtons.Controls.Add(_deleteButton);
            footer.Controls.Add(leftButtons, 0, 0);
            FlowLayoutPanel rightButtons = new FlowLayoutPanel();
            rightButtons.AutoSize = true;
            rightButtons.WrapContents = false;
            rightButtons.Padding = new Padding(0, 7, 0, 0);
            Button close = UiStyle.NewButton(L("Common.Close"), 88, false);
            close.Click += delegate { Close(); };
            rightButtons.Controls.Add(close);
            footer.Controls.Add(rightButtons, 1, 0);
            root.Controls.Add(footer, 0, 3);

            LoadGroups();
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

        private void LoadGroups()
        {
            try { _groups = _library.Load(); }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                _groups = new List<AliasGroup>();
            }
            Render();
        }

        private void Render()
        {
            string q = (_search.Text ?? "").Trim();
            _grid.Rows.Clear();
            int shown = 0;
            foreach (AliasGroup group in _groups)
            {
                List<string> aliases = group.Names
                    .Where(delegate(string x)
                    {
                        return !String.Equals(AuthorRules.NormalizeText(x), AuthorRules.NormalizeText(group.Canonical), StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
                string aliasesText = String.Join("  |  ", aliases.ToArray());
                if (q.Length > 0 &&
                    (group.Canonical ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    aliasesText.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int row = _grid.Rows.Add(group.Canonical, aliasesText, aliases.Count);
                _grid.Rows[row].Tag = group;
                shown++;
            }
            _summary.Text = LF("AliasManager.Summary", shown, _groups.Count);
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            int selected = _grid != null ? _grid.SelectedRows.Count : 0;
            if (_editButton != null) _editButton.Enabled = selected == 1;
            if (_deleteButton != null) _deleteButton.Enabled = selected > 0;
        }

        private AliasGroup GetSingleSelected()
        {
            if (_grid.SelectedRows.Count != 1) return null;
            return _grid.SelectedRows[0].Tag as AliasGroup;
        }

        private void AddGroup()
        {
            AliasGroup result = EditGroup(null);
            if (result == null) return;
            _groups.Add(result);
            SaveAndRender();
        }

        private void EditSelected()
        {
            AliasGroup selected = GetSingleSelected();
            if (selected == null) return;
            AliasGroup result = EditGroup(selected);
            if (result == null) return;
            int index = _groups.IndexOf(selected);
            if (index >= 0) _groups[index] = result;
            SaveAndRender();
        }

        private void DeleteSelected()
        {
            if (_grid.SelectedRows.Count == 0) return;
            if (UiMessageBox.Show(this, LF("AliasManager.DeleteConfirm", _grid.SelectedRows.Count), L("AliasManager.Delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            HashSet<AliasGroup> remove = new HashSet<AliasGroup>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                AliasGroup g = row.Tag as AliasGroup;
                if (g != null) remove.Add(g);
            }
            _groups = _groups.Where(delegate(AliasGroup g) { return !remove.Contains(g); }).ToList();
            SaveAndRender();
        }

        private void SaveAndRender()
        {
            try
            {
                _library.Save(_groups);
                _groups = _library.Load();
                Render();
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("AliasManager.SaveFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static List<string> SplitAliases(string text)
        {
            string raw = (text ?? "").Replace('｜', '|').Replace("\r", "|").Replace("\n", "|");
            List<string> result = new List<string>();
            foreach (string part in raw.Split('|'))
            {
                string value = (part ?? "").Trim();
                if (value.Length > 0) result.Add(value);
            }
            return result;
        }

        private AliasGroup EditGroup(AliasGroup source)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = source == null ? L("AliasManager.AddTitle") : L("AliasManager.EditTitle");
                dlg.ClientSize = new Size(520, 250);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                UiStyle.ApplyDialog(dlg, Font);

                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.Padding = new Padding(18);
                root.ColumnCount = 1;
                root.RowCount = 6;
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
                dlg.Controls.Add(root);

                Label canonicalLabel = UiStyle.NewCaption(L("AliasManager.Canonical"));
                canonicalLabel.Dock = DockStyle.Fill;
                canonicalLabel.TextAlign = ContentAlignment.BottomLeft;
                root.Controls.Add(canonicalLabel, 0, 0);
                TextBox canonical = new TextBox();
                canonical.Dock = DockStyle.Fill;
                canonical.Text = source != null ? source.Canonical ?? "" : "";
                root.Controls.Add(canonical, 0, 1);

                Label aliasesLabel = UiStyle.NewCaption(L("AliasManager.AliasesOnePerLine"));
                aliasesLabel.Dock = DockStyle.Fill;
                aliasesLabel.TextAlign = ContentAlignment.BottomLeft;
                root.Controls.Add(aliasesLabel, 0, 2);
                TextBox aliases = new TextBox();
                aliases.Dock = DockStyle.Fill;
                aliases.Multiline = false;
                if (source != null)
                {
                    List<string> values = source.Names.Where(delegate(string x)
                    {
                        return !String.Equals(AuthorRules.NormalizeText(x), AuthorRules.NormalizeText(source.Canonical), StringComparison.OrdinalIgnoreCase);
                    }).ToList();
                    aliases.Text = String.Join(" | ", values.ToArray());
                }
                root.Controls.Add(aliases, 0, 3);
                Label hint = new Label();
                hint.Text = L("AliasManager.EditHint");
                hint.ForeColor = UiStyle.Muted;
                hint.Dock = DockStyle.Fill;
                root.Controls.Add(hint, 0, 4);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.WrapContents = false;
                buttons.Padding = new Padding(0, 7, 0, 0);
                Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
                cancel.DialogResult = DialogResult.Cancel;
                Button save = UiStyle.NewButton(L("Common.Save"), 88, true);
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(save);
                root.Controls.Add(buttons, 0, 5);
                dlg.CancelButton = cancel;
                dlg.AcceptButton = save;

                AliasGroup output = null;
                save.Click += delegate
                {
                    string c = (canonical.Text ?? "").Trim();
                    if (c.Length == 0)
                    {
                        UiMessageBox.Show(dlg, L("AliasManager.CanonicalRequired"), dlg.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        canonical.Focus();
                        return;
                    }
                    List<string> names = new List<string>();
                    names.Add(c);
                    foreach (string value in SplitAliases(aliases.Text))
                    {
                        if (value.Length > 0 && !names.Any(delegate(string n) { return String.Equals(AuthorRules.NormalizeText(n), AuthorRules.NormalizeText(value), StringComparison.OrdinalIgnoreCase); }))
                            names.Add(value);
                    }
                    if (names.Count < 2)
                    {
                        UiMessageBox.Show(dlg, L("AliasManager.AliasRequired"), dlg.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        aliases.Focus();
                        return;
                    }
                    output = new AliasGroup();
                    output.Canonical = c;
                    foreach (string n in names)
                    {
                        output.Names.Add(n);
                        foreach (string norm in AuthorRules.GetLiteralNorms(n)) output.Norms.Add(norm);
                    }
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                if (dlg.ShowDialog(this) == DialogResult.OK)
                    return output;
                return null;
            }
        }
    }
}
