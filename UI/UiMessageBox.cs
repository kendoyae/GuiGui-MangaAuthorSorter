using System;
using System.Drawing;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Application-styled replacement for the native message box. Keeping the
    /// action row on the same surface removes the system grey footer that made
    /// confirmation prompts look unrelated to the rest of the application.
    /// </summary>
    internal static class UiMessageBox
    {
        private static string _ok = "OK";
        private static string _yes = "Yes";
        private static string _no = "No";

        public static void Configure(LanguageManager language)
        {
            if (language == null) return;
            _ok = language.Get("Common.OK");
            _yes = language.Get("Common.Yes");
            _no = language.Get("Common.No");
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(null, text, caption, buttons, icon);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (Form dialog = Build(owner, text, caption, buttons, icon))
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        }

        private static Form Build(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            Font font = owner is Control ? ((Control)owner).Font : SystemFonts.MessageBoxFont;
            Form dialog = new Form();
            UiStyle.ApplyDialog(dialog, font);
            dialog.Text = caption ?? "";
            dialog.AutoScaleMode = AutoScaleMode.Dpi;
            dialog.ClientSize = new Size(430, 170);
            dialog.MinimumSize = new Size(360, 170);
            dialog.MaximumSize = new Size(760, 520);
            dialog.AutoSize = true;
            dialog.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.BackColor = UiStyle.Background;
            root.Padding = new Padding(22, 20, 22, 16);
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, icon == MessageBoxIcon.None ? 0F : 48F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            dialog.Controls.Add(root);

            PictureBox picture = new PictureBox();
            picture.Size = new Size(32, 32);
            picture.Margin = new Padding(0, 2, 16, 0);
            picture.SizeMode = PictureBoxSizeMode.CenterImage;
            picture.Image = GetIcon(icon);
            picture.Visible = picture.Image != null;
            root.Controls.Add(picture, 0, 0);

            Label message = new Label();
            message.Text = text ?? "";
            message.ForeColor = UiStyle.Text;
            message.BackColor = UiStyle.Background;
            message.AutoSize = true;
            message.MaximumSize = new Size(620, 0);
            message.MinimumSize = new Size(280, 50);
            message.Margin = new Padding(0, 2, 0, 12);
            root.Controls.Add(message, 1, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.AutoSize = true;
            actions.BackColor = UiStyle.Background;
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.WrapContents = false;
            actions.Margin = new Padding(0, 10, 0, 0);
            root.SetColumnSpan(actions, 2);
            root.Controls.Add(actions, 0, 1);

            if (buttons == MessageBoxButtons.YesNo)
            {
                Button no = UiStyle.NewButton(_no, 86, false);
                no.DialogResult = DialogResult.No;
                Button yes = UiStyle.NewButton(_yes, 86, true);
                yes.DialogResult = DialogResult.Yes;
                actions.Controls.Add(no);
                actions.Controls.Add(yes);
                dialog.AcceptButton = yes;
                dialog.CancelButton = no;
            }
            else
            {
                Button ok = UiStyle.NewButton(_ok, 86, true);
                ok.DialogResult = DialogResult.OK;
                actions.Controls.Add(ok);
                dialog.AcceptButton = ok;
                dialog.CancelButton = ok;
            }

            return dialog;
        }

        private static Image GetIcon(MessageBoxIcon icon)
        {
            if (icon == MessageBoxIcon.Error) return SystemIcons.Error.ToBitmap();
            if (icon == MessageBoxIcon.Warning) return SystemIcons.Warning.ToBitmap();
            if (icon == MessageBoxIcon.Question) return SystemIcons.Question.ToBitmap();
            if (icon == MessageBoxIcon.Information) return SystemIcons.Information.ToBitmap();
            return null;
        }
    }
}
