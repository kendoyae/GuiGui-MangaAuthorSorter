using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class FastDataGridView : DataGridView
    {
        private DataGridViewColumn _liveResizeColumn;
        private int _liveResizeStartX;
        private int _liveResizeStartWidth;

        public FastDataGridView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            UpdateStyles();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1)
            {
                DataGridViewColumn divider = FindResizableDivider(e.Location);
                if (divider != null)
                {
                    _liveResizeColumn = divider;
                    _liveResizeStartX = e.X;
                    _liveResizeStartWidth = divider.Width;
                    Capture = true;
                    Cursor = Cursors.VSplit;
                    return;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_liveResizeColumn != null &&
                (e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                int width = _liveResizeStartWidth + e.X - _liveResizeStartX;
                width = System.Math.Max(_liveResizeColumn.MinimumWidth, width);
                if (_liveResizeColumn.Width != width)
                {
                    _liveResizeColumn.Width = width;
                    Invalidate();
                    Update();
                }
                return;
            }

            Cursor = FindResizableDivider(e.Location) != null
                ? Cursors.VSplit
                : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_liveResizeColumn != null)
            {
                _liveResizeColumn = null;
                Capture = false;
                Cursor = Cursors.Default;
                return;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(System.EventArgs e)
        {
            if (!Capture)
                _liveResizeColumn = null;
            base.OnMouseCaptureChanged(e);
        }

        private DataGridViewColumn FindResizableDivider(System.Drawing.Point location)
        {
            if (location.Y < 0 || location.Y > ColumnHeadersHeight)
                return null;

            foreach (DataGridViewColumn column in Columns)
            {
                if (!column.Visible || column.Resizable == DataGridViewTriState.False)
                    continue;
                System.Drawing.Rectangle bounds = GetColumnDisplayRectangle(column.Index, true);
                if (bounds.Width > 0 && System.Math.Abs(location.X - bounds.Right) <= 4)
                    return column;
            }
            return null;
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
