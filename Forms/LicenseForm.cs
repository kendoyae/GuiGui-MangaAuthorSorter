using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class LicenseForm : Form
    {
        private readonly LanguageManager _language;
        private readonly string _appDir;
        private readonly ListBox _sections;
        private readonly Label _sectionTitle;
        private readonly RichTextBox _sectionBody;
        private readonly SplitContainer _body;
        private int _widestSection;

        private readonly string[] _titleKeys = new[]
        {
            "License.Section.Application",
            "License.Section.Everything",
            "License.Section.Danbooru",
            "License.Section.Microsoft",
            "License.Section.External",
            "License.Section.Network"
        };

        private readonly string[] _bodyKeys = new[]
        {
            "License.Body.Application",
            "License.Body.Everything",
            "License.Body.Danbooru",
            "License.Body.Microsoft",
            "License.Body.External",
            "License.Body.Network"
        };

        public LicenseForm(LanguageManager language, Font appFont, string appDir)
        {
            _language = language;
            _appDir = appDir ?? "";

            UiStyle.ApplyDialog(this, appFont);
            Text = language.Get("License.Title");
            ClientSize = new Size(860, 560);
            MinimumSize = new Size(800, 520);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(20, 16, 20, 14);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;

            Label title = new Label();
            title.Text = language.Get("License.Title");
            title.Font = new Font(appFont.FontFamily, 13F, FontStyle.Bold);
            title.ForeColor = UiStyle.Text;
            title.AutoSize = true;
            title.Location = new Point(0, 0);
            header.Controls.Add(title);

            Label intro = new Label();
            intro.Text = language.Get("License.Description");
            intro.ForeColor = UiStyle.Muted;
            intro.AutoSize = true;
            intro.Location = new Point(1, 30);
            header.Controls.Add(intro);
            root.Controls.Add(header, 0, 0);
            root.Controls.Add(UiStyle.NewDivider(), 0, 1);

            _body = new SplitContainer();
            _body.Dock = DockStyle.Fill;
            _body.Orientation = Orientation.Vertical;
            _body.FixedPanel = FixedPanel.Panel1;
            _body.IsSplitterFixed = true;
            _body.SplitterWidth = 1;
            _body.SplitterDistance = 220;
            _body.Margin = new Padding(0, 12, 0, 0);
            _body.Panel1.Padding = new Padding(0, 0, 12, 0);
            _body.Panel2.Padding = new Padding(18, 0, 0, 0);

            _sections = new ListBox();
            _sections.Dock = DockStyle.Fill;
            _sections.BorderStyle = BorderStyle.FixedSingle;
            _sections.IntegralHeight = false;
            _sections.ItemHeight = 28;
            _sections.HorizontalScrollbar = false;
            _widestSection = 0;
            for (int i = 0; i < _titleKeys.Length; i++)
            {
                string sectionText = language.Get(_titleKeys[i]);
                _sections.Items.Add(sectionText);
                _widestSection = Math.Max(
                    _widestSection,
                    TextRenderer.MeasureText(
                        sectionText,
                        appFont,
                        new Size(Int32.MaxValue, Int32.MaxValue),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width);
            }
            _sections.SelectedIndexChanged += delegate { ShowSelectedSection(); };
            _body.Panel1.Controls.Add(_sections);

            TableLayoutPanel detail = new TableLayoutPanel();
            detail.Dock = DockStyle.Fill;
            detail.ColumnCount = 1;
            detail.RowCount = 2;
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _sectionTitle = new Label();
            _sectionTitle.Dock = DockStyle.Fill;
            _sectionTitle.Font = new Font(appFont.FontFamily, 10.5F, FontStyle.Bold);
            _sectionTitle.ForeColor = UiStyle.Text;
            _sectionTitle.TextAlign = ContentAlignment.MiddleLeft;
            detail.Controls.Add(_sectionTitle, 0, 0);

            _sectionBody = new RichTextBox();
            _sectionBody.Dock = DockStyle.Fill;
            _sectionBody.ReadOnly = true;
            _sectionBody.BorderStyle = BorderStyle.None;
            _sectionBody.BackColor = UiStyle.Background;
            _sectionBody.ForeColor = UiStyle.Text;
            _sectionBody.DetectUrls = true;
            _sectionBody.ScrollBars = RichTextBoxScrollBars.Vertical;
            _sectionBody.WordWrap = true;
            _sectionBody.TabStop = false;
            detail.Controls.Add(_sectionBody, 0, 1);
            _body.Panel2.Controls.Add(detail);

            root.Controls.Add(_body, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            footer.Padding = new Padding(0, 10, 0, 0);

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.FlowDirection = FlowDirection.LeftToRight;
            left.WrapContents = false;
            left.Margin = new Padding(0);
            string openText = language.Get("License.OpenNoticeFile");
            Button openNotice = UiStyle.NewButton(openText, Math.Max(116, TextRenderer.MeasureText(openText, appFont).Width + 36), false);
            openNotice.Click += delegate { OpenNoticeFile(); };
            left.Controls.Add(openNotice);
            footer.Controls.Add(left, 0, 0);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.Margin = new Padding(0);
            string closeText = language.Get("Common.Close");
            Button close = UiStyle.NewButton(closeText, Math.Max(88, TextRenderer.MeasureText(closeText, appFont).Width + 34), false);
            close.DialogResult = DialogResult.Cancel;
            close.Click += delegate { Close(); };
            right.Controls.Add(close);
            footer.Controls.Add(right, 1, 0);
            root.Controls.Add(footer, 0, 3);

            _sections.SelectedIndex = 0;
            AcceptButton = close;
            CancelButton = close;
            Shown += delegate
            {
                try { BeginInvoke(new MethodInvoker(ApplySectionWidth)); }
                catch (InvalidOperationException) { }
            };
        }

        private void ApplySectionWidth()
        {
            if (_body == null || _body.IsDisposed || _body.ClientSize.Width <= 0) return;
            int desired = Math.Max(220, _widestSection + 40);
            int maximum = Math.Max(180, _body.ClientSize.Width - _body.SplitterWidth - 420);
            desired = Math.Min(desired, maximum);
            try
            {
                _body.Panel1MinSize = 0;
                _body.Panel2MinSize = 0;
                _body.SplitterDistance = desired;
                _body.Panel1MinSize = desired;
                _body.Panel2MinSize = Math.Min(420, Math.Max(0, _body.ClientSize.Width - desired - _body.SplitterWidth));
            }
            catch (ArgumentOutOfRangeException) { }
        }

        private void ShowSelectedSection()
        {
            int index = _sections.SelectedIndex;
            if (index < 0 || index >= _titleKeys.Length)
                return;

            _sectionTitle.Text = _language.Get(_titleKeys[index]);
            _sectionBody.Text = _language.Get(_bodyKeys[index]);
            _sectionBody.SelectionStart = 0;
            _sectionBody.SelectionLength = 0;
        }

        private void OpenNoticeFile()
        {
            string path = Path.Combine(_appDir, "LICENSE_NOTICES.txt");
            if (!File.Exists(path))
            {
                UiMessageBox.Show(this, _language.Get("License.NoticeMissing"), _language.Get("License.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, _language.Get("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
