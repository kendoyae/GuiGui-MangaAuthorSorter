using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class DeveloperPanelForm : Form
    {
        public DeveloperPanelForm(
            LanguageManager language,
            Font appFont,
            Action<IWin32Window> openOnlineAuthorSettings)
        {
            UiStyle.ApplyDialog(this, appFont);
            Text = language.Get("Developer.Title");
            ClientSize = new Size(620, 360);
            MinimumSize = new Size(580, 330);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(24, 20, 24, 14);
            root.Margin = new Padding(0);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            Controls.Add(root);

            TableLayoutPanel header = new TableLayoutPanel();
            header.AutoSize = true;
            header.Dock = DockStyle.Top;
            header.ColumnCount = 1;
            header.RowCount = 2;
            header.Margin = new Padding(0);

            Label title = new Label();
            title.Text = language.Get("Developer.Title");
            title.Font = new Font(appFont.FontFamily, 16F, FontStyle.Bold);
            title.ForeColor = UiStyle.Text;
            title.AutoSize = true;
            title.Margin = new Padding(0, 0, 0, 8);
            header.Controls.Add(title, 0, 0);

            Label description = new Label();
            description.Text = language.Get("Developer.Description");
            description.ForeColor = UiStyle.Muted;
            description.AutoSize = true;
            description.MaximumSize = new Size(550, 0);
            description.Margin = new Padding(0);
            header.Controls.Add(description, 0, 1);
            root.Controls.Add(header, 0, 0);
            root.Controls.Add(UiStyle.NewDivider(), 0, 2);

            TableLayoutPanel feature = new TableLayoutPanel();
            feature.Dock = DockStyle.Top;
            feature.AutoSize = true;
            feature.Margin = new Padding(0, 18, 0, 0);
            feature.ColumnCount = 2;
            feature.RowCount = 2;
            feature.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            feature.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label featureTitle = new Label();
            featureTitle.Text = language.Get("Developer.OnlineAuthor");
            featureTitle.Font = new Font(appFont, FontStyle.Bold);
            featureTitle.AutoSize = true;
            featureTitle.Margin = new Padding(0, 3, 12, 6);
            feature.Controls.Add(featureTitle, 0, 0);

            Label featureDescription = new Label();
            featureDescription.Text = language.Get("Developer.OnlineAuthorDescription");
            featureDescription.ForeColor = UiStyle.Muted;
            featureDescription.AutoSize = true;
            featureDescription.MaximumSize = new Size(400, 0);
            featureDescription.Margin = new Padding(0, 0, 12, 0);
            feature.Controls.Add(featureDescription, 0, 1);

            Button open = UiStyle.NewButton(language.Get("Developer.OpenSettings"), 130, false);
            open.Anchor = AnchorStyles.Right;
            open.Margin = new Padding(12, 0, 0, 0);
            open.Click += delegate
            {
                if (openOnlineAuthorSettings != null)
                    openOnlineAuthorSettings(this);
            };
            feature.Controls.Add(open, 1, 0);
            feature.SetRowSpan(open, 2);
            root.Controls.Add(feature, 0, 3);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.ColumnCount = 1;
            Button close = UiStyle.NewButton(language.Get("Common.Close"), 88, false);
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(0, 10, 0, 0);
            close.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(close, 0, 0);
            root.Controls.Add(UiStyle.NewDivider(), 0, 4);
            root.Controls.Add(footer, 0, 5);

            CancelButton = close;
        }
    }
}
