using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class ExecutionConfirmDialog : Form
    {
        private readonly LanguageManager _language;

        public bool Confirmed { get; private set; }

        public ExecutionConfirmDialog(
            LanguageManager language,
            Font appFont,
            int movableCount,
            int skippedCount,
            string batchSize,
            string targetFreeSize)
        {
            _language = language;
            Confirmed = false;

            Text = L("Status.ExecuteConfirmTitle");
            ClientSize = new Size(600, 286);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = UiStyle.Background;
            root.Padding = new Padding(22, 20, 22, 18);
            root.Margin = new Padding(0);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            Controls.Add(root);

            Label title = new Label();
            title.Text = L("Status.ExecuteConfirmTitle");
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.ForeColor = UiStyle.Text;
            title.AutoSize = true;
            title.Anchor = AnchorStyles.Left;
            title.Margin = new Padding(0);
            root.Controls.Add(title, 0, 0);

            Label message = new Label();
            message.Text = LF(
                "Status.ExecuteConfirmBodyDetailed",
                movableCount,
                skippedCount,
                batchSize,
                targetFreeSize);
            message.ForeColor = UiStyle.Muted;
            message.Dock = DockStyle.Fill;
            message.TextAlign = ContentAlignment.TopLeft;
            message.Padding = new Padding(0, 9, 0, 8);
            message.Margin = new Padding(0);
            root.Controls.Add(message, 0, 1);

            Panel divider = new Panel();
            divider.Dock = DockStyle.Fill;
            divider.BackColor = UiStyle.Border;
            divider.Margin = new Padding(0);
            root.Controls.Add(divider, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = UiStyle.Background;
            footer.Margin = new Padding(0);
            footer.Padding = new Padding(0, 8, 0, 4);
            footer.ColumnCount = 3;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(footer, 0, 3);

            Button execute = NewAutoWidthButton(L("Main.Execute"), 104, true);
            execute.Anchor = AnchorStyles.None;
            execute.Margin = new Padding(8, 0, 0, 0);
            execute.Click += delegate
            {
                Confirmed = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancel = NewAutoWidthButton(L("Common.Cancel"), 88, false);
            cancel.Anchor = AnchorStyles.None;
            cancel.Margin = new Padding(8, 0, 0, 0);
            cancel.Click += delegate
            {
                Confirmed = false;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            footer.Controls.Add(execute, 1, 0);
            footer.Controls.Add(cancel, 2, 0);

            int requiredWidth =
                root.Padding.Left + root.Padding.Right +
                execute.Width + cancel.Width +
                execute.Margin.Horizontal + cancel.Margin.Horizontal +
                280;
            if (ClientSize.Width < requiredWidth)
                ClientSize = new Size(requiredWidth, ClientSize.Height);

            AcceptButton = execute;
            CancelButton = cancel;
        }

        private Button NewAutoWidthButton(string text, int minimumWidth, bool primary)
        {
            Button button = UiStyle.NewButton(text, minimumWidth, primary);
            Size preferred = TextRenderer.MeasureText(
                text ?? "",
                button.Font,
                new Size(Int32.MaxValue, button.Height),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            button.Width = Math.Max(minimumWidth, preferred.Width + 34);
            button.Height = 32;
            return button;
        }

        private string L(string key)
        {
            return _language != null ? _language.Get(key) : key;
        }

        private string LF(string key, params object[] args)
        {
            return _language != null
                ? _language.Format(key, args)
                : String.Format(key, args);
        }
    }
}
