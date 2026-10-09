using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class OnlineAuthorSettingsForm : Form
    {
        private readonly LanguageManager _language;
        private readonly CheckBox _enabled;
        private readonly CheckBox _saveCache;
        private readonly NumericUpDown _maxLookups;
        private readonly CheckBox _useLocalReference;
        private readonly CheckBox _useEhentai;
        private readonly string _entityPath;
        private readonly Action _openEntityLibrary;
        private readonly Action _openReferenceLibrary;

        public bool LookupEnabled { get; private set; }
        public string ProviderId { get; private set; }
        public bool SaveCache { get; private set; }
        public int MaxLookupsPerScan { get; private set; }
        public bool UseLocalReference { get; private set; }
        public bool UseEhentai { get; private set; }

        public OnlineAuthorSettingsForm(
            LanguageManager language,
            Font appFont,
            bool enabled,
            string providerId,
            bool saveCache,
            int maxLookups,
            bool useLocalReference,
            bool useEhentai,
            string entityPath,
            Action openEntityLibrary,
            Action openReferenceLibrary)
        {
            _language = language;
            _entityPath = entityPath ?? "";
            _openEntityLibrary = openEntityLibrary;
            _openReferenceLibrary = openReferenceLibrary;
            LookupEnabled = enabled;
            ProviderId = "EvidenceChain";
            SaveCache = saveCache;
            MaxLookupsPerScan = Math.Max(1, Math.Min(100, maxLookups));
            UseLocalReference = useLocalReference;
            UseEhentai = useEhentai;

            Text = L("Dialog.OnlineAuthor.Title");
            ClientSize = new Size(780, 650);
            MinimumSize = new Size(740, 610);
            MaximizeBox = false;
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(20, 16, 20, 14);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            Controls.Add(root);

            Panel intro = new Panel();
            intro.Dock = DockStyle.Fill;
            Label title = new Label();
            title.Text = L("Dialog.OnlineAuthor.Title");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            intro.Controls.Add(title);
            Label hint = new Label();
            hint.Text = L("Dialog.OnlineAuthor.Description");
            hint.ForeColor = UiStyle.Muted;
            hint.Location = new Point(0, 28);
            hint.AutoSize = true;
            hint.MaximumSize = new Size(670, 0);
            intro.Resize += delegate
            {
                hint.MaximumSize = new Size(Math.Max(120, intro.ClientSize.Width), 0);
            };
            intro.Controls.Add(hint);
            root.Controls.Add(intro, 0, 0);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.ColumnCount = 2;
            table.RowCount = 4;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            root.Controls.Add(table, 0, 1);

            _enabled = new CheckBox();
            _enabled.Text = L("Dialog.OnlineAuthor.Enable");
            _enabled.AutoSize = true;
            _enabled.Checked = enabled;
            _enabled.Anchor = AnchorStyles.Left;
            _enabled.CheckedChanged += delegate { UpdateEnabledState(); };
            table.Controls.Add(_enabled, 1, 0);
            table.Controls.Add(UiStyle.NewCaption(""), 0, 0);

            _saveCache = new CheckBox();
            _saveCache.Text = L("Dialog.OnlineAuthor.SaveCache");
            _saveCache.AutoSize = true;
            _saveCache.Checked = saveCache;
            _saveCache.Anchor = AnchorStyles.Left;
            AddRow(table, 1, L("Dialog.OnlineAuthor.LocalLibrary"), _saveCache);

            _maxLookups = new NumericUpDown();
            _maxLookups.Minimum = 1;
            _maxLookups.Maximum = 100;
            _maxLookups.Value = MaxLookupsPerScan;
            _maxLookups.Width = 100;
            _maxLookups.Anchor = AnchorStyles.Left;
            AddRow(table, 2, L("Dialog.OnlineAuthor.MaxLookups"), _maxLookups);

            Label policy = new Label();
            policy.Text = L("Dialog.OnlineAuthor.Policy");
            policy.ForeColor = UiStyle.Muted;
            policy.AutoSize = true;
            policy.MaximumSize = new Size(500, 0);
            policy.Anchor = AnchorStyles.Left;
            table.Resize += delegate
            {
                int available = Math.Max(180, table.ClientSize.Width - 190);
                policy.MaximumSize = new Size(available, 0);
            };
            AddRow(table, 3, L("Dialog.OnlineAuthor.AutoRule"), policy);

            GroupBox chainBox = new GroupBox();
            chainBox.Text = L("Dialog.OnlineAuthor.SourcesTitle");
            chainBox.Dock = DockStyle.Fill;
            chainBox.Padding = new Padding(12, 12, 12, 10);
            FlowLayoutPanel chain = new FlowLayoutPanel();
            chain.Dock = DockStyle.Fill;
            chain.FlowDirection = FlowDirection.TopDown;
            chain.WrapContents = false;
            chain.AutoScroll = true;
            _useLocalReference = NewSourceCheckBox(L("Dialog.OnlineAuthor.SourceLocal"), useLocalReference);
            _useEhentai = NewSourceCheckBox(L("Dialog.OnlineAuthor.SourceEhentai"), useEhentai);
            chain.Controls.Add(_useLocalReference);
            chain.Controls.Add(_useEhentai);
            chainBox.Controls.Add(chain);
            root.Controls.Add(chainBox, 0, 2);

            Panel divider = new Panel();
            divider.Dock = DockStyle.Fill;
            divider.BackColor = UiStyle.Border;
            root.Controls.Add(divider, 0, 3);

            FlowLayoutPanel tools = new FlowLayoutPanel();
            tools.Dock = DockStyle.Fill;
            tools.FlowDirection = FlowDirection.LeftToRight;
            tools.WrapContents = false;
            tools.Padding = new Padding(0, 8, 0, 0);
            Button openLibrary = UiStyle.NewButton(L("Dialog.OnlineAuthor.OpenLibrary"), 190, false);
            openLibrary.Click += delegate { OpenEntityLibrary(); };
            tools.Controls.Add(openLibrary);
            Button referenceLibrary = UiStyle.NewButton(L("AuthorReference.Manage"), 190, false);
            referenceLibrary.Click += delegate { if (_openReferenceLibrary != null) _openReferenceLibrary(); };
            tools.Controls.Add(referenceLibrary);
            Label file = new Label();
            file.Text = System.IO.Path.GetFileName(_entityPath);
            file.ForeColor = UiStyle.Muted;
            file.AutoSize = true;
            file.Margin = new Padding(10, 8, 0, 0);
            tools.Controls.Add(file);
            root.Controls.Add(tools, 0, 4);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 8, 0, 0);
            Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Click += delegate { Close(); };
            Button save = UiStyle.NewButton(L("Common.Save"), 88, true);
            save.Click += delegate { SaveValues(); };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);
            root.Controls.Add(buttons, 0, 5);
            UpdateEnabledState();
            AcceptButton = save;
            CancelButton = cancel;
            UiStyle.RelayoutLocalizedTree(this);
        }

        private string L(string key) { return _language.Get(key); }

        private static CheckBox NewSourceCheckBox(string text, bool value)
        {
            CheckBox box = new CheckBox(); box.Text = text; box.Checked = value; box.AutoSize = true; box.Margin = new Padding(4, 5, 4, 2); return box;
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, Control control)
        {
            Label label = UiStyle.NewCaption(caption);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            table.Controls.Add(label, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private void UpdateEnabledState()
        {
            bool enabled = _enabled != null && _enabled.Checked;
            if (_saveCache != null) _saveCache.Enabled = enabled;
            if (_maxLookups != null) _maxLookups.Enabled = enabled;
            if (_useLocalReference != null) _useLocalReference.Enabled = enabled;
            if (_useEhentai != null) _useEhentai.Enabled = enabled;
        }

        private void SaveValues()
        {
            LookupEnabled = _enabled.Checked;
            ProviderId = "EvidenceChain";
            SaveCache = _saveCache.Checked;
            MaxLookupsPerScan = Decimal.ToInt32(_maxLookups.Value);
            UseLocalReference = _useLocalReference.Checked;
            UseEhentai = _useEhentai.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OpenEntityLibrary()
        {
            if (_openEntityLibrary != null) _openEntityLibrary();
        }
    }
}
