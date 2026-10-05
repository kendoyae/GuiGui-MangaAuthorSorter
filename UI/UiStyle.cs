using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal static class UiStyle
    {
        public static readonly Color Background = Color.White;
        public static readonly Color Text = Color.FromArgb(35, 37, 40);
        public static readonly Color Muted = Color.FromArgb(92, 95, 100);
        public static readonly Color Border = Color.FromArgb(224, 226, 230);
        public static readonly Color Soft = Color.FromArgb(247, 248, 250);
        public static readonly Color Selection = Color.FromArgb(232, 240, 250);
        public static readonly Color Accent = Color.FromArgb(0, 120, 215);
        public static readonly Color Danger = Color.FromArgb(190, 45, 45);

        private sealed class FlatMenuRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
            {
                // WinForms paints this unused gutter grey even when menu items
                // have no images. Match it to the menu surface instead.
                using (SolidBrush brush = new SolidBrush(Background))
                    e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        private static readonly ToolStripRenderer MenuRenderer = new FlatMenuRenderer();


        private sealed class ButtonVisualState
        {
            public bool Primary;
            public bool Danger;
            public bool Hooked;
        }

        private static readonly ConditionalWeakTable<Button, ButtonVisualState> ButtonStates =
            new ConditionalWeakTable<Button, ButtonVisualState>();

        public static void ApplyButtonStateStyle(Button button, bool primary, bool danger)
        {
            if (button == null) return;

            ButtonVisualState state = ButtonStates.GetValue(button, delegate(Button b) { return new ButtonVisualState(); });
            state.Primary = primary;
            state.Danger = danger;

            if (!state.Hooked)
            {
                state.Hooked = true;
                button.EnabledChanged += delegate
                {
                    ButtonVisualState current;
                    if (ButtonStates.TryGetValue(button, out current))
                        ApplyButtonVisual(button, current);
                };
            }

            ApplyButtonVisual(button, state);
        }

        private static void ApplyButtonVisual(Button button, ButtonVisualState state)
        {
            if (button == null || state == null) return;

            if (!button.Enabled)
            {
                button.BackColor = Soft;
                button.FlatAppearance.BorderColor = Border;
                button.ForeColor = Color.FromArgb(155, 158, 163);
                button.Cursor = Cursors.Default;
                return;
            }

            button.Cursor = Cursors.Hand;
            if (state.Primary)
            {
                button.BackColor = Color.FromArgb(245, 249, 253);
                button.FlatAppearance.BorderColor = Accent;
                button.ForeColor = Text;
            }
            else if (state.Danger)
            {
                button.BackColor = Background;
                button.FlatAppearance.BorderColor = Color.FromArgb(224, 180, 180);
                button.ForeColor = Danger;
            }
            else
            {
                button.BackColor = Background;
                button.FlatAppearance.BorderColor = Color.FromArgb(204, 207, 212);
                button.ForeColor = Text;
            }
        }

        public static void ApplyDialog(Form form, Font font)
        {
            if (form == null) return;
            ApplyAppIcon(form);
            // Keep every secondary window on the same DPI scaling model as the main window.
            // This prevents footer/buttons from being clipped when Windows display scaling is above 100%.
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Font = font;
            form.BackColor = Background;
            form.ForeColor = Text;
            form.StartPosition = FormStartPosition.CenterParent;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.SizeGripStyle = SizeGripStyle.Hide;
            // Respect a dialog's own MinimumSize/MaximumSize. Older versions
            // cleared them here, which defeated localization-safe minimum layouts.
            form.ShowInTaskbar = false;
            form.Load += delegate
            {
                RelayoutLocalizedTree(form);
            };
        }

        public static void ApplyAppIcon(Form form)
        {
            if (form == null) return;
            try
            {
                form.Icon = LoadAppIcon(32);
            }
            catch
            {
                // Keep the system default if the executable icon is unavailable.
            }
        }

        public static Icon LoadAppIcon(int size)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MangaAuthorSorter.AppIcon.ico");
            if (stream == null)
                throw new InvalidOperationException("Embedded application icon was not found.");

            using (stream)
            using (Icon icon = new Icon(stream, size, size))
                return (Icon)icon.Clone();
        }

        public static Image LoadAboutIconImage()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MangaAuthorSorter.AppIcon64.png");
            if (stream == null)
                throw new InvalidOperationException("Embedded About icon image was not found.");

            using (stream)
            using (Image image = Image.FromStream(stream))
                return new Bitmap(image);
        }

        public static Button StyleButton(Button button, bool primary, bool danger)
        {
            if (button == null) return null;
            button.Height = 32;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.UseVisualStyleBackColor = false;
            if (button.Padding == Padding.Empty)
                button.Padding = new Padding(10, 0, 10, 0);
            ApplyButtonStateStyle(button, primary, danger);
            return button;
        }

        public static int MeasureButtonWidth(string text, Font font, int minimumWidth)
        {
            int measured = TextRenderer.MeasureText(
                text ?? "",
                font ?? SystemFonts.MessageBoxFont,
                new Size(Int32.MaxValue, Int32.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            return Math.Max(Math.Max(0, minimumWidth), measured + 34);
        }

        public static void FitButtonToText(Button button, int minimumWidth)
        {
            if (button == null) return;
            int min = Math.Max(minimumWidth, button.MinimumSize.Width);
            button.MinimumSize = new Size(min, 32);
            button.AutoSize = false;
            button.AutoEllipsis = false;
            button.Width = MeasureButtonWidth(button.Text, button.Font, min);
            button.Height = 32;
        }

        public static Button NewButton(string text, int minimumWidth, bool primary)
        {
            Button button = new Button();
            button.Text = text ?? "";
            StyleButton(button, primary, false);
            FitButtonToText(button, minimumWidth);
            return button;
        }

        public static void StyleMenu(MenuStrip menu)
        {
            if (menu == null) return;
            menu.Renderer = MenuRenderer;
            menu.BackColor = Background;
            foreach (ToolStripItem item in menu.Items)
                StyleMenuItem(item as ToolStripMenuItem);
        }

        public static void StyleMenu(ContextMenuStrip menu)
        {
            if (menu == null) return;
            menu.Renderer = MenuRenderer;
            menu.BackColor = Background;
            foreach (ToolStripItem item in menu.Items)
            {
                item.BackColor = Background;
                StyleMenuItem(item as ToolStripMenuItem);
            }
        }

        private static void StyleMenuItem(ToolStripMenuItem item)
        {
            if (item == null) return;
            item.BackColor = Background;
            item.DropDown.BackColor = Background;
            item.DropDown.Renderer = MenuRenderer;
            foreach (ToolStripItem child in item.DropDownItems)
            {
                child.BackColor = Background;
                StyleMenuItem(child as ToolStripMenuItem);
            }
        }

        public static void FitComboBoxToItems(ComboBox combo, int minimumWidth, int maximumWidth)
        {
            if (combo == null) return;
            int preferred = Math.Max(0, minimumWidth);
            foreach (object item in combo.Items)
            {
                string text = item != null ? item.ToString() : "";
                int width = TextRenderer.MeasureText(
                    text ?? "", combo.Font,
                    new Size(Int32.MaxValue, Int32.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 38;
                if (width > preferred) preferred = width;
            }
            if (maximumWidth > 0) preferred = Math.Min(preferred, maximumWidth);
            combo.Width = preferred;
            combo.DropDownWidth = Math.Max(combo.Width, preferred);
        }


        internal sealed class GridAutoSizeSnapshot
        {
            internal readonly Dictionary<DataGridViewColumn, DataGridViewAutoSizeColumnMode> Modes =
                new Dictionary<DataGridViewColumn, DataGridViewAutoSizeColumnMode>();
        }

        public static GridAutoSizeSnapshot SuspendGridAutoSize(DataGridView grid)
        {
            GridAutoSizeSnapshot snapshot = new GridAutoSizeSnapshot();
            if (grid == null || grid.IsDisposed) return snapshot;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                DataGridViewAutoSizeColumnMode mode = column.AutoSizeMode;
                snapshot.Modes[column] = mode;
                if (mode == DataGridViewAutoSizeColumnMode.AllCells ||
                    mode == DataGridViewAutoSizeColumnMode.DisplayedCells ||
                    mode == DataGridViewAutoSizeColumnMode.ColumnHeader ||
                    mode == DataGridViewAutoSizeColumnMode.Fill)
                {
                    // Keep the current pixel width while rows are inserted.
                    // Restoring the responsive mode after the batch causes only
                    // one final measurement/layout pass instead of one per row.
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                }
            }

            return snapshot;
        }

        public static void RestoreGridAutoSize(
            DataGridView grid,
            GridAutoSizeSnapshot snapshot)
        {
            if (grid == null || grid.IsDisposed || snapshot == null) return;

            // Restore measured short columns first while Fill columns are still
            // frozen, then restore Fill so the remaining width is distributed once.
            foreach (KeyValuePair<DataGridViewColumn, DataGridViewAutoSizeColumnMode> pair in snapshot.Modes)
            {
                if (pair.Key == null) continue;
                DataGridViewAutoSizeColumnMode mode = pair.Value;
                if (mode != DataGridViewAutoSizeColumnMode.Fill)
                    pair.Key.AutoSizeMode = mode;
            }
            foreach (KeyValuePair<DataGridViewColumn, DataGridViewAutoSizeColumnMode> pair in snapshot.Modes)
            {
                if (pair.Value == DataGridViewAutoSizeColumnMode.Fill)
                    pair.Key.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }

            grid.PerformLayout();
        }

        public static void ConfigureShortColumn(DataGridViewColumn column, int minimumWidth)
        {
            if (column == null) return;
            column.MinimumWidth = Math.Max(48, minimumWidth);
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
        }

        public static void ConfigureFillColumn(DataGridViewColumn column, float fillWeight, int minimumWidth)
        {
            if (column == null) return;
            column.MinimumWidth = Math.Max(60, minimumWidth);
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.FillWeight = Math.Max(1F, fillWeight);
            column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
        }

        public static void RelayoutLocalizedTree(Control root)
        {
            if (root == null) return;
            foreach (Control child in root.Controls)
            {
                Button button = child as Button;
                if (button != null && !String.IsNullOrEmpty(button.Text))
                    FitButtonToText(button, button.MinimumSize.Width);

                DataGridView grid = child as DataGridView;
                if (grid != null)
                    GridInteraction.RefreshLocalizedLayout(grid);

                RelayoutLocalizedTree(child);
            }
            root.PerformLayout();
        }

        public static void StyleGrid(DataGridView grid)
        {
            if (grid == null) return;
            grid.BackgroundColor = Background;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Border;
            grid.RowHeadersVisible = false;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Soft;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Soft;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(grid.Font, FontStyle.Regular);
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersHeight = 30;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.DefaultCellStyle.BackColor = Background;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = Selection;
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.DefaultCellStyle.Padding = new Padding(3, 0, 3, 0);
            grid.RowTemplate.Height = 27;
            // Keep both horizontal and vertical rules visible. Without vertical
            // rules, adjacent text columns visually merge and their draggable
            // boundaries are difficult to discover.
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        }

        public static Panel CreateSection(string title, string description)
        {
            Panel panel = new Panel();
            panel.BackColor = Background;
            panel.Padding = new Padding(0);

            Label titleLabel = new Label();
            titleLabel.Text = title ?? "";
            titleLabel.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5F, FontStyle.Bold);
            titleLabel.ForeColor = Text;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(0, 0);
            panel.Controls.Add(titleLabel);

            if (!String.IsNullOrWhiteSpace(description))
            {
                Label descriptionLabel = new Label();
                descriptionLabel.Text = description;
                descriptionLabel.ForeColor = Muted;
                descriptionLabel.AutoSize = false;
                descriptionLabel.Location = new Point(0, 26);
                descriptionLabel.Size = new Size(600, 38);
                descriptionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                panel.Controls.Add(descriptionLabel);
            }

            return panel;
        }

        public static Label NewCaption(string text)
        {
            Label label = new Label();
            label.Text = text ?? "";
            label.ForeColor = Muted;
            label.AutoSize = true;
            return label;
        }

        public static Panel NewDivider()
        {
            Panel p = new Panel();
            p.Height = 1;
            p.BackColor = Border;
            p.Dock = DockStyle.Top;
            return p;
        }
    }
}
