using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    /// <summary>
    /// 统一 DataGridView 的多语言列宽交互：
    /// - 保留页面声明的 AllCells / Fill / None 策略；
    /// - 短枚举列可按当前语言自动宽度，长文本列使用 Fill；
    /// - 表头与普通单元格默认禁止换行；
    /// - 空间不足时优先压缩 Fill 列并允许横向滚动；
    /// - 支持表头右键“自动调整列宽 / 恢复默认列宽 / 显示隐藏列”；
    /// - 双击列分隔线自动适配当前列；
    /// - 记住用户列宽和列显示状态。
    /// </summary>
    internal static class GridInteraction
    {
        private const int MinimumColumnWidth = 60;
        private const int MaximumAutoWidth = 520;

        private sealed class PersistedColumnState
        {
            public int Width;
            public bool Visible;
        }

        private sealed class GridController
        {
            public DataGridView Grid;
            public string Key;
            public LanguageManager Language;
            public bool AllowVisibility;
            public bool Loading;
            public readonly Dictionary<string, int> DefaultWidths =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, bool> DefaultVisibility =
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            public ContextMenuStrip HeaderMenu;
            public Timer SaveTimer;
            public Timer UnlockTimer;
            public bool WidthsUnlocked;
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, PersistedColumnState> Saved =
            new Dictionary<string, PersistedColumnState>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<DataGridView, GridController> Controllers =
            new Dictionary<DataGridView, GridController>();
        private static bool _loaded;

        private static string LayoutPath
        {
            get
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    AppFiles.GridLayouts);
            }
        }

        public static void Apply(
            DataGridView grid,
            string key,
            LanguageManager language,
            bool allowColumnVisibility)
        {
            if (grid == null || String.IsNullOrWhiteSpace(key))
                return;

            RemoveController(grid);

            GridController controller = new GridController();
            controller.Grid = grid;
            controller.Key = key.Trim();
            controller.Language = language;
            controller.AllowVisibility = allowColumnVisibility;
            controller.Loading = true;

            grid.AllowUserToResizeColumns = true;
            grid.ScrollBars = ScrollBars.Both;
            grid.ShowCellToolTips = true;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.NotSet)
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Resizable = DataGridViewTriState.True;
                column.MinimumWidth = Math.Max(MinimumColumnWidth, column.MinimumWidth);
                column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
                column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;

                controller.DefaultWidths[column.Name] =
                    Math.Max(column.MinimumWidth, column.Width);
                controller.DefaultVisibility[column.Name] = column.Visible;
            }

            LoadSavedStates();
            RestoreSavedState(controller);

            controller.HeaderMenu = new ContextMenuStrip();
            controller.HeaderMenu.ShowImageMargin = false;

            controller.SaveTimer = new Timer();
            controller.SaveTimer.Interval = 300;
            controller.SaveTimer.Tick += delegate
            {
                controller.SaveTimer.Stop();
                SaveGridState(controller);
            };

            // AllCells/DisplayedCells calculate a useful initial width but WinForms
            // locks those columns against mouse resizing. After initial population,
            // freeze the measured pixel widths to None so every list column can be
            // dragged and persisted. Fill columns already support mouse resizing.
            controller.UnlockTimer = new Timer();
            controller.UnlockTimer.Interval = 50;
            controller.UnlockTimer.Tick += delegate
            {
                controller.UnlockTimer.Stop();
                FreezeAutoSizedColumns(controller);
            };
            grid.RowsAdded += delegate { ScheduleWidthUnlock(controller); };
            grid.HandleCreated += delegate { ScheduleWidthUnlock(controller); };

            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right)
                    return;

                BuildHeaderMenu(controller);
                controller.HeaderMenu.Show(Cursor.Position);
            };

            grid.ColumnDividerDoubleClick += delegate(object sender, DataGridViewColumnDividerDoubleClickEventArgs e)
            {
                AutoSizeColumn(controller, e.ColumnIndex);
                e.Handled = true;
            };

            grid.ColumnWidthChanged += delegate(object sender, DataGridViewColumnEventArgs e)
            {
                if (!controller.Loading)
                    ScheduleSave(controller);
            };

            grid.ColumnStateChanged += delegate(object sender, DataGridViewColumnStateChangedEventArgs e)
            {
                if (!controller.Loading &&
                    (e.StateChanged & DataGridViewElementStates.Visible) != 0)
                {
                    ScheduleSave(controller);
                }
            };

            grid.Disposed += delegate
            {
                try
                {
                    if (controller.SaveTimer != null)
                        controller.SaveTimer.Stop();
                    if (controller.UnlockTimer != null)
                        controller.UnlockTimer.Stop();
                    SaveGridState(controller);
                }
                catch { }
                RemoveController(grid);
            };

            controller.Loading = false;
            Controllers[grid] = controller;
            ScheduleWidthUnlock(controller);
        }

        private static void ScheduleWidthUnlock(GridController controller)
        {
            if (controller == null || controller.WidthsUnlocked ||
                controller.UnlockTimer == null)
            {
                return;
            }

            controller.UnlockTimer.Stop();
            controller.UnlockTimer.Start();
        }

        private static void FreezeAutoSizedColumns(GridController controller)
        {
            if (controller == null || controller.Grid == null ||
                controller.Grid.IsDisposed || controller.WidthsUnlocked)
            {
                return;
            }

            controller.Loading = true;
            try
            {
                foreach (DataGridViewColumn column in controller.Grid.Columns)
                {
                    DataGridViewAutoSizeColumnMode mode = column.AutoSizeMode;
                    if (mode == DataGridViewAutoSizeColumnMode.AllCells ||
                        mode == DataGridViewAutoSizeColumnMode.DisplayedCells ||
                        mode == DataGridViewAutoSizeColumnMode.ColumnHeader)
                    {
                        int width = Math.Max(column.MinimumWidth, column.Width);
                        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                        column.Width = width;
                    }

                    column.Resizable = DataGridViewTriState.True;
                }

                controller.WidthsUnlocked = true;
            }
            finally
            {
                controller.Loading = false;
            }
        }

        private static void BuildHeaderMenu(GridController controller)
        {
            ContextMenuStrip menu = controller.HeaderMenu;
            menu.Items.Clear();

            ToolStripMenuItem autoFit = new ToolStripMenuItem(
                L(controller, "GridMenu.AutoFit", "自动调整列宽"));
            autoFit.Click += delegate { AutoSizeAll(controller); };
            menu.Items.Add(autoFit);

            ToolStripMenuItem reset = new ToolStripMenuItem(
                L(controller, "GridMenu.ResetWidths", "恢复默认列宽"));
            reset.Click += delegate { ResetWidths(controller); };
            menu.Items.Add(reset);

            if (controller.AllowVisibility)
            {
                menu.Items.Add(new ToolStripSeparator());

                ToolStripMenuItem visibility = new ToolStripMenuItem(
                    L(controller, "GridMenu.Columns", "显示/隐藏列"));

                foreach (DataGridViewColumn column in controller.Grid.Columns)
                {
                    DataGridViewColumn captured = column;
                    ToolStripMenuItem item = new ToolStripMenuItem(column.HeaderText);
                    item.Checked = column.Visible;
                    item.CheckOnClick = false;
                    item.Click += delegate
                    {
                        ToggleColumn(controller, captured);
                    };
                    visibility.DropDownItems.Add(item);
                }

                menu.Items.Add(visibility);
            }

            UiStyle.StyleMenu(menu);
        }

        private static string L(
            GridController controller,
            string key,
            string fallback)
        {
            if (controller.Language == null)
                return fallback;

            string value = controller.Language.Get(key);
            if (String.IsNullOrWhiteSpace(value) ||
                String.Equals(value, key, StringComparison.Ordinal))
            {
                return fallback;
            }

            return value;
        }

        private static void ToggleColumn(
            GridController controller,
            DataGridViewColumn column)
        {
            if (column == null)
                return;

            if (column.Visible)
            {
                int visibleCount = 0;
                foreach (DataGridViewColumn current in controller.Grid.Columns)
                {
                    if (current.Visible)
                        visibleCount++;
                }

                // 至少保留一列，避免表格被用户隐藏成完全空白。
                if (visibleCount <= 1)
                    return;

                if (controller.Grid.CurrentCell != null &&
                    controller.Grid.CurrentCell.OwningColumn == column)
                {
                    foreach (DataGridViewColumn candidate in controller.Grid.Columns)
                    {
                        if (candidate != column && candidate.Visible)
                        {
                            int rowIndex = controller.Grid.CurrentCell.RowIndex;
                            if (rowIndex >= 0 && rowIndex < controller.Grid.Rows.Count)
                                controller.Grid.CurrentCell = controller.Grid.Rows[rowIndex].Cells[candidate.Index];
                            break;
                        }
                    }
                }

                column.Visible = false;
            }
            else
            {
                column.Visible = true;
            }

            SaveGridState(controller);
        }

        private static void ResetWidths(GridController controller)
        {
            controller.Loading = true;
            try
            {
                foreach (DataGridViewColumn column in controller.Grid.Columns)
                {
                    int width;
                    if (controller.DefaultWidths.TryGetValue(column.Name, out width) &&
                        column.AutoSizeMode == DataGridViewAutoSizeColumnMode.None)
                    {
                        column.Width = Math.Max(column.MinimumWidth, width);
                    }
                }
            }
            finally
            {
                controller.Loading = false;
            }

            SaveGridState(controller);
        }

        private static void AutoSizeAll(GridController controller)
        {
            controller.Loading = true;
            try
            {
                foreach (DataGridViewColumn column in controller.Grid.Columns)
                {
                    if (!column.Visible)
                        continue;

                    AutoSizeColumnCore(controller.Grid, column);
                }
            }
            finally
            {
                controller.Loading = false;
            }

            SaveGridState(controller);
        }

        private static void AutoSizeColumn(
            GridController controller,
            int columnIndex)
        {
            if (columnIndex < 0 ||
                columnIndex >= controller.Grid.Columns.Count)
            {
                return;
            }

            DataGridViewColumn column =
                controller.Grid.Columns[columnIndex];

            if (!column.Visible)
                return;

            controller.Loading = true;
            try
            {
                AutoSizeColumnCore(controller.Grid, column);
            }
            finally
            {
                controller.Loading = false;
            }

            SaveGridState(controller);
        }

        private static void AutoSizeColumnCore(
            DataGridView grid,
            DataGridViewColumn column)
        {
            if (column.AutoSizeMode != DataGridViewAutoSizeColumnMode.None &&
                column.AutoSizeMode != DataGridViewAutoSizeColumnMode.NotSet)
            {
                return;
            }

            int preferred = Math.Max(MinimumColumnWidth, column.MinimumWidth);

            try
            {
                preferred = column.GetPreferredWidth(
                    DataGridViewAutoSizeColumnMode.DisplayedCells,
                    true);
            }
            catch
            {
                preferred = column.Width;
            }

            int headerWidth = TextRenderer.MeasureText(
                column.HeaderText ?? "",
                grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font).Width + 28;

            if (preferred < headerWidth)
                preferred = headerWidth;

            if (preferred < MinimumColumnWidth)
                preferred = MinimumColumnWidth;
            if (preferred > MaximumAutoWidth)
                preferred = MaximumAutoWidth;

            column.Width = preferred;
        }

        private static void RestoreSavedState(GridController controller)
        {
            foreach (DataGridViewColumn column in controller.Grid.Columns)
            {
                PersistedColumnState state;
                if (!Saved.TryGetValue(
                        MakeStateKey(controller.Key, column.Name),
                        out state))
                {
                    continue;
                }

                if (state.Width >= column.MinimumWidth)
                {
                    // A remembered user width takes priority over automatic
                    // sizing, including columns that were originally Fill.
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.Width = state.Width;
                    column.Resizable = DataGridViewTriState.True;
                }

                if (controller.AllowVisibility)
                    column.Visible = state.Visible;
            }

            // 防止配置文件里把所有列都隐藏。
            bool anyVisible = false;
            foreach (DataGridViewColumn column in controller.Grid.Columns)
            {
                if (column.Visible)
                {
                    anyVisible = true;
                    break;
                }
            }

            if (!anyVisible && controller.Grid.Columns.Count > 0)
                controller.Grid.Columns[0].Visible = true;
        }

        private static void ScheduleSave(GridController controller)
        {
            if (controller == null || controller.SaveTimer == null)
                return;

            controller.SaveTimer.Stop();
            controller.SaveTimer.Start();
        }

        private static void SaveGridState(GridController controller)
        {
            if (controller == null || controller.Grid == null || controller.Grid.IsDisposed)
                return;

            lock (SyncRoot)
            {
                LoadSavedStates();

                foreach (DataGridViewColumn column in controller.Grid.Columns)
                {
                    PersistedColumnState state = new PersistedColumnState();
                    state.Width = Math.Max(MinimumColumnWidth, column.Width);
                    state.Visible = column.Visible;
                    Saved[MakeStateKey(controller.Key, column.Name)] = state;
                }

                SaveAllStates();
            }
        }

        private static void LoadSavedStates()
        {
            lock (SyncRoot)
            {
                if (_loaded)
                    return;

                _loaded = true;
                Saved.Clear();

                if (!File.Exists(LayoutPath))
                    return;

                try
                {
                    foreach (string raw in File.ReadAllLines(LayoutPath, Encoding.UTF8))
                    {
                        string line = (raw ?? "").Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                            continue;

                        string[] parts = line.Split('\t');
                        if (parts.Length != 4)
                            continue;

                        string gridKey = Decode(parts[0]);
                        string columnName = Decode(parts[1]);
                        int width;
                        if (gridKey.Length == 0 ||
                            columnName.Length == 0 ||
                            !Int32.TryParse(parts[2], out width))
                        {
                            continue;
                        }

                        bool visible = parts[3] != "0";
                        PersistedColumnState state = new PersistedColumnState();
                        state.Width = Math.Max(MinimumColumnWidth, width);
                        state.Visible = visible;
                        Saved[MakeStateKey(gridKey, columnName)] = state;
                    }
                }
                catch
                {
                    // 列宽配置损坏不能影响软件启动。
                    Saved.Clear();
                }
            }
        }

        private static void SaveAllStates()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("# MangaAuthorSorter - Grid Layouts");
                lines.Add("# grid-key<TAB>column<TAB>width<TAB>visible");

                List<string> keys = new List<string>(Saved.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string composite in keys)
                {
                    int separator = composite.IndexOf('\u001f');
                    if (separator <= 0)
                        continue;

                    string gridKey = composite.Substring(0, separator);
                    string columnName = composite.Substring(separator + 1);
                    PersistedColumnState state = Saved[composite];

                    lines.Add(
                        Encode(gridKey) + "\t" +
                        Encode(columnName) + "\t" +
                        state.Width.ToString() + "\t" +
                        (state.Visible ? "1" : "0"));
                }

                File.WriteAllLines(
                    LayoutPath,
                    lines.ToArray(),
                    new UTF8Encoding(true));
            }
            catch
            {
                // 便携目录不可写时，仅放弃记忆列宽，不影响主功能。
            }
        }

        private static string MakeStateKey(
            string gridKey,
            string columnName)
        {
            return (gridKey ?? "") + "\u001f" + (columnName ?? "");
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(
                    Convert.FromBase64String(value ?? ""));
            }
            catch
            {
                return "";
            }
        }

        public static bool SetBulkLayoutLoading(DataGridView grid, bool loading)
        {
            if (grid == null) return false;
            GridController controller;
            if (!Controllers.TryGetValue(grid, out controller) || controller == null)
                return false;

            bool previous = controller.Loading;
            controller.Loading = loading;
            return previous;
        }

        public static void RefreshLocalizedLayout(DataGridView grid)
        {
            if (grid == null || grid.IsDisposed) return;

            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
                column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
                if (column.AutoSizeMode == DataGridViewAutoSizeColumnMode.AllCells ||
                    column.AutoSizeMode == DataGridViewAutoSizeColumnMode.DisplayedCells ||
                    column.AutoSizeMode == DataGridViewAutoSizeColumnMode.ColumnHeader)
                {
                    try { grid.AutoResizeColumn(column.Index, column.AutoSizeMode); } catch { }
                }
            }
            grid.PerformLayout();
            grid.Invalidate();
        }

        private static void RemoveController(DataGridView grid)
        {
            if (grid == null)
                return;

            GridController controller;
            if (Controllers.TryGetValue(grid, out controller))
            {
                if (controller.HeaderMenu != null)
                {
                    try { controller.HeaderMenu.Dispose(); }
                    catch { }
                }
                if (controller.SaveTimer != null)
                {
                    try { controller.SaveTimer.Dispose(); }
                    catch { }
                }
                if (controller.UnlockTimer != null)
                {
                    try { controller.UnlockTimer.Dispose(); }
                    catch { }
                }
                Controllers.Remove(grid);
            }
        }
    }
}
