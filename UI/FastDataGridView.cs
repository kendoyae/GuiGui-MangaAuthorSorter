using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class FastDataGridView : DataGridView
    {
        public FastDataGridView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            UpdateStyles();
        }
    }

    internal sealed class GridOverlayTextBox : TextBox
    {
        protected override bool ProcessCmdKey(
            ref Message msg,
            Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) ||
                keyData == (Keys.Control | Keys.Insert))
            {
                if (SelectionLength > 0)
                    Copy();

                // 即使没有选中文本，也要吞掉命令，
                // 避免父级 DataGridView 把整行复制到剪贴板。
                return true;
            }

            if (keyData == (Keys.Control | Keys.A))
            {
                SelectAll();
                return true;
            }

            if (!ReadOnly &&
                keyData == (Keys.Control | Keys.X))
            {
                if (SelectionLength > 0)
                    Cut();

                return true;
            }

            if (!ReadOnly &&
                (keyData == (Keys.Control | Keys.V) ||
                 keyData == (Keys.Shift | Keys.Insert)))
            {
                Paste();
                return true;
            }

            return base.ProcessCmdKey(
                ref msg,
                keyData);
        }
    }

}
