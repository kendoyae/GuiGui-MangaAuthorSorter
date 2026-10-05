using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class TagCleaningRulesForm : Form
    {
        private readonly TagCleaningRuleStore _store;
        private readonly LanguageManager _language;
        private readonly FastDataGridView _grid;
        private readonly Label _summary;
        private readonly Button _editButton;
        private readonly Button _deleteButton;
        private List<TagCleaningRule> _rules;

        public TagCleaningRulesForm(TagCleaningRuleStore store, LanguageManager language, Font appFont)
        {
            _store = store;
            _language = language;
            _rules = _store.LoadRules();

            Text = L("Dialog.TagCleaning.Title");
            ClientSize = new Size(1160, 650);
            MinimumSize = new Size(860, 560);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 16, 18, 14);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("Dialog.TagCleaning.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            header.Controls.Add(title);
            Label desc = new Label();
            desc.Text = L("Dialog.TagCleaning.Description");
            desc.ForeColor = UiStyle.Muted;
            desc.Location = new Point(0, 27);
            desc.Size = new Size(1105, 35);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(desc);
            root.Controls.Add(header, 0, 0);

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
            DataGridViewTextBoxColumn enabledColumn = NewColumn("Enabled", L("Dialog.TagCleaning.Enabled"), 72);
            DataGridViewTextBoxColumn nameColumn = NewColumn("Name", L("Dialog.TagCleaning.RuleName"), 190);
            DataGridViewTextBoxColumn exampleColumn = NewColumn("Example", L("Dialog.TagCleaning.Example"), 230);
            DataGridViewTextBoxColumn typeColumn = NewColumn("Type", L("Dialog.TagCleaning.VisualRuleType"), 160);
            DataGridViewTextBoxColumn patternColumn = NewColumn("Pattern", L("Dialog.TagCleaning.RuleDescription"), 430);
            UiStyle.ConfigureShortColumn(enabledColumn, 58);
            UiStyle.ConfigureFillColumn(nameColumn, 20F, 140);
            UiStyle.ConfigureFillColumn(exampleColumn, 27F, 160);
            UiStyle.ConfigureShortColumn(typeColumn, 95);
            UiStyle.ConfigureFillColumn(patternColumn, 53F, 220);
            _grid.Columns.Add(enabledColumn);
            _grid.Columns.Add(nameColumn);
            _grid.Columns.Add(exampleColumn);
            _grid.Columns.Add(typeColumn);
            _grid.Columns.Add(patternColumn);
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
            GridInteraction.Apply(_grid, "TagCleaningRulesV2", _language, false);
            root.Controls.Add(_grid, 0, 1);

            TableLayoutPanel hintPanel = new TableLayoutPanel();
            hintPanel.Dock = DockStyle.Fill;
            hintPanel.ColumnCount = 2;
            hintPanel.RowCount = 1;
            hintPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            hintPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hintPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label hint = new Label();
            hint.Text = L("Dialog.TagCleaning.Hint");
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
            root.Controls.Add(hintPanel, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.WrapContents = false;
            left.Padding = new Padding(0, 7, 0, 0);
            Button add = UiStyle.NewButton(L("Dialog.TagCleaning.Add"), 92, false);
            add.Click += delegate { AddRule(); };
            _editButton = UiStyle.NewButton(L("Common.Edit"), 92, false);
            _editButton.Click += delegate { EditSelected(); };
            _deleteButton = UiStyle.NewButton(L("Common.DeleteSelected"), 104, false);
            UiStyle.StyleButton(_deleteButton, false, true);
            _deleteButton.Click += delegate { DeleteSelected(); };
            Button restore = UiStyle.NewButton(L("Dialog.TagCleaning.RestoreDefaults"), 138, false);
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
            root.Controls.Add(footer, 0, 3);

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
            int selectedIndex = -1;
            if (_grid.CurrentRow != null) selectedIndex = _grid.CurrentRow.Index;

            _grid.Rows.Clear();
            for (int i = 0; i < _rules.Count; i++)
            {
                TagCleaningRule rule = _rules[i];
                int rowIndex = _grid.Rows.Add(
                    rule.Enabled ? L("Common.Yes") : L("Common.No"),
                    GetRuleName(rule),
                    GetRuleExample(rule),
                    GetVisualTypeDisplay(rule),
                    GetRuleSummary(rule));
                _grid.Rows[rowIndex].Tag = rule;
                if (!rule.Enabled)
                    _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = UiStyle.Muted;
            }

            _summary.Text = LF("Dialog.TagCleaning.Summary", _rules.Count);
            if (_grid.Rows.Count > 0)
            {
                int index = selectedIndex;
                if (index < 0) index = 0;
                if (index >= _grid.Rows.Count) index = _grid.Rows.Count - 1;
                _grid.Rows[index].Selected = true;
                _grid.CurrentCell = _grid.Rows[index].Cells[0];
            }
            UpdateActionButtons();
        }


        private string GetRuleName(TagCleaningRule rule)
        {
            if (rule == null) return L("Dialog.TagCleaning.CustomRule");
            if (!String.IsNullOrWhiteSpace(rule.NameKey))
            {
                string translated = L(rule.NameKey);
                if (!String.IsNullOrWhiteSpace(translated) && !String.Equals(translated, rule.NameKey, StringComparison.Ordinal))
                    return translated;
            }
            if (!String.IsNullOrWhiteSpace(rule.Name)) return rule.Name.Trim();
            return L("Dialog.TagCleaning.CustomRule");
        }

        private string GetRuleExample(TagCleaningRule rule)
        {
            if (rule == null) return "";
            if (!String.IsNullOrWhiteSpace(rule.Example)) return rule.Example.Trim();
            if (!String.Equals(TagCleaningMatchType.Normalize(rule.MatchType), TagCleaningMatchType.Regex, StringComparison.OrdinalIgnoreCase))
                return (rule.Pattern ?? "").Trim();
            return "";
        }
        private string GetVisualTypeDisplay(TagCleaningRule rule)
        {
            string type = TagCleaningVisualRuleType.Normalize(rule);
            if (type == TagCleaningVisualRuleType.Contains) return L("Dialog.TagCleaning.VisualTypeContains");
            if (type == TagCleaningVisualRuleType.StartsWith) return L("Dialog.TagCleaning.VisualTypeStartsWith");
            if (type == TagCleaningVisualRuleType.EndsWith) return L("Dialog.TagCleaning.VisualTypeEndsWith");
            if (type == TagCleaningVisualRuleType.PrefixDigits) return L("Dialog.TagCleaning.VisualTypePrefixDigits");
            if (type == TagCleaningVisualRuleType.NumericDate) return L("Dialog.TagCleaning.VisualTypeNumericDate");
            if (type == TagCleaningVisualRuleType.CjkDate) return L("Dialog.TagCleaning.VisualTypeCjkDate");
            if (type == TagCleaningVisualRuleType.AdvancedRegex) return L("Dialog.TagCleaning.VisualTypeAdvancedRegex");
            return L("Dialog.TagCleaning.VisualTypeExact");
        }

        private string GetRuleSummary(TagCleaningRule rule)
        {
            if (rule == null) return "";
            string type = TagCleaningVisualRuleType.Normalize(rule);
            string value = (rule.EditorValue ?? "").Trim();
            if (type == TagCleaningVisualRuleType.Exact)
                return LF("Dialog.TagCleaning.SummaryExact", value.Length > 0 ? value : (rule.Pattern ?? ""));
            if (type == TagCleaningVisualRuleType.Contains)
                return LF("Dialog.TagCleaning.SummaryContains", value.Length > 0 ? value : (rule.Pattern ?? ""));
            if (type == TagCleaningVisualRuleType.StartsWith)
                return LF("Dialog.TagCleaning.SummaryStartsWith", value);
            if (type == TagCleaningVisualRuleType.EndsWith)
                return LF("Dialog.TagCleaning.SummaryEndsWith", value);
            if (type == TagCleaningVisualRuleType.PrefixDigits)
                return LF("Dialog.TagCleaning.SummaryPrefixDigits", value, Math.Max(1, rule.MinDigits), Math.Max(Math.Max(1, rule.MinDigits), rule.MaxDigits));
            if (type == TagCleaningVisualRuleType.NumericDate)
                return L("Dialog.TagCleaning.SummaryNumericDate");
            if (type == TagCleaningVisualRuleType.CjkDate)
                return L("Dialog.TagCleaning.SummaryCjkDate");
            return rule.Pattern ?? "";
        }

        private void UpdateActionButtons()
        {
            bool one = _grid.SelectedRows.Count == 1;
            bool any = _grid.SelectedRows.Count > 0;
            _editButton.Enabled = one;
            _deleteButton.Enabled = any;
        }

        private void AddRule()
        {
            TagCleaningRule rule = new TagCleaningRule();
            using (TagCleaningRuleEditForm dlg = new TagCleaningRuleEditForm(_language, Font, rule, true))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (IsDuplicate(dlg.Rule, -1))
                {
                    UiMessageBox.Show(this, L("Dialog.TagCleaning.Duplicate"), L("Dialog.TagCleaning.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _rules.Add(dlg.Rule.Clone());
                RenderRules();
                if (_grid.Rows.Count > 0)
                {
                    int index = _grid.Rows.Count - 1;
                    _grid.ClearSelection();
                    _grid.Rows[index].Selected = true;
                    _grid.CurrentCell = _grid.Rows[index].Cells[0];
                }
            }
        }

        private void EditSelected()
        {
            if (_grid.SelectedRows.Count != 1) return;
            int index = _grid.SelectedRows[0].Index;
            if (index < 0 || index >= _rules.Count) return;

            using (TagCleaningRuleEditForm dlg = new TagCleaningRuleEditForm(_language, Font, _rules[index], false))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (IsDuplicate(dlg.Rule, index))
                {
                    UiMessageBox.Show(this, L("Dialog.TagCleaning.Duplicate"), L("Dialog.TagCleaning.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _rules[index] = dlg.Rule.Clone();
                RenderRules();
                if (index < _grid.Rows.Count)
                {
                    _grid.ClearSelection();
                    _grid.Rows[index].Selected = true;
                    _grid.CurrentCell = _grid.Rows[index].Cells[0];
                }
            }
        }

        private bool IsDuplicate(TagCleaningRule rule, int exceptIndex)
        {
            string type = TagCleaningMatchType.Normalize(rule.MatchType);
            string pattern = (rule.Pattern ?? "").Trim();
            for (int i = 0; i < _rules.Count; i++)
            {
                if (i == exceptIndex) continue;
                TagCleaningRule other = _rules[i];
                if (String.Equals(TagCleaningMatchType.Normalize(other.MatchType), type, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals((other.Pattern ?? "").Trim(), pattern, StringComparison.OrdinalIgnoreCase))
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

            DialogResult confirm = UiMessageBox.Show(
                this,
                LF("Dialog.TagCleaning.DeleteConfirm", indices.Count),
                L("Dialog.TagCleaning.DeleteTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            foreach (int index in indices.Distinct().OrderByDescending(delegate(int x) { return x; }))
                _rules.RemoveAt(index);
            RenderRules();
        }

        private void RestoreDefaults()
        {
            DialogResult confirm = UiMessageBox.Show(
                this,
                L("Dialog.TagCleaning.RestoreConfirm"),
                L("Dialog.TagCleaning.RestoreDefaults"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;
            _rules = TagCleaningRuleStore.GetDefaultRules();
            RenderRules();
        }

        private void SaveAndClose()
        {
            try
            {
                _store.Save(_rules);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, LF("Dialog.TagCleaning.SaveFailed", ex.Message), L("Common.Error.SaveFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class TagCleaningRuleEditForm : Form
    {
        private readonly LanguageManager _language;
        private readonly CheckBox _enabled;
        private readonly TextBox _name;
        private readonly ComboBox _visualType;
        private readonly Panel _parameterPanel;
        private readonly Label _valueLabel;
        private readonly TextBox _value;
        private readonly Label _digitLabel;
        private readonly NumericUpDown _minDigits;
        private readonly NumericUpDown _maxDigits;
        private readonly Label _presetHint;
        private readonly TextBox _regex;
        private readonly TextBox _example;
        private readonly TextBox _testInput;
        private readonly Label _testResult;
        private readonly string _originalSystemNameKey;
        private readonly string _originalDisplayName;
        private bool _updating;
        public TagCleaningRule Rule { get; private set; }

        private sealed class VisualTypeItem
        {
            public string Id;
            public string Text;
            public override string ToString() { return Text; }
        }

        public TagCleaningRuleEditForm(LanguageManager language, Font appFont, TagCleaningRule source, bool adding)
        {
            _language = language;
            Rule = source != null ? source.Clone() : new TagCleaningRule();
            _originalSystemNameKey = Rule.NameKey ?? "";
            _originalDisplayName = GetRuleDisplayName(Rule);
            Text = adding ? L("Dialog.TagCleaning.AddTitle") : L("Dialog.TagCleaning.EditTitle");
            ClientSize = new Size(760, 565);
            MinimumSize = new Size(720, 540);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(20, 18, 20, 14);
            root.ColumnCount = 2;
            root.RowCount = 8;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            Label enabledLabel = UiStyle.NewCaption(L("Dialog.TagCleaning.Enabled"));
            enabledLabel.Anchor = AnchorStyles.Left;
            root.Controls.Add(enabledLabel, 0, 0);
            _enabled = new CheckBox();
            _enabled.Text = L("Dialog.TagCleaning.EnableRule");
            _enabled.Checked = Rule.Enabled;
            _enabled.AutoSize = true;
            _enabled.Anchor = AnchorStyles.Left;
            root.Controls.Add(_enabled, 1, 0);

            Label nameLabel = UiStyle.NewCaption(L("Dialog.TagCleaning.RuleName"));
            nameLabel.Anchor = AnchorStyles.Left;
            root.Controls.Add(nameLabel, 0, 1);
            _name = new TextBox();
            _name.Text = _originalDisplayName;
            _name.Dock = DockStyle.Fill;
            _name.Margin = new Padding(0, 8, 0, 8);
            root.Controls.Add(_name, 1, 1);

            Label typeLabel = UiStyle.NewCaption(L("Dialog.TagCleaning.VisualRuleType"));
            typeLabel.Anchor = AnchorStyles.Left;
            root.Controls.Add(typeLabel, 0, 2);
            _visualType = new ComboBox();
            _visualType.DropDownStyle = ComboBoxStyle.DropDownList;
            _visualType.Width = 270;
            _visualType.Anchor = AnchorStyles.Left;
            AddVisualType(TagCleaningVisualRuleType.Exact, "Dialog.TagCleaning.VisualTypeExact");
            AddVisualType(TagCleaningVisualRuleType.Contains, "Dialog.TagCleaning.VisualTypeContains");
            AddVisualType(TagCleaningVisualRuleType.StartsWith, "Dialog.TagCleaning.VisualTypeStartsWith");
            AddVisualType(TagCleaningVisualRuleType.EndsWith, "Dialog.TagCleaning.VisualTypeEndsWith");
            AddVisualType(TagCleaningVisualRuleType.PrefixDigits, "Dialog.TagCleaning.VisualTypePrefixDigits");
            AddVisualType(TagCleaningVisualRuleType.NumericDate, "Dialog.TagCleaning.VisualTypeNumericDate");
            AddVisualType(TagCleaningVisualRuleType.CjkDate, "Dialog.TagCleaning.VisualTypeCjkDate");
            AddVisualType(TagCleaningVisualRuleType.AdvancedRegex, "Dialog.TagCleaning.VisualTypeAdvancedRegex");
            UiStyle.FitComboBoxToItems(_visualType, 220, 420);
            root.Controls.Add(_visualType, 1, 2);

            Label paramsLabel = UiStyle.NewCaption(L("Dialog.TagCleaning.RuleParameters"));
            paramsLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            paramsLabel.Padding = new Padding(0, 10, 0, 0);
            root.Controls.Add(paramsLabel, 0, 3);

            _parameterPanel = new Panel();
            _parameterPanel.Dock = DockStyle.Fill;
            _parameterPanel.Margin = new Padding(0, 5, 0, 5);
            root.Controls.Add(_parameterPanel, 1, 3);

            _valueLabel = new Label();
            _valueLabel.Location = new Point(0, 3);
            _valueLabel.Size = new Size(560, 20);
            _valueLabel.ForeColor = UiStyle.Muted;
            _parameterPanel.Controls.Add(_valueLabel);

            _value = new TextBox();
            _value.Location = new Point(0, 25);
            _value.Size = new Size(560, 25);
            _value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _parameterPanel.Controls.Add(_value);

            _digitLabel = new Label();
            _digitLabel.Location = new Point(0, 61);
            _digitLabel.AutoSize = true;
            _digitLabel.Text = L("Dialog.TagCleaning.DigitRange");
            _parameterPanel.Controls.Add(_digitLabel);

            _minDigits = new NumericUpDown();
            _minDigits.Minimum = 1;
            _minDigits.Maximum = 12;
            _minDigits.Value = 1;
            _minDigits.Location = new Point(105, 58);
            _minDigits.Width = 62;
            _parameterPanel.Controls.Add(_minDigits);

            Label to = new Label();
            to.Text = "–";
            to.AutoSize = true;
            to.Location = new Point(176, 62);
            _parameterPanel.Controls.Add(to);

            _maxDigits = new NumericUpDown();
            _maxDigits.Minimum = 1;
            _maxDigits.Maximum = 12;
            _maxDigits.Value = 4;
            _maxDigits.Location = new Point(194, 58);
            _maxDigits.Width = 62;
            _parameterPanel.Controls.Add(_maxDigits);

            _presetHint = new Label();
            _presetHint.Location = new Point(0, 9);
            _presetHint.Size = new Size(565, 65);
            _presetHint.ForeColor = UiStyle.Muted;
            _presetHint.AutoSize = false;
            _parameterPanel.Controls.Add(_presetHint);

            _regex = new TextBox();
            _regex.Location = new Point(0, 25);
            _regex.Size = new Size(560, 25);
            _regex.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _parameterPanel.Controls.Add(_regex);

            Label exampleLabel = UiStyle.NewCaption(L("Dialog.TagCleaning.Example"));
            exampleLabel.Anchor = AnchorStyles.Left;
            root.Controls.Add(exampleLabel, 0, 4);
            _example = new TextBox();
            _example.Text = Rule.Example ?? "";
            _example.Dock = DockStyle.Fill;
            _example.Margin = new Padding(0, 8, 0, 8);
            root.Controls.Add(_example, 1, 4);

            Label testLabel = UiStyle.NewCaption(L("Dialog.TagCleaning.TestLabel"));
            testLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            testLabel.Padding = new Padding(0, 10, 0, 0);
            root.Controls.Add(testLabel, 0, 5);
            Panel testPanel = new Panel();
            testPanel.Dock = DockStyle.Fill;
            testPanel.Margin = new Padding(0, 5, 0, 5);
            _testInput = new TextBox();
            _testInput.Location = new Point(0, 4);
            _testInput.Size = new Size(560, 25);
            _testInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            testPanel.Controls.Add(_testInput);
            _testResult = new Label();
            _testResult.Location = new Point(0, 38);
            _testResult.Size = new Size(560, 34);
            _testResult.ForeColor = UiStyle.Muted;
            _testResult.AutoSize = false;
            testPanel.Controls.Add(_testResult);
            root.Controls.Add(testPanel, 1, 5);

            Label hint = new Label();
            hint.Text = L("Dialog.TagCleaning.VisualEditHint");
            hint.ForeColor = UiStyle.Muted;
            hint.AutoSize = false;
            hint.Dock = DockStyle.Fill;
            hint.Padding = new Padding(0, 8, 0, 0);
            root.SetColumnSpan(hint, 2);
            root.Controls.Add(hint, 0, 6);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Dock = DockStyle.Fill;
            footer.Padding = new Padding(0, 7, 0, 0);
            Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Button ok = UiStyle.NewButton(L("Common.OK"), 88, true);
            ok.Click += delegate { ValidateAndClose(); };
            footer.Controls.Add(cancel);
            footer.Controls.Add(ok);
            root.SetColumnSpan(footer, 2);
            root.Controls.Add(footer, 0, 7);

            LoadVisualState();
            _visualType.SelectedIndexChanged += delegate { if (!_updating) { UpdateVisualUi(); UpdateTest(); } };
            _value.TextChanged += delegate { UpdateTest(); };
            _regex.TextChanged += delegate { UpdateTest(); };
            _minDigits.ValueChanged += delegate { NormalizeDigitRange(true); UpdateTest(); };
            _maxDigits.ValueChanged += delegate { NormalizeDigitRange(false); UpdateTest(); };
            _testInput.TextChanged += delegate { UpdateTest(); };
            _example.TextChanged += delegate { };

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void AddVisualType(string id, string key)
        {
            _visualType.Items.Add(new VisualTypeItem { Id = id, Text = L(key) });
        }

        private void LoadVisualState()
        {
            _updating = true;
            string visual = TagCleaningVisualRuleType.Normalize(Rule);
            for (int i = 0; i < _visualType.Items.Count; i++)
            {
                VisualTypeItem item = _visualType.Items[i] as VisualTypeItem;
                if (item != null && String.Equals(item.Id, visual, StringComparison.OrdinalIgnoreCase))
                {
                    _visualType.SelectedIndex = i;
                    break;
                }
            }
            if (_visualType.SelectedIndex < 0) _visualType.SelectedIndex = 0;

            if (visual == TagCleaningVisualRuleType.AdvancedRegex)
            {
                _regex.Text = Rule.Pattern ?? "";
            }
            else
            {
                _value.Text = !String.IsNullOrWhiteSpace(Rule.EditorValue) ? Rule.EditorValue : (Rule.MatchType == TagCleaningMatchType.Exact || Rule.MatchType == TagCleaningMatchType.Contains ? (Rule.Pattern ?? "") : "");
            }
            int min = Rule.MinDigits > 0 ? Rule.MinDigits : 1;
            int max = Rule.MaxDigits >= min ? Rule.MaxDigits : Math.Max(4, min);
            _minDigits.Value = Math.Min(_minDigits.Maximum, Math.Max(_minDigits.Minimum, min));
            _maxDigits.Value = Math.Min(_maxDigits.Maximum, Math.Max(_maxDigits.Minimum, max));
            _updating = false;
            UpdateVisualUi();
            UpdateTest();
        }

        private string CurrentVisualType()
        {
            VisualTypeItem item = _visualType.SelectedItem as VisualTypeItem;
            return item != null ? item.Id : TagCleaningVisualRuleType.Exact;
        }

        private void UpdateVisualUi()
        {
            string type = CurrentVisualType();
            bool normalText = type == TagCleaningVisualRuleType.Exact || type == TagCleaningVisualRuleType.Contains || type == TagCleaningVisualRuleType.StartsWith || type == TagCleaningVisualRuleType.EndsWith;
            bool prefixDigits = type == TagCleaningVisualRuleType.PrefixDigits;
            bool preset = type == TagCleaningVisualRuleType.NumericDate || type == TagCleaningVisualRuleType.CjkDate;
            bool advanced = type == TagCleaningVisualRuleType.AdvancedRegex;

            _valueLabel.Visible = normalText || prefixDigits;
            _value.Visible = normalText || prefixDigits;
            _digitLabel.Visible = prefixDigits;
            _minDigits.Visible = prefixDigits;
            _maxDigits.Visible = prefixDigits;
            foreach (Control c in _parameterPanel.Controls)
            {
                if (c is Label && c != _valueLabel && c != _digitLabel && c != _presetHint)
                    c.Visible = prefixDigits;
            }
            _presetHint.Visible = preset;
            _regex.Visible = advanced;

            if (type == TagCleaningVisualRuleType.Exact) _valueLabel.Text = L("Dialog.TagCleaning.ValueExact");
            else if (type == TagCleaningVisualRuleType.Contains) _valueLabel.Text = L("Dialog.TagCleaning.ValueContains");
            else if (type == TagCleaningVisualRuleType.StartsWith) _valueLabel.Text = L("Dialog.TagCleaning.ValueStartsWith");
            else if (type == TagCleaningVisualRuleType.EndsWith) _valueLabel.Text = L("Dialog.TagCleaning.ValueEndsWith");
            else if (prefixDigits) _valueLabel.Text = L("Dialog.TagCleaning.ValuePrefixDigits");

            if (type == TagCleaningVisualRuleType.NumericDate) _presetHint.Text = L("Dialog.TagCleaning.NumericDateHint");
            else if (type == TagCleaningVisualRuleType.CjkDate) _presetHint.Text = L("Dialog.TagCleaning.CjkDateHint");
            else _presetHint.Text = "";

            if (advanced)
            {
                _valueLabel.Visible = true;
                _valueLabel.Text = L("Dialog.TagCleaning.AdvancedRegexLabel");
                _regex.Location = new Point(0, 25);
                _regex.Visible = true;
            }
        }

        private void NormalizeDigitRange(bool minChanged)
        {
            if (_updating) return;
            _updating = true;
            if (minChanged && _minDigits.Value > _maxDigits.Value) _maxDigits.Value = _minDigits.Value;
            if (!minChanged && _maxDigits.Value < _minDigits.Value) _minDigits.Value = _maxDigits.Value;
            _updating = false;
        }

        private TagCleaningRule BuildRuleForUi()
        {
            TagCleaningRule next = new TagCleaningRule();
            next.Enabled = _enabled.Checked;
            next.EditorType = CurrentVisualType();
            next.EditorValue = (_value.Text ?? "").Trim();
            next.MinDigits = Decimal.ToInt32(_minDigits.Value);
            next.MaxDigits = Decimal.ToInt32(_maxDigits.Value);
            next.Pattern = next.EditorType == TagCleaningVisualRuleType.AdvancedRegex ? (_regex.Text ?? "").Trim() : (Rule.Pattern ?? "");
            next.Example = (_example.Text ?? "").Trim();
            return next;
        }

        private void UpdateTest()
        {
            if (_updating || _testResult == null) return;
            string test = (_testInput.Text ?? "").Trim();
            if (test.Length == 0)
            {
                _testResult.Text = L("Dialog.TagCleaning.TestEmpty");
                _testResult.ForeColor = UiStyle.Muted;
                return;
            }

            TagCleaningRule candidate = BuildRuleForUi();
            bool matched;
            string error;
            if (!TagCleaningRuleStore.TestRule(candidate, test, out matched, out error))
            {
                _testResult.Text = LF("Dialog.TagCleaning.TestInvalid", error);
                _testResult.ForeColor = UiStyle.Danger;
                return;
            }

            _testResult.Text = matched ? L("Dialog.TagCleaning.TestMatched") : L("Dialog.TagCleaning.TestNotMatched");
            _testResult.ForeColor = matched ? Color.FromArgb(21, 128, 61) : UiStyle.Muted;
        }

        private string GetRuleDisplayName(TagCleaningRule rule)
        {
            if (rule == null) return "";
            if (!String.IsNullOrWhiteSpace(rule.NameKey))
            {
                string translated = L(rule.NameKey);
                if (!String.IsNullOrWhiteSpace(translated) && !String.Equals(translated, rule.NameKey, StringComparison.Ordinal))
                    return translated;
            }
            return (rule.Name ?? "").Trim();
        }

        private string L(string key) { return _language.Get(key); }
        private string LF(string key, params object[] args) { return _language.Format(key, args); }

        private void ValidateAndClose()
        {
            TagCleaningRule next = BuildRuleForUi();
            string enteredName = (_name.Text ?? "").Trim();
            if (enteredName.Length == 0)
            {
                UiMessageBox.Show(this, L("Dialog.TagCleaning.NameRequired"), L("Dialog.TagCleaning.InvalidTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                _name.Focus();
                return;
            }

            if (!String.IsNullOrWhiteSpace(_originalSystemNameKey) && String.Equals(enteredName, _originalDisplayName, StringComparison.Ordinal))
            {
                next.NameKey = _originalSystemNameKey;
                next.Name = "";
            }
            else
            {
                next.NameKey = "";
                next.Name = enteredName;
            }

            string error;
            if (!TagCleaningRuleStore.ApplyVisualDefinition(next, out error))
            {
                UiMessageBox.Show(this, LF("Dialog.TagCleaning.InvalidRule", error), L("Dialog.TagCleaning.InvalidTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (next.EditorType == TagCleaningVisualRuleType.AdvancedRegex) _regex.Focus(); else _value.Focus();
                return;
            }

            Rule = next;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
