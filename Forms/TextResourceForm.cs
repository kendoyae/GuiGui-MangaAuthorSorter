using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class TextResourceForm : Form
    {
        public TextResourceForm(LanguageManager language, Font appFont, string title, string content)
        {
            UiStyle.ApplyDialog(this, appFont);
            Text = title ?? "";
            ClientSize = new Size(780, 600);
            MinimumSize = new Size(620, 460);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18, 16, 18, 12);
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            Controls.Add(root);

            RichTextBox body = new RichTextBox();
            body.Dock = DockStyle.Fill;
            body.ReadOnly = true;
            body.BorderStyle = BorderStyle.FixedSingle;
            body.BackColor = UiStyle.Background;
            body.ForeColor = UiStyle.Text;
            body.DetectUrls = true;
            body.WordWrap = true;
            body.Text = content ?? "";
            root.Controls.Add(body, 0, 0);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Padding = new Padding(0, 12, 0, 0);
            Button close = UiStyle.NewButton(language.Get("Common.Close"), 92, false);
            close.DialogResult = DialogResult.Cancel;
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);
            root.Controls.Add(footer, 0, 1);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
