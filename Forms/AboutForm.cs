using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class AboutForm : Form
    {
        private const string GitHubFeedbackUrl = UpdateService.RepositoryUrl + "/issues";
        private const string QqGroup = "1103654612";
        private const string QqJoinLinkFileName = "QQ_GROUP_JOIN_URL.txt";
        private const string DefaultQqJoinUrl = "http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=fyelXxwKBK21nb7oyCsZBdtZN7ldEzRJ&authKey=N%2FrBIUpHX09uz9sJagQyCzDvRu4d1XAOELBkI5Plp6qCWN7c0rdp496yhMBH3yV1&noverify=0&group_code=1103654612";

        private readonly LanguageManager _language;
        private readonly string _appDir;
        private readonly Label _feedbackLabel;
        private readonly Label _updateLabel;

        public AboutForm(LanguageManager language, Font appFont, string appDir, Action checkUpdates)
        {
            _language = language;
            _appDir = appDir ?? "";

            UiStyle.ApplyDialog(this, appFont);
            Text = language.Get("About.Title");
            ClientSize = new Size(620, 462);
            MinimumSize = new Size(590, 442);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(24, 20, 24, 14);
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;

            PictureBox appIcon = new PictureBox();
            appIcon.Size = new Size(64, 64);
            // Center the 64×64 artwork in an 80 px visual column and align its
            // vertical center with the three information lines on the right.
            appIcon.Location = new Point(8, 38);
            appIcon.SizeMode = PictureBoxSizeMode.CenterImage;
            try
            {
                appIcon.Image = UiStyle.LoadAboutIconImage();
            }
            catch
            {
                // The text header remains usable if Windows cannot read the icon.
            }
            appIcon.Disposed += delegate
            {
                if (appIcon.Image != null)
                    appIcon.Image.Dispose();
            };
            header.Controls.Add(appIcon);

            Label product = new Label();
            product.Text = language.Get("About.ProductName");
            product.Font = new Font(appFont.FontFamily, 18F, FontStyle.Bold);
            product.ForeColor = UiStyle.Text;
            product.AutoSize = true;
            product.Location = new Point(0, 0);
            header.Controls.Add(product);

            Label tagline = new Label();
            tagline.Text = language.Get("About.Tagline");
            tagline.ForeColor = UiStyle.Muted;
            tagline.AutoSize = true;
            tagline.Location = new Point(96, 38);
            header.Controls.Add(tagline);

            Label developer = new Label();
            developer.Text = language.Get("About.Developer") + " kendo";
            developer.ForeColor = UiStyle.Muted;
            developer.AutoSize = true;
            developer.Location = new Point(96, 64);
            header.Controls.Add(developer);

            Label version = new Label();
            version.Text = language.Get("About.Version") + " " + AppVersion.Display;
            version.ForeColor = UiStyle.Muted;
            version.AutoSize = true;
            version.Location = new Point(96, 87);
            header.Controls.Add(version);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(UiStyle.NewDivider(), 0, 1);

            TableLayoutPanel project = CreateSectionTable(language.Get("About.ProjectTitle"));
            FlowLayoutPanel projectButtons = NewButtonRow();

            Button github = NewAutoButton(language.Get("About.GitHub"), 100, false);
            github.Click += delegate { OpenUrl(UpdateService.RepositoryUrl); };
            projectButtons.Controls.Add(github);

            Button update = NewAutoButton(language.Get("About.CheckUpdates"), 112, false);
            update.Click += delegate
            {
                Close();
                if (checkUpdates != null) checkUpdates();
            };
            projectButtons.Controls.Add(update);
            project.Controls.Add(projectButtons, 0, 1);
            project.SetColumnSpan(projectButtons, 2);

            _updateLabel = NewMutedLabel("");
            _updateLabel.Dock = DockStyle.Fill;
            _updateLabel.TextAlign = ContentAlignment.MiddleLeft;
            project.Controls.Add(_updateLabel, 0, 2);
            project.SetColumnSpan(_updateLabel, 2);
            root.Controls.Add(project, 0, 2);

            root.Controls.Add(UiStyle.NewDivider(), 0, 3);

            TableLayoutPanel feedback = CreateSectionTable(language.Get("About.FeedbackTitle"));
            FlowLayoutPanel feedbackButtons = NewButtonRow();

            Button qq = NewAutoButton(language.Get("About.QQFeedbackGroup"), 142, false);
            qq.Click += delegate { OpenQqFeedbackGroup(); };
            feedbackButtons.Controls.Add(qq);

            Button githubFeedback = NewAutoButton(language.Get("About.GitHubFeedback"), 126, false);
            githubFeedback.Click += delegate { OpenUrl(GitHubFeedbackUrl); };
            feedbackButtons.Controls.Add(githubFeedback);

            feedback.Controls.Add(feedbackButtons, 0, 1);
            feedback.SetColumnSpan(feedbackButtons, 2);

            _feedbackLabel = NewMutedLabel("");
            _feedbackLabel.Dock = DockStyle.Fill;
            _feedbackLabel.TextAlign = ContentAlignment.MiddleLeft;
            feedback.Controls.Add(_feedbackLabel, 0, 2);
            feedback.SetColumnSpan(_feedbackLabel, 2);
            root.Controls.Add(feedback, 0, 4);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            footer.Padding = new Padding(0, 10, 0, 0);

            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.FlowDirection = FlowDirection.LeftToRight;
            left.WrapContents = false;
            left.Margin = new Padding(0);
            Button license = NewAutoButton(language.Get("About.License"), 92, false);
            license.Click += delegate
            {
                using (LicenseForm dlg = new LicenseForm(_language, Font))
                    dlg.ShowDialog(this);
            };
            left.Controls.Add(license);
            footer.Controls.Add(left, 0, 0);

            FlowLayoutPanel right = new FlowLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.FlowDirection = FlowDirection.RightToLeft;
            right.WrapContents = false;
            right.Margin = new Padding(0);
            Button close = NewAutoButton(language.Get("Common.Close"), 88, false);
            close.DialogResult = DialogResult.Cancel;
            close.Click += delegate { Close(); };
            right.Controls.Add(close);
            footer.Controls.Add(right, 1, 0);

            root.Controls.Add(footer, 0, 6);

            AcceptButton = close;
            CancelButton = close;
        }

        private FlowLayoutPanel NewButtonRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0, 4, 0, 4);
            return row;
        }

        private TableLayoutPanel CreateSectionTable(string titleText)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Margin = new Padding(0);
            table.Padding = new Padding(0, 8, 0, 0);
            table.ColumnCount = 2;
            table.RowCount = 3;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

            Label title = NewSectionTitle(titleText);
            title.Dock = DockStyle.Fill;
            table.Controls.Add(title, 0, 0);
            table.SetColumnSpan(title, 2);
            return table;
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
            int preferred = TextRenderer.MeasureText(text ?? "", Font).Width + 38;
            Button button = UiStyle.NewButton(text, Math.Max(minimumWidth, preferred), primary);
            button.Margin = new Padding(0, 0, 12, 0);
            return button;
        }

        private void OpenQqFeedbackGroup()
        {
            string joinUrl = LoadQqJoinUrl();
            if (!String.IsNullOrWhiteSpace(joinUrl))
            {
                if (TryOpenExternal(joinUrl))
                {
                    _feedbackLabel.Text = _language.Get("About.QQWebFallbackOpening");
                    return;
                }
            }

            try
            {
                Clipboard.SetText(QqGroup);
                _feedbackLabel.Text = _language.Get("About.QQCopiedOnly");
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, _language.Get("Common.Error.SaveFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                UiMessageBox.Show(
                    this,
                    String.Format(_language.Get("About.QQOpenFailedNoCopy"), QqGroup),
                    _language.Get("About.QQOpenFailedTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            UiMessageBox.Show(
                this,
                String.Format(_language.Get("About.QQOpenFailed"), QqGroup),
                _language.Get("About.QQOpenFailedTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static bool TryOpenExternal(string target)
        {
            if (String.IsNullOrWhiteSpace(target)) return false;
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        private string LoadQqJoinUrl()
        {
            try
            {
                string path = Path.Combine(_appDir, "Assets", QqJoinLinkFileName);
                if (!File.Exists(path))
                    return DefaultQqJoinUrl;

                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//"))
                        continue;

                    int http = line.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
                    if (http < 0)
                        http = line.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
                    int tencent = line.IndexOf("tencent://", StringComparison.OrdinalIgnoreCase);

                    int start = -1;
                    if (http >= 0 && tencent >= 0) start = Math.Min(http, tencent);
                    else if (http >= 0) start = http;
                    else if (tencent >= 0) start = tencent;

                    if (start < 0)
                        continue;

                    string url = line.Substring(start).Trim();
                    int whitespace = url.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                    if (whitespace > 0)
                        url = url.Substring(0, whitespace);
                    return url;
                }
            }
            catch { }

            return DefaultQqJoinUrl;
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
