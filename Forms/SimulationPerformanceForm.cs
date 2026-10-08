using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class SimulationPerformanceForm : Form
    {
        private readonly LanguageManager _language;
        private readonly SimulationBenchmarkService _service;
        private readonly int _maxAuthors;
        private readonly string _groupTemplate;
        private readonly List<string> _recognizedGroupTemplates;
        private readonly string _authorFolderTemplate;
        private readonly List<string> _recognizedAuthorFolderTemplates;
        private readonly string _databasePath;

        private readonly NumericUpDown _sourceCount;
        private readonly NumericUpDown _uniqueAuthorCount;
        private readonly NumericUpDown _existingAuthorPercent;
        private readonly NumericUpDown _targetFileCount;
        private readonly NumericUpDown _incrementalPercent;
        private readonly NumericUpDown _seed;
        private readonly CheckBox _sameName;
        private readonly ComboBox _nameMode;
        private readonly ComboBox _runMode;
        private readonly ComboBox _authorIndexCacheMode;
        private readonly Label _databaseStatus;
        private readonly DataGridView _grid;
        private readonly TextBox _details;
        private readonly Button _runButton;
        private readonly Button _copyButton;
        private readonly Label _progressStage;
        private readonly Label _progressDetail;
        private readonly Label _progressElapsed;
        private readonly ProgressBar _progressBar;
        private readonly System.Windows.Forms.Timer _progressTimer;
        private readonly Stopwatch _runWatch = new Stopwatch();
        private string _lastProgressLogKey = "";
        private bool _running;
        private volatile bool _cancelRequested;
        private SimulationBenchmarkReport _lastReport;

        public SimulationPerformanceForm(
            LanguageManager language,
            string databasePath,
            AuthorEntityStore entityStore,
            TagCleaningRuleStore tagCleaningStore,
            AuthorRecognitionMode recognitionMode,
            int maxAuthors,
            string groupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string authorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates)
        {
            _language = language;
            _databasePath = databasePath ?? "";
            _service = new SimulationBenchmarkService(
                _databasePath, entityStore, tagCleaningStore, recognitionMode);
            _maxAuthors = Math.Max(1, maxAuthors);
            _groupTemplate = groupTemplate ?? GroupNaming.DefaultTemplate;
            _recognizedGroupTemplates = recognizedGroupTemplates != null
                ? new List<string>(recognizedGroupTemplates)
                : new List<string> { GroupNaming.DefaultTemplate };
            _authorFolderTemplate = authorFolderTemplate ?? AuthorFolderNaming.DefaultTemplate;
            _recognizedAuthorFolderTemplates = recognizedAuthorFolderTemplates != null
                ? new List<string>(recognizedAuthorFolderTemplates)
                : new List<string> { AuthorFolderNaming.DefaultTemplate };

            Text = L("Simulation.Title");
            Size = new Size(1120, 800);
            MinimumSize = new Size(940, 680);
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            UiStyle.ApplyDialog(this, SystemFonts.MessageBoxFont);

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                ColumnCount = 1,
                RowCount = 6
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label title = new Label
            {
                AutoSize = true,
                Text = L("Simulation.Title"),
                Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 6)
            };
            Label description = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(990, 0),
                Text = L("Simulation.Description"),
                ForeColor = UiStyle.Muted,
                Margin = new Padding(0, 0, 0, 14)
            };

            TableLayoutPanel parameters = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = UiStyle.Soft,
                Padding = new Padding(14),
                ColumnCount = 4,
                RowCount = 6,
                Margin = new Padding(0, 0, 0, 14)
            };
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _sourceCount = Number(1, 100000, 1500, 0);
            _uniqueAuthorCount = Number(1, 1500, 300, 0);
            _existingAuthorPercent = Number(0, 100, 80, 0);
            _targetFileCount = Number(0, 100000, 150, 0);
            _incrementalPercent = Number(0.1M, 100M, 1M, 1);
            _seed = Number(0, Int32.MaxValue, 20261008, 0);
            _sameName = new CheckBox { AutoSize = true, Text = L("Simulation.SameName"), Checked = true, Anchor = AnchorStyles.Left };

            _nameMode = Combo();
            _nameMode.Items.AddRange(new object[]
            {
                L("Simulation.NameMode.Canonical"),
                L("Simulation.NameMode.Roman"),
                L("Simulation.NameMode.Alias"),
                L("Simulation.NameMode.Mixed")
            });
            _nameMode.SelectedIndex = 3;

            _runMode = Combo();
            _runMode.Items.AddRange(new object[]
            {
                L("Simulation.RunMode.First"),
                L("Simulation.RunMode.Cached"),
                L("Simulation.RunMode.Incremental"),
                L("Simulation.RunMode.Complete")
            });
            _runMode.SelectedIndex = 3;

            _authorIndexCacheMode = Combo();
            _authorIndexCacheMode.Items.AddRange(new object[]
            {
                L("Simulation.AuthorIndexCache.Cold"),
                L("Simulation.AuthorIndexCache.Warm")
            });
            _authorIndexCacheMode.SelectedIndex = 1;

            _sourceCount.ValueChanged += delegate
            {
                decimal max = Math.Max(1, _sourceCount.Value);
                _uniqueAuthorCount.Maximum = max;
                if (_uniqueAuthorCount.Value > max) _uniqueAuthorCount.Value = max;
            };

            AddParameter(parameters, 0, 0, L("Simulation.SourceCount"), _sourceCount);
            AddParameter(parameters, 2, 0, L("Simulation.ExistingAuthorPercent"), _existingAuthorPercent);
            AddParameter(parameters, 0, 1, L("Simulation.UniqueAuthorCount"), _uniqueAuthorCount);
            AddParameter(parameters, 2, 1, L("Simulation.NameMode"), _nameMode);
            AddParameter(parameters, 0, 2, L("Simulation.TargetFileCount"), _targetFileCount);
            AddParameter(parameters, 2, 2, L("Simulation.RunMode"), _runMode);
            AddParameter(parameters, 0, 3, L("Simulation.AuthorIndexCacheMode"), _authorIndexCacheMode);
            AddParameter(parameters, 2, 3, L("Simulation.IncrementalPercent"), _incrementalPercent);
            AddParameter(parameters, 0, 4, L("Simulation.RandomSeed"), _seed);
            parameters.Controls.Add(_sameName, 3, 4);

            parameters.Controls.Add(LabelFor(L("Simulation.Database")), 0, 5);
            _databaseStatus = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = UiStyle.Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(3, 6, 3, 3)
            };
            parameters.Controls.Add(_databaseStatus, 1, 5);
            parameters.SetColumnSpan(_databaseStatus, 3);

            Panel progressCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 86,
                BackColor = UiStyle.Soft,
                Padding = new Padding(14, 10, 14, 10),
                Margin = new Padding(0, 0, 0, 14)
            };
            TableLayoutPanel progressLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = Padding.Empty
            };
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _progressStage = new Label
            {
                AutoSize = true,
                Text = L("Simulation.Progress.Idle"),
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 8, 4)
            };
            _progressElapsed = new Label
            {
                AutoSize = true,
                Text = String.Format(L("Simulation.Progress.Elapsed"), "0.0 s"),
                ForeColor = UiStyle.Muted,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(8, 0, 0, 4)
            };
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 1000,
                Value = 0,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 2, 0, 3)
            };
            _progressDetail = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 22,
                AutoEllipsis = true,
                Text = L("Simulation.Progress.ReadyDetail"),
                ForeColor = UiStyle.Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            progressLayout.Controls.Add(_progressStage, 0, 0);
            progressLayout.Controls.Add(_progressElapsed, 1, 0);
            progressLayout.Controls.Add(_progressBar, 0, 1);
            progressLayout.SetColumnSpan(_progressBar, 2);
            progressLayout.Controls.Add(_progressDetail, 0, 2);
            progressLayout.SetColumnSpan(_progressDetail, 2);
            progressCard.Controls.Add(progressLayout);

            _progressTimer = new System.Windows.Forms.Timer();
            _progressTimer.Interval = 200;
            _progressTimer.Tick += delegate
            {
                if (_running && _runWatch.IsRunning)
                    _progressElapsed.Text = String.Format(L("Simulation.Progress.Elapsed"), FormatElapsed(_runWatch.Elapsed));
            };

            SplitContainer content = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };
            _grid = BuildGrid();
            _details = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9F),
                BackColor = SystemColors.Window
            };
            content.Panel1.Controls.Add(_grid);
            content.Panel2.Controls.Add(_details);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 12, 0, 0)
            };
            Button close = UiStyle.NewButton(L("Common.Close"), 110, false);
            close.Click += delegate { if (!_running) Close(); else RequestCancel(); };
            _copyButton = UiStyle.NewButton(L("Simulation.CopyReport"), 140, false);
            _copyButton.Enabled = false;
            _copyButton.Click += delegate { CopyReport(); };
            _runButton = UiStyle.NewButton(L("Simulation.Start"), 150, true);
            _runButton.Click += RunClicked;
            actions.Controls.Add(close);
            actions.Controls.Add(_copyButton);
            actions.Controls.Add(_runButton);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(description, 0, 1);
            root.Controls.Add(parameters, 0, 2);
            root.Controls.Add(progressCard, 0, 3);
            root.Controls.Add(content, 0, 4);
            root.Controls.Add(actions, 0, 5);
            Controls.Add(root);

            Shown += delegate
            {
                RefreshDatabaseStatus();
                BeginInvoke((MethodInvoker)delegate { ApplyContentSplitterLayout(content); });
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (_running)
                {
                    RequestCancel();
                    e.Cancel = true;
                }
            };
            FormClosed += delegate
            {
                _progressTimer.Stop();
                _progressTimer.Dispose();
            };
        }


        private static void ApplyContentSplitterLayout(SplitContainer content)
        {
            if (content == null || content.IsDisposed) return;

            int span = content.Orientation == Orientation.Horizontal
                ? content.ClientSize.Height
                : content.ClientSize.Width;
            int usable = span - content.SplitterWidth;
            if (usable <= 0) return;

            const int desiredDistance = 250;
            const int desiredPanel1Min = 160;
            const int desiredPanel2Min = 160;

            // SplitContainer validates SplitterDistance against the current client size.
            // During the form constructor the control has not been laid out yet, so setting
            // a fixed distance/minimum there can throw on normal DPI/layout configurations.
            int panel1Min = Math.Min(desiredPanel1Min, Math.Max(0, usable / 2));
            int panel2Min = Math.Min(desiredPanel2Min, Math.Max(0, usable - panel1Min));
            if (panel1Min + panel2Min > usable)
                panel2Min = Math.Max(0, usable - panel1Min);

            int maxDistance = Math.Max(panel1Min, usable - panel2Min);
            int distance = Math.Max(panel1Min, Math.Min(desiredDistance, maxDistance));

            try
            {
                content.Panel1MinSize = 0;
                content.Panel2MinSize = 0;
                content.SplitterDistance = distance;
                content.Panel1MinSize = panel1Min;
                content.Panel2MinSize = panel2Min;
            }
            catch (ArgumentOutOfRangeException)
            {
                // A late DPI/layout pass may resize the control between the measurements
                // above and the property assignments. Keep the form usable instead of
                // surfacing a WinForms SplitContainer initialization exception.
                content.Panel1MinSize = 0;
                content.Panel2MinSize = 0;
            }
        }

        private string L(string key) { return _language.Get(key); }

        private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                DecimalPlaces = decimals,
                Increment = decimals > 0 ? 0.1M : 1M,
                ThousandsSeparator = decimals == 0,
                Margin = new Padding(3, 3, 12, 3)
            };
        }

        private static ComboBox Combo()
        {
            return new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(3, 3, 12, 3)
            };
        }

        private static Label LabelFor(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = text,
                ForeColor = UiStyle.Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 6, 3, 3)
            };
        }

        private static void AddParameter(TableLayoutPanel panel, int column, int row, string label, Control control)
        {
            panel.Controls.Add(LabelFor(label), column, row);
            panel.Controls.Add(control, column + 1, row);
        }

        private DataGridView BuildGrid()
        {
            DataGridView grid = new FastDataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            grid.Columns.Add("Mode", L("Simulation.Column.Mode"));
            grid.Columns.Add("Files", L("Simulation.Column.Files"));
            grid.Columns.Add("Cache", L("Simulation.Column.CacheHits"));
            grid.Columns.Add("Recalc", L("Simulation.Column.Recalculated"));
            grid.Columns.Add("Conflict", L("Simulation.Column.Conflicts"));
            grid.Columns.Add("Move", L("Simulation.Column.Moves"));
            grid.Columns.Add("Recognition", L("Simulation.Column.MatchMs"));
            grid.Columns.Add("Total", L("Simulation.Column.TotalMs"));
            UiStyle.StyleGrid(grid);
            UiStyle.ConfigureFillColumn(grid.Columns[0], 120, 160);
            for (int i = 1; i < grid.Columns.Count; i++) UiStyle.ConfigureShortColumn(grid.Columns[i], 90);
            return grid;
        }

        private void RefreshDatabaseStatus()
        {
            try
            {
                PublicAuthorIndexInfo info = new GuiGuiAuthorIndexDatabase(_databasePath).GetInfo();
                if (!info.Exists)
                {
                    _databaseStatus.Text = L("Simulation.DatabaseMissing") + "  " + _databasePath;
                    _databaseStatus.ForeColor = Color.Firebrick;
                    _runButton.Enabled = false;
                    return;
                }
                if (!String.IsNullOrWhiteSpace(info.Error))
                {
                    _databaseStatus.Text = info.Error;
                    _databaseStatus.ForeColor = Color.Firebrick;
                    _runButton.Enabled = false;
                    return;
                }
                _databaseStatus.Text = String.Format(L("Simulation.DatabaseReady"), info.Artists.ToString("N0"));
                _databaseStatus.ForeColor = UiStyle.Muted;
                _runButton.Enabled = true;
            }
            catch (Exception ex)
            {
                _databaseStatus.Text = ex.Message;
                _databaseStatus.ForeColor = Color.Firebrick;
                _runButton.Enabled = false;
            }
        }

        private async void RunClicked(object sender, EventArgs e)
        {
            if (_running)
            {
                RequestCancel();
                return;
            }

            SimulationBenchmarkOptions options = ReadOptions();
            SetRunning(true);
            _details.Clear();
            AppendProgressLog(L("Simulation.Running"));
            _grid.Rows.Clear();
            _lastReport = null;
            _copyButton.Enabled = false;

            try
            {
                SimulationBenchmarkReport report = await Task.Run(delegate
                {
                    return _service.Run(
                        options,
                        _maxAuthors,
                        _groupTemplate,
                        _recognizedGroupTemplates,
                        _authorFolderTemplate,
                        _recognizedAuthorFolderTemplates,
                        delegate { return _cancelRequested; },
                        delegate(SimulationProgressInfo info) { UpdateProgress(info); });
                });
                _lastReport = report;
                ShowReport(report);
                _copyButton.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                UpdateProgress(new SimulationProgressInfo { Stage = SimulationProgressStage.Canceled, Current = 0, Total = 0, Indeterminate = false });
                AppendProgressLog(L("Simulation.Canceled"));
            }
            catch (Exception ex)
            {
                UpdateProgress(new SimulationProgressInfo { Stage = SimulationProgressStage.Failed, CurrentItem = ex.Message, Indeterminate = false });
                AppendProgressLog(ex.ToString());
                UiMessageBox.Show(this, ex.Message, L("Simulation.Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetRunning(false);
            }
        }

        private SimulationBenchmarkOptions ReadOptions()
        {
            SimulationBenchmarkOptions options = new SimulationBenchmarkOptions();
            options.SourceFileCount = Decimal.ToInt32(_sourceCount.Value);
            options.UniqueAuthorCount = Decimal.ToInt32(_uniqueAuthorCount.Value);
            options.ExistingAuthorPercent = Decimal.ToInt32(_existingAuthorPercent.Value);
            options.ExistingTargetFileCount = Decimal.ToInt32(_targetFileCount.Value);
            options.UseSameNameTargetFiles = _sameName.Checked;
            options.IncrementalChangePercent = _incrementalPercent.Value;
            options.Seed = Decimal.ToInt32(_seed.Value);
            options.NameMode = (SimulationNameMode)Math.Max(0, _nameMode.SelectedIndex);
            options.RunMode = (SimulationRunMode)Math.Max(0, _runMode.SelectedIndex);
            options.AuthorIndexCacheMode = (SimulationAuthorIndexCacheMode)Math.Max(0, _authorIndexCacheMode.SelectedIndex);
            return options;
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _cancelRequested = false;
            if (running)
            {
                _runWatch.Restart();
                _progressTimer.Start();
                _lastProgressLogKey = "";
            }
            else
            {
                _runWatch.Stop();
                _progressTimer.Stop();
                _progressElapsed.Text = String.Format(L("Simulation.Progress.Elapsed"), FormatElapsed(_runWatch.Elapsed));
            }
            _sourceCount.Enabled = !running;
            _uniqueAuthorCount.Enabled = !running;
            _existingAuthorPercent.Enabled = !running;
            _targetFileCount.Enabled = !running;
            _incrementalPercent.Enabled = !running;
            _seed.Enabled = !running;
            _sameName.Enabled = !running;
            _nameMode.Enabled = !running;
            _runMode.Enabled = !running;
            _authorIndexCacheMode.Enabled = !running;
            _runButton.Enabled = true;
            _runButton.Text = running ? L("Simulation.Cancel") : L("Simulation.Start");
        }

        private void RequestCancel()
        {
            if (!_running) return;
            _cancelRequested = true;
            _runButton.Enabled = false;
            _runButton.Text = L("Simulation.Canceling");
            UpdateProgress(new SimulationProgressInfo
            {
                Stage = SimulationProgressStage.Canceling,
                Current = 0,
                Total = 0,
                Indeterminate = true
            });
        }

        private void UpdateProgress(SimulationProgressInfo info)
        {
            if (info == null || IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<SimulationProgressInfo>(UpdateProgress), info); }
                catch { }
                return;
            }

            string stage = ProgressStageText(info.Stage);
            string round = LocalRoundName(info.RoundName);
            if (!String.IsNullOrWhiteSpace(round)) stage = round + " · " + stage;
            _progressStage.Text = stage;

            List<string> detailParts = new List<string>();
            if (info.Total > 0)
                detailParts.Add(String.Format(L("Simulation.Progress.Count"), info.Current.ToString("N0"), info.Total.ToString("N0")));
            if (!String.IsNullOrWhiteSpace(info.CurrentItem))
            {
                string item = info.CurrentItem;
                try
                {
                    string fileName = Path.GetFileName(item);
                    if (!String.IsNullOrWhiteSpace(fileName)) item = fileName;
                }
                catch { }
                detailParts.Add(String.Format(L("Simulation.Progress.CurrentItem"), item));
            }
            _progressDetail.Text = detailParts.Count > 0
                ? String.Join("    ", detailParts.ToArray())
                : L("Simulation.Progress.Working");

            bool terminal = info.Stage == SimulationProgressStage.Completed ||
                info.Stage == SimulationProgressStage.Canceled ||
                info.Stage == SimulationProgressStage.Failed;
            if (terminal)
            {
                if (_progressBar.Style != ProgressBarStyle.Continuous)
                    _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = info.Stage == SimulationProgressStage.Completed ? 1000 : 0;
            }
            else if (info.Indeterminate || info.Total <= 0)
            {
                if (_progressBar.Style != ProgressBarStyle.Marquee)
                    _progressBar.Style = ProgressBarStyle.Marquee;
            }
            else
            {
                if (_progressBar.Style != ProgressBarStyle.Continuous)
                    _progressBar.Style = ProgressBarStyle.Continuous;
                int value = info.Total <= 0 ? 0 : (int)Math.Max(0, Math.Min(1000, (long)info.Current * 1000L / Math.Max(1, info.Total)));
                _progressBar.Value = value;
            }

            string logKey = info.Stage.ToString() + "|" + (info.RoundName ?? "");
            if (!String.Equals(logKey, _lastProgressLogKey, StringComparison.Ordinal))
            {
                _lastProgressLogKey = logKey;
                AppendProgressLog(stage);
            }
        }

        private string ProgressStageText(SimulationProgressStage stage)
        {
            switch (stage)
            {
                case SimulationProgressStage.Initializing: return L("Simulation.Stage.Initializing");
                case SimulationProgressStage.CheckingDatabase: return L("Simulation.Stage.CheckingDatabase");
                case SimulationProgressStage.LoadingAuthors: return L("Simulation.Stage.LoadingAuthors");
                case SimulationProgressStage.PreparingAuthorIndex: return L("Simulation.Stage.PreparingAuthorIndex");
                case SimulationProgressStage.GeneratingSources: return L("Simulation.Stage.GeneratingSources");
                case SimulationProgressStage.BuildingTargetEnvironment: return L("Simulation.Stage.BuildingTargetEnvironment");
                case SimulationProgressStage.PreparingBaseline: return L("Simulation.Stage.PreparingBaseline");
                case SimulationProgressStage.RecognizingAndPlanning: return L("Simulation.Stage.RecognizingAndPlanning");
                case SimulationProgressStage.CacheLookup: return L("Simulation.Stage.CacheLookup");
                case SimulationProgressStage.IncrementalChanges: return L("Simulation.Stage.IncrementalChanges");
                case SimulationProgressStage.ConflictAnalysis: return L("Simulation.Stage.ConflictAnalysis");
                case SimulationProgressStage.SimulatedOrganize: return L("Simulation.Stage.SimulatedOrganize");
                case SimulationProgressStage.Finalizing: return L("Simulation.Stage.Finalizing");
                case SimulationProgressStage.Completed: return L("Simulation.Stage.Completed");
                case SimulationProgressStage.Canceling: return L("Simulation.Stage.Canceling");
                case SimulationProgressStage.Canceled: return L("Simulation.Stage.Canceled");
                case SimulationProgressStage.Failed: return L("Simulation.Stage.Failed");
                default: return L("Simulation.Progress.Working");
            }
        }

        private void AppendProgressLog(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return;
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine;
            _details.AppendText(line);
            _details.SelectionStart = _details.TextLength;
            _details.ScrollToCaret();
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes >= 1.0)
                return ((int)elapsed.TotalMinutes).ToString() + ":" + elapsed.Seconds.ToString("D2") + "." + (elapsed.Milliseconds / 100).ToString();
            return elapsed.TotalSeconds.ToString("0.0") + " s";
        }

        private void ShowReport(SimulationBenchmarkReport report)
        {
            _grid.Rows.Clear();
            foreach (SimulationRoundResult round in report.Rounds)
            {
                _grid.Rows.Add(
                    LocalRoundName(round.Name),
                    round.SourceFiles.ToString("N0"),
                    round.CacheHits.ToString("N0"),
                    round.RecalculatedFiles.ToString("N0"),
                    (round.TargetConflicts + round.BatchConflicts).ToString("N0"),
                    round.SimulatedMoves.ToString("N0"),
                    round.RecognitionPlanMs + " ms",
                    round.TotalMs + " ms");
            }
            _details.Text = FormatReport(report);
        }

        private string LocalRoundName(string name)
        {
            if (name == "首次扫描") return L("Simulation.RunMode.First");
            if (name == "缓存扫描") return L("Simulation.RunMode.Cached");
            if (name == "增量扫描") return L("Simulation.RunMode.Incremental");
            if (name == "基线准备") return L("Simulation.Round.Baseline");
            return name ?? "";
        }

        private string AuthorIndexCacheModeText(SimulationAuthorIndexCacheMode mode)
        {
            return mode == SimulationAuthorIndexCacheMode.Cold
                ? L("Simulation.AuthorIndexCache.Cold")
                : L("Simulation.AuthorIndexCache.Warm");
        }

        private string FormatReport(SimulationBenchmarkReport report)
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine(L("Simulation.ReportTitle"));
            s.AppendLine(L("Simulation.Report.Safe") + ": " + L("Simulation.Report.SafeValue"));
            s.AppendLine(L("Simulation.Database") + ": " + report.DatabasePath);
            s.AppendLine(L("Simulation.Report.AvailableArtists") + ": " + report.AvailableArtists.ToString("N0"));
            s.AppendLine(L("Simulation.Report.Generated") + ": " + report.GeneratedFiles.ToString("N0"));
            s.AppendLine(L("Simulation.Report.UniqueAuthors") + ": " + report.UniqueAuthors.ToString("N0"));
            s.AppendLine(L("Simulation.Report.AverageFilesPerAuthor") + ": " + report.AverageFilesPerAuthor.ToString("0.00"));
            s.AppendLine(L("Simulation.Report.ExistingAuthors") + ": " + report.ExistingAuthorFolders.ToString("N0"));
            s.AppendLine(L("Simulation.Report.ExistingFiles") + ": " + report.ExistingTargetFiles.ToString("N0"));
            s.AppendLine(L("Simulation.RandomSeed") + ": " + report.Seed.ToString());
            s.AppendLine(L("Simulation.AuthorIndexCacheMode") + ": " + AuthorIndexCacheModeText(report.AuthorIndexCacheMode));
            s.AppendLine(L("Simulation.Report.AuthorIndexBuild") + ": " + report.AuthorIndexBuildMs + " ms");
            s.AppendLine(L("Simulation.Report.AuthorIndexStats") + ": " + String.Format(L("Simulation.Report.AuthorIndexStatsValue"),
                report.AuthorIndexArtists.ToString("N0"), report.AuthorIndexLookupKeys.ToString("N0"),
                report.AuthorIndexIdentityNames.ToString("N0"), report.AuthorIndexRelations.ToString("N0")));
            s.AppendLine(L("Simulation.Report.Preparation") + ": " + report.DataPreparationMs + " ms");
            foreach (SimulationRoundResult round in report.Rounds)
            {
                s.AppendLine();
                s.AppendLine("[" + LocalRoundName(round.Name) + "]");
                s.AppendLine(L("Simulation.Column.Files") + ": " + round.SourceFiles.ToString("N0"));
                s.AppendLine(L("Simulation.Column.CacheHits") + ": " + round.CacheHits.ToString("N0"));
                s.AppendLine(L("Simulation.Column.Recalculated") + ": " + round.RecalculatedFiles.ToString("N0"));
                s.AppendLine(L("Simulation.Report.IndexChanged") + ": " + round.IndexChangedFiles.ToString("N0"));
                s.AppendLine(L("Simulation.Report.Matched") + ": " + round.Matched.ToString("N0"));
                s.AppendLine(L("Simulation.Report.NewAuthors") + ": " + round.NewAuthors.ToString("N0"));
                s.AppendLine(L("Simulation.Report.Ambiguous") + ": " + round.Ambiguous.ToString("N0"));
                s.AppendLine(L("Simulation.Report.Unrecognized") + ": " + round.Unrecognized.ToString("N0"));
                s.AppendLine(L("Simulation.Report.TargetConflicts") + ": " + round.TargetConflicts.ToString("N0"));
                s.AppendLine(L("Simulation.Report.BatchConflicts") + ": " + round.BatchConflicts.ToString("N0"));
                s.AppendLine(L("Simulation.Report.Moves") + ": " + round.SimulatedMoves.ToString("N0"));
                s.AppendLine(L("Simulation.Report.Skips") + ": " + round.SimulatedSkips.ToString("N0"));
                s.AppendLine(L("Simulation.Column.MatchMs") + ": " + round.RecognitionPlanMs + " ms");
                s.AppendLine(L("Simulation.Report.OrganizeMs") + ": " + round.OrganizeMs + " ms");
                s.AppendLine(L("Simulation.Column.TotalMs") + ": " + round.TotalMs + " ms");
            }
            return s.ToString().TrimEnd();
        }

        private void CopyReport()
        {
            if (_lastReport == null) return;
            try { Clipboard.SetText(FormatReport(_lastReport)); }
            catch { }
        }
    }
}
