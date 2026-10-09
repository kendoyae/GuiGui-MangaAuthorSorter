using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class AuthorReferenceLibraryForm : Form
    {
        private readonly LanguageManager _language;
        private AuthorReferenceLibraryService _service;
        private readonly ComboBox _source;
        private readonly Label _remoteSize;
        private readonly Label _remoteDate;
        private readonly Label _localInfo;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly Label _license;
        private readonly Button _officialCheck;
        private readonly Button _officialDownload;
        private readonly Button _officialImport;
        private readonly Button _fullBuild;
        private PublicAuthorReleaseManifest _release;

        public AuthorReferenceLibraryForm(LanguageManager language, Font appFont)
        {
            _language = language;
            _service = AuthorReferenceLibraryService.Current;
            Text = L("AuthorReference.Title");
            ClientSize = new Size(760, 550);
            MinimumSize = new Size(720, 520);
            MaximizeBox = false;
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 14), ColumnCount = 1, RowCount = 7 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            Panel intro = new Panel { Dock = DockStyle.Fill };
            Label title = new Label { Text = L("AuthorReference.Title"), Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold), AutoSize = true };
            Label desc = new Label { Text = L("AuthorReference.Description"), ForeColor = UiStyle.Muted, Location = new Point(0, 28), AutoSize = true, MaximumSize = new Size(640, 0) };
            intro.Controls.Add(title); intro.Controls.Add(desc); root.Controls.Add(intro, 0, 0);

            GroupBox information = new GroupBox { Text = L("AuthorReference.RemoteTitle"), Dock = DockStyle.Fill, Padding = new Padding(12) };
            TableLayoutPanel info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F)); info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Label sourceLabel = UiStyle.NewCaption(L("AuthorReference.Source")); sourceLabel.Dock = DockStyle.Fill; sourceLabel.TextAlign = ContentAlignment.MiddleLeft;
            _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            _source.Items.Add(L("AuthorReference.OfficialSection"));
            _source.SelectedIndex = 0;
            info.Controls.Add(sourceLabel, 0, 0); info.Controls.Add(_source, 1, 0);
            _remoteSize = AddInfo(info, 1, L("AuthorReference.FileSize"));
            _remoteDate = AddInfo(info, 2, L("AuthorReference.UpdateDate"));
            _localInfo = AddInfo(info, 3, L("AuthorReference.LocalVersion"));
            information.Controls.Add(info); root.Controls.Add(information, 0, 1);

            Panel progressPanel = new Panel { Dock = DockStyle.Fill };
            _status = new Label { Dock = DockStyle.Top, Height = 26, Text = L("AuthorReference.StatusIdle"), AutoEllipsis = true };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 20, Top = 30, Minimum = 0, Maximum = 100 };
            progressPanel.Controls.Add(_progress); progressPanel.Controls.Add(_status); root.Controls.Add(progressPanel, 0, 2);


            GroupBox databaseActions = new GroupBox { Text = L("AuthorReference.OfficialSection"), Dock = DockStyle.Fill, Padding = new Padding(8) };
            FlowLayoutPanel databaseButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            _officialCheck = UiStyle.NewButton(L("AuthorReference.OfficialCheck"), 112, false);
            _officialDownload = UiStyle.NewButton(L("AuthorReference.OfficialDownload"), 122, true);
            _officialImport = UiStyle.NewButton(L("AuthorReference.OfficialImport"), 120, false);
            _fullBuild = UiStyle.NewButton(L("AuthorReference.FullBuild"), 134, false);
            _officialDownload.Enabled = false;
            _officialCheck.Click += async delegate { await CheckOfficialAsync(); };
            _officialDownload.Click += async delegate { await DownloadOfficialAsync(); };
            _officialImport.Click += async delegate { await ImportOfficialAsync(); };
            _fullBuild.Click += delegate { using (AdvancedAuthorBuilderForm advanced = new AdvancedAuthorBuilderForm(_language, Font)) advanced.ShowDialog(this); UpdateLocalInfo(); };
            databaseButtons.Controls.Add(_officialCheck); databaseButtons.Controls.Add(_officialDownload);
            databaseButtons.Controls.Add(_officialImport); databaseButtons.Controls.Add(_fullBuild);
            databaseActions.Controls.Add(databaseButtons); root.Controls.Add(databaseActions, 0, 4);

            _license = new Label { Text = L("AuthorReference.License"), ForeColor = UiStyle.Muted, Dock = DockStyle.Fill, AutoSize = false };
            root.Controls.Add(_license, 0, 5);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button close = UiStyle.NewButton(L("Common.Close"), 88, false); close.Click += delegate { Close(); };
            footer.Controls.Add(close); root.Controls.Add(footer, 0, 6);

            UpdateLocalInfo();
            SetBusy(false);
            Shown += async delegate
            {
                // Ordinary users should fetch the developer's small ready-to-use index
                // before being offered a potentially huge initial raw CSV crawl.
                await CheckOfficialAsync();
            };
            UiStyle.RelayoutLocalizedTree(this);
        }

        private Label AddInfo(TableLayoutPanel table, int row, string caption)
        {
            Label left = UiStyle.NewCaption(caption); left.Dock = DockStyle.Fill; left.TextAlign = ContentAlignment.MiddleLeft;
            Label value = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "—" };
            table.Controls.Add(left, 0, row); table.Controls.Add(value, 1, row); return value;
        }

        private async Task CheckOfficialAsync()
        {
            SetBusy(true); _status.Text = L("AuthorReference.OfficialChecking");
            try
            {
                _release = await PublicAuthorDistributionService.Current.CheckAsync();
                _status.Text = LF("AuthorReference.OfficialFound", _release.revision, FormatSize(_release.size));
                _remoteSize.Text = FormatSize(_release.size);
                _remoteDate.Text = _release.revision;
            }
            catch (Exception ex) { _release = null; _status.Text = ex.Message; }
            finally { SetBusy(false); }
        }
        private async Task DownloadOfficialAsync()
        {
            if (_release == null) return;
            if (UiMessageBox.Show(this, LF("AuthorReference.OfficialConfirm", _release.revision), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SetBusy(true); _status.Text = L("AuthorReference.OfficialDownloading");
            try
            {
                await PublicAuthorDistributionService.Current.InstallAsync(_release, delegate(long received, long total)
                {
                    if (IsDisposed || Disposing) return;
                    try { BeginInvoke(new MethodInvoker(delegate
                    {
                        _progress.Value = total > 0 ? (int)Math.Min(100L, received * 100L / total) : 0;
                    })); } catch { }
                });
                _status.Text = L("AuthorReference.PublicMergeRestart");
                _release = null;
            }
            catch (Exception ex) { _status.Text = LF("AuthorReference.StatusError", ex.Message); }
            finally { SetBusy(false); }
        }
        private async Task ImportOfficialAsync()
        {
            using (OpenFileDialog picker = new OpenFileDialog())
            {
                picker.Filter = "SQLite (*.db)|*.db|All files (*.*)|*.*";
                picker.Title = L("AuthorReference.OfficialImport");
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                if (UiMessageBox.Show(this, L("AuthorReference.ImportConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                SetBusy(true); _status.Text = L("AuthorReference.OfficialImporting");
                try
                {
                    await PublicAuthorDistributionService.Current.ImportAsync(picker.FileName);
                    _status.Text = L("AuthorReference.PublicMergeRestart");
                }
                catch (Exception ex) { _status.Text = LF("AuthorReference.StatusError", ex.Message); }
                finally { SetBusy(false); }
            }
        }

        private void UpdateLocalInfo()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppFiles.AuthorIndexDatabase);
            using (GuiGuiAuthorIndexDatabase database = new GuiGuiAuthorIndexDatabase(path))
            {
                PublicAuthorIndexInfo local = database.GetInfo();
                _localInfo.Text = local.Exists && String.IsNullOrEmpty(local.Error)
                    ? LF("AuthorReference.IndexSummary", local.Artists, local.Groups, local.Relations)
                    : L("AuthorReference.NotInstalled");
            }
        }

        private void SetBusy(bool busy)
        {
            _officialCheck.Enabled = !busy && !_service.IsDownloading;
            _officialDownload.Enabled = !busy && !_service.IsDownloading && _release != null;
            _officialImport.Enabled = !busy && !_service.IsDownloading;
            _fullBuild.Enabled = !busy && !_service.IsDownloading;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            string[] units = { "B", "KB", "MB", "GB" }; double value = bytes; int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.00") + " " + units[unit];
        }
        private string L(string key) { return _language.Get(key); }
        private string LF(string key, params object[] args) { return _language.Format(key, args); }
    }
}
