using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class UpdateDialog : Form
    {
        public UpdateDialog(LanguageManager language, Font appFont, UpdateCheckResult result)
        {
            UiStyle.ApplyDialog(this, appFont);
            Text = language.Get("Update.AvailableTitle");
            ClientSize = new Size(620, 470);
            MinimumSize = new Size(540, 400);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(22);
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            Controls.Add(root);

            Label title = new Label();
            title.Text = String.IsNullOrWhiteSpace(result.ReleaseName) ? language.Get("Update.AvailableTitle") : result.ReleaseName;
            title.Font = new Font(appFont.FontFamily, 15F, FontStyle.Bold);
            title.AutoSize = true;
            title.Margin = new Padding(0, 0, 0, 14);
            root.Controls.Add(title);

            Label versions = new Label();
            versions.Text = String.Format(language.Get("Update.VersionSummary"), AppVersion.Display, "V" + result.LatestVersion);
            versions.AutoSize = true;
            versions.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(versions);

            Label published = new Label();
            published.Text = language.Get("Update.PublishedAt") + " " + (result.PublishedAt.HasValue ? result.PublishedAt.Value.ToString("yyyy-MM-dd HH:mm") : "—");
            published.ForeColor = UiStyle.Muted;
            published.AutoSize = true;
            published.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(published);

            TextBox notes = new TextBox();
            notes.Dock = DockStyle.Fill;
            notes.Multiline = true;
            notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.Text = String.IsNullOrWhiteSpace(result.ReleaseNotes) ? language.Get("Update.NoNotes") : result.ReleaseNotes;
            root.Controls.Add(notes);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0);
            buttons.Padding = new Padding(0, 12, 0, 0);
            buttons.ColumnCount = 3;
            buttons.RowCount = 1;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button later = UiStyle.NewButton(language.Get("Update.Later"), 100, false);
            later.DialogResult = DialogResult.Cancel;
            later.Margin = new Padding(0);
            Button download = UiStyle.NewButton(language.Get("Update.Download"), 130, true);
            download.Margin = new Padding(0, 0, 8, 0);
            download.Click += delegate
            {
                string url = String.IsNullOrWhiteSpace(result.ReleaseUrl) ? UpdateService.ReleasesUrl : result.ReleaseUrl;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { UiMessageBox.Show(this, language.Get("Update.OpenFailed"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            buttons.Controls.Add(download, 1, 0);
            buttons.Controls.Add(later, 2, 0);
            root.Controls.Add(buttons);
            AcceptButton = download;
            CancelButton = later;
        }
    }
}
