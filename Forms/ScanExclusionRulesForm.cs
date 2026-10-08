using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class ScanExclusionRulesForm : Form
    {
        private readonly ScanExclusionRuleStore _store;
        private readonly LanguageManager _language;
        private readonly CheckBox _globalEnabled;
        private readonly FastDataGridView _grid;
        private readonly Label _summary;
        private readonly Button _editButton;
        private readonly Button _deleteButton;
        private List<ScanExclusionRule> _rules;

        public ScanExclusionRulesForm(ScanExclusionRuleStore store, LanguageManager language, Font appFont)
        {
            _store = store;
            _language = language;
            _rules = _store.LoadRules();

            Text = L("Dialog.ScanExclusion.Title");
            ClientSize = new Size(1160, 690);
            MinimumSize = new Size(940, 600);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 16, 18, 14);
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("Dialog.ScanExclusion.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            header.Controls.Add(title);
            Label desc = new Label();
            desc.Text = L("Dialog.ScanExclusion.Description");
            desc.ForeColor = UiStyle.Muted;
            desc.Location = new Point(0, 27);
            desc.Size = new Size(1100, 35);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(desc);
            root.Controls.Add(header, 0, 0);

            _globalEnabled = new CheckBox();
            _globalEnabled.Text = L("Dialog.ScanExclusion.GlobalEnable");
            _globalEnabled.Checked = _store.IsGloballyEnabled();
            _globalEnabled.AutoSize = true;
            _globalEnabled.Anchor = AnchorStyles.Left;
            _globalEnabled.Margin = new Padding(0, 5, 0, 0);
            root.Controls.Add(_globalEnabled, 0, 1);

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
            DataGridViewTextBoxColumn enabledColumn = NewColumn("Enabled", L("Dialog.ScanExclusion.Enabled"), 70);
            DataGridViewTextBoxColumn nameColumn = NewColumn("Name", L("Dialog.ScanExclusion.RuleName"), 180);
            DataGridViewTextBoxColumn exampleColumn = NewColumn("Example", L("Dialog.ScanExclusion.Example"), 245);
            DataGridViewTextBoxColumn scopeColumn = NewColumn("Scope", L("Dialog.ScanExclusion.Scope"), 105);
            DataGridViewTextBoxColumn typeColumn = NewColumn("Type", L("Dialog.ScanExclusion.MatchType"), 120);
            DataGridViewTextBoxColumn pattern = NewColumn("Pattern", L("Dialog.ScanExclusion.Pattern"), 360);
            UiStyle.ConfigureShortColumn(enabledColumn, 58);
            UiStyle.ConfigureFillColumn(nameColumn, 18F, 135);
            UiStyle.ConfigureFillColumn(exampleColumn, 26F, 170);
            UiStyle.ConfigureShortColumn(scopeColumn, 86);
            UiStyle.ConfigureShortColumn(typeColumn, 96);
            UiStyle.ConfigureFillColumn(pattern, 56F, 220);
            _grid.Columns.Add(enabledColumn);
            _grid.Columns.Add(nameColumn);
            _grid.Columns.Add(exampleColumn);
            _grid.Columns.Add(scopeColumn);
            _grid.Columns.Add(typeColumn);
            _grid.Columns.Add(pattern);
            _grid.SelectionChanged += delegate { UpdateActionButtons(); };
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
                else if (e.KeyCode == Keys.Enter)
                {
                    EditSelected();
                    e.SuppressKeyPress = true;
                }
            };
            UiStyle.StyleGrid(_grid);
            GridInteraction.Apply(_grid, "ScanExclusionRulesV2", _language, false);
            root.Controls.Add(_grid, 0, 2);

            TableLayoutPanel hintPanel = new TableLayoutPanel();
            hintPanel.Dock = DockStyle.Fill;
            hintPanel.ColumnCount = 2;
            hintPanel.RowCount = 1;
            hintPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            hintPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hintPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label hint = new Label();
            hint.Text = L("Dialog.ScanExclusion.Hint");
            hint.ForeColor = UiStyle.Muted;
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.AutoEllipsis = true;
            hint.Margin = new Padding(0, 4, 12, 0);
            hintPanel.Controls.Add(hint, 0, 0);
            _summary = new Label();
            _summary.TextAlign = ContentAlignment.MiddleRight;
            _summary.ForeColor = UiStyle.Muted;
            _summary.AutoSize = true;
            _summary.Anchor = AnchorStyles.Right;
            _summary.Margin = new Padding(8, 4, 0, 0);
            hintPanel.Controls.Add(_summary, 1, 0);
            root.Controls.Add(hintPanel, 0, 3);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.WrapContents = false;
            left.Padding = new Padding(0, 7, 0, 0);
            Button add = UiStyle.NewButton(L("Dialog.ScanExclusion.Add"), 92, false);
            add.Click += delegate { AddRule(); };
            _editButton = UiStyle.NewButton(L("Common.Edit"), 92, false);
            _editButton.Click += delegate { EditSelected(); };
            _deleteButton = UiStyle.NewButton(L("Common.DeleteSelected"), 104, false);
            UiStyle.StyleButton(_deleteButton, false, true);
            _deleteButton.Click += delegate { DeleteSelected(); };
            Button restore = UiStyle.NewButton(L("Dialog.ScanExclusion.RestoreDefaults"), 138, false);
            restore.Click += delegate { RestoreDefaults(); };
            left.Controls.Add(add);
            left.Controls.Add(_editButton);
            left.Controls.Add(_deleteButton);
            left.Controls.Add(restore);
            footer.Controls.Add(left, 0, 0);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.AutoSize = true;
            right.WrapContents = false;
            right.Padding = new Padding(0, 7, 0, 0);
            Button save = UiStyle.NewButton(L("Common.Save"), 88, true);
            save.Click += delegate { SaveAndClose(); };
            Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            right.Controls.Add(save);
            right.Controls.Add(cancel);
            footer.Controls.Add(right, 1, 0);
            root.Controls.Add(footer, 0, 4);

            RenderRules();
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

        private void RenderRules()
        {
            int selectedIndex = _grid.CurrentRow != null ? _grid.CurrentRow.Index : -1;
            _grid.Rows.Clear();

            for (int i = 0; i < _rules.Count; i++)
            {
                ScanExclusionRule rule = _rules[i];
                int index = _grid.Rows.Add(
                    rule.Enabled ? L("Common.Yes") : L("Common.No"),
                    rule.Name,
                    rule.Example,
                    GetScopeText(rule.Scope),
                    GetMatchTypeText(rule.MatchType),
                    GetRuleSummary(rule));
                _grid.Rows[index].Tag = rule;
                if (!rule.Enabled) _grid.Rows[index].DefaultCellStyle.ForeColor = UiStyle.Muted;
            }

            _summary.Text = LF("Dialog.ScanExclusion.Summary", _rules.Count);
            if (_grid.Rows.Count > 0)
            {
                int index = selectedIndex < 0 ? 0 : selectedIndex;
                if (index >= _grid.Rows.Count) index = _grid.Rows.Count - 1;
                _grid.ClearSelection();
                _grid.Rows[index].Selected = true;
                _grid.CurrentCell = _grid.Rows[index].Cells[0];
            }
            UpdateActionButtons();
        }

        private string GetScopeText(string scope)
        {
            return String.Equals(ScanExclusionScope.Normalize(scope), ScanExclusionScope.FolderName, StringComparison.OrdinalIgnoreCase)
                ? L("Dialog.ScanExclusion.ScopeFolder")
                : L("Dialog.ScanExclusion.ScopeFile");
        }

        private string GetMatchTypeText(string type)
        {
            type = ScanExclusionMatchType.Normalize(type);
            if (type == ScanExclusionMatchType.Contains) return L("Dialog.ScanExclusion.TypeContains");
            if (type == ScanExclusionMatchType.StartsWith) return L("Dialog.ScanExclusion.TypeStartsWith");
            if (type == ScanExclusionMatchType.EndsWith) return L("Dialog.ScanExclusion.TypeEndsWith");
            if (type == ScanExclusionMatchType.Regex) return L("Dialog.ScanExclusion.TypeRegex");
            return L("Dialog.ScanExclusion.TypeExact");
        }

        private string GetRuleSummary(ScanExclusionRule rule)
        {
            string p = (rule.Pattern ?? "").Trim();
            string type = ScanExclusionMatchType.Normalize(rule.MatchType);
            if (type == ScanExclusionMatchType.Contains) return LF("Dialog.ScanExclusion.SummaryContains", p);
            if (type == ScanExclusionMatchType.StartsWith) return LF("Dialog.ScanExclusion.SummaryStartsWith", p);
            if (type == ScanExclusionMatchType.EndsWith) return LF("Dialog.ScanExclusion.SummaryEndsWith", p);
            if (type == ScanExclusionMatchType.Regex) return p;
            return LF("Dialog.ScanExclusion.SummaryExact", p);
        }

        private void UpdateActionButtons()
        {
            _editButton.Enabled = _grid.SelectedRows.Count == 1;
            _deleteButton.Enabled = _grid.SelectedRows.Count > 0;
        }

        private void AddRule()
        {
            ScanExclusionRule rule = new ScanExclusionRule();
            using (ScanExclusionRuleEditForm dlg = new ScanExclusionRuleEditForm(_language, Font, rule, true))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (IsDuplicate(dlg.Rule, -1))
                {
                    UiMessageBox.Show(this, L("Dialog.ScanExclusion.Duplicate"), L("Dialog.ScanExclusion.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _rules.Add(dlg.Rule.Clone());
                RenderRules();
            }
        }

        private void EditSelected()
        {
            if (_grid.SelectedRows.Count != 1) return;
            int index = _grid.SelectedRows[0].Index;
            if (index < 0 || index >= _rules.Count) return;

            using (ScanExclusionRuleEditForm dlg = new ScanExclusionRuleEditForm(_language, Font, _rules[index], false))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (IsDuplicate(dlg.Rule, index))
                {
                    UiMessageBox.Show(this, L("Dialog.ScanExclusion.Duplicate"), L("Dialog.ScanExclusion.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _rules[index] = dlg.Rule.Clone();
                RenderRules();
            }
        }

        private bool IsDuplicate(ScanExclusionRule rule, int exceptIndex)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                if (i == exceptIndex) continue;
                ScanExclusionRule other = _rules[i];
                if (String.Equals(ScanExclusionScope.Normalize(other.Scope), ScanExclusionScope.Normalize(rule.Scope), StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(ScanExclusionMatchType.Normalize(other.MatchType), ScanExclusionMatchType.Normalize(rule.MatchType), StringComparison.OrdinalIgnoreCase) &&
                    String.Equals((other.Pattern ?? "").Trim(), (rule.Pattern ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void DeleteSelected()
        {
            List<int> indices = new List<int>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
                if (row.Index >= 0 && row.Index < _rules.Count) indices.Add(row.Index);
            if (indices.Count == 0) return;

            if (UiMessageBox.Show(this, LF("Dialog.ScanExclusion.DeleteConfirm", indices.Count), L("Dialog.ScanExclusion.DeleteTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            foreach (int index in indices.Distinct().OrderByDescending(delegate(int x) { return x; }))
                _rules.RemoveAt(index);
            RenderRules();
        }

        private void RestoreDefaults()
        {
            if (UiMessageBox.Show(this, L("Dialog.ScanExclusion.RestoreConfirm"), L("Dialog.ScanExclusion.RestoreDefaults"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _rules = ScanExclusionRuleStore.GetDefaultRules();
            _globalEnabled.Checked = true;
            RenderRules();
        }

        private void SaveAndClose()
        {
            try
            {
                _store.Save(_globalEnabled.Checked, _rules);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, LF("Dialog.ScanExclusion.SaveFailed", ex.Message), L("Common.Error.SaveFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class ScanExclusionRuleEditForm : Form
    {
        private readonly LanguageManager _language;
        private readonly CheckBox _enabled;
        private readonly TextBox _name;
        private readonly TextBox _example;
        private readonly ComboBox _scope;
        private readonly ComboBox _matchType;
        private readonly TextBox _pattern;
        private readonly TextBox _testInput;
        private readonly Label _testResult;
        public ScanExclusionRule Rule { get; private set; }

        private sealed class ValueItem
        {
            public string Id;
            public string Text;
            public override string ToString() { return Text; }
        }

        public ScanExclusionRuleEditForm(LanguageManager language, Font appFont, ScanExclusionRule source, bool adding)
        {
            _language = language;
            Rule = source != null ? source.Clone() : new ScanExclusionRule();
            Text = adding ? L("Dialog.ScanExclusion.AddTitle") : L("Dialog.ScanExclusion.EditTitle");
            ClientSize = new Size(720, 470);
            MinimumSize = new Size(680, 440);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(20, 18, 20, 14);
            root.ColumnCount = 2;
            root.RowCount = 8;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 6 ? 70F : 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            _enabled = new CheckBox();
            _enabled.Text = L("Dialog.ScanExclusion.EnableRule");
            _enabled.Checked = Rule.Enabled;
            _enabled.AutoSize = true;
            root.Controls.Add(_enabled, 1, 0);

            AddLabel(root, L("Dialog.ScanExclusion.RuleName"), 1);
            _name = new TextBox();
            _name.Dock = DockStyle.Fill;
            _name.Text = Rule.Name ?? "";
            root.Controls.Add(_name, 1, 1);

            AddLabel(root, L("Dialog.ScanExclusion.Example"), 2);
            _example = new TextBox();
            _example.Dock = DockStyle.Fill;
            _example.Text = Rule.Example ?? "";
            root.Controls.Add(_example, 1, 2);

            AddLabel(root, L("Dialog.ScanExclusion.Scope"), 3);
            _scope = new ComboBox();
            _scope.DropDownStyle = ComboBoxStyle.DropDownList;
            _scope.Dock = DockStyle.Left;
            _scope.Width = 220;
            _scope.Items.Add(new ValueItem { Id = ScanExclusionScope.FileName, Text = L("Dialog.ScanExclusion.ScopeFile") });
            _scope.Items.Add(new ValueItem { Id = ScanExclusionScope.FolderName, Text = L("Dialog.ScanExclusion.ScopeFolder") });
            UiStyle.FitComboBoxToItems(_scope, 180, 360);
            SelectById(_scope, ScanExclusionScope.Normalize(Rule.Scope));
            _scope.SelectedIndexChanged += delegate { UpdateTest(); };
            root.Controls.Add(_scope, 1, 3);

            AddLabel(root, L("Dialog.ScanExclusion.MatchType"), 4);
            _matchType = new ComboBox();
            _matchType.DropDownStyle = ComboBoxStyle.DropDownList;
            _matchType.Dock = DockStyle.Left;
            _matchType.Width = 250;
            _matchType.Items.Add(new ValueItem { Id = ScanExclusionMatchType.Contains, Text = L("Dialog.ScanExclusion.TypeContains") });
            _matchType.Items.Add(new ValueItem { Id = ScanExclusionMatchType.Exact, Text = L("Dialog.ScanExclusion.TypeExact") });
            _matchType.Items.Add(new ValueItem { Id = ScanExclusionMatchType.StartsWith, Text = L("Dialog.ScanExclusion.TypeStartsWith") });
            _matchType.Items.Add(new ValueItem { Id = ScanExclusionMatchType.EndsWith, Text = L("Dialog.ScanExclusion.TypeEndsWith") });
            _matchType.Items.Add(new ValueItem { Id = ScanExclusionMatchType.Regex, Text = L("Dialog.ScanExclusion.TypeRegex") });
            UiStyle.FitComboBoxToItems(_matchType, 200, 400);
            SelectById(_matchType, ScanExclusionMatchType.Normalize(Rule.MatchType));
            _matchType.SelectedIndexChanged += delegate { UpdateTest(); };
            root.Controls.Add(_matchType, 1, 4);

            AddLabel(root, L("Dialog.ScanExclusion.Pattern"), 5);
            _pattern = new TextBox();
            _pattern.Dock = DockStyle.Fill;
            _pattern.Text = Rule.Pattern ?? "";
            _pattern.TextChanged += delegate { UpdateTest(); };
            root.Controls.Add(_pattern, 1, 5);

            AddLabel(root, L("Dialog.ScanExclusion.LiveTest"), 6);
            Panel testPanel = new Panel();
            testPanel.Dock = DockStyle.Fill;
            _testInput = new TextBox();
            _testInput.Location = new Point(0, 0);
            _testInput.Size = new Size(540, 26);
            _testInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _testInput.TextChanged += delegate { UpdateTest(); };
            testPanel.Controls.Add(_testInput);
            _testResult = new Label();
            _testResult.Location = new Point(0, 32);
            _testResult.Size = new Size(540, 30);
            _testResult.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _testResult.ForeColor = UiStyle.Muted;
            testPanel.Controls.Add(_testResult);
            root.Controls.Add(testPanel, 1, 6);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 50;
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label hint = new Label();
            hint.Text = L("Dialog.ScanExclusion.EditHint");
            hint.ForeColor = UiStyle.Muted;
            hint.Dock = DockStyle.Fill;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(hint, 0, 0);
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 8, 0, 0);
            Button ok = UiStyle.NewButton(L("Common.OK"), 88, true);
            ok.Click += delegate { Commit(); };
            Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            actions.Controls.Add(ok);
            actions.Controls.Add(cancel);
            footer.Controls.Add(actions, 1, 0);
            root.Controls.Add(footer, 0, 7);
            root.SetColumnSpan(footer, 2);

            UpdateTest();
        }

        private string L(string key) { return _language.Get(key); }
        private string LF(string key, params object[] args) { return _language.Format(key, args); }

        private void AddLabel(TableLayoutPanel root, string text, int row)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            root.Controls.Add(label, 0, row);
        }

        private static void SelectById(ComboBox combo, string id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ValueItem item = combo.Items[i] as ValueItem;
                if (item != null && String.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private string SelectedId(ComboBox combo)
        {
            ValueItem item = combo.SelectedItem as ValueItem;
            return item != null ? item.Id : "";
        }

        private bool TestMatches(string text, string type, string pattern, out string error)
        {
            error = "";
            if (String.IsNullOrWhiteSpace(pattern))
            {
                error = L("Dialog.ScanExclusion.PatternRequired");
                return false;
            }

            type = ScanExclusionMatchType.Normalize(type);
            if (type == ScanExclusionMatchType.Exact) return String.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);
            if (type == ScanExclusionMatchType.Contains) return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            if (type == ScanExclusionMatchType.StartsWith) return text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
            if (type == ScanExclusionMatchType.EndsWith) return text.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);

            try
            {
                System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(200));
                return regex.IsMatch(text ?? "");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void UpdateTest()
        {
            if (_testResult == null) return;
            string sample = _testInput.Text ?? "";
            if (sample.Length == 0)
            {
                _testResult.Text = L("Dialog.ScanExclusion.TestEmpty");
                _testResult.ForeColor = UiStyle.Muted;
                return;
            }

            string error;
            bool matched = TestMatches(sample, SelectedId(_matchType), (_pattern.Text ?? "").Trim(), out error);
            if (!String.IsNullOrWhiteSpace(error))
            {
                _testResult.Text = LF("Dialog.ScanExclusion.TestInvalid", error);
                _testResult.ForeColor = Color.FromArgb(180, 70, 60);
                return;
            }

            _testResult.Text = matched
                ? L("Dialog.ScanExclusion.TestMatched")
                : L("Dialog.ScanExclusion.TestNotMatched");
            _testResult.ForeColor = matched ? Color.FromArgb(21, 128, 61) : UiStyle.Muted;
        }

        private void Commit()
        {
            string name = (_name.Text ?? "").Trim();
            string pattern = (_pattern.Text ?? "").Trim();
            if (name.Length == 0)
            {
                UiMessageBox.Show(this, L("Dialog.ScanExclusion.NameRequired"), L("Dialog.ScanExclusion.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
                return;
            }
            if (pattern.Length == 0)
            {
                UiMessageBox.Show(this, L("Dialog.ScanExclusion.PatternRequired"), L("Dialog.ScanExclusion.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _pattern.Focus();
                return;
            }

            ScanExclusionRule candidate = new ScanExclusionRule();
            candidate.Enabled = _enabled.Checked;
            candidate.Name = name;
            candidate.Example = (_example.Text ?? "").Trim();
            candidate.Scope = ScanExclusionScope.Normalize(SelectedId(_scope));
            candidate.MatchType = ScanExclusionMatchType.Normalize(SelectedId(_matchType));
            candidate.Pattern = pattern;

            string error;
            if (!ScanExclusionRuleStore.ValidateRules(new ScanExclusionRule[] { candidate }, out error))
            {
                string localized = _language.TranslateSource(error);
                const string regexPrefix = "正则表达式无效：";
                if ((error ?? "").StartsWith(regexPrefix, StringComparison.Ordinal))
                    localized = LF("Dialog.ScanExclusion.RegexInvalid", error.Substring(regexPrefix.Length));
                UiMessageBox.Show(this, localized, L("Dialog.ScanExclusion.InvalidTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Rule = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class ScanExcludedItemsForm : Form
    {
        private readonly LanguageManager _language;

        public ScanExcludedItemsForm(LanguageManager language, Font appFont, IList<ScanExcludedItem> items)
        {
            _language = language;
            Text = L("Dialog.ScanExcluded.Title");
            ClientSize = new Size(980, 560);
            MinimumSize = new Size(760, 460);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(16, 14, 16, 12);
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            Label desc = new Label();
            desc.Text = _language.Format("Dialog.ScanExcluded.Description", items != null ? items.Count : 0);
            desc.Dock = DockStyle.Fill;
            desc.ForeColor = UiStyle.Muted;
            root.Controls.Add(desc, 0, 0);

            FastDataGridView grid = new FastDataGridView();
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            DataGridViewTextBoxColumn itemColumn = NewColumn("Name", L("Dialog.ScanExcluded.Item"), 260);
            DataGridViewTextBoxColumn scopeColumn = NewColumn("Scope", L("Dialog.ScanExcluded.Scope"), 100);
            DataGridViewTextBoxColumn ruleColumn = NewColumn("Rule", L("Dialog.ScanExcluded.Rule"), 170);
            DataGridViewTextBoxColumn path = NewColumn("Path", L("Dialog.ScanExcluded.Path"), 400);
            UiStyle.ConfigureFillColumn(itemColumn, 28F, 180);
            UiStyle.ConfigureShortColumn(scopeColumn, 82);
            UiStyle.ConfigureFillColumn(ruleColumn, 22F, 140);
            UiStyle.ConfigureFillColumn(path, 50F, 260);
            grid.Columns.Add(itemColumn);
            grid.Columns.Add(scopeColumn);
            grid.Columns.Add(ruleColumn);
            grid.Columns.Add(path);
            UiStyle.StyleGrid(grid);
            GridInteraction.Apply(grid, "ScanExcludedItems", _language, false);

            if (items != null)
            {
                foreach (ScanExcludedItem item in items)
                {
                    grid.Rows.Add(
                        item.DisplayName,
                        String.Equals(item.Scope, ScanExclusionScope.FolderName, StringComparison.OrdinalIgnoreCase)
                            ? L("Dialog.ScanExclusion.ScopeFolder")
                            : L("Dialog.ScanExclusion.ScopeFile"),
                        item.RuleName,
                        item.Path);
                }
            }
            root.Controls.Add(grid, 0, 1);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Padding = new Padding(0, 8, 0, 0);
            Button close = UiStyle.NewButton(L("Common.Close"), 88, true);
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);
            root.Controls.Add(footer, 0, 2);
        }

        private string L(string key) { return _language.Get(key); }

        private DataGridViewTextBoxColumn NewColumn(string name, string title, int width)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = title;
            c.Width = width;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return c;
        }
    }
}
