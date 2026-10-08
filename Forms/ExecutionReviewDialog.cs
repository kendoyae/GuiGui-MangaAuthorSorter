using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal enum ExecutionReviewChoice
    {
        Cancel = 0,
        OrganizeAndSkip = 1,
        HandleReview = 2
    }

    internal sealed class ExecutionReviewDialog : Form
    {
        private readonly LanguageManager _language;

        public ExecutionReviewChoice Choice { get; private set; }

        public ExecutionReviewDialog(
            LanguageManager language,
            Font appFont,
            int movableCount,
            int reviewCount,
            int skippedCount,
            string batchSize,
            string targetFreeSize)
        {
            _language = language;
            Choice = ExecutionReviewChoice.Cancel;

            Text = LF("Dialog.ExecuteWithReview.Title", reviewCount);
            ClientSize = new Size(590, 268);
            UiStyle.ApplyDialog(this, appFont);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(22, 20, 22, 18);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            Controls.Add(root);

            Label title = new Label();
            title.Text = LF("Dialog.ExecuteWithReview.Title", reviewCount);
            title.Font = new Font(appFont.FontFamily, 11F, FontStyle.Bold);
            title.AutoSize = true;
            title.Anchor = AnchorStyles.Left;
            root.Controls.Add(title, 0, 0);

            Label message = new Label();
            message.Text = LF(
                "Dialog.ExecuteWithReview.MessageDetailed",
                movableCount,
                reviewCount,
                skippedCount,
                batchSize,
                targetFreeSize);
            message.ForeColor = UiStyle.Muted;
            message.Dock = DockStyle.Fill;
            message.TextAlign = ContentAlignment.TopLeft;
            message.Padding = new Padding(0, 8, 0, 0);
            root.Controls.Add(message, 0, 1);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.ColumnCount = 4;
            buttons.RowCount = 1;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            buttons.Margin = new Padding(0);
            buttons.Padding = new Padding(0, 6, 0, 6);
            root.Controls.Add(buttons, 0, 2);

            Button cancel = NewAutoWidthButton(L("Common.Cancel"), 88, false);
            cancel.Anchor = AnchorStyles.None;
            cancel.Margin = new Padding(8, 0, 0, 0);
            cancel.Click += delegate
            {
                Choice = ExecutionReviewChoice.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Button handleReview = NewAutoWidthButton(
                L("Dialog.ExecuteWithReview.HandleReview"),
                132,
                false);
            handleReview.Anchor = AnchorStyles.None;
            handleReview.Margin = new Padding(8, 0, 0, 0);
            handleReview.Click += delegate
            {
                Choice = ExecutionReviewChoice.HandleReview;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button organize = NewAutoWidthButton(
                LF(
                    "Dialog.ExecuteWithReview.OrganizeAndSkip",
                    movableCount,
                    reviewCount),
                188,
                true);
            organize.Anchor = AnchorStyles.None;
            organize.Margin = new Padding(8, 0, 0, 0);
            organize.Click += delegate
            {
                Choice = ExecutionReviewChoice.OrganizeAndSkip;
                DialogResult = DialogResult.OK;
                Close();
            };

            buttons.Controls.Add(organize, 1, 0);
            buttons.Controls.Add(handleReview, 2, 0);
            buttons.Controls.Add(cancel, 3, 0);

            int requiredWidth =
                root.Padding.Left + root.Padding.Right +
                organize.Width + handleReview.Width + cancel.Width +
                organize.Margin.Horizontal + handleReview.Margin.Horizontal + cancel.Margin.Horizontal +
                24;
            if (ClientSize.Width < requiredWidth)
                ClientSize = new Size(requiredWidth, ClientSize.Height);

            AcceptButton = organize;
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
