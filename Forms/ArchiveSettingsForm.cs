using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class ArchiveSettingsForm : Form
    {
        private readonly LanguageManager _language;
        private readonly AuthorEntityStore _authorStore;
        private readonly NumericUpDown _maxAuthors;
        private readonly NumericUpDown _safetyReserveGb;
        private readonly TextBox _prefix;
        private readonly ComboBox _rule;
        private readonly TextBox _suffix;
        private readonly Label _groupPreview;
        private readonly TextBox _authorTemplate;
        private readonly Label _authorPreview;

        internal sealed class RuleItem
        {
            public string Id = "";
            public string Text = "";
            public override string ToString() { return Text; }
        }

        public int MaxAuthors { get; private set; }
        public decimal SafetyReserveGb { get; private set; }
        public string GroupTemplate { get; private set; }
        public string AuthorFolderTemplate { get; private set; }

        public ArchiveSettingsForm(LanguageManager language, Font appFont, int maxAuthors, string groupTemplate, string authorFolderTemplate, decimal safetyReserveGb, AuthorEntityStore authorStore)
        {
            _language = language;
            _authorStore = authorStore;
            MaxAuthors = maxAuthors;
            SafetyReserveGb = safetyReserveGb;
            GroupTemplate = groupTemplate;
            AuthorFolderTemplate = authorFolderTemplate;

            Text = L("Dialog.ArchiveSettings.Title");
            ClientSize = new Size(760, 680);
            MinimumSize = new Size(720, 660);
            MaximizeBox = false;
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(20, 16, 20, 14);
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            Panel intro = new Panel();
            intro.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("Dialog.ArchiveSettings.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            intro.Controls.Add(title);
            Label desc = new Label();
            desc.Text = L("Dialog.ArchiveSettings.Description");
            desc.ForeColor = UiStyle.Muted;
            desc.Location = new Point(0, 27);
            ConfigureWrappedHint(desc, 674);
            intro.Resize += delegate
            {
                desc.MaximumSize = new Size(Math.Max(120, intro.ClientSize.Width), 0);
            };
            intro.Controls.Add(desc);
            root.Controls.Add(intro, 0, 0);

            Panel group = new Panel();
            group.Dock = DockStyle.Fill;
            Label groupTitle = new Label();
            groupTitle.Text = L("Dialog.ArchiveSettings.GroupTitle");
            groupTitle.Font = new Font(appFont, FontStyle.Bold);
            groupTitle.AutoSize = true;
            groupTitle.Location = new Point(0, 8);
            group.Controls.Add(groupTitle);

            int editorCaptionWidth = MeasureEditorCaptionWidth(
                L("Dialog.ArchiveSettings.MaxAuthors"),
                L("Dialog.ArchiveSettings.GroupPrefix"),
                L("Dialog.ArchiveSettings.NumberingRule"),
                L("Dialog.ArchiveSettings.GroupSuffix"),
                L("Dialog.ArchiveSettings.Preview"),
                L("Dialog.ArchiveSettings.AuthorTemplate"),
                L("Dialog.ArchiveSettings.AuthorPreview"));

            TableLayoutPanel groupTable = new TableLayoutPanel();
            groupTable.Location = new Point(0, 34);
            groupTable.Size = new Size(674, 194);
            groupTable.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            groupTable.ColumnCount = 2;
            groupTable.RowCount = 5;
            groupTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, editorCaptionWidth));
            groupTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++) groupTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            group.Controls.Add(groupTable);
            Action layoutGroupSection = delegate
            {
                groupTable.Width = Math.Max(120, group.ClientSize.Width);
            };
            group.Resize += delegate { layoutGroupSection(); };

            _maxAuthors = new NumericUpDown();
            _maxAuthors.Minimum = 1;
            _maxAuthors.Maximum = 999;
            _maxAuthors.Value = Math.Max(1, Math.Min(999, maxAuthors));
            _maxAuthors.Width = 100;
            _maxAuthors.Anchor = AnchorStyles.Left;
            AddRow(groupTable, 0, L("Dialog.ArchiveSettings.MaxAuthors"), _maxAuthors);

            string currentPrefix, currentRule, currentSuffix;
            if (!GroupNaming.TryDecomposeTemplate(groupTemplate, out currentPrefix, out currentRule, out currentSuffix))
            {
                currentPrefix = GroupNaming.DefaultPrefix;
                currentRule = GroupNaming.DefaultRuleId;
                currentSuffix = GroupNaming.DefaultSuffix;
            }

            _prefix = new TextBox();
            _prefix.Text = currentPrefix;
            _prefix.Dock = DockStyle.Fill;
            _prefix.Margin = new Padding(3, 7, 3, 5);
            AddRow(groupTable, 1, L("Dialog.ArchiveSettings.GroupPrefix"), _prefix);

            _rule = new ComboBox();
            _rule.DropDownStyle = ComboBoxStyle.DropDownList;
            // Align the numbering-rule editor with the prefix/suffix editors.
            _rule.Dock = DockStyle.Fill;
            _rule.Margin = new Padding(3, 7, 3, 5);
            AddRule(GroupNaming.RuleNumeric, L("GroupNumbering.Numeric"));
            AddRule(GroupNaming.RuleNumeric2, L("GroupNumbering.Numeric2"));
            AddRule(GroupNaming.RuleNumeric3, L("GroupNumbering.Numeric3"));
            AddRule(GroupNaming.RuleAlphaUpper, L("GroupNumbering.AlphaUpper"));
            AddRule(GroupNaming.RuleAlphaLower, L("GroupNumbering.AlphaLower"));
            UiStyle.FitComboBoxToItems(_rule, 180, 360);
            for (int i = 0; i < _rule.Items.Count; i++)
            {
                RuleItem item = _rule.Items[i] as RuleItem;
                if (item != null && String.Equals(item.Id, currentRule, StringComparison.OrdinalIgnoreCase))
                {
                    _rule.SelectedIndex = i;
                    break;
                }
            }
            if (_rule.SelectedIndex < 0 && _rule.Items.Count > 0) _rule.SelectedIndex = 0;
            AddRow(groupTable, 2, L("Dialog.ArchiveSettings.NumberingRule"), _rule);

            _suffix = new TextBox();
            _suffix.Text = currentSuffix;
            _suffix.Dock = DockStyle.Fill;
            _suffix.Margin = new Padding(3, 7, 3, 5);
            AddRow(groupTable, 3, L("Dialog.ArchiveSettings.GroupSuffix"), _suffix);

            _groupPreview = new Label();
            _groupPreview.Dock = DockStyle.Fill;
            _groupPreview.TextAlign = ContentAlignment.MiddleLeft;
            _groupPreview.ForeColor = UiStyle.Muted;
            _groupPreview.AutoEllipsis = true;
            AddRow(groupTable, 4, L("Dialog.ArchiveSettings.Preview"), _groupPreview);
            root.Controls.Add(group, 0, 1);

            Panel divider = new Panel();
            divider.Dock = DockStyle.Fill;
            divider.BackColor = UiStyle.Border;
            root.Controls.Add(divider, 0, 2);

            Panel safety = new Panel();
            safety.Dock = DockStyle.Fill;
            Label safetyTitle = new Label();
            safetyTitle.Text = L("Dialog.ArchiveSettings.SafetyTitle");
            safetyTitle.Font = new Font(appFont, FontStyle.Bold);
            safetyTitle.AutoSize = true;
            safetyTitle.Location = new Point(0, 10);
            safety.Controls.Add(safetyTitle);

            TableLayoutPanel safetyTable = new TableLayoutPanel();
            safetyTable.Location = new Point(0, 34);
            safetyTable.Size = new Size(674, 42);
            safetyTable.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            safetyTable.ColumnCount = 3;
            safetyTable.RowCount = 1;
            safetyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            safetyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            safetyTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            safetyTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            safety.Controls.Add(safetyTable);

            Label safetyCaption = UiStyle.NewCaption(
                L("Dialog.ArchiveSettings.SafetyReserve"));
            safetyCaption.Dock = DockStyle.Fill;
            safetyCaption.TextAlign = ContentAlignment.MiddleLeft;
            safetyTable.Controls.Add(safetyCaption, 0, 0);

            _safetyReserveGb = new NumericUpDown();
            _safetyReserveGb.DecimalPlaces = 1;
            _safetyReserveGb.Increment = 1M;
            _safetyReserveGb.Minimum = 0M;
            _safetyReserveGb.Maximum = 1024M;
            _safetyReserveGb.Value = Math.Max(0M, Math.Min(1024M, safetyReserveGb));
            _safetyReserveGb.Width = 100;
            _safetyReserveGb.Anchor = AnchorStyles.Left;
            safetyTable.Controls.Add(_safetyReserveGb, 1, 0);

            Label safetyUnit = new Label();
            safetyUnit.Text = "GB";
            safetyUnit.AutoSize = true;
            safetyUnit.Anchor = AnchorStyles.Left;
            safetyTable.Controls.Add(safetyUnit, 2, 0);

            root.Controls.Add(safety, 0, 5);

            Panel divider2 = new Panel();
            divider2.Dock = DockStyle.Fill;
            divider2.BackColor = UiStyle.Border;
            root.Controls.Add(divider2, 0, 4);

            Panel author = new Panel();
            author.Dock = DockStyle.Fill;
            Label authorTitle = new Label();
            authorTitle.Text = L("Dialog.ArchiveSettings.AuthorFolderTitle");
            authorTitle.Font = new Font(appFont, FontStyle.Bold);
            authorTitle.AutoSize = true;
            authorTitle.Location = new Point(0, 12);
            author.Controls.Add(authorTitle);
            Label authorHint = new Label();
            authorHint.Text = L("Dialog.ArchiveSettings.AuthorFolderHint");
            authorHint.ForeColor = UiStyle.Muted;
            authorHint.Location = new Point(0, 38);
            ConfigureWrappedHint(authorHint, 674);
            author.Controls.Add(authorHint);

            TableLayoutPanel authorTable = new TableLayoutPanel();
            authorTable.Location = new Point(0, authorHint.Bottom + 8);
            authorTable.Size = new Size(674, 82);
            authorTable.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            authorTable.ColumnCount = 2;
            authorTable.RowCount = 2;
            authorTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, editorCaptionWidth));
            authorTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            authorTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            authorTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            author.Controls.Add(authorTable);
            Action layoutAuthorSection = delegate
            {
                authorHint.MaximumSize = new Size(Math.Max(120, author.ClientSize.Width), 0);
                authorTable.Top = authorHint.Bottom + 8;
                authorTable.Width = Math.Max(120, author.ClientSize.Width);
            };
            author.Resize += delegate { layoutAuthorSection(); };
            authorHint.SizeChanged += delegate { layoutAuthorSection(); };

            _authorTemplate = new TextBox();
            _authorTemplate.Text = authorFolderTemplate;
            _authorTemplate.Dock = DockStyle.Fill;
            _authorTemplate.Margin = new Padding(3, 7, 3, 5);
            AddRow(authorTable, 0, L("Dialog.ArchiveSettings.AuthorTemplate"), _authorTemplate);

            _authorPreview = new Label();
            _authorPreview.Dock = DockStyle.Fill;
            _authorPreview.TextAlign = ContentAlignment.MiddleLeft;
            _authorPreview.ForeColor = UiStyle.Muted;
            _authorPreview.AutoEllipsis = true;
            AddRow(authorTable, 1, L("Dialog.ArchiveSettings.AuthorPreview"), _authorPreview);
            root.Controls.Add(author, 0, 3);

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
            Button folderRules = UiStyle.NewButton(L("EntityManager.RulesTab"), 160, false);
            folderRules.AutoSize = true;
            folderRules.Click += delegate {
                using (FolderNameRulesForm rules = new FolderNameRulesForm(_authorStore, _language, Font)) rules.ShowDialog(this);
            };
            buttons.Controls.Add(folderRules);
            root.Controls.Add(buttons, 0, 6);
            AcceptButton = save;
            CancelButton = cancel;

            _prefix.TextChanged += delegate { RefreshGroupPreview(); };
            _suffix.TextChanged += delegate { RefreshGroupPreview(); };
            _rule.SelectedIndexChanged += delegate { RefreshGroupPreview(); };
            _authorTemplate.TextChanged += delegate { RefreshAuthorPreview(); };
            save.Click += delegate { SaveValues(); };

            layoutGroupSection();
            layoutAuthorSection();
            RefreshGroupPreview();
            RefreshAuthorPreview();
            UiStyle.RelayoutLocalizedTree(this);
        }

        private string L(string key) { return _language.Get(key); }
        private string T(string value) { return _language.TranslateSource(value); }

        private int MeasureEditorCaptionWidth(params string[] captions)
        {
            int widest = 0;
            foreach (string caption in captions)
            {
                int width = TextRenderer.MeasureText(
                    caption ?? "",
                    Font,
                    new Size(Int32.MaxValue, Int32.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                if (width > widest)
                    widest = width;
            }

            // Match the default TableLayoutPanel cell margins while leaving a
            // small multilingual breathing space before the editor column.
            return Math.Max(80, widest + 14);
        }

        private static void ConfigureWrappedHint(Label label, int maxWidth)
        {
            if (label == null) return;
            label.AutoSize = true;
            label.MaximumSize = new Size(Math.Max(120, maxWidth), 0);
            label.AutoEllipsis = false;
            label.UseMnemonic = false;
            label.TextAlign = ContentAlignment.TopLeft;
        }

        private void AddRule(string id, string text)
        {
            RuleItem item = new RuleItem();
            item.Id = id;
            item.Text = text;
            _rule.Items.Add(item);
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, Control value)
        {
            Label label = UiStyle.NewCaption(caption);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            table.Controls.Add(label, 0, row);
            table.Controls.Add(value, 1, row);
        }

        private string RuleId
        {
            get
            {
                RuleItem item = _rule.SelectedItem as RuleItem;
                return item != null ? item.Id : GroupNaming.DefaultRuleId;
            }
        }

        private void RefreshGroupPreview()
        {
            string normalized, error;
            if (GroupNaming.TryBuildTemplate(_prefix.Text, RuleId, _suffix.Text, out normalized, out error))
            {
                _groupPreview.ForeColor = UiStyle.Muted;
                _groupPreview.Text = GroupNaming.Render(normalized, 1) + "  /  " + GroupNaming.Render(normalized, 2) + "  /  " + GroupNaming.Render(normalized, 3);
            }
            else
            {
                _groupPreview.ForeColor = UiStyle.Danger;
                _groupPreview.Text = T(error);
            }
        }

        private void RefreshAuthorPreview()
        {
            string normalized, error;
            if (AuthorFolderNaming.TryValidateTemplate(_authorTemplate.Text, out normalized, out error))
            {
                _authorPreview.ForeColor = UiStyle.Muted;
                _authorPreview.Text = AuthorFolderNaming.Render(normalized, L("Dialog.ArchiveSettings.AuthorSample"));
            }
            else
            {
                _authorPreview.ForeColor = UiStyle.Danger;
                _authorPreview.Text = L(error);
            }
        }

        private void SaveValues()
        {
            string group, groupError;
            if (!GroupNaming.TryBuildTemplate(_prefix.Text, RuleId, _suffix.Text, out group, out groupError))
            {
                UiMessageBox.Show(this, T(groupError), L("Dialog.InvalidGroupTemplate"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _prefix.Focus();
                return;
            }
            string author, authorError;
            if (!AuthorFolderNaming.TryValidateTemplate(_authorTemplate.Text, out author, out authorError))
            {
                UiMessageBox.Show(this, L(authorError), L("Dialog.InvalidAuthorFolderStyle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _authorTemplate.Focus();
                return;
            }
            MaxAuthors = Decimal.ToInt32(_maxAuthors.Value);
            SafetyReserveGb = _safetyReserveGb.Value;
            GroupTemplate = group;
            AuthorFolderTemplate = author;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
