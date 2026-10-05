using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class SupportForm : Form
    {
        private readonly LanguageManager _language;
        private readonly DonationService _donationService;
        private PictureBox _wechatPicture;
        private PictureBox _alipayPicture;
        private Button _afdianButton;
        private Button _koFiButton;
        private string _afdianUrl = DonationService.DefaultAfdianUrl;
        private string _koFiUrl = DonationService.DefaultKoFiUrl;

        public SupportForm(LanguageManager language, Font appFont, string appDir)
        {
            _language = language;
            _donationService = new DonationService(appDir);

            UiStyle.ApplyDialog(this, appFont);
            Text = language.Get("Support.Title");
            ClientSize = new Size(620, 600);
            MinimumSize = new Size(600, 580);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(28, 20, 28, 14);
            root.ColumnCount = 1;
            root.RowCount = 9;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 276F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;

            Label title = new Label();
            title.Text = language.Get("Support.Title");
            title.Font = new Font(appFont.FontFamily, 16F, FontStyle.Bold);
            title.ForeColor = UiStyle.Text;
            title.AutoSize = true;
            title.Location = new Point(0, 0);
            header.Controls.Add(title);

            Label subtitle = NewMutedLabel(language.Get("Support.Subtitle"));
            subtitle.AutoSize = true;
            subtitle.Location = new Point(1, 37);
            header.Controls.Add(subtitle);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(UiStyle.NewDivider(), 0, 1);

            Label methodsTitle = NewSectionTitle(language.Get("Support.Methods"));
            methodsTitle.Dock = DockStyle.Fill;
            methodsTitle.TextAlign = ContentAlignment.BottomLeft;
            methodsTitle.Padding = new Padding(0, 0, 0, 6);
            root.Controls.Add(methodsTitle, 0, 2);

            TableLayoutPanel qrRow = new TableLayoutPanel();
            qrRow.Dock = DockStyle.Fill;
            qrRow.Margin = new Padding(0);
            qrRow.ColumnCount = 2;
            qrRow.RowCount = 1;
            qrRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            qrRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            qrRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            qrRow.Controls.Add(CreateQrCard(language.Get("Support.WeChat"), EmbeddedResourceService.SupportWeChat, false, out _wechatPicture), 0, 0);
            qrRow.Controls.Add(CreateQrCard(language.Get("Support.Alipay"), EmbeddedResourceService.SupportAlipay, true, out _alipayPicture), 1, 0);
            root.Controls.Add(qrRow, 0, 3);

            Label onlineTitle = NewSectionTitle(language.Get("Support.OnlineSupport"));
            onlineTitle.Dock = DockStyle.Fill;
            onlineTitle.TextAlign = ContentAlignment.BottomLeft;
            onlineTitle.Padding = new Padding(0, 0, 0, 6);
            root.Controls.Add(onlineTitle, 0, 4);

            FlowLayoutPanel onlineButtons = new FlowLayoutPanel();
            onlineButtons.Dock = DockStyle.Fill;
            onlineButtons.Margin = new Padding(0);
            onlineButtons.Padding = new Padding(0, 4, 0, 0);
            onlineButtons.FlowDirection = FlowDirection.LeftToRight;
            onlineButtons.WrapContents = false;

            _afdianButton = NewAutoButton(_language.Get("Support.Afdian"), 160, false);
            _afdianButton.Margin = new Padding(0, 0, 12, 0);
            _afdianButton.Click += delegate { OpenUrl(_afdianUrl); };
            onlineButtons.Controls.Add(_afdianButton);

            _koFiButton = NewAutoButton(_language.Get("Support.KoFi"), 160, false);
            _koFiButton.Margin = new Padding(0);
            _koFiButton.Click += delegate { OpenUrl(_koFiUrl); };
            onlineButtons.Controls.Add(_koFiButton);

            root.Controls.Add(onlineButtons, 0, 5);

            root.Controls.Add(UiStyle.NewDivider(), 0, 7);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.ColumnCount = 1;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Button close = NewAutoButton(language.Get("Common.Close"), 88, false);
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(18, 10, 0, 8);
            close.DialogResult = DialogResult.Cancel;
            close.Click += delegate { Close(); };
            footer.Controls.Add(close, 0, 0);

            root.Controls.Add(footer, 0, 8);

            AcceptButton = close;
            CancelButton = close;
            Shown += async delegate { await LoadDonationContentAsync(); };
            FormClosed += delegate
            {
                DisposePicture(_wechatPicture);
                DisposePicture(_alipayPicture);
            };
        }

        private Control CreateQrCard(string titleText, string resourceName, bool rightCard, out PictureBox picture)
        {
            TableLayoutPanel card = new TableLayoutPanel();
            card.Anchor = AnchorStyles.None;
            card.Size = new Size(250, 274);
            card.Margin = rightCard ? new Padding(12, 0, 0, 0) : new Padding(0, 0, 12, 0);
            card.ColumnCount = 1;
            card.RowCount = 2;
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 240F));

            Label title = new Label();
            title.Text = titleText ?? "";
            title.Font = new Font(Font, FontStyle.Bold);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleCenter;
            card.Controls.Add(title, 0, 0);

            Panel imageHost = new Panel();
            imageHost.Anchor = AnchorStyles.None;
            imageHost.Size = new Size(240, 240);
            imageHost.BackColor = Color.White;

            Image image = EmbeddedResourceService.ReadImage(resourceName);
            picture = null;
            if (image != null)
            {
                picture = new PictureBox();
                picture.Dock = DockStyle.Fill;
                picture.Padding = new Padding(0);
                picture.SizeMode = PictureBoxSizeMode.Zoom;
                picture.Image = image;
                imageHost.Controls.Add(picture);
            }
            else
            {
                Label placeholder = NewMutedLabel(_language.Get("Support.QrComingSoon"));
                placeholder.Dock = DockStyle.Fill;
                placeholder.TextAlign = ContentAlignment.MiddleCenter;
                placeholder.Padding = new Padding(8);
                imageHost.Controls.Add(placeholder);
            }

            card.Controls.Add(imageHost, 0, 1);
            return card;
        }

        private async System.Threading.Tasks.Task LoadDonationContentAsync()
        {
            DonationContent content;
            try { content = await _donationService.LoadAsync(); }
            catch { return; }
            if (content == null || IsDisposed) return;

            ApplyRemoteImage(_wechatPicture, content.WeChatImage, content.WeChatEnabled);
            ApplyRemoteImage(_alipayPicture, content.AlipayImage, content.AlipayEnabled);
            _afdianUrl = content.AfdianUrl;
            _koFiUrl = content.KoFiUrl;
            _afdianButton.Visible = content.AfdianEnabled;
            _koFiButton.Visible = content.KoFiEnabled;
        }

        private static void ApplyRemoteImage(PictureBox picture, byte[] bytes, bool enabled)
        {
            if (picture == null) return;
            picture.Visible = enabled;
            if (!enabled || bytes == null || bytes.Length == 0) return;

            Image replacement = null;
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes))
                using (Image source = Image.FromStream(stream, true, true))
                    replacement = new Bitmap(source);
            }
            catch { return; }

            Image previous = picture.Image;
            picture.Image = replacement;
            if (previous != null)
            {
                try { previous.Dispose(); }
                catch { }
            }
        }

        private static void DisposePicture(PictureBox picture)
        {
            if (picture == null || picture.Image == null) return;
            try { picture.Image.Dispose(); }
            catch { }
            picture.Image = null;
        }

        private static Label NewSectionTitle(string text)
        {
            Label label = new Label();
            label.Text = text ?? "";
            label.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5F, FontStyle.Bold);
            label.ForeColor = UiStyle.Text;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static Label NewMutedLabel(string text)
        {
            Label label = new Label();
            label.Text = text ?? "";
            label.ForeColor = UiStyle.Muted;
            label.AutoSize = false;
            return label;
        }

        private Button NewAutoButton(string text, int minimumWidth, bool primary)
        {
            int preferred = TextRenderer.MeasureText(text ?? "", Font).Width + 30;
            return UiStyle.NewButton(text, Math.Max(minimumWidth, preferred), primary);
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, _language.Get("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }
}
