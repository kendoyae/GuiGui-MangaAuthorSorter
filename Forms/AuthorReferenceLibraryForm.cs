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
        private readonly Button _check;
        private readonly Button _download;
        private readonly Button _pause;
        private readonly Label _license;
        private AuthorReferenceRemoteInfo _remote;

        public AuthorReferenceLibraryForm(LanguageManager language, Font appFont)
        {
            _language = language;
            _service = AuthorReferenceLibraryService.Current;
            Text = L("AuthorReference.Title");
            ClientSize = new Size(700, 455);
            MinimumSize = new Size(650, 430);
            MaximizeBox = false;
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 14), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
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
            _source.Items.Add(L("AuthorReference.Source.EhTagTranslation"));
            _source.Items.Add(L("AuthorReference.Source.EhTagDb"));
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

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            _check = UiStyle.NewButton(L("AuthorReference.Check"), 120, false);
            _download = UiStyle.NewButton(L("AuthorReference.Download"), 140, true);
            _pause = UiStyle.NewButton(L("AuthorReference.Pause"), 110, false);
            _check.Click += async delegate { await CheckAsync(); };
            _download.Click += async delegate { await DownloadAsync(); };
            _pause.Click += delegate { Pause(); };
            actions.Controls.Add(_check); actions.Controls.Add(_download); actions.Controls.Add(_pause); root.Controls.Add(actions, 0, 3);

            _license = new Label { Text = L("AuthorReference.License"), ForeColor = UiStyle.Muted, Dock = DockStyle.Fill, AutoSize = false };
            root.Controls.Add(_license, 0, 4);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button close = UiStyle.NewButton(L("Common.Close"), 88, false); close.Click += delegate { Close(); };
            footer.Controls.Add(close); root.Controls.Add(footer, 0, 5);

            _service.ProgressChanged += ServiceProgressChanged;
            _source.SelectedIndexChanged += async delegate { await SwitchSourceAsync(); };
            FormClosed += delegate { _service.ProgressChanged -= ServiceProgressChanged; };
            FormClosing += delegate { if (_service.IsDownloading) _service.Pause(); };
            UpdateLocalInfo();
            SetBusy(false);
            Shown += async delegate { await CheckAsync(); };
            UiStyle.RelayoutLocalizedTree(this);
        }

        private async Task SwitchSourceAsync()
        {
            AuthorReferenceLibraryService[] services = AuthorReferenceLibraryService.All;
            int selected = Math.Max(0, Math.Min(_source.SelectedIndex, services.Length - 1));
            AuthorReferenceLibraryService next = services[selected];
            if (Object.ReferenceEquals(next, _service)) return;
            if (_service.IsDownloading)
            {
                _source.SelectedIndex = Array.IndexOf(services, _service);
                return;
            }
            _service.ProgressChanged -= ServiceProgressChanged;
            _service = next;
            _license.Text = selected == 0 ? L("AuthorReference.License") : L("AuthorReference.License.EhTagDb");
            _service.ProgressChanged += ServiceProgressChanged;
            _remote = null;
            _remoteSize.Text = "—";
            _remoteDate.Text = "—";
            _progress.Value = 0;
            UpdateLocalInfo();
            await CheckAsync();
        }

        private Label AddInfo(TableLayoutPanel table, int row, string caption)
        {
            Label left = UiStyle.NewCaption(caption); left.Dock = DockStyle.Fill; left.TextAlign = ContentAlignment.MiddleLeft;
            Label value = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "—" };
            table.Controls.Add(left, 0, row); table.Controls.Add(value, 1, row); return value;
        }

        private async Task CheckAsync()
        {
            SetBusy(true); _status.Text = L("AuthorReference.StatusChecking");
            try
            {
                _remote = await _service.GetRemoteInfoAsync();
                _remoteSize.Text = FormatSize(_remote.Size);
                _remoteDate.Text = FormatDate(_remote.UpdatedUtc);
                AuthorReferenceLocalInfo local = _service.GetLocalInfo();
                bool update = local.Exists && _remote.UpdatedUtc > local.UpdatedUtc.AddMinutes(1);
                _status.Text = update ? L("AuthorReference.StatusUpdateAvailable") : (local.Exists ? L("AuthorReference.StatusCurrent") : L("AuthorReference.StatusReady"));
                _download.Text = local.Exists ? L("AuthorReference.Update") : L("AuthorReference.Download");
            }
            catch (Exception ex) { _status.Text = LF("AuthorReference.StatusError", ex.Message); }
            finally { SetBusy(false); }
        }

        private async Task DownloadAsync()
        {
            SetBusy(true); _pause.Text = L("AuthorReference.Pause"); _pause.Enabled = true;
            _status.Text = File.Exists(_service.PartialPath) ? L("AuthorReference.StatusResuming") : L("AuthorReference.StatusDownloading");
            try
            {
                AuthorReferenceDownloadResult result = await _service.DownloadAsync(_remote);
                if (result == AuthorReferenceDownloadResult.Paused)
                {
                    _status.Text = L("AuthorReference.StatusPaused");
                    _download.Text = L("AuthorReference.Resume");
                }
                else
                {
                    _progress.Value = 100;
                    _status.Text = L("AuthorReference.StatusComplete");
                    _download.Text = L("AuthorReference.Update");
                    UpdateLocalInfo();
                }
            }
            catch (Exception ex) { _status.Text = LF("AuthorReference.StatusInterrupted", ex.Message); _download.Text = L("AuthorReference.Resume"); }
            finally { SetBusy(false); }
        }

        private void Pause()
        {
            if (!_service.IsDownloading) return;
            _status.Text = L("AuthorReference.StatusPausing");
            _pause.Enabled = false;
            _service.Pause();
        }

        private void ServiceProgressChanged(AuthorReferenceDownloadProgress info)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    int value = info.Total > 0 ? (int)Math.Min(100, info.Received * 100L / info.Total) : 0;
                    _progress.Value = Math.Max(0, value);
                    _status.Text = LF("AuthorReference.StatusProgress", FormatSize(info.Received), FormatSize(info.Total), FormatSize((long)info.BytesPerSecond));
                }));
            }
            catch { }
        }

        private void UpdateLocalInfo()
        {
            AuthorReferenceLocalInfo local = _service.GetLocalInfo();
            _localInfo.Text = local.Exists
                ? (local.ArtistCount > 0
                    ? LF("AuthorReference.LocalInfo", local.ArtistCount, FormatSize(local.Size), FormatDate(local.UpdatedUtc), ShortRevision(local.Revision))
                    : LF("AuthorReference.LocalArchiveInfo", FormatSize(local.Size), FormatDate(local.UpdatedUtc), ShortRevision(local.Revision)))
                : L("AuthorReference.NotInstalled");
        }

        private void SetBusy(bool busy)
        {
            _check.Enabled = !busy && !_service.IsDownloading;
            _download.Enabled = !busy && !_service.IsDownloading;
            _pause.Enabled = _service.IsDownloading;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            string[] units = { "B", "KB", "MB", "GB" }; double value = bytes; int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.00") + " " + units[unit];
        }
        private static string FormatDate(DateTime utc) { return utc <= DateTime.MinValue ? "—" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); }
        private static string ShortRevision(string value) { return String.IsNullOrWhiteSpace(value) ? "—" : value.Substring(0, Math.Min(8, value.Length)); }
        private string L(string key) { return _language.Get(key); }
        private string LF(string key, params object[] args) { return _language.Format(key, args); }
    }
}
