using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MangaAuthorSorter
{
    internal sealed class MainForm : Form
    {
        private readonly string _appDir;
        private readonly AliasLibrary _aliasLibrary;
        private readonly BlockListStore _blockList;
        private readonly UserSettingsStore _settingsStore;
        private readonly LanguageManager _language;
        private readonly FileTypeProfileStore _fileTypeProfileStore;
        private readonly AuthorEntityStore _authorEntityStore;
        private readonly TagCleaningRuleStore _tagCleaningStore;
        private readonly ScanExclusionRuleStore _scanExclusionStore;
        private readonly ArchiveEngine _engine;
        private readonly EverythingService _everything;
        private readonly ScanWarmupService _scanWarmup;
        private readonly HistoryStore _historyStore;
        private readonly UpdateService _updateService = new UpdateService();
        private ToolStripMenuItem _checkUpdatesItem;
        private UpdateCheckResult _availableUpdate;
        private bool _updateCheckRunning;
        private List<PlanItem> _plan = new List<PlanItem>();
        private List<ScanExcludedItem> _lastScanExcludedItems = new List<ScanExcludedItem>();

        private TextBox _txtSource;
        private TextBox _txtRoot;
        private NumericUpDown _numMax;
        private NumericUpDown _numScanLimit;
        private CheckBox _chkRecursive;
        private ComboBox _cmbScanMode;
        private TextBox _txtGroupTemplate;
        private Label _lblGroupTemplatePreview;
        private Button _btnPreview;
        private Button _btnExecute;
        private Button _btnBrowseSource;
        private Button _btnBrowseTarget;
        private LinkLabel _lblSearch;
        private LinkLabel _lblStatus;
        private Label _lblSpaceStatus;
        private Panel _progressGroup;
        private ProgressBar _progressBar;
        private Label _lblProgressFile;
        private Label _lblProgressPath;
        private FastDataGridView _grid;
        private Font _gridStatusBoldFont;
        private Font _gridStatusLinkFont;
        private int _hoveredStatusRow = -1;
        private Font _filterRegularFont;
        private Font _filterBoldFont;
        private List<PlanItem> _allGridItems = new List<PlanItem>();
        private List<PlanItem> _visibleGridItems = new List<PlanItem>();
        private bool _applyingGridFilter;
        private ContextMenuStrip _gridContextMenu;
        private ToolStripMenuItem _openFileLocationMenuItem;
        private ToolStripMenuItem _copyFullPathMenuItem;
        private ToolStripMenuItem _authorActionMenuItem;
        private ToolStripMenuItem _manageExclusionMenuItem;
        private ToolStripSeparator _gridActionSeparator;
        private ToolStripMenuItem _blockMenuItem;
        private ToolStripMenuItem _menuPreviewItem;
        private ToolStripMenuItem _menuExecuteItem;
        private ToolStripMenuItem _menuOpenSourceItem;
        private ToolStripMenuItem _menuOpenTargetItem;
        private MenuStrip _topMenu;

        // V1.10.5 separates full-scan ambiguity identity from filtered-plan destination allocation.
        private Panel _rootPanel;
        private Panel _headerPanel;
        private Panel _filterBar;
        private SplitContainer _workspaceSplit;
        private Panel _progressPanel;
        private TextBox _txtListSearch;
        private Button _btnToggleDetails;
        private ToolTip _detailsToolTip;
        private ToolTip _scanCountToolTip;
        private const int DetailPanelWidth = 340;
        private readonly Dictionary<string, Button> _filterButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private string _viewFilter = "all";
        private bool _settingStatusReviewLink;
        private const int ExecuteButtonDefaultWidth = 100;
        private const int ReviewButtonWidth = 132;

        private Label _detailTitle;
        private Label _detailEmpty;
        private FlowLayoutPanel _detailContent;
        private TableLayoutPanel _detailActions;
        private Panel _detailFileBlock;
        private Panel _detailStatusBlock;
        private Panel _detailAuthorBlock;
        private Panel _detailMatchedBlock;
        private Panel _detailProcessBlock;
        private Panel _detailTargetBlock;
        private Panel _detailResultBlock;
        private Label _detailStatus;
        private Label _detailAuthor;
        private Label _detailMatched;
        private Label _detailFileName;
        private Label _detailProcess;
        private Label _detailTargetPath;
        private Label _detailResult;
        private Button _detailAssignButton;
        private Button _detailExcludeButton;

        private GridOverlayTextBox _overlayEditor;
        private PlanItem _editingItem;
        private bool _editingFileName;
        private bool _overlayClosing;
        private string _currentRoot = "";
        private string _currentSourceRoot = "";
        private int _currentMaxAuthors = 20;
        private ScanModeKind _currentScanMode =
            ScanModeKind.Global;

        private string _currentGroupTemplate =
            GroupNaming.DefaultTemplate;

        private List<string> _recognizedGroupTemplates =
            new List<string>
            {
                GroupNaming.DefaultTemplate
            };

        private string _authorFolderTemplateSetting =
            AuthorFolderNaming.DefaultTemplate;

        private string _currentAuthorFolderTemplate =
            AuthorFolderNaming.DefaultTemplate;

        private List<string> _recognizedAuthorFolderTemplates =
            new List<string>
            {
                AuthorFolderNaming.DefaultTemplate
            };

        private FileTypeProfileConfig _fileTypeConfig =
            new FileTypeProfileConfig();

        private List<string> _scanExtensions =
            FileTypeRules.GetCompressionDefaults();

        private bool _isExecuting;
        private bool _isScanning;
        private bool _executionSafetyAllowsMove;
        private bool _scanCancelRequested;
        private BackgroundWorker _scanWorker;
        private Stopwatch _scanResponseTimer;
        private System.Windows.Forms.Timer _warmupDebounceTimer;
        private ScanPerformanceForm _scanPerformanceForm;
        private decimal _safetyReserveGb = 5M;
        private bool _onlineAuthorLookupEnabled;
        private string _onlineAuthorProvider = "Danbooru";
        private bool _saveOnlineAuthorCache = true;
        private int _maxOnlineLookupsPerScan = 20;

        private sealed class ScanRequest
        {
            public string Source = "";
            public string Root = "";
            public int MaxAuthors;
            public int RequestedLimit;
            public bool Recursive;
            public ScanModeKind Mode;
            public string GroupTemplate = "";
            public List<string> RecognizedGroupTemplates = new List<string>();
            public string AuthorFolderTemplate = "";
            public List<string> RecognizedAuthorFolderTemplates = new List<string>();
            public List<string> Extensions = new List<string>();
            public bool OnlineLookupEnabled;
            public string OnlineProvider = "Danbooru";
            public bool SaveOnlineCache = true;
            public int MaxOnlineLookups = 20;
            public ScanWarmupRequest WarmupRequest;
        }

        private sealed class ScanRunResult
        {
            public SearchResult Search = new SearchResult();
            public List<PlanItem> Plan = new List<PlanItem>();
            public int ClassifiedCount;
            public int MatchedCount;
            public int CandidateCount;
            public bool EarlyStopped;
            public OnlineAuthorResolutionStats OnlineStats = new OnlineAuthorResolutionStats();
            public long ElapsedMilliseconds;
            public ScanWarmupMetrics WarmupMetrics = new ScanWarmupMetrics();
            public long CandidatePrepareMs;
            public long RecognitionPlanMs;
        }

        private sealed class MoveProgressInfo
        {
            public int Current;
            public int Total;
            public string FileName = "";
            public string SourcePath = "";
            public string TargetPath = "";
            public string Phase = "";
            public long ProcessedBytes;
            public long TotalBytes;
            public long TargetFreeBytes = -1;
        }

        private sealed class GridRenderMetrics
        {
            public long SortMs;
            public long RowBuildMs;
            public long AddRowsMs;
            public long LayoutMs;
            public long FinalizeMs;
            public long TotalMs;
        }

        private sealed class MoveRunResult
        {
            public int Success;
            public int Skipped;
            public int Failed;
            public int ProcessedCount;
            public long ProcessedBytes;
            public bool Aborted;
            public string AbortMessage = "";
        }

        private sealed class ExecutionSafetyCheck
        {
            public bool Passed;
            public bool TargetExists;
            public bool TargetWritable = true;
            public bool SpaceKnown;
            public long BatchBytes;
            public long RequiredBytes;
            public long FreeBytes;
            public long ReserveBytes;
            public int MissingSources;
            public int TargetConflicts;
            public bool PlanPathsChanged;
            public string Detail = "";
        }

        private sealed class GroupNumberingRuleItem
        {
            public string Id = "";
            public string Display = "";

            public override string ToString()
            {
                return Display;
            }
        }

        public MainForm()
        {
            _appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Load persisted language preference first. On the first run,
            // LanguageManager falls back to the Windows UI language.
            _settingsStore =
                new UserSettingsStore(
                    AppFiles.ResolveDataFile(
                        _appDir,
                        AppFiles.UserSettings,
                        "用户设置.ini"));

            UserSettingsData initialSettings =
                _settingsStore.Load();

            _language =
                new LanguageManager(
                    Path.Combine(
                        _appDir,
                        "Languages"));

            _language.Initialize(
                initialSettings.LanguageCode);

            // Data-file names stay language-neutral. Only the explanatory
            // comments written when a file is first created follow the
            // current application language. Existing files keep their header.
            _aliasLibrary =
                new AliasLibrary(
                    AppFiles.ResolveDataFile(
                        _appDir,
                        AppFiles.AuthorAliases,
                        "作者别名库.txt"),
                    _language.Get);

            _blockList =
                new BlockListStore(
                    AppFiles.ResolveDataFile(
                        _appDir,
                        AppFiles.ExclusionList,
                        "屏蔽列表.txt"),
                    _language.Get);

            _fileTypeProfileStore =
                new FileTypeProfileStore(
                    AppFiles.ResolveDataFile(
                        _appDir,
                        AppFiles.FileTypeProfiles,
                        "文件类型配置.json"));

            _authorEntityStore =
                new AuthorEntityStore(
                    AppFiles.ResolveDataFile(
                        _appDir,
                        AppFiles.AuthorEntities,
                        "作者实体库.json"));

            _tagCleaningStore =
                new TagCleaningRuleStore(
                    AppFiles.ResolveDataFile(
                        _appDir,
                        AppFiles.TagCleaningRules,
                        "标签清洗规则.json"));

            _scanExclusionStore =
                new ScanExclusionRuleStore(
                    AppFiles.ResolveDataFile(
                        _appDir,
                        AppFiles.ScanExclusionRules,
                        "扫描排除规则.json"));

            _historyStore =
                new HistoryStore(
                    Path.Combine(
                        _appDir,
                        AppFiles.History));

            _aliasLibrary.EnsureExists();
            _blockList.EnsureExists();
            _authorEntityStore.EnsureExists();
            _tagCleaningStore.EnsureExists();
            _scanExclusionStore.EnsureExists();

            _onlineAuthorLookupEnabled = initialSettings.OnlineAuthorLookupEnabled;
            _onlineAuthorProvider = initialSettings.OnlineAuthorProvider;
            _saveOnlineAuthorCache = initialSettings.SaveOnlineAuthorCache;
            _maxOnlineLookupsPerScan = initialSettings.MaxOnlineLookupsPerScan;

            _engine =
                new ArchiveEngine(
                    _aliasLibrary,
                    _authorEntityStore,
                    _tagCleaningStore);

            _everything =
                new EverythingService(
                    _appDir,
                    _scanExclusionStore);

            _scanWarmup = new ScanWarmupService(_everything, _engine);

            ScanPerformanceDiagnostics.Initialize(
                Path.Combine(_appDir, AppFiles.ScanPerformanceLog),
                initialSettings.PerformanceDiagnosticsEnabled,
                initialSettings.ScanWarmupEnabled,
                initialSettings.EverythingEnabled);

            InitializeUi();
            LoadPersistentPaths();
            InitializeScanWarmup();
            ScanPerformanceDiagnostics.SettingsChanged += HandlePerformanceSettingsChanged;
            UpdateInitialSearchLabel();
            RefreshExecutionSafetyUi();
            UpdateMainActionAvailability();
            UpdateWorkflowGuidanceStatus();

            FormClosed += delegate
            {
                if (_warmupDebounceTimer != null) _warmupDebounceTimer.Dispose();
                if (_scanCountToolTip != null) _scanCountToolTip.Dispose();
                _scanWarmup.Dispose();
                ScanPerformanceDiagnostics.SettingsChanged -= HandlePerformanceSettingsChanged;
                if (_gridStatusBoldFont != null)
                {
                    _gridStatusBoldFont.Dispose();
                    _gridStatusBoldFont = null;
                }
                if (_gridStatusLinkFont != null)
                {
                    _gridStatusLinkFont.Dispose();
                    _gridStatusLinkFont = null;
                }
                if (_filterRegularFont != null)
                {
                    _filterRegularFont.Dispose();
                    _filterRegularFont = null;
                }
                if (_filterBoldFont != null)
                {
                    _filterBoldFont.Dispose();
                    _filterBoldFont = null;
                }
            };

            FormClosing +=
                delegate(
                    object sender,
                    FormClosingEventArgs e)
                {
                    if (_isExecuting)
                    {
                        e.Cancel = true;
                        UiMessageBox.Show(
                            this,
                            L("Status.ExecutingCloseBlocked"),
                            L("Status.ExecutingTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    if (_isScanning)
                    {
                        e.Cancel = true;
                        RequestScanCancellation();
                        UiMessageBox.Show(
                            this,
                            L("Status.ScanningCloseBlocked"),
                            L("Status.ScanningTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    SavePersistentPaths();
                };
        }

        private string L(string key)
        {
            return _language.Get(key);
        }

        private string LF(string key, params object[] args)
        {
            return _language.Format(key, args);
        }

        private string T(string source)
        {
            return _language.TranslateSource(source);
        }

        private string GetScanModeDisplayName(
            ScanModeKind mode)
        {
            switch (mode)
            {
                case ScanModeKind.Exact:
                    return L("ScanMode.Exact");
                case ScanModeKind.NewAuthor:
                    return L("ScanMode.New");
                case ScanModeKind.Ambiguous:
                    return L("ScanMode.Ambiguous");
                case ScanModeKind.Unrecognized:
                    return L("ScanMode.Unrecognized");
                default:
                    return L("ScanMode.Global");
            }
        }

        private string GetScanModeDescription(
            ScanModeKind mode)
        {
            switch (mode)
            {
                case ScanModeKind.Exact:
                    return L("ScanMode.Exact.Desc");
                case ScanModeKind.NewAuthor:
                    return L("ScanMode.New.Desc");
                case ScanModeKind.Ambiguous:
                    return L("ScanMode.Ambiguous.Desc");
                case ScanModeKind.Unrecognized:
                    return L("ScanMode.Unrecognized.Desc");
                default:
                    return L("ScanMode.Global.Desc");
            }
        }

        private string GetFileTypeProfileDisplayName(
            FileTypeProfile profile)
        {
            if (profile == null)
                return L("Common.NotSet");

            string raw = (profile.Name ?? "").Trim();

            if (raw.Length == 0 ||
                String.Equals(raw, "未命名模式", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(raw, "未命名方案", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(raw, "Unnamed profile", StringComparison.OrdinalIgnoreCase))
            {
                return L("FileProfile.Unnamed");
            }

            if (String.Equals(
                    profile.Id,
                    FileTypeRules.DefaultArchiveProfileId,
                    StringComparison.OrdinalIgnoreCase) &&
                (raw.Length == 0 ||
                 String.Equals(raw, "压缩包模式", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(raw, "压缩文件", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(raw, "Archive files", StringComparison.OrdinalIgnoreCase)))
            {
                return L("FileProfile.Archive");
            }

            if (String.Equals(
                    profile.Id,
                    FileTypeRules.DefaultVideoProfileId,
                    StringComparison.OrdinalIgnoreCase) &&
                (raw.Length == 0 ||
                 String.Equals(raw, "视频模式", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(raw, "视频文件", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(raw, "Video files", StringComparison.OrdinalIgnoreCase)))
            {
                return L("FileProfile.Video");
            }

            return raw.Length > 0
                ? raw
                : L("FileProfile.Unnamed");
        }

        private string GetRecognitionNameByMark(
            string mark)
        {
            switch (mark)
            {
                case "[✓]":
                    return L("Recognition.DirectMatch");
                case "[≈]":
                    return L("Recognition.NormalizedShort");
                case "[↔]":
                    return L("Recognition.AliasMatch");
                case "[ID]":
                    return L("Recognition.EntityMatch");
                case "[◆]":
                    return L("Recognition.SocietyMatch");
                case "[●]":
                    return L("Recognition.AuthorMatch");
                case "[+]":
                    return L("Recognition.New");
                case "[!]":
                    return L("Recognition.Ambiguous");
                case "[★]":
                    return L("Recognition.ManualAssigned");
                case "[×]":
                    return L("Recognition.UnrecognizedShort");
                default:
                    return "";
            }
        }

        private string GetRecognitionVisualName(
            RecognitionVisual visual)
        {
            if (visual == null)
                return "";

            string code = (visual.Code ?? "").ToLowerInvariant();

            if (code == "direct") return L("Recognition.DirectMatch");
            if (code == "normalized") return L("Recognition.NormalizedShort");
            if (code == "alias") return L("Recognition.AliasMatch");
            if (code == "entity") return L("Recognition.EntityMatch");
            if (code == "society") return L("Recognition.SocietyMatch");
            if (code == "author") return L("Recognition.AuthorMatch");
            if (code == "new") return L("Recognition.New");
            if (code == "new-reuse") return L("Recognition.NewReuse");
            if (code == "ambiguous") return L("Recognition.Ambiguous");
            if (code == "manual") return L("Recognition.ManualAssigned");
            if (code == "unrecognized") return L("Recognition.UnrecognizedShort");

            return GetRecognitionNameByMark(visual.Mark);
        }

        private string FormatExtensionSummary(
            IEnumerable<string> extensions,
            int maxItems)
        {
            List<string> list =
                FileTypeRules.NormalizeExtensions(
                    extensions);

            if (list.Count == 0)
                return L("Common.NotSet");

            if (maxItems <= 0 ||
                list.Count <= maxItems)
            {
                return String.Join(
                    ", ",
                    list.ToArray());
            }

            return String.Join(
                    ", ",
                    list.Take(maxItems).ToArray()) +
                LF("FileTypes.More", list.Count);
        }

        private string LocalizeSearchDetail(string detail)
        {
            string value = detail ?? "";

            if (value == "Everything 官方 SDK 高速模式")
                return L("SearchDetail.EverythingFast");
            if (value == "Everything 官方 SDK 高速模式（结果 0）")
                return L("SearchDetail.EverythingFastZero");
            if (value == "Everything SDK 返回 0 个结果，但本地目录存在符合当前文件类型设置的文件；本次已自动回退普通模式")
                return L("SearchDetail.FallbackNoResults");
            if (value == "Everything 未运行，使用普通文件系统扫描")
                return L("SearchDetail.FileSystem");

            const string fallbackPrefix =
                "Everything 已运行，但 SDK 查询失败；已自动回退：";

            if (value.StartsWith(fallbackPrefix, StringComparison.Ordinal))
                return LF("SearchDetail.FallbackFailed", value.Substring(fallbackPrefix.Length));

            const string errorPrefix =
                "Everything SDK 查询失败，错误代码：";

            if (value.StartsWith(errorPrefix, StringComparison.Ordinal))
                return LF("SearchDetail.SDKQueryError", value.Substring(errorPrefix.Length));

            return value;
        }

        private string LocalizePlanStatus(string status)
        {
            string value = status ?? "";

            if (value == "无法识别作者") return L("PlanStatus.Unrecognized");
            if (value == "同作者识别有歧义，跳过") return L("PlanStatus.Ambiguous");
            if (value == "社团/作者均有匹配，需要选择") return L("PlanStatus.NeedChoice");
            if (value == "作者候选需要确认") return L("PlanStatus.CandidateConfirm");
            if (value == "匹配到已有作者") return L("PlanStatus.Matched");
            if (value == "本轮新作者（复用目录）") return L("PlanStatus.NewReuse");
            if (value == "文件已在目标作者文件夹") return L("PlanStatus.AlreadyThere");
            if (value == "目标已有同名文件，跳过") return L("PlanStatus.TargetExists");
            if (value == "手动指定作者文件夹") return L("PlanStatus.Manual");
            if (value == "手动指定作者") return L("PlanStatus.ManualAuthor");
            if (value == "扫描规则排除") return L("PlanStatus.Excluded");

            const string newPrefix = "新作者，计划放入 ";
            if (value.StartsWith(newPrefix, StringComparison.Ordinal))
                return LF("PlanStatus.NewToGroup", value.Substring(newPrefix.Length));

            return T(value);
        }

        private string LocalizeMatchWhy(string why)
        {
            string value = why ?? "";

            if (value == "手动指定作者文件夹；已写入作者别名库") return L("Reason.Manual");
            if (value == "手动指定作者；已写入作者别名库；目标目录按当前列表重新规划") return L("Reason.ManualNewAuthor");
            if (value == "本轮扫描已识别为同一新作者") return L("Reason.NewReuse");
            if (value == "未找到已有作者，按新作者处理") return L("Reason.NewAuthor");
            if (value == "作者核心名称完全一致") return L("Reason.Exact");
            if (value == "作者名标准化后完全一致") return L("Reason.Exact");
            if (value == "社团名 / 括号作者名 / 空格标点标准化匹配") return L("Reason.Normalized");
            if (value == "作者目录名称标准化匹配（空白 / 全半角 / 括号容错）") return L("Reason.NormalizedSafe");
            if (value == "发现疑似命名差异的作者目录（中点 / 横线 / 波浪线 / 外层引号 / 多余右括号等）；请使用“指定作者...”确认") return L("Reason.CautiousCandidate");
            if (value == "作者候选同时命中多个自定义别名组") return L("Reason.AliasAmbiguous");
            if (value == "自定义别名组对应多个现有作者目录") return L("Reason.AliasMultipleDirs");
            if (value == "同一个作者候选可匹配多个已有目录") return L("Reason.GenericMultiple");
            if (value == "同一个结构化作者候选可匹配多个已有目录") return L("Reason.StructuredMultiple");
            if (value == "多个前置身份标签或社团/作者候选分别匹配到不同作者文件夹；请双击“识别依据”选择") return L("Reason.MultiTagConflict");
            if (value == "多个前置身份标签或社团/作者候选需要确认；请双击“识别依据”选择") return L("Reason.MultiTagChoice");
            if (value == "未找到同作者目录") return L("Reason.NotFound");

            const string aliasUnified = "别名库统一为：";
            if (value.StartsWith(aliasUnified, StringComparison.Ordinal))
                return LF("Reason.AliasUnified", value.Substring(aliasUnified.Length));

            const string aliasLibrary = "作者别名库：";
            if (value.StartsWith(aliasLibrary, StringComparison.Ordinal))
                return LF("Reason.AliasLibrary", value.Substring(aliasLibrary.Length));

            const string entityLibrary = "作者实体库：";
            if (value.StartsWith(entityLibrary, StringComparison.Ordinal))
                return LF("Reason.EntityLibrary", value.Substring(entityLibrary.Length));

            if (value == "本地作者实体库中同一名称对应多个作者实体")
                return L("Reason.EntityAmbiguous");

            Match m = Regex.Match(
                value,
                "^社团 / 作者结构格式标准化匹配：「(.+?) \\((.+?)\\)」↔「(.+?)」$");
            if (m.Success)
                return LF("Reason.StructuredNormalized", m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);

            m = Regex.Match(
                value,
                "^社团名称段「(.+?)」匹配到已有作者目录$");
            if (m.Success)
                return LF("Reason.SocietySegment", m.Groups[1].Value);

            m = Regex.Match(
                value,
                "^社团名称段「(.+?)」匹配到多个作者文件夹；请双击“识别依据”选择$");
            if (m.Success)
                return LF("Reason.SocietySegmentMultiple", m.Groups[1].Value);

            m = Regex.Match(
                value,
                "^社团名「(.+?)」匹配到多个作者文件夹；请双击“识别依据”选择$");
            if (m.Success)
                return LF("Reason.SocietyMultiple", m.Groups[1].Value);

            m = Regex.Match(
                value,
                "^作者名「(.+?)」匹配到多个作者文件夹；请双击“识别依据”选择$");
            if (m.Success)
                return LF("Reason.AuthorMultiple", m.Groups[1].Value);

            m = Regex.Match(
                value,
                "^社团名「(.+?)」和作者名「(.+?)」均指向同一作者目录$");
            if (m.Success)
                return LF("Reason.SocietyAndAuthorSame", m.Groups[1].Value, m.Groups[2].Value);

            m = Regex.Match(
                value,
                "^社团名「(.+?)」与作者名「(.+?)」分别匹配到不同作者文件夹；请双击“识别依据”选择$");
            if (m.Success)
                return LF("Reason.SocietyAndAuthorDifferent", m.Groups[1].Value, m.Groups[2].Value);

            m = Regex.Match(
                value,
                "^社团名「(.+?)」与作者名「(.+?)」分别命中不同作者候选，其中包含本轮新作者；请使用“指定作者\\.\\.\\.”确认$");
            if (m.Success)
                return LF("Reason.SocietyAndAuthorDifferentWithPlanned", m.Groups[1].Value, m.Groups[2].Value);

            m = Regex.Match(
                value,
                "^仅社团名「(.+?)」匹配到已有作者目录$");
            if (m.Success)
                return LF("Reason.SocietyOnly", m.Groups[1].Value);

            m = Regex.Match(
                value,
                "^仅作者名「(.+?)」匹配到已有作者目录$");
            if (m.Success)
                return LF("Reason.AuthorOnly", m.Groups[1].Value);

            m = Regex.Match(
                value,
                "^「(.+?) \\((.+?)\\)」匹配到多个候选作者文件夹；请双击“识别依据”选择$");
            if (m.Success)
                return LF("Reason.CompositeMultiple", m.Groups[1].Value, m.Groups[2].Value);

            return T(value);
        }

        private void InitializeUi()
        {
            UiStyle.ApplyAppIcon(this);
            Text = L("App.Title") + " " + AppVersion.Display;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1280, 720);
            Size = new Size(1480, 900);
            Font = SystemFonts.MessageBoxFont;
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildTopMenu();

            _rootPanel = new Panel();
            // Do not use DockStyle.Fill here. MenuStrip + Fill docking order can vary
            // during WinForms DPI/layout passes and can place the path row underneath
            // the menu. Keep the application surface explicitly below the live menu
            // bounds instead.
            _rootPanel.Dock = DockStyle.None;
            _rootPanel.BackColor = Color.White;
            Controls.Add(_rootPanel);
            LayoutRootPanelBelowMenu();

            ClientSizeChanged += delegate { LayoutRootPanelBelowMenu(); };
            Layout += delegate { LayoutRootPanelBelowMenu(); };
            if (_topMenu != null)
                _topMenu.SizeChanged += delegate { LayoutRootPanelBelowMenu(); };
            Shown += delegate
            {
                BeginInvoke(new MethodInvoker(LayoutRootPanelBelowMenu));
                BeginInvoke(new MethodInvoker(StartAutomaticUpdateCheck));
            };

            // Hidden state controls retained for compatibility with the
            // existing scan/engine code. They do not participate in layout.
            _numMax = new NumericUpDown();
            _numMax.Minimum = 1;
            _numMax.Maximum = 999;
            _numMax.Value = 20;
            _numMax.Visible = false;
            _rootPanel.Controls.Add(_numMax);

            _txtGroupTemplate = new TextBox();
            _txtGroupTemplate.Text = GroupNaming.DefaultTemplate;
            _txtGroupTemplate.Visible = false;
            _txtGroupTemplate.TextChanged += delegate
            {
                UpdateGroupTemplatePreview();
                if (_plan.Count > 0 && _lblStatus != null)
                    _lblStatus.Text = L("Status.ArchiveSettingsChanged");
            };
            _rootPanel.Controls.Add(_txtGroupTemplate);

            _lblGroupTemplatePreview = new Label();
            _lblGroupTemplatePreview.Visible = false;
            _rootPanel.Controls.Add(_lblGroupTemplatePreview);

            // V1.9.2: use one explicit two-row layout instead of stacking
            // Dock=Fill and Dock=Top siblings. This prevents the workspace from
            // sitting underneath the header on different DPI/WinForms layouts.
            TableLayoutPanel appLayout = new TableLayoutPanel();
            appLayout.Dock = DockStyle.Fill;
            appLayout.BackColor = Color.White;
            appLayout.ColumnCount = 1;
            appLayout.RowCount = 2;
            appLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            appLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124F));
            appLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            appLayout.Margin = new Padding(0);
            appLayout.Padding = new Padding(0);
            _rootPanel.Controls.Add(appLayout);
            appLayout.BringToFront();

            Panel headerHost = new Panel();
            headerHost.Dock = DockStyle.Fill;
            headerHost.BackColor = Color.White;
            appLayout.Controls.Add(headerHost, 0, 0);
            BuildWorkspaceHeader(headerHost);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Color.White;
            appLayout.Controls.Add(body, 0, 1);
            BuildBodyWorkspace(body);

            UpdateGroupTemplatePreview();
        }

        private void BuildWorkspaceHeader(Control parent)
        {
            _headerPanel = new Panel();
            _headerPanel.Dock = DockStyle.Fill;
            _headerPanel.BackColor = Color.White;
            _headerPanel.Padding = new Padding(18, 12, 18, 8);
            parent.Controls.Add(_headerPanel);

            TableLayoutPanel headerLayout = new TableLayoutPanel();
            headerLayout.Dock = DockStyle.Fill;
            headerLayout.BackColor = Color.White;
            headerLayout.ColumnCount = 1;
            headerLayout.RowCount = 2;
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            headerLayout.Margin = new Padding(0);
            headerLayout.Padding = new Padding(0);
            _headerPanel.Controls.Add(headerLayout);

            TableLayoutPanel pathRow = new TableLayoutPanel();
            pathRow.Dock = DockStyle.Fill;
            pathRow.BackColor = Color.White;
            pathRow.ColumnCount = 2;
            pathRow.RowCount = 1;
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pathRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pathRow.Margin = new Padding(0);
            pathRow.Padding = new Padding(0);
            headerLayout.Controls.Add(pathRow, 0, 0);

            pathRow.Controls.Add(CreatePathEditor(true), 0, 0);
            pathRow.Controls.Add(CreatePathEditor(false), 1, 0);

            TableLayoutPanel commandLayout = new TableLayoutPanel();
            commandLayout.Dock = DockStyle.Fill;
            commandLayout.BackColor = Color.White;
            commandLayout.ColumnCount = 2;
            commandLayout.RowCount = 1;
            commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
            commandLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            commandLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            commandLayout.Margin = new Padding(0);
            commandLayout.Padding = new Padding(0, 5, 0, 0);
            headerLayout.Controls.Add(commandLayout, 0, 1);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.BackColor = Color.White;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.Margin = new Padding(0);
            flow.Padding = new Padding(0);
            commandLayout.Controls.Add(flow, 0, 0);

            // Keep the execution-space summary in the upper-right command area.
            // The Everything backend indicator is shown in the bottom-right status bar.
            _lblSpaceStatus = new Label();
            // Match the baseline of the command-row labels instead of vertically
            // centering inside the whole table cell (which made the text sit low).
            _lblSpaceStatus.AutoSize = true;
            _lblSpaceStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblSpaceStatus.TextAlign = ContentAlignment.MiddleRight;
            _lblSpaceStatus.AutoEllipsis = true;
            _lblSpaceStatus.ForeColor = UiStyle.Muted;
            _lblSpaceStatus.Margin = new Padding(8, 7, 0, 0);
            _lblSpaceStatus.Text = L("Status.SpaceWaiting");
            commandLayout.Controls.Add(_lblSpaceStatus, 1, 0);

            flow.Controls.Add(CreateInlineLabel(L("Main.ScanMode")));

            _cmbScanMode = new ComboBox();
            _cmbScanMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbScanMode.Width = 170;
            _cmbScanMode.Margin = new Padding(0, 2, 8, 0);
            _cmbScanMode.Items.Add(L("ScanMode.Global"));
            _cmbScanMode.Items.Add(L("ScanMode.Exact"));
            _cmbScanMode.Items.Add(L("ScanMode.New"));
            _cmbScanMode.Items.Add(L("ScanMode.Ambiguous"));
            _cmbScanMode.Items.Add(L("ScanMode.Unrecognized"));
            UiStyle.FitComboBoxToItems(_cmbScanMode, 150, 260);
            _cmbScanMode.SelectedIndex = 0;
            _cmbScanMode.SelectedIndexChanged += delegate
            {
                if (_lblStatus != null && _plan.Count > 0)
                    _lblStatus.Text = L("Status.ModeChanged");
                ScheduleScanWarmup(false);
            };
            flow.Controls.Add(_cmbScanMode);

            _btnPreview = CreateCommandButton(L("Main.ScanPreview"), 100);
            _btnPreview.Click += delegate { ScanPreview(); };
            flow.Controls.Add(_btnPreview);

            _btnExecute = CreateCommandButton(L("Main.Execute"), ExecuteButtonDefaultWidth);
            _btnExecute.Enabled = false;
            _btnExecute.Click += delegate { HandlePrimaryAction(); };
            _btnExecute.EnabledChanged += delegate
            {
                if (_menuExecuteItem != null)
                    _menuExecuteItem.Enabled = _btnExecute.Enabled;
            };
            flow.Controls.Add(_btnExecute);

            Panel separator = new Panel();
            separator.Width = 1;
            separator.Height = 26;
            separator.BackColor = Color.FromArgb(224, 226, 230);
            separator.Margin = new Padding(8, 3, 10, 0);
            flow.Controls.Add(separator);

            Label scanCountLabel = CreateInlineLabel(L("Main.ScanCount"));
            flow.Controls.Add(scanCountLabel);
            _numScanLimit = new NumericUpDown();
            _numScanLimit.Width = 65;
            _numScanLimit.Minimum = 0;
            _numScanLimit.Maximum = 100000;
            _numScanLimit.Value = 50;
            _numScanLimit.ThousandsSeparator = true;
            _numScanLimit.TextAlign = HorizontalAlignment.Center;
            _numScanLimit.Margin = new Padding(0, 2, 10, 0);
            _numScanLimit.ValueChanged += delegate { ScheduleScanWarmup(false); };
            flow.Controls.Add(_numScanLimit);
            _scanCountToolTip = new ToolTip();
            _scanCountToolTip.ShowAlways = true;
            _scanCountToolTip.SetToolTip(_numScanLimit, L("Main.ScanCountHint"));
            _scanCountToolTip.SetToolTip(scanCountLabel, L("Main.ScanCountHint"));

            _chkRecursive = new CheckBox();
            _chkRecursive.Text = L("Main.Recursive");
            _chkRecursive.AutoSize = true;
            _chkRecursive.Margin = new Padding(0, 6, 0, 0);
            _chkRecursive.CheckedChanged += delegate { ScheduleScanWarmup(false); };
            flow.Controls.Add(_chkRecursive);

            Panel bottomBorder = new Panel();
            bottomBorder.Dock = DockStyle.Bottom;
            bottomBorder.Height = 1;
            bottomBorder.BackColor = Color.FromArgb(232, 233, 236);
            _headerPanel.Controls.Add(bottomBorder);
            bottomBorder.BringToFront();
        }

        private Control CreatePathEditor(bool source)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.ColumnCount = 3;
            row.RowCount = 1;
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            row.Margin = source
                ? new Padding(0, 0, 10, 0)
                : new Padding(10, 0, 0, 0);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label label = new Label();
            label.Text = source ? L("Main.SourcePath") : L("Main.TargetPath");
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(0, 0, 8, 0);
            row.Controls.Add(label, 0, 0);

            TextBox textBox = new TextBox();
            textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            textBox.Margin = new Padding(0, 0, 8, 0);
            row.Controls.Add(textBox, 1, 0);

            int browseColumn = 2;

            Button browse = CreateFlatButton(L("Common.Browse"), 78, 28);
            browse.Anchor = AnchorStyles.Left;
            browse.Margin = new Padding(0);
            row.Controls.Add(browse, browseColumn, 0);

            if (source)
            {
                _txtSource = textBox;
                _btnBrowseSource = browse;
                _txtSource.TextChanged += delegate { HandlePathEditorChanged(); };
                browse.Click += delegate { BrowseFolder(_txtSource, L("Menu.OpenSource")); };
            }
            else
            {
                _txtRoot = textBox;
                _btnBrowseTarget = browse;
                _txtRoot.TextChanged += delegate { HandlePathEditorChanged(); };
                browse.Click += delegate { BrowseFolder(_txtRoot, L("Menu.OpenTarget")); };
            }

            return row;
        }

        private void HandlePathEditorChanged()
        {
            ScheduleScanWarmup(true);
            if (_plan.Count > 0 &&
                (!PathsEqual(_txtRoot != null ? _txtRoot.Text : "", _currentRoot) ||
                 !PathsEqual(_txtSource != null ? _txtSource.Text : "", _currentSourceRoot)))
            {
                if (_lblSpaceStatus != null)
                {
                    _lblSpaceStatus.Text = L("Status.SpaceRescanRequired");
                    _lblSpaceStatus.ForeColor = UiStyle.Danger;
                }

                if (_btnExecute != null && !_isExecuting)
                    _btnExecute.Enabled = false;

                UpdateMainActionAvailability();
                return;
            }

            RefreshExecutionSafetyUi();
            UpdateMainActionAvailability();
            UpdateWorkflowGuidanceStatus();
        }

        private void UpdateWorkflowGuidanceStatus()
        {
            if (_lblStatus == null || _isScanning || _isExecuting)
                return;

            // Do not overwrite a completed scan/result summary. This guidance is
            // for the pre-scan path-selection workflow only.
            if (_plan != null && _plan.Count > 0)
                return;

            string source = _txtSource != null ? (_txtSource.Text ?? "").Trim() : "";
            string target = _txtRoot != null ? (_txtRoot.Text ?? "").Trim() : "";

            if (source.Length == 0 || !Directory.Exists(source))
            {
                SetWorkflowStatusLink(L("Status.GuideSelectSource"), "source");
                return;
            }

            if (target.Length == 0 || !Directory.Exists(target))
            {
                SetWorkflowStatusLink(L("Status.GuideSelectTarget"), "target");
                return;
            }

            SetWorkflowStatusLink(L("Status.GuideScanPreview"), "preview");
        }

        private void SetWorkflowStatusLink(string text, string action)
        {
            if (_lblStatus == null)
                return;

            _settingStatusReviewLink = true;
            try
            {
                _lblStatus.Links.Clear();
                _lblStatus.Text = text ?? "";
                if (_lblStatus.Text.Length > 0)
                    _lblStatus.Links.Add(0, _lblStatus.Text.Length, action);
            }
            finally
            {
                _settingStatusReviewLink = false;
            }
        }

        private bool HasValidScanPaths()
        {
            string source = _txtSource != null ? (_txtSource.Text ?? "").Trim() : "";
            string target = _txtRoot != null ? (_txtRoot.Text ?? "").Trim() : "";
            return source.Length > 0 && target.Length > 0 &&
                Directory.Exists(source) && Directory.Exists(target);
        }

        private void UpdateMainActionAvailability()
        {
            bool pathsReady = HasValidScanPaths();
            bool hasExtensions = _scanExtensions != null && _scanExtensions.Count > 0;
            bool canPreview = _isScanning
                ? !_isExecuting
                : (!_isExecuting && pathsReady && hasExtensions);

            if (_btnPreview != null)
                _btnPreview.Enabled = canPreview;
            if (_menuPreviewItem != null)
                _menuPreviewItem.Enabled = canPreview;

            if (_menuOpenSourceItem != null)
            {
                string source = _txtSource != null ? (_txtSource.Text ?? "").Trim() : "";
                _menuOpenSourceItem.Enabled = !_isExecuting && !_isScanning && source.Length > 0 && Directory.Exists(source);
            }

            if (_menuOpenTargetItem != null)
            {
                string target = _txtRoot != null ? (_txtRoot.Text ?? "").Trim() : "";
                _menuOpenTargetItem.Enabled = !_isExecuting && !_isScanning && target.Length > 0 && Directory.Exists(target);
            }

            UpdatePrimaryActionPresentation();

            int movableCount = _plan != null
                ? _plan.Count(delegate(PlanItem p) { return p != null && p.CanMove; })
                : 0;
            int reviewCount = CountUnresolvedItems();
            bool reviewOnly = movableCount == 0 && reviewCount > 0;

            if (_btnExecute != null)
            {
                if (_isScanning || _isExecuting)
                {
                    _btnExecute.Enabled = false;
                }
                else if (_plan == null || _plan.Count == 0)
                {
                    _btnExecute.Enabled = false;
                }
                else if (reviewOnly)
                {
                    // Review-only is a navigation action, not a file move. It stays
                    // available even when execution safety cannot yet be evaluated.
                    _btnExecute.Enabled = true;
                }
                else
                {
                    // Safety is evaluated when the plan or its paths change.
                    // Reuse that result when only the visible status view changes;
                    // probing every source and target file here made tab switches
                    // perform thousands of synchronous disk calls on the UI thread.
                    _btnExecute.Enabled = _executionSafetyAllowsMove;
                }

                if (!reviewOnly &&
                    String.Equals(_viewFilter, "excluded", StringComparison.OrdinalIgnoreCase))
                {
                    _btnExecute.Enabled = false;
                }
            }

            if (_menuExecuteItem != null && _btnExecute != null)
                _menuExecuteItem.Enabled = _btnExecute.Enabled && !_isExecuting && !_isScanning;
        }

        private void UpdatePrimaryActionPresentation()
        {
            int movableCount = _plan != null
                ? _plan.Count(delegate(PlanItem p) { return p != null && p.CanMove; })
                : 0;
            int reviewCount = CountUnresolvedItems();
            bool reviewOnly = movableCount == 0 && reviewCount > 0;

            if (_btnExecute != null)
            {
                _btnExecute.Text = reviewOnly
                    ? LF("Action.HandleReviewCount", reviewCount)
                    : L("Main.Execute");
                _btnExecute.Width = reviewOnly
                    ? ReviewButtonWidth
                    : ExecuteButtonDefaultWidth;
            }

            if (_menuExecuteItem != null)
            {
                _menuExecuteItem.Text = reviewOnly
                    ? LF("Action.HandleReviewCount", reviewCount)
                    : L("Menu.Execute");
            }
        }

        private void HandlePrimaryAction()
        {
            if (_isExecuting || _isScanning)
                return;

            int movableCount = _plan != null
                ? _plan.Count(delegate(PlanItem p) { return p != null && p.CanMove; })
                : 0;
            int reviewCount = CountUnresolvedItems();

            if (movableCount == 0 && reviewCount > 0)
            {
                OpenNeedsReview();
                return;
            }

            ExecuteMove();
        }

        private Label CreateInlineLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(0, 7, 6, 0);
            return label;
        }

        private Button CreateCommandButton(string text, int width)
        {
            Button button = CreateFlatButton(text, width, 30);
            button.BackColor = Color.White;
            button.Margin = new Padding(0, 1, 6, 0);
            return button;
        }

        private Button CreateFlatButton(string text, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.MinimumSize = new Size(Math.Max(0, width), height);
            button.Width = UiStyle.MeasureButtonWidth(text, Font, width);
            button.Height = height;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 213, 218);
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(32, 33, 36);
            button.UseVisualStyleBackColor = false;
            UiStyle.ApplyButtonStateStyle(button, false, false);
            return button;
        }

        private void BuildBodyWorkspace(Panel body)
        {
            // Main workspace uses the full width; navigation is handled by the filter bar and top menus.
            // The main list now uses the whole available width; History lives
            // under the Tools menu and the attention aggregate is available in
            // the top filter bar.
            body.Padding = new Padding(18, 0, 18, 0);

            TableLayoutPanel work = new TableLayoutPanel();
            work.Dock = DockStyle.Fill;
            work.BackColor = Color.White;
            work.ColumnCount = 1;
            work.RowCount = 4;
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            work.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            work.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            work.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            work.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            work.Margin = new Padding(0);
            work.Padding = new Padding(0);
            body.Controls.Add(work);

            Panel filterHost = new Panel();
            filterHost.Dock = DockStyle.Fill;
            filterHost.BackColor = Color.White;
            work.Controls.Add(filterHost, 0, 0);
            BuildViewFilterBar(filterHost);

            Panel centerHost = new Panel();
            centerHost.Dock = DockStyle.Fill;
            centerHost.BackColor = Color.White;
            work.Controls.Add(centerHost, 0, 1);

            // The details pane has a fixed width. Visibility is controlled by the
            // compact button next to Search; the divider is not draggable.
            _workspaceSplit = new SplitContainer();
            _workspaceSplit.Size = new Size(1100, 500);
            _workspaceSplit.Dock = DockStyle.Fill;
            _workspaceSplit.Orientation = Orientation.Vertical;
            _workspaceSplit.FixedPanel = FixedPanel.Panel2;
            _workspaceSplit.IsSplitterFixed = true;
            _workspaceSplit.SplitterWidth = 1;
            _workspaceSplit.Panel1MinSize = 560;
            _workspaceSplit.Panel2MinSize = 300;
            _workspaceSplit.SplitterDistance = 1100 - DetailPanelWidth - 1;
            _workspaceSplit.BackColor = Color.FromArgb(231, 232, 235);
            _workspaceSplit.Panel1.BackColor = Color.White;
            _workspaceSplit.Panel2.BackColor = Color.White;
            centerHost.Controls.Add(_workspaceSplit);

            centerHost.SizeChanged += delegate
            {
                if (_workspaceSplit == null || _workspaceSplit.Panel2Collapsed)
                    return;

                RestoreDetailPanelWidth();
            };

            BuildGrid();
            BuildDetailsPanel();
            RestoreDetailPanelWidth();
            UpdateDetailsToggleButton();

            BuildExecutionProgress();
            _progressPanel.Dock = DockStyle.Fill;
            work.Controls.Add(_progressPanel, 0, 2);

            Panel statusBar = new Panel();
            statusBar.Dock = DockStyle.Fill;
            statusBar.BackColor = Color.White;
            statusBar.Padding = new Padding(12, 5, 12, 0);
            statusBar.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.DrawLine(
                    Pens.Gainsboro,
                    0,
                    0,
                    statusBar.Width,
                    0);
            };

            TableLayoutPanel statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.BackColor = Color.White;
            statusLayout.ColumnCount = 2;
            statusLayout.RowCount = 1;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            statusLayout.Margin = new Padding(0);
            statusLayout.Padding = new Padding(0);
            statusBar.Controls.Add(statusLayout);

            _lblStatus = new LinkLabel();
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Text = "";
            _lblStatus.ForeColor = Color.FromArgb(80, 82, 86);
            _lblStatus.LinkColor = UiStyle.Accent;
            _lblStatus.ActiveLinkColor = Color.FromArgb(0, 90, 170);
            _lblStatus.VisitedLinkColor = UiStyle.Accent;
            _lblStatus.LinkBehavior = LinkBehavior.HoverUnderline;
            _lblStatus.TextChanged += delegate
            {
                if (!_settingStatusReviewLink)
                    _lblStatus.Links.Clear();
            };
            _lblStatus.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                string action = e != null && e.Link != null
                    ? e.Link.LinkData as string
                    : null;
                if (String.Equals(action, "source", StringComparison.OrdinalIgnoreCase))
                {
                    if (_btnBrowseSource != null && _btnBrowseSource.Enabled)
                        _btnBrowseSource.PerformClick();
                }
                else if (String.Equals(action, "target", StringComparison.OrdinalIgnoreCase))
                {
                    if (_btnBrowseTarget != null && _btnBrowseTarget.Enabled)
                        _btnBrowseTarget.PerformClick();
                }
                else if (String.Equals(action, "preview", StringComparison.OrdinalIgnoreCase))
                {
                    if (_btnPreview != null && _btnPreview.Enabled)
                        _btnPreview.PerformClick();
                }
                else if (String.Equals(action, "deferred", StringComparison.OrdinalIgnoreCase))
                    OpenDeferredItems();
                else if (String.Equals(action, "review", StringComparison.OrdinalIgnoreCase))
                    OpenNeedsReview();
            };
            statusLayout.Controls.Add(_lblStatus, 0, 0);

            _lblSearch = new LinkLabel();
            _lblSearch.Dock = DockStyle.Fill;
            _lblSearch.TextAlign = ContentAlignment.MiddleRight;
            _lblSearch.AutoEllipsis = true;
            _lblSearch.ForeColor = Color.DimGray;
            _lblSearch.LinkColor = UiStyle.Accent;
            _lblSearch.ActiveLinkColor = Color.FromArgb(0, 90, 170);
            _lblSearch.LinkBehavior = LinkBehavior.HoverUnderline;
            _lblSearch.Margin = new Padding(8, 0, 0, 0);
            _lblSearch.LinkClicked += EverythingStatusLinkClicked;
            statusLayout.Controls.Add(_lblSearch, 1, 0);

            work.Controls.Add(statusBar, 0, 3);
        }

        private void BuildViewFilterBar(Control parent)
        {
            _filterBar = new Panel();
            _filterBar.Dock = DockStyle.Top;
            _filterBar.Height = 44;
            _filterBar.BackColor = Color.White;
            _filterBar.Padding = new Padding(0, 7, 0, 5);
            parent.Controls.Add(_filterBar);
            _filterBar.BringToFront();

            TableLayoutPanel barLayout = new TableLayoutPanel();
            barLayout.Dock = DockStyle.Fill;
            barLayout.BackColor = Color.White;
            barLayout.ColumnCount = 2;
            barLayout.RowCount = 1;
            barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            barLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            barLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            barLayout.Margin = new Padding(0);
            barLayout.Padding = new Padding(0);
            _filterBar.Controls.Add(barLayout);

            FlowLayoutPanel filters = new FlowLayoutPanel();
            filters.Dock = DockStyle.Fill;
            filters.WrapContents = false;
            filters.AutoScroll = true;
            filters.Margin = new Padding(0);
            filters.Padding = new Padding(0);
            barLayout.Controls.Add(filters, 0, 0);

            // No separate “View:” label. Filters begin at the left edge and
            // the former sidebar's Review entry is preserved as an aggregate
            // attention filter here.
            AddFilterButton(filters, L("Filter.All"), "all");
            AddFilterButton(filters, L("Nav.Review"), "attention");
            AddFilterButton(filters, L("Filter.Matched"), "matched");
            AddFilterButton(filters, L("Filter.NewAuthor"), "new");
            AddFilterButton(filters, L("Filter.Ambiguous"), "ambiguous");
            AddFilterButton(filters, L("Filter.Unrecognized"), "unrecognized");
            AddFilterButton(filters, L("Filter.Duplicate"), "duplicate");
            AddFilterButton(filters, L("Filter.Excluded"), "excluded");

            // Search stays at the far right. Keep the label, text box and detail
            // toggle on one optical baseline so the command strip remains tidy
            // under different DPI/font scaling.
            TableLayoutPanel searchHost = new TableLayoutPanel();
            searchHost.Dock = DockStyle.Fill;
            searchHost.BackColor = Color.White;
            searchHost.ColumnCount = 3;
            searchHost.RowCount = 1;
            searchHost.AutoSize = true;
            searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
            searchHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
            searchHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            searchHost.Margin = new Padding(0);
            searchHost.Padding = new Padding(0, 2, 0, 2);
            barLayout.Controls.Add(searchHost, 1, 0);

            Label searchLabel = new Label();
            searchLabel.Text = L("Filter.Search");
            searchLabel.Dock = DockStyle.Fill;
            searchLabel.TextAlign = ContentAlignment.MiddleRight;
            searchLabel.Margin = new Padding(0, 0, 6, 0);
            searchHost.Controls.Add(searchLabel, 0, 0);

            _txtListSearch = new TextBox();
            _txtListSearch.Dock = DockStyle.Fill;
            _txtListSearch.Margin = new Padding(0, 2, 6, 2);
            _txtListSearch.TextChanged += delegate
            {
                string keep = GetCurrentSelectedPath();
                ApplyCurrentGridFilter(keep);
            };
            searchHost.Controls.Add(_txtListSearch, 1, 0);

            _detailsToolTip = new ToolTip();
            _detailsToolTip.ShowAlways = true;

            _btnToggleDetails = new Button();
            _btnToggleDetails.Text = "";
            _btnToggleDetails.Size = new Size(32, 26);
            _btnToggleDetails.Anchor = AnchorStyles.None;
            _btnToggleDetails.Margin = new Padding(4, 0, 0, 0);
            _btnToggleDetails.Padding = new Padding(0);
            _btnToggleDetails.FlatStyle = FlatStyle.Flat;
            _btnToggleDetails.FlatAppearance.BorderColor = Color.FromArgb(210, 213, 218);
            _btnToggleDetails.FlatAppearance.BorderSize = 1;
            _btnToggleDetails.FlatAppearance.MouseOverBackColor = Color.FromArgb(242, 244, 247);
            _btnToggleDetails.FlatAppearance.MouseDownBackColor = UiStyle.Selection;
            _btnToggleDetails.BackColor = Color.White;
            _btnToggleDetails.ForeColor = Color.FromArgb(70, 72, 76);
            _btnToggleDetails.Cursor = Cursors.Hand;
            _btnToggleDetails.TabStop = false;
            _btnToggleDetails.Paint += delegate(object sender, PaintEventArgs e)
            {
                DrawDetailsChevron(e.Graphics, _btnToggleDetails.ClientRectangle);
            };
            _btnToggleDetails.Click += delegate { ToggleDetailsPanel(); };
            searchHost.Controls.Add(_btnToggleDetails, 2, 0);

            Panel bottomBorder = new Panel();
            bottomBorder.Dock = DockStyle.Bottom;
            bottomBorder.Height = 1;
            bottomBorder.BackColor = Color.FromArgb(231, 232, 235);
            _filterBar.Controls.Add(bottomBorder);
            bottomBorder.BringToFront();

            UpdateFilterCounts();
            UpdateViewFilterButtonStyles();
        }

        private void AddFilterButton(FlowLayoutPanel host, string text, string filter)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.Height = 28;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(55, 57, 61);
            button.UseVisualStyleBackColor = false;
            button.Padding = new Padding(8, 0, 8, 0);
            button.Margin = new Padding(0, 0, 2, 0);
            button.Tag = filter;
            button.Click += delegate { SetViewFilter(filter); };
            host.Controls.Add(button);
            _filterButtons[filter] = button;
        }

        private void ToggleDetailsPanel()
        {
            if (_workspaceSplit == null)
                return;

            if (_workspaceSplit.Panel2Collapsed)
            {
                _workspaceSplit.Panel2Collapsed = false;
                RestoreDetailPanelWidth();
            }
            else
            {
                _workspaceSplit.Panel2Collapsed = true;
            }

            UpdateDetailsToggleButton();
        }

        private void RestoreDetailPanelWidth()
        {
            if (_workspaceSplit == null || _workspaceSplit.Panel2Collapsed)
                return;

            int maxPanel2 = _workspaceSplit.ClientSize.Width -
                _workspaceSplit.Panel1MinSize - _workspaceSplit.SplitterWidth;
            if (maxPanel2 < _workspaceSplit.Panel2MinSize)
                return;

            int preferredPanelWidth = DetailPanelWidth;
            if (_detailAssignButton != null && _detailExcludeButton != null)
            {
                preferredPanelWidth = Math.Max(
                    preferredPanelWidth,
                    _detailAssignButton.Width + _detailExcludeButton.Width + 44);
            }

            int panel2Width = Math.Max(
                _workspaceSplit.Panel2MinSize,
                Math.Min(preferredPanelWidth, maxPanel2));
            int distance = _workspaceSplit.ClientSize.Width -
                panel2Width - _workspaceSplit.SplitterWidth;

            if (distance >= _workspaceSplit.Panel1MinSize)
            {
                try { _workspaceSplit.SplitterDistance = distance; } catch { }
            }
        }

        private void UpdateDetailsToggleButton()
        {
            if (_btnToggleDetails == null || _workspaceSplit == null)
                return;

            bool collapsed = _workspaceSplit.Panel2Collapsed;
            _btnToggleDetails.Invalidate();

            if (_detailsToolTip != null)
            {
                _detailsToolTip.SetToolTip(
                    _btnToggleDetails,
                    collapsed ? L("Details.Show") : L("Details.Hide"));
            }
        }

        private void DrawDetailsChevron(Graphics graphics, Rectangle bounds)
        {
            if (graphics == null || _workspaceSplit == null)
                return;

            bool pointLeft = _workspaceSplit.Panel2Collapsed;
            int centerX = bounds.Left + bounds.Width / 2;
            // Move the optical center up by one pixel. Font glyphs tended to look
            // slightly low in the old text-based button at common Windows DPI.
            int centerY = bounds.Top + bounds.Height / 2 - 1;
            int halfHeight = 5;
            int halfWidth = 3;

            Point top = pointLeft
                ? new Point(centerX + halfWidth, centerY - halfHeight)
                : new Point(centerX - halfWidth, centerY - halfHeight);
            Point middle = pointLeft
                ? new Point(centerX - halfWidth, centerY)
                : new Point(centerX + halfWidth, centerY);
            Point bottom = pointLeft
                ? new Point(centerX + halfWidth, centerY + halfHeight)
                : new Point(centerX - halfWidth, centerY + halfHeight);

            System.Drawing.Drawing2D.SmoothingMode oldMode = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Color.FromArgb(70, 72, 76), 1.6F))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                graphics.DrawLines(pen, new Point[] { top, middle, bottom });
            }
            graphics.SmoothingMode = oldMode;
        }

        private void BuildDetailsPanel()
        {
            Panel host = _workspaceSplit.Panel2;
            host.Padding = new Padding(18, 14, 18, 14);
            host.BackColor = Color.White;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Color.White;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            host.Controls.Add(root);

            _detailTitle = new Label();
            _detailTitle.Text = L("Details.Title");
            _detailTitle.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
            _detailTitle.Dock = DockStyle.Fill;
            _detailTitle.TextAlign = ContentAlignment.MiddleLeft;
            _detailTitle.Margin = new Padding(0);
            root.Controls.Add(_detailTitle, 0, 0);

            _detailContent = new FlowLayoutPanel();
            _detailContent.Dock = DockStyle.Fill;
            _detailContent.BackColor = Color.White;
            _detailContent.FlowDirection = FlowDirection.TopDown;
            _detailContent.WrapContents = false;
            _detailContent.AutoScroll = true;
            _detailContent.Margin = new Padding(0);
            _detailContent.Padding = new Padding(0, 4, 0, 6);
            _detailContent.ClientSizeChanged += delegate { ResizeDetailBlocks(); };
            root.Controls.Add(_detailContent, 0, 1);

            _detailEmpty = new Label();
            _detailEmpty.Text = L("Details.Empty");
            _detailEmpty.ForeColor = UiStyle.Muted;
            _detailEmpty.Size = new Size(286, 56);
            _detailEmpty.Margin = new Padding(0, 8, 0, 0);
            _detailEmpty.UseMnemonic = false;
            _detailContent.Controls.Add(_detailEmpty);

            int labelWidth = CalculateDetailLabelWidth(new string[]
            {
                L("Details.Status"),
                L("Details.Author"),
                L("Details.Matched"),
                L("Details.Process"),
                L("Details.Target")
            });

            // V1.11.8: the processing preview uses one permanent information
            // skeleton. Selecting a different status changes values only; it
            // never adds/removes rows, so the panel does not jump vertically.
            _detailFileBlock = CreateDetailLongBlock(
                L("Details.FileName"),
                42,
                out _detailFileName);
            _detailContent.Controls.Add(_detailFileBlock);

            _detailStatusBlock = CreateDetailRowBlock(
                L("Details.Status"),
                labelWidth,
                20,
                out _detailStatus);
            _detailContent.Controls.Add(_detailStatusBlock);

            _detailAuthorBlock = CreateDetailRowBlock(
                L("Details.Author"),
                labelWidth,
                20,
                out _detailAuthor);
            _detailContent.Controls.Add(_detailAuthorBlock);

            _detailMatchedBlock = CreateDetailRowBlock(
                L("Details.Matched"),
                labelWidth,
                20,
                out _detailMatched);
            _detailContent.Controls.Add(_detailMatchedBlock);

            _detailProcessBlock = CreateDetailRowBlock(
                L("Details.Process"),
                labelWidth,
                20,
                out _detailProcess);
            _detailContent.Controls.Add(_detailProcessBlock);

            _detailTargetBlock = CreateDetailRowBlock(
                L("Details.Target"),
                labelWidth,
                40,
                out _detailTargetPath);
            _detailContent.Controls.Add(_detailTargetBlock);

            _detailResultBlock = CreateDetailLongBlock(
                L("Details.Result"),
                42,
                out _detailResult);
            _detailResultBlock.Margin = new Padding(0, 8, 0, 0);
            _detailContent.Controls.Add(_detailResultBlock);

            _detailActions = new TableLayoutPanel();
            _detailActions.Dock = DockStyle.Fill;
            _detailActions.BackColor = Color.White;
            _detailActions.ColumnCount = 2;
            _detailActions.RowCount = 1;
            _detailActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _detailActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _detailActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _detailActions.Margin = new Padding(0, 6, 0, 0);
            _detailActions.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(UiStyle.Border))
                    e.Graphics.DrawLine(pen, 0, 0, _detailActions.ClientSize.Width, 0);
            };
            root.Controls.Add(_detailActions, 0, 2);

            _detailAssignButton = UiStyle.NewButton(L("Details.AssignAuthor"), 0, false);
            _detailAssignButton.Dock = DockStyle.Fill;
            _detailAssignButton.Margin = new Padding(0, 4, 4, 0);
            _detailAssignButton.Enabled = false;
            _detailAssignButton.Click += delegate
            {
                HandleDetailPrimaryAction();
            };
            _detailActions.Controls.Add(_detailAssignButton, 0, 0);

            _detailExcludeButton = UiStyle.NewButton(L("Details.Exclude"), 0, false);
            _detailExcludeButton.Dock = DockStyle.Fill;
            _detailExcludeButton.Margin = new Padding(4, 4, 0, 0);
            _detailExcludeButton.Enabled = false;
            _detailExcludeButton.Click += delegate { BlockSelectedArchives(); };
            _detailActions.Controls.Add(_detailExcludeButton, 1, 0);

            ResizeDetailBlocks();
            UpdateDetailsFromSelection();
        }

        private Panel CreateDetailLongBlock(
            string caption,
            int valueHeight,
            out Label value)
        {
            Panel block = new Panel();
            block.Size = new Size(286, 24 + valueHeight + 8);
            block.Margin = new Padding(0, 0, 0, 2);
            block.BackColor = Color.White;

            Label label = new Label();
            label.Text = caption;
            label.ForeColor = UiStyle.Muted;
            label.AutoSize = true;
            label.UseMnemonic = false;
            label.Location = new Point(0, 0);
            block.Controls.Add(label);

            value = new Label();
            value.ForeColor = UiStyle.Text;
            value.BackColor = Color.White;
            value.Location = new Point(0, 22);
            value.Size = new Size(286, valueHeight);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            value.AutoEllipsis = true;
            value.UseMnemonic = false;
            block.Controls.Add(value);
            return block;
        }

        private Panel CreateDetailRowBlock(
            string caption,
            int labelWidth,
            int valueHeight,
            out Label value)
        {
            int rowHeight = Math.Max(30, valueHeight + 8);
            int valueLeft = labelWidth + 8;

            Panel block = new Panel();
            block.Size = new Size(286, rowHeight);
            block.Margin = new Padding(0, 0, 0, 2);
            block.BackColor = Color.White;

            Label label = new Label();
            label.Text = caption;
            label.ForeColor = UiStyle.Muted;
            label.Location = new Point(0, 4);
            label.Size = new Size(labelWidth, 20);
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
            label.TextAlign = ContentAlignment.TopLeft;
            block.Controls.Add(label);

            value = new Label();
            value.Location = new Point(valueLeft, 4);
            value.Size = new Size(Math.Max(60, 286 - valueLeft), valueHeight);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            value.AutoSize = false;
            value.AutoEllipsis = true;
            value.ForeColor = UiStyle.Text;
            value.TextAlign = ContentAlignment.TopLeft;
            value.UseMnemonic = false;
            block.Controls.Add(value);
            return block;
        }

        private int CalculateDetailLabelWidth(IEnumerable<string> captions)
        {
            int widest = 0;
            foreach (string caption in captions ?? Enumerable.Empty<string>())
            {
                Size measured = TextRenderer.MeasureText(
                    caption ?? "",
                    Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                widest = Math.Max(widest, measured.Width);
            }

            // Keep a practical boundary, but let the active language decide the
            // actual width. Labels stay single-line; the value column fills the
            // remaining space.
            return Math.Max(72, Math.Min(150, widest + 12));
        }

        private void ResizeDetailBlocks()
        {
            if (_detailContent == null)
                return;

            int width = Math.Max(220, _detailContent.ClientSize.Width - 2);
            Control[] blocks = new Control[]
            {
                _detailFileBlock,
                _detailStatusBlock,
                _detailAuthorBlock,
                _detailMatchedBlock,
                _detailProcessBlock,
                _detailTargetBlock,
                _detailResultBlock
            };

            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] != null)
                    blocks[i].Width = width;
            }

            if (_detailEmpty != null)
                _detailEmpty.Width = width;
        }

        private string DetailValueOrDash(string value)
        {
            return String.IsNullOrWhiteSpace(value)
                ? L("Common.EmptyValue")
                : value.Trim();
        }

        private void HandleDetailPrimaryAction()
        {
            if (_isScanning || _isExecuting)
                return;

            PlanItem item = GetSingleSelectedPlanItem();
            if (item == null)
                return;

            if (item.IsExcludedPreview)
            {
                ShowScanExclusionRulesDialog();
                return;
            }

            RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
            string code = visual != null
                ? (visual.Code ?? "").ToLowerInvariant()
                : "";

            if (code == "unrecognized")
            {
                AssignAuthorNameToUnrecognized(item);
                return;
            }

            ChooseManualAuthorFolder(item);
        }

        private void AssignAuthorNameToUnrecognized(PlanItem item)
        {
            if (item == null || _isScanning || _isExecuting)
                return;

            string authorName;
            if (!ShowManualAuthorNameDialog(out authorName))
                return;

            authorName = (authorName ?? "").Trim();
            if (authorName.Length == 0)
                return;

            item.ManualTargetDir = "";
            item.ManualTargetName = authorName;
            item.ManualTargetAuthor = authorName;

            string keep = item.SourcePath;
            RefreshPlan(keep);
            _lblStatus.Text = LF("Status.ManualAuthorNamed", authorName);
        }

        private bool ShowManualAuthorNameDialog(out string authorName)
        {
            authorName = "";
            string result = "";
            using (Form dlg = new Form())
            {
                dlg.Text = L("Dialog.ManualAuthorName.Title");
                dlg.ClientSize = new Size(430, 176);
                dlg.MinimumSize = dlg.Size;
                dlg.MaximumSize = dlg.Size;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                UiStyle.ApplyDialog(dlg, Font);

                Label description = new Label();
                description.Text = L("Dialog.ManualAuthorName.Description");
                description.Location = new Point(18, 16);
                description.Size = new Size(394, 40);
                description.ForeColor = UiStyle.Muted;
                dlg.Controls.Add(description);

                Label label = new Label();
                label.Text = L("Dialog.ManualAuthorName.Label");
                label.Location = new Point(18, 70);
                label.Size = new Size(72, 24);
                label.TextAlign = ContentAlignment.MiddleLeft;
                dlg.Controls.Add(label);

                TextBox input = new TextBox();
                input.Location = new Point(92, 70);
                input.Size = new Size(320, 24);
                dlg.Controls.Add(input);

                Button ok = UiStyle.NewButton(L("Common.OK"), 88, true);
                ok.Location = new Point(226, 119);
                ok.Click += delegate
                {
                    string value = (input.Text ?? "").Trim();
                    if (value.Length == 0)
                    {
                        UiMessageBox.Show(
                            dlg,
                            L("Dialog.ManualAuthorName.Empty"),
                            L("Dialog.ManualAuthorName.Title"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }
                    if (value == "." ||
                        value == ".." ||
                        value.EndsWith(".", StringComparison.Ordinal) ||
                        value.EndsWith(" ", StringComparison.Ordinal) ||
                        value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        UiMessageBox.Show(
                            dlg,
                            L("Dialog.ManualAuthorName.Invalid"),
                            L("Dialog.ManualAuthorName.Title"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    result = value;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };
                dlg.Controls.Add(ok);

                Button cancel = UiStyle.NewButton(L("Common.Cancel"), 88, false);
                cancel.Location = new Point(324, 119);
                cancel.Click += delegate
                {
                    dlg.DialogResult = DialogResult.Cancel;
                    dlg.Close();
                };
                dlg.Controls.Add(cancel);

                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                input.Select();

                bool accepted = dlg.ShowDialog(this) == DialogResult.OK;
                if (accepted)
                    authorName = result;
                return accepted;
            }
        }

        private void LayoutRootPanelBelowMenu()
        {
            if (_rootPanel == null)
                return;

            int top = 0;
            if (_topMenu != null && _topMenu.Visible)
                top = _topMenu.Bottom;

            int width = ClientSize.Width;
            int height = ClientSize.Height - top;
            if (width < 0) width = 0;
            if (height < 0) height = 0;

            Rectangle desired = new Rectangle(0, top, width, height);
            if (_rootPanel.Bounds != desired)
                _rootPanel.Bounds = desired;

            if (_topMenu != null)
                _topMenu.BringToFront();
        }

        private void BuildTopMenu()
        {
            MenuStrip menu = new MenuStrip();
            _topMenu = menu;
            menu.Dock = DockStyle.Top;
            menu.BackColor = Color.White;
            menu.GripStyle = ToolStripGripStyle.Hidden;
            menu.Padding = new Padding(8, 2, 0, 2);

            ToolStripMenuItem fileMenu = new ToolStripMenuItem(L("Menu.File"));
            _menuPreviewItem = new ToolStripMenuItem(L("Menu.ScanPreview"));
            _menuPreviewItem.ShortcutKeys = Keys.F5;
            _menuPreviewItem.Click += delegate
            {
                if (_btnPreview != null && _btnPreview.Enabled && !_isExecuting)
                    ScanPreview();
            };

            _menuExecuteItem = new ToolStripMenuItem(L("Menu.Execute"));
            _menuExecuteItem.ShortcutKeys = Keys.Control | Keys.Enter;
            _menuExecuteItem.Enabled = false;
            _menuExecuteItem.Click += delegate
            {
                if (_btnExecute != null && _btnExecute.Enabled && !_isExecuting && !_isScanning)
                    HandlePrimaryAction();
            };

            _menuOpenSourceItem = new ToolStripMenuItem(L("Menu.OpenSource"));
            _menuOpenSourceItem.Click += delegate { OpenFolderFromTextBox(_txtSource, L("Main.SourceName")); };

            _menuOpenTargetItem = new ToolStripMenuItem(L("Menu.OpenTarget"));
            _menuOpenTargetItem.Click += delegate { OpenFolderFromTextBox(_txtRoot, L("Main.TargetName")); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem(L("Menu.Exit"));
            exitItem.Click += delegate { Close(); };

            fileMenu.DropDownItems.Add(_menuPreviewItem);
            fileMenu.DropDownItems.Add(_menuExecuteItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(_menuOpenSourceItem);
            fileMenu.DropDownItems.Add(_menuOpenTargetItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exitItem);

            ToolStripMenuItem settingsMenu = new ToolStripMenuItem(L("Menu.Settings"));
            ToolStripMenuItem archiveSettingsItem = new ToolStripMenuItem(L("Menu.ArchiveSettings"));
            archiveSettingsItem.Click += delegate { ShowArchiveSettingsDialog(); };

            ToolStripMenuItem fileTypesItem = new ToolStripMenuItem(L("Menu.FileTypeManager"));
            fileTypesItem.Click += delegate { ShowFileTypeDialog(); };

            ToolStripMenuItem tagCleaningItem = new ToolStripMenuItem(L("Menu.TagCleaningRules"));
            tagCleaningItem.Click += delegate { ShowTagCleaningRulesDialog(); };

            ToolStripMenuItem scanExclusionItem = new ToolStripMenuItem(L("Menu.ScanExclusionRules"));
            scanExclusionItem.Click += delegate { ShowScanExclusionRulesDialog(); };

            ToolStripMenuItem onlineAuthorItem = new ToolStripMenuItem(L("Menu.OnlineAuthorSettings"));
            onlineAuthorItem.Click += delegate { ShowOnlineAuthorSettingsDialog(); };

            ToolStripMenuItem languageMenu = new ToolStripMenuItem(L("Menu.Language"));

            foreach (LanguagePackInfo pack in _language.GetPacks())
            {
                LanguagePackInfo capturedPack = pack;
                ToolStripMenuItem languageItem =
                    new ToolStripMenuItem(
                        capturedPack.Name +
                        " (" +
                        capturedPack.Code +
                        ")");

                languageItem.Checked =
                    String.Equals(
                        capturedPack.Code,
                        _language.CurrentCode,
                        StringComparison.OrdinalIgnoreCase);

                languageItem.Click +=
                    delegate
                    {
                        if (String.Equals(
                                capturedPack.Code,
                                _language.CurrentCode,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        SavePersistentPaths();
                        _settingsStore.UpdateLanguage(
                            capturedPack.Code);

                        UiMessageBox.Show(
                            this,
                            L("Status.LanguageRestart"),
                            capturedPack.Name,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        Application.Restart();
                        Close();
                    };

                languageMenu.DropDownItems.Add(
                    languageItem);
            }

            ToolStripMenuItem checkLanguagesItem = new ToolStripMenuItem(L("Menu.CheckLanguagePacks"));
            checkLanguagesItem.Click += delegate { ShowLanguagePackCheck(); };

            ToolStripMenuItem languageFolderItem = new ToolStripMenuItem(L("Menu.OpenLanguageFolder"));
            languageFolderItem.Click += delegate
            {
                try
                {
                    Directory.CreateDirectory(_language.DirectoryPath);
                    Process.Start("explorer.exe", "\"" + _language.DirectoryPath + "\"");
                }
                catch (Exception ex)
                {
                    UiMessageBox.Show(this, ex.Message, L("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            if (languageMenu.DropDownItems.Count > 0)
                languageMenu.DropDownItems.Add(new ToolStripSeparator());
            languageMenu.DropDownItems.Add(checkLanguagesItem);
            languageMenu.DropDownItems.Add(languageFolderItem);

            settingsMenu.DropDownItems.Add(archiveSettingsItem);
            settingsMenu.DropDownItems.Add(fileTypesItem);
            settingsMenu.DropDownItems.Add(tagCleaningItem);
            settingsMenu.DropDownItems.Add(scanExclusionItem);
            settingsMenu.DropDownItems.Add(onlineAuthorItem);
            settingsMenu.DropDownItems.Add(new ToolStripSeparator());
            settingsMenu.DropDownItems.Add(languageMenu);

            // Keep the Settings hierarchy content-sized in every language.
            // ToolStrip normally auto-sizes, but make it explicit so a long
            // localized item can never inherit a stale/fixed drop-down width.
            settingsMenu.DropDown.AutoSize = true;
            languageMenu.DropDown.AutoSize = true;
            foreach (ToolStripItem item in settingsMenu.DropDownItems)
                item.AutoSize = true;
            foreach (ToolStripItem item in languageMenu.DropDownItems)
                item.AutoSize = true;

            ToolStripMenuItem toolsMenu = new ToolStripMenuItem(L("Menu.Tools"));
            ToolStripMenuItem historyItem = new ToolStripMenuItem(L("Nav.History"));
            historyItem.Click += delegate { ShowHistory(); };

            ToolStripMenuItem aliasItem = new ToolStripMenuItem(L("Menu.AliasLibrary"));
            aliasItem.Click += delegate { OpenAliasLibrary(); };

            ToolStripMenuItem blockItem = new ToolStripMenuItem(L("Menu.BlockList"));
            blockItem.Click += delegate { ShowBlockListDialog(); };

            ToolStripMenuItem recheckEverythingItem = new ToolStripMenuItem(L("Menu.RecheckEverything"));
            recheckEverythingItem.Click += delegate
            {
                UpdateInitialSearchLabel();
                if (_lblStatus != null)
                    _lblStatus.Text = L("Status.EverythingRechecked");
            };

            ToolStripMenuItem appFolderItem = new ToolStripMenuItem(L("Menu.OpenAppFolder"));
            appFolderItem.Click += delegate
            {
                try
                {
                    Process.Start("explorer.exe", "\"" + _appDir + "\"");
                }
                catch (Exception ex)
                {
                    UiMessageBox.Show(this, ex.Message, L("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            toolsMenu.DropDownItems.Add(historyItem);
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add(aliasItem);
            toolsMenu.DropDownItems.Add(blockItem);
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add(recheckEverythingItem);
            toolsMenu.DropDownItems.Add(appFolderItem);

            ToolStripMenuItem helpMenu = new ToolStripMenuItem(L("Menu.Help"));
            ToolStripMenuItem performanceItem = new ToolStripMenuItem(L("Menu.PerformanceDiagnostics"));
            performanceItem.Click += delegate { ShowScanPerformancePanel(); };
            ToolStripMenuItem readmeItem = new ToolStripMenuItem(L("Menu.Readme"));
            readmeItem.Click += delegate { OpenReadme(); };
            ToolStripMenuItem changelogItem = new ToolStripMenuItem(L("Menu.Changelog"));
            changelogItem.Click += delegate { OpenChangelog(); };
            _checkUpdatesItem = new ToolStripMenuItem(L("Menu.CheckUpdates"));
            _checkUpdatesItem.Click += delegate { CheckForUpdatesManually(); };
            ToolStripMenuItem aboutItem = new ToolStripMenuItem(L("Menu.About"));
            aboutItem.Click += delegate { ShowAboutDialog(); };
            ToolStripMenuItem supportItem = new ToolStripMenuItem(L("Menu.Support"));
            supportItem.Click += delegate { ShowSupportDialog(); };
            helpMenu.DropDownItems.Add(readmeItem);
            helpMenu.DropDownItems.Add(changelogItem);
            helpMenu.DropDownItems.Add(performanceItem);
            helpMenu.DropDownItems.Add(new ToolStripSeparator());
            helpMenu.DropDownItems.Add(_checkUpdatesItem);
            helpMenu.DropDownItems.Add(aboutItem);
            helpMenu.DropDownItems.Add(supportItem);

            menu.Items.Add(fileMenu);
            menu.Items.Add(settingsMenu);
            menu.Items.Add(toolsMenu);
            menu.Items.Add(helpMenu);

            UiStyle.StyleMenu(menu);

            MainMenuStrip = menu;
            Controls.Add(menu);
        }

        private GroupNumberingRuleItem[] GetGroupNumberingRuleItems()
        {
            return new GroupNumberingRuleItem[]
            {
                new GroupNumberingRuleItem
                {
                    Id = GroupNaming.RuleNumeric,
                    Display = L("GroupNumbering.Numeric")
                },
                new GroupNumberingRuleItem
                {
                    Id = GroupNaming.RuleNumeric2,
                    Display = L("GroupNumbering.Numeric2")
                },
                new GroupNumberingRuleItem
                {
                    Id = GroupNaming.RuleNumeric3,
                    Display = L("GroupNumbering.Numeric3")
                },
                new GroupNumberingRuleItem
                {
                    Id = GroupNaming.RuleAlphaUpper,
                    Display = L("GroupNumbering.AlphaUpper")
                },
                new GroupNumberingRuleItem
                {
                    Id = GroupNaming.RuleAlphaLower,
                    Display = L("GroupNumbering.AlphaLower")
                }
            };
        }

        private static string GetSelectedGroupRuleId(ComboBox combo)
        {
            GroupNumberingRuleItem item =
                combo != null
                    ? combo.SelectedItem as GroupNumberingRuleItem
                    : null;

            return item != null
                ? item.Id
                : GroupNaming.DefaultRuleId;
        }

        private void ShowLanguagePackCheck()
        {
            using (LanguagePackCheckForm dlg = new LanguagePackCheckForm(_language, Font))
            {
                dlg.ShowDialog(this);
            }
        }

        private void ShowArchiveSettingsDialog()
        {
            using (ArchiveSettingsForm dlg = new ArchiveSettingsForm(
                _language,
                Font,
                Decimal.ToInt32(_numMax.Value),
                _txtGroupTemplate.Text,
                _authorFolderTemplateSetting,
                _safetyReserveGb))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                _numMax.Value = dlg.MaxAuthors;
                _txtGroupTemplate.Text = dlg.GroupTemplate;
                _authorFolderTemplateSetting = dlg.AuthorFolderTemplate;
                _safetyReserveGb = dlg.SafetyReserveGb;
                _settingsStore.UpdateSafetyReserve(_safetyReserveGb);
                UpdateGroupTemplatePreview();
                SavePersistentPaths();
                RefreshExecutionSafetyUi();
                _engine.InvalidateTargetDirectoryCache();
                ScheduleScanWarmup(false);

                if (_lblStatus != null)
                {
                    _lblStatus.Text = LF(
                        "Status.ArchiveSettingsSaved",
                        dlg.MaxAuthors,
                        GroupNaming.Render(dlg.GroupTemplate, 1),
                        GroupNaming.Render(dlg.GroupTemplate, 2),
                        AuthorFolderNaming.Render(
                            dlg.AuthorFolderTemplate,
                            L("Dialog.ArchiveSettings.AuthorSample")));
                }
            }
        }

        private void ShowTagCleaningRulesDialog()
        {
            using (TagCleaningRulesForm dlg = new TagCleaningRulesForm(
                _tagCleaningStore,
                _language,
                Font))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                if (_lblStatus != null)
                    _lblStatus.Text = L("Status.TagCleaningRulesSaved");
                ScheduleScanWarmup(false);
            }
        }

        private void ShowScanExclusionRulesDialog()
        {
            using (ScanExclusionRulesForm dlg = new ScanExclusionRulesForm(
                _scanExclusionStore,
                _language,
                Font))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (_lblStatus != null)
                        _lblStatus.Text = L("Status.ScanExclusionRulesSaved");
                    ScheduleScanWarmup(false);
                }
            }
        }

        private void ShowLastScanExcludedItems()
        {
            if (_lastScanExcludedItems == null || _lastScanExcludedItems.Count == 0)
                return;

            using (ScanExcludedItemsForm dlg = new ScanExcludedItemsForm(
                _language,
                Font,
                _lastScanExcludedItems))
            {
                dlg.ShowDialog(this);
            }
        }

        private void UpdateScanExcludedLink()
        {
            // V1.11.6: scan exclusions are part of the main filter bar instead
            // of a separate status-bar link. Keep this method as a lightweight
            // compatibility call site for existing scan-completion paths.
            UpdateFilterCounts();
        }

        private void ShowOnlineAuthorSettingsDialog()
        {
            using (OnlineAuthorSettingsForm dlg = new OnlineAuthorSettingsForm(
                _language,
                Font,
                _onlineAuthorLookupEnabled,
                _onlineAuthorProvider,
                _saveOnlineAuthorCache,
                _maxOnlineLookupsPerScan,
                _authorEntityStore.Path))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                _onlineAuthorLookupEnabled = dlg.LookupEnabled;
                _onlineAuthorProvider = dlg.ProviderId;
                _saveOnlineAuthorCache = dlg.SaveCache;
                _maxOnlineLookupsPerScan = dlg.MaxLookupsPerScan;

                _settingsStore.UpdateOnlineAuthorSettings(
                    _onlineAuthorLookupEnabled,
                    _onlineAuthorProvider,
                    _saveOnlineAuthorCache,
                    _maxOnlineLookupsPerScan);

                if (_lblStatus != null)
                {
                    _lblStatus.Text = _onlineAuthorLookupEnabled
                        ? LF("Status.OnlineAuthorEnabled", _onlineAuthorProvider, _maxOnlineLookupsPerScan)
                        : L("Status.OnlineAuthorDisabled");
                }
            }
        }

        private void OpenFolderFromTextBox(TextBox box, string displayName)
        {
            if (box == null || !Directory.Exists(box.Text.Trim()))
            {
                UiMessageBox.Show(
                    this,
                    LF("Status.PathMissing", displayName),
                    L("Status.CannotOpen"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start("explorer.exe", "\"" + box.Text.Trim() + "\"");
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenReadme()
        {
            string content = EmbeddedResourceService.ReadText(EmbeddedResourceService.Readme);
            using (TextResourceForm dlg = new TextResourceForm(_language, Font, L("Menu.Readme"), content))
                dlg.ShowDialog(this);
        }

        private void OpenChangelog()
        {
            string content = EmbeddedResourceService.ReadText(EmbeddedResourceService.Changelog);
            using (TextResourceForm dlg = new TextResourceForm(_language, Font, L("Menu.Changelog"), content))
                dlg.ShowDialog(this);
        }

        private void ShowAboutDialog()
        {
            using (AboutForm dlg = new AboutForm(_language, Font, _appDir, CheckForUpdatesManually))
            {
                dlg.ShowDialog(this);
            }
        }

        private async void StartAutomaticUpdateCheck()
        {
            UserSettingsData settings = _settingsStore.Load();
            if (settings.LastUpdateCheckUtc.HasValue &&
                DateTime.UtcNow - settings.LastUpdateCheckUtc.Value < TimeSpan.FromHours(24))
                return;

            UpdateCheckResult result = await RunUpdateCheckAsync();
            if (result == null) return;
            _settingsStore.UpdateLastUpdateCheckUtc(DateTime.UtcNow);
            if (!result.HasUpdate || _availableUpdate != null) return;

            _availableUpdate = result;
            if (_checkUpdatesItem != null)
                _checkUpdatesItem.Text = String.Format(L("Menu.UpdateAvailable"), "V" + result.LatestVersion);
        }

        private async void CheckForUpdatesManually()
        {
            if (_updateCheckRunning) return;
            if (_availableUpdate != null)
            {
                ShowUpdateDialog(_availableUpdate);
                return;
            }

            if (_checkUpdatesItem != null)
            {
                _checkUpdatesItem.Enabled = false;
                _checkUpdatesItem.Text = L("Update.Checking");
            }

            UpdateCheckResult result = await RunUpdateCheckAsync();
            if (_checkUpdatesItem != null)
            {
                _checkUpdatesItem.Enabled = true;
                _checkUpdatesItem.Text = L("Menu.CheckUpdates");
            }
            if (result == null) return;

            _settingsStore.UpdateLastUpdateCheckUtc(DateTime.UtcNow);
            if (result.HasUpdate)
            {
                _availableUpdate = result;
                ShowUpdateDialog(result);
                return;
            }

            string message;
            if (result.Status == UpdateCheckStatus.UpToDate)
                message = String.Format(L("Update.UpToDate"), AppVersion.Display, "V" + result.LatestVersion);
            else if (result.Status == UpdateCheckStatus.DevelopmentVersion)
                message = String.Format(L("Update.DevelopmentVersion"), AppVersion.Display, "V" + result.LatestVersion);
            else if (result.Status == UpdateCheckStatus.NoRelease)
                message = L("Update.NoRelease");
            else if (result.Status == UpdateCheckStatus.Timeout)
                message = L("Update.Timeout");
            else if (result.Status == UpdateCheckStatus.InvalidVersion)
                message = L("Update.InvalidVersion");
            else
                message = L("Update.Unavailable");

            UiMessageBox.Show(this, message, L("Menu.CheckUpdates"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async System.Threading.Tasks.Task<UpdateCheckResult> RunUpdateCheckAsync()
        {
            if (_updateCheckRunning) return null;
            _updateCheckRunning = true;
            try { return await _updateService.CheckAsync(); }
            finally { _updateCheckRunning = false; }
        }

        private void ShowUpdateDialog(UpdateCheckResult result)
        {
            using (UpdateDialog dlg = new UpdateDialog(_language, Font, result))
                dlg.ShowDialog(this);
        }

        private void ShowSupportDialog()
        {
            using (SupportForm dlg = new SupportForm(_language, Font))
            {
                dlg.ShowDialog(this);
            }
        }

        private string GetCurrentSelectedPath()
        {
            if (_grid == null)
                return null;

            if (_grid.SelectedRows.Count > 0)
            {
                PlanItem item = GetGridItem(_grid.SelectedRows[0].Index);
                if (item != null)
                    return item.SourcePath;
            }

            return null;
        }

        private PlanItem GetSingleSelectedPlanItem()
        {
            if (_grid == null || _grid.SelectedRows.Count != 1)
                return null;

            return GetGridItem(_grid.SelectedRows[0].Index);
        }

        private void SetViewFilter(string filter)
        {
            string normalized = String.IsNullOrWhiteSpace(filter) ? "all" : filter;
            // “待确认” keeps its action-oriented behavior when there is work to do,
            // but an empty state must still be filterable just like every other tab.
            if (String.Equals(normalized, "attention", StringComparison.OrdinalIgnoreCase) &&
                CountUnresolvedItems() > 0)
            {
                OpenNeedsReview();
                return;
            }

            // “重复” is a normal status filter. Do not make its clickability depend
            // on the current count; “重复 0” must still open an empty duplicate view.
            // The bottom deferred-items link still calls OpenDeferredItems() when it
            // wants the action-oriented behavior of selecting the first result.
            _viewFilter = normalized;
            string keep = GetCurrentSelectedPath();
            UpdateViewFilterButtonStyles();
            ApplyCurrentGridFilter(keep);
            // Filtering does not change the plan. Restore the already evaluated
            // execution state without synchronously probing every file again.
            UpdateMainActionAvailability();
        }

        private void OpenNeedsReview()
        {
            if (_plan == null || CountUnresolvedItems() <= 0)
                return;

            _viewFilter = "attention";
            if (_txtListSearch != null && _txtListSearch.TextLength > 0)
                _txtListSearch.Text = "";

            UpdateViewFilterButtonStyles();
            ApplyCurrentGridFilter(null);

            DataGridViewRow firstVisible = GetFirstVisibleGridRow();
            if (_grid != null && firstVisible != null)
            {
                _grid.ClearSelection();
                DataGridViewRow first = firstVisible;
                first.Selected = true;
                _grid.CurrentCell = first.Cells["FileName"];
                try { _grid.FirstDisplayedScrollingRowIndex = first.Index; } catch { }
            }

            if (_workspaceSplit != null && _workspaceSplit.Panel2Collapsed)
            {
                _workspaceSplit.Panel2Collapsed = false;
                RestoreDetailPanelWidth();
                UpdateDetailsToggleButton();
            }

            UpdateDetailsFromSelection();
            if (_grid != null)
                _grid.Focus();

            UpdateMainActionAvailability();
        }

        private void OpenDeferredItems()
        {
            if (_plan == null || CountDeferredItems() <= 0)
                return;

            _viewFilter = "duplicate";
            if (_txtListSearch != null && _txtListSearch.TextLength > 0)
                _txtListSearch.Text = "";

            UpdateViewFilterButtonStyles();
            ApplyCurrentGridFilter(null);

            DataGridViewRow firstVisible = GetFirstVisibleGridRow();
            if (_grid != null && firstVisible != null)
            {
                _grid.ClearSelection();
                DataGridViewRow first = firstVisible;
                first.Selected = true;
                _grid.CurrentCell = first.Cells["FileName"];
                try { _grid.FirstDisplayedScrollingRowIndex = first.Index; } catch { }
            }

            UpdateDetailsFromSelection();
            if (_grid != null)
                _grid.Focus();

            UpdateMainActionAvailability();
        }

        private static bool IsDeferredItem(PlanItem item)
        {
            if (item == null || item.IsExcludedPreview || item.CanMove)
                return false;

            string status = item.Status ?? "";
            return status == "目标已有同名文件，跳过" ||
                status == "文件已在目标作者文件夹";
        }

        private int CountDeferredItems()
        {
            if (_plan == null)
                return 0;

            return _plan.Count(delegate(PlanItem p) { return IsDeferredItem(p); });
        }

        private bool MatchesViewFilter(PlanItem item)
        {
            if (item == null)
                return false;

            if (item.IsExcludedPreview)
            {
                return String.Equals(_viewFilter, "all", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(_viewFilter, "excluded", StringComparison.OrdinalIgnoreCase);
            }

            if (IsDeferredItem(item))
            {
                return String.Equals(_viewFilter, "all", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(_viewFilter, "duplicate", StringComparison.OrdinalIgnoreCase);
            }

            RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
            string mark = visual != null ? visual.Mark : "";

            if (String.Equals(_viewFilter, "excluded", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(_viewFilter, "duplicate", StringComparison.OrdinalIgnoreCase))
                return false;
            if (String.Equals(_viewFilter, "attention", StringComparison.OrdinalIgnoreCase))
                return mark == "[!]" || mark == "[×]";
            if (String.Equals(_viewFilter, "new", StringComparison.OrdinalIgnoreCase))
                return mark == "[+]";
            if (String.Equals(_viewFilter, "ambiguous", StringComparison.OrdinalIgnoreCase))
                return mark == "[!]";
            if (String.Equals(_viewFilter, "unrecognized", StringComparison.OrdinalIgnoreCase))
                return mark == "[×]";
            if (String.Equals(_viewFilter, "matched", StringComparison.OrdinalIgnoreCase))
            {
                return mark == "[✓]" ||
                    mark == "[≈]" ||
                    mark == "[↔]" ||
                    mark == "[ID]" ||
                    mark == "[◆]" ||
                    mark == "[●]" ||
                    mark == "[★]" ||
                    (item.CanMove && mark != "[+]");
            }

            return true;
        }

        private bool MatchesSearchText(PlanItem item, string search)
        {
            if (item == null)
                return false;

            if (search.Length == 0)
                return true;

            string[] terms = Regex.Split(search, @"\s+")
                .Where(delegate(string term) { return !String.IsNullOrWhiteSpace(term); })
                .ToArray();

            foreach (string term in terms)
            {
                bool matched = ContainsIgnoreCase(item.FileName, term) ||
                    ContainsIgnoreCase(item.Author, term) ||
                    ContainsIgnoreCase(item.MatchedAs, term) ||
                    ContainsIgnoreCase(GetDisplayReasonText(item), term) ||
                    ContainsIgnoreCase(item.TargetDir, term) ||
                    ContainsIgnoreCase(LocalizePlanStatus(item.Status), term) ||
                    ContainsIgnoreCase(item.ExclusionRuleName, term) ||
                    ContainsIgnoreCase(item.ExclusionScope, term);
                if (!matched)
                    return false;
            }
            return true;
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return (text ?? "").IndexOf(
                value ?? "",
                StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void UpdateViewFilterButtonStyles()
        {
            foreach (KeyValuePair<string, Button> pair in _filterButtons)
                ApplyFilterButtonStyle(pair.Value, pair.Key);
        }

        private void ApplyFilterButtonStyle(Button button, string filter)
        {
            if (button == null)
                return;

            bool selected = String.Equals(
                _viewFilter,
                filter,
                StringComparison.OrdinalIgnoreCase);

            button.BackColor = selected
                ? Color.FromArgb(235, 242, 252)
                : Color.White;
            button.ForeColor = selected
                ? Color.FromArgb(28, 83, 148)
                : Color.FromArgb(55, 57, 61);
            if (_filterRegularFont == null)
                _filterRegularFont = new Font(Font, FontStyle.Regular);
            if (_filterBoldFont == null)
                _filterBoldFont = new Font(Font, FontStyle.Bold);
            button.Font = selected ? _filterBoldFont : _filterRegularFont;
        }

        private void UpdateFilterCounts()
        {
            int excluded = _lastScanExcludedItems != null
                ? _lastScanExcludedItems.Count
                : 0;
            int total = (_plan != null ? _plan.Count : 0) + excluded;
            int matched = 0;
            int newAuthor = 0;
            int ambiguous = 0;
            int unrecognized = 0;
            int duplicate = 0;

            if (_plan != null)
            {
                foreach (PlanItem item in _plan)
                {
                    if (IsDeferredItem(item))
                    {
                        duplicate++;
                        continue;
                    }

                    RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
                    string mark = visual != null ? visual.Mark : "";
                    if (mark == "[+]") newAuthor++;
                    else if (mark == "[!]") ambiguous++;
                    else if (mark == "[×]") unrecognized++;
                    else if (mark == "[✓]" || mark == "[≈]" || mark == "[↔]" || mark == "[ID]" ||
                             mark == "[◆]" || mark == "[●]" || mark == "[★]" || item.CanMove)
                        matched++;
                }
            }

            SetFilterButtonText("all", L("Filter.All"), total);
            int needsReview = ambiguous + unrecognized;
            SetFilterButtonText(
                "attention",
                (needsReview > 0 ? "⚠ " : "") + L("Nav.Review"),
                needsReview);
            SetFilterButtonText("matched", L("Filter.Matched"), matched);
            SetFilterButtonText("new", L("Filter.NewAuthor"), newAuthor);
            SetFilterButtonText("ambiguous", L("Filter.Ambiguous"), ambiguous);
            SetFilterButtonText("unrecognized", L("Filter.Unrecognized"), unrecognized);
            SetFilterButtonText("duplicate", L("Filter.Duplicate"), duplicate);
            SetFilterButtonText("excluded", L("Filter.Excluded"), excluded);
            UpdatePrimaryActionPresentation();
        }

        private void SetFilterButtonText(string key, string title, int count)
        {
            Button button;
            if (_filterButtons.TryGetValue(key, out button) && button != null)
                button.Text = title + "  " + count.ToString();
        }

        private void SetMainStatusLinks(string text, int reviewCount, int deferredCount)
        {
            if (_lblStatus == null)
                return;

            _settingStatusReviewLink = true;
            try
            {
                _lblStatus.Links.Clear();
                _lblStatus.Text = text ?? "";

                if (reviewCount > 0)
                {
                    string linkText = LF("Status.NeedsReviewLinkText", reviewCount);
                    int start = _lblStatus.Text.IndexOf(
                        linkText,
                        StringComparison.CurrentCulture);
                    if (start >= 0)
                        _lblStatus.Links.Add(start, linkText.Length, "review");
                }

                if (deferredCount > 0)
                {
                    string linkText = LF("Status.DeferredLinkText", deferredCount);
                    int start = _lblStatus.Text.IndexOf(
                        linkText,
                        StringComparison.CurrentCulture);
                    if (start >= 0)
                        _lblStatus.Links.Add(start, linkText.Length, "deferred");
                }
            }
            finally
            {
                _settingStatusReviewLink = false;
            }
        }

        private IEnumerable<PlanItem> GetPreviewItems()
        {
            if (_plan != null)
            {
                foreach (PlanItem item in _plan)
                {
                    if (item != null)
                        yield return item;
                }
            }

            if (_lastScanExcludedItems == null)
                yield break;

            foreach (ScanExcludedItem excluded in _lastScanExcludedItems)
            {
                PlanItem preview = CreateExcludedPreviewItem(excluded);
                if (preview != null)
                    yield return preview;
            }
        }

        private PlanItem CreateExcludedPreviewItem(ScanExcludedItem excluded)
        {
            if (excluded == null || String.IsNullOrWhiteSpace(excluded.Path))
                return null;

            PlanItem item = new PlanItem();
            item.IsExcludedPreview = true;
            item.ExclusionRuleName = excluded.RuleName ?? "";
            item.ExclusionScope = excluded.Scope ?? "";
            item.ExclusionIsDirectory = excluded.IsDirectory;
            item.SourcePath = excluded.Path ?? "";
            item.FileName = !String.IsNullOrWhiteSpace(excluded.DisplayName)
                ? excluded.DisplayName
                : Path.GetFileName(excluded.Path ?? "");
            item.Status = "扫描规则排除";
            item.CanMove = false;

            try
            {
                if (excluded.IsDirectory && Directory.Exists(excluded.Path))
                {
                    DirectoryInfo directory = new DirectoryInfo(excluded.Path);
                    item.LastWriteTime = directory.LastWriteTime;
                }
                else if (File.Exists(excluded.Path))
                {
                    FileInfo file = new FileInfo(excluded.Path);
                    item.LastWriteTime = file.LastWriteTime;
                    item.FileSize = file.Length;
                }
            }
            catch { }

            return item;
        }

        private string GetDisplayReasonText(PlanItem item)
        {
            if (item == null)
                return "";

            if (item.IsExcludedPreview)
            {
                string scope = LocalizeExclusionScope(item.ExclusionScope);
                return LF(
                    item.ExclusionIsDirectory
                        ? "Reason.ScanExcludedFolder"
                        : "Reason.ScanExcluded",
                    item.ExclusionRuleName,
                    scope);
            }

            return LocalizeMatchWhy(item.MatchWhy);
        }

        private string LocalizeExclusionScope(string scope)
        {
            if (String.Equals(scope, ScanExclusionScope.FolderName, StringComparison.OrdinalIgnoreCase))
                return L("Dialog.ScanExclusion.ScopeFolder");
            return L("Dialog.ScanExclusion.ScopeFile");
        }

        private void SetDetailBlocksVisible(bool visible)
        {
            if (_detailFileBlock != null) _detailFileBlock.Visible = visible;
            if (_detailStatusBlock != null) _detailStatusBlock.Visible = visible;
            if (_detailAuthorBlock != null) _detailAuthorBlock.Visible = visible;
            if (_detailMatchedBlock != null) _detailMatchedBlock.Visible = visible;
            if (_detailProcessBlock != null) _detailProcessBlock.Visible = visible;
            if (_detailTargetBlock != null) _detailTargetBlock.Visible = visible;
            if (_detailResultBlock != null) _detailResultBlock.Visible = visible;
        }

        private void ConfigureDetailActions(
            bool primaryVisible,
            string primaryText,
            bool primaryEnabled,
            bool primaryEmphasis,
            bool secondaryVisible,
            string secondaryText,
            bool secondaryEnabled)
        {
            if (_detailActions == null ||
                _detailAssignButton == null ||
                _detailExcludeButton == null)
                return;

            _detailActions.SuspendLayout();
            try
            {
                // Hide both first so changing a column span never creates a
                // transient overlap with a still-visible neighboring control.
                _detailAssignButton.Visible = false;
                _detailExcludeButton.Visible = false;

                // Primary action always starts in the left cell. When it is the
                // only action (for example an excluded row), let it use the full
                // width. The secondary-only multi-selection action stays in the
                // right cell; avoiding cell moves keeps TableLayout behavior
                // stable across DPI/layout recalculations.
                _detailActions.SetColumnSpan(
                    _detailAssignButton,
                    primaryVisible && !secondaryVisible ? 2 : 1);
                _detailActions.SetColumnSpan(_detailExcludeButton, 1);

                _detailAssignButton.Visible = primaryVisible;
                _detailAssignButton.Text = primaryText ?? L("Details.AssignAuthor");
                _detailAssignButton.Enabled = primaryEnabled;
                _detailAssignButton.Margin = primaryVisible && !secondaryVisible
                    ? new Padding(0, 4, 0, 0)
                    : new Padding(0, 4, 4, 0);
                UiStyle.StyleButton(_detailAssignButton, primaryEmphasis, false);

                _detailExcludeButton.Visible = secondaryVisible;
                _detailExcludeButton.Text = secondaryText ?? L("Details.Exclude");
                _detailExcludeButton.Enabled = secondaryEnabled;
                _detailExcludeButton.Margin = new Padding(4, 4, 0, 0);
                UiStyle.StyleButton(_detailExcludeButton, false, false);
            }
            finally
            {
                _detailActions.ResumeLayout(true);
            }
        }

        private string GetDetailTargetDirectory(PlanItem item)
        {
            if (item == null)
                return "";

            string targetDir = item.TargetDir ?? "";
            if (String.IsNullOrWhiteSpace(targetDir) &&
                !String.IsNullOrWhiteSpace(item.TargetPath))
            {
                try { targetDir = Path.GetDirectoryName(item.TargetPath) ?? ""; }
                catch { targetDir = ""; }
            }

            if (String.IsNullOrWhiteSpace(targetDir))
                return "";

            string root = _currentRoot;
            if (String.IsNullOrWhiteSpace(root) && _txtRoot != null)
                root = (_txtRoot.Text ?? "").Trim();

            try
            {
                string fullTarget = Path.GetFullPath(targetDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullRoot = String.IsNullOrWhiteSpace(root)
                    ? ""
                    : Path.GetFullPath(root)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (fullRoot.Length > 0)
                {
                    if (String.Equals(fullTarget, fullRoot, StringComparison.OrdinalIgnoreCase))
                        return ".";

                    string prefix = fullRoot + Path.DirectorySeparatorChar;
                    if (fullTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return fullTarget.Substring(prefix.Length);
                }

                return new DirectoryInfo(fullTarget).Name;
            }
            catch
            {
                return targetDir;
            }
        }

        private string GetDetailCandidateSummary(PlanItem item)
        {
            if (item == null)
                return "";

            List<string> names = new List<string>();
            for (int i = 0; i < item.CandidateNames.Count; i++)
            {
                string name = (item.CandidateNames[i] ?? "").Trim();
                if (name.Length > 0 &&
                    !names.Contains(name, StringComparer.CurrentCultureIgnoreCase))
                {
                    names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                for (int i = 0; i < item.CandidatePaths.Count; i++)
                {
                    string path = item.CandidatePaths[i] ?? "";
                    if (path.Length == 0)
                        continue;
                    try
                    {
                        string name = new DirectoryInfo(path).Name;
                        if (name.Length > 0 &&
                            !names.Contains(name, StringComparer.CurrentCultureIgnoreCase))
                            names.Add(name);
                    }
                    catch { }
                }
            }

            if (names.Count == 0)
                return "";

            int show = Math.Min(3, names.Count);
            string text = String.Join(" / ", names.Take(show).ToArray());
            if (names.Count > show)
                text += " …";
            return LF("Details.Candidates", text);
        }

        private void UpdateDetailsFromSelection()
        {
            if (_detailEmpty == null || _grid == null)
                return;

            int count = _grid.SelectedRows.Count;
            PlanItem item = GetSingleSelectedPlanItem();

            if (item == null)
            {
                SetDetailBlocksVisible(false);
                _detailEmpty.Visible = true;
                _detailEmpty.Text = count > 1
                    ? LF("Details.Multiple", count)
                    : L("Details.Empty");

                bool hasExcludableSelection = false;
                foreach (DataGridViewRow selectedRow in _grid.SelectedRows)
                {
                    PlanItem selectedItem = GetGridItem(selectedRow.Index);
                    if (selectedItem != null && !selectedItem.IsExcludedPreview)
                    {
                        hasExcludableSelection = true;
                        break;
                    }
                }

                // Keep both action slots visible even when no single item is
                // selected. Only enabled state changes, so the bottom action bar
                // never shifts horizontally or vertically.
                ConfigureDetailActions(
                    true,
                    L("Details.AssignAuthor"),
                    false,
                    false,
                    true,
                    L("Details.Exclude"),
                    count > 1 && !_isExecuting && !_isScanning && hasExcludableSelection);
                return;
            }

            SetDetailBlocksVisible(true);
            _detailEmpty.Visible = false;

            RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
            string visualCode = visual != null
                ? (visual.Code ?? "").ToLowerInvariant()
                : "";

            bool isExcluded = item.IsExcludedPreview;
            bool isAmbiguous = !isExcluded && visualCode == "ambiguous";
            bool isUnrecognized = !isExcluded && visualCode == "unrecognized";
            bool isNew = !isExcluded &&
                (visualCode == "new" || visualCode == "new-reuse");
            bool isManual = !isExcluded && visualCode == "manual";
            bool targetExists = !isExcluded &&
                String.Equals(
                    item.Status ?? "",
                    "目标已有同名文件，跳过",
                    StringComparison.Ordinal);
            bool alreadyThere = !isExcluded &&
                String.Equals(
                    item.Status ?? "",
                    "文件已在目标作者文件夹",
                    StringComparison.Ordinal);

            // Permanent information skeleton:
            // File name -> Status -> Recognized author -> Matched author ->
            // Processing method -> Target -> Result.
            _detailFileName.Text = DetailValueOrDash(item.FileName);
            _detailStatus.Text = DetailValueOrDash(GetGridStatusText(item, visual));
            _detailStatus.ForeColor = GetGridStatusColor(item, visual);
            _detailAuthor.Text = DetailValueOrDash(isExcluded ? "" : item.Author);

            string matchedText = item.MatchedAs ?? "";
            if (isExcluded || isUnrecognized || isNew)
                matchedText = "";
            else if (isAmbiguous)
                matchedText = L("Details.MultipleCandidates");
            _detailMatched.Text = DetailValueOrDash(matchedText);

            string processText;
            string targetText = "";
            string resultText;
            Color resultColor = UiStyle.Text;
            string primaryText = L("Details.AssignOtherAuthor");
            bool primaryEnabled = !_isExecuting && !_isScanning &&
                !String.IsNullOrWhiteSpace(item.Author);
            bool primaryEmphasis = false;
            string secondaryText = L("Details.Exclude");
            bool secondaryEnabled = !_isExecuting && !_isScanning;

            if (isExcluded)
            {
                processText = L("Details.Process.Excluded");
                resultText = String.IsNullOrWhiteSpace(item.ExclusionRuleName)
                    ? L("Details.Result.Excluded")
                    : LF("Details.Result.ExcludedRule", item.ExclusionRuleName);
                resultColor = Color.FromArgb(100, 116, 139);
                primaryText = L("Details.ManageExclusionRules");
                primaryEnabled = !_isExecuting && !_isScanning;
                secondaryText = L("Details.AlreadyExcluded");
                secondaryEnabled = false;
            }
            else if (isUnrecognized)
            {
                processText = L("Details.Process.Unrecognized");
                resultText = L("Details.Result.Unrecognized");
                resultColor = Color.FromArgb(185, 28, 28);
                primaryText = L("Details.AssignAuthor");
                primaryEnabled = !_isExecuting && !_isScanning;
                primaryEmphasis = true;
            }
            else if (isAmbiguous)
            {
                processText = L("Details.Process.Ambiguous");
                resultText = L("Details.Result.Ambiguous");
                resultColor = Color.FromArgb(194, 65, 12);
                primaryText = L("Details.ChooseAuthor");
                primaryEnabled = !_isExecuting &&
                    !_isScanning &&
                    !String.IsNullOrWhiteSpace(item.Author);
                primaryEmphasis = true;
            }
            else if (targetExists)
            {
                processText = L("Details.Process.TargetExists");
                targetText = GetDetailTargetDirectory(item);
                resultText = L("Details.Result.TargetExists");
                resultColor = Color.FromArgb(194, 65, 12);
            }
            else if (alreadyThere)
            {
                processText = L("Details.Process.AlreadyThere");
                targetText = GetDetailTargetDirectory(item);
                resultText = L("Details.Result.AlreadyThere");
                resultColor = Color.FromArgb(100, 116, 139);
            }
            else if (isNew)
            {
                processText = visualCode == "new-reuse"
                    ? L("Details.Process.NewReuse")
                    : L("Details.Process.NewFolder");
                targetText = GetDetailTargetDirectory(item);
                resultText = visualCode == "new-reuse"
                    ? L("Details.Result.Ready")
                    : L("Details.Result.NewFolder");
                resultColor = Color.FromArgb(21, 128, 61);
            }
            else
            {
                processText = isManual
                    ? L("Details.Process.Manual")
                    : L("Details.Process.Existing");
                targetText = GetDetailTargetDirectory(item);
                resultText = L("Details.Result.Ready");
                resultColor = Color.FromArgb(21, 128, 61);
            }

            _detailProcess.Text = DetailValueOrDash(processText);
            _detailTargetPath.Text = DetailValueOrDash(targetText);
            _detailResult.Text = DetailValueOrDash(resultText);
            _detailResult.ForeColor = resultColor;

            if (_detailsToolTip != null)
            {
                _detailsToolTip.SetToolTip(
                    _detailFileName,
                    item.SourcePath ?? "");
                string fullTarget = !String.IsNullOrWhiteSpace(item.TargetPath)
                    ? item.TargetPath
                    : item.TargetDir ?? "";
                _detailsToolTip.SetToolTip(
                    _detailTargetPath,
                    fullTarget);
            }

            ConfigureDetailActions(
                true,
                primaryText,
                primaryEnabled,
                primaryEmphasis,
                true,
                secondaryText,
                secondaryEnabled);
        }

        private string GetGridStatusText(PlanItem item, RecognitionVisual visual)
        {
            if (item == null)
                return "";

            string rawStatus = item.Status ?? "";

            if (item.IsExcludedPreview)
                return L("GridStatus.Excluded");

            if (rawStatus == "文件已在目标作者文件夹")
                return L("GridStatus.AlreadyThere");
            if (rawStatus == "目标已有同名文件，跳过")
                return L("GridStatus.TargetExists");

            if (String.Equals(rawStatus, L("Status.Moved"), StringComparison.CurrentCulture))
                return L("Status.Moved");
            if (String.Equals(rawStatus, L("Status.SourceMissingDuringMove"), StringComparison.CurrentCulture))
                return L("Status.SourceMissingDuringMove");
            if (String.Equals(rawStatus, L("Status.TargetExistsDuringMove"), StringComparison.CurrentCulture))
                return L("Status.TargetExistsDuringMove");
            if (rawStatus.StartsWith(L("Status.FailedPrefix").Replace("{0}", ""), StringComparison.CurrentCulture))
                return rawStatus;

            string recognition = GetRecognitionVisualName(visual);
            if (!String.IsNullOrWhiteSpace(recognition))
                return recognition;

            return LocalizePlanStatus(rawStatus);
        }

        private Color GetGridStatusColor(PlanItem item, RecognitionVisual visual)
        {
            if (item == null)
                return Color.DimGray;

            string rawStatus = item.Status ?? "";

            if (item.IsExcludedPreview)
                return Color.FromArgb(100, 116, 139);

            if (rawStatus == "文件已在目标作者文件夹")
                return Color.FromArgb(107, 114, 128);
            if (rawStatus == "目标已有同名文件，跳过")
                return Color.FromArgb(234, 88, 12);
            if (String.Equals(rawStatus, L("Status.Moved"), StringComparison.CurrentCulture))
                return Color.FromArgb(22, 163, 74);
            if (String.Equals(rawStatus, L("Status.SourceMissingDuringMove"), StringComparison.CurrentCulture) ||
                rawStatus.StartsWith(L("Status.FailedPrefix").Replace("{0}", ""), StringComparison.CurrentCulture))
                return Color.FromArgb(220, 38, 38);
            if (String.Equals(rawStatus, L("Status.TargetExistsDuringMove"), StringComparison.CurrentCulture))
                return Color.FromArgb(234, 88, 12);

            return visual != null ? visual.Color : Color.DimGray;
        }

        private string GetGridStatusTooltip(PlanItem item, RecognitionVisual visual)
        {
            if (item == null)
                return "";

            string compact = GetGridStatusText(item, visual);
            string fullStatus = LocalizePlanStatus(item.Status);
            string reason = GetDisplayReasonText(item);

            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(compact))
                parts.Add(compact);
            if (!String.IsNullOrWhiteSpace(fullStatus) &&
                !String.Equals(fullStatus, compact, StringComparison.CurrentCulture))
                parts.Add(fullStatus);
            if (!String.IsNullOrWhiteSpace(reason))
                parts.Add(reason);

            return String.Join("\r\n", parts.ToArray());
        }

        private string FormatFileSize(long bytes)
        {
            double value = bytes;
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }

            if (unit == 0)
                return ((long)value).ToString() + " " + units[unit];

            return value.ToString("0.##") + " " + units[unit];
        }

        private string GetFileSizeText(PlanItem item)
        {
            if (item != null && item.FileSize >= 0)
                return FormatFileSize(item.FileSize);

            try
            {
                if (item != null && File.Exists(item.SourcePath))
                {
                    item.FileSize = new FileInfo(item.SourcePath).Length;
                    return FormatFileSize(item.FileSize);
                }
            }
            catch { }
            return "";
        }

        private void BuildRecognitionLegend()
        {
            FlowLayoutPanel panel =
                new FlowLayoutPanel();

            panel.Location =
                new Point(15, 247);
            panel.Size =
                new Size(1345, 24);
            panel.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Left |
                AnchorStyles.Right;
            panel.WrapContents = false;
            panel.AutoScroll = false;
            panel.Margin = new Padding(0);
            panel.Padding = new Padding(0);

            foreach (
                RecognitionLegendItem item
                in RecognitionVisualResolver.GetLegendItems())
            {
                Label mark =
                    new Label();

                mark.Text = item.Mark;
                mark.AutoSize = true;
                mark.ForeColor = item.Color;
                mark.Font =
                    new Font(
                        "Segoe UI Symbol",
                        9F,
                        FontStyle.Bold);
                mark.Margin =
                    new Padding(0, 2, 2, 0);

                Label name =
                    new Label();

                name.Text = GetRecognitionNameByMark(item.Mark);
                name.AutoSize = true;
                name.ForeColor = Color.Black;
                name.Margin =
                    new Padding(0, 2, 12, 0);

                panel.Controls.Add(mark);
                panel.Controls.Add(name);
            }

            Controls.Add(panel);
        }

        private void BuildExecutionProgress()
        {
            _progressGroup = new Panel();
            _progressGroup.Dock = DockStyle.Bottom;
            _progressGroup.Height = 68;
            _progressGroup.BackColor = Color.White;
            _progressGroup.Padding = new Padding(12, 8, 12, 5);
            _progressPanel = _progressGroup;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 21F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _progressGroup.Controls.Add(layout);

            _progressBar = new ProgressBar();
            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Margin = new Padding(0, 0, 0, 2);
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Value = 0;
            _progressBar.Style = ProgressBarStyle.Continuous;
            layout.Controls.Add(_progressBar, 0, 0);

            _lblProgressFile = new Label();
            _lblProgressFile.Dock = DockStyle.Fill;
            _lblProgressFile.Margin = new Padding(0, 3, 0, 0);
            _lblProgressFile.AutoEllipsis = true;
            _lblProgressFile.Text = L("Main.Waiting");
            layout.Controls.Add(_lblProgressFile, 0, 1);

            _lblProgressPath = new Label();
            _lblProgressPath.Dock = DockStyle.Fill;
            _lblProgressPath.Margin = new Padding(0, 0, 0, 0);
            _lblProgressPath.AutoEllipsis = true;
            _lblProgressPath.ForeColor = UiStyle.Muted;
            _lblProgressPath.Text = "";
            layout.Controls.Add(_lblProgressPath, 0, 2);

            _progressGroup.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.DrawLine(Pens.Gainsboro, 0, 0, _progressGroup.Width, 0);
            };
        }

        private void ResetExecutionProgress()
        {
            _progressBar.Value = 0;
            _lblProgressFile.Text = L("Main.Waiting");
            _lblProgressPath.Text = "";
        }

        private void ResetScanProgress()
        {
            if (_progressBar != null)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.MarqueeAnimationSpeed = 30;
                _progressBar.Value = 0;
            }
            if (_lblProgressFile != null)
                _lblProgressFile.Text = L("Status.ScanPreparing");
            if (_lblProgressPath != null)
                _lblProgressPath.Text = "";
        }

        private void SetScanUiState(
            bool scanning)
        {
            _isScanning = scanning;

            if (MainMenuStrip != null)
                MainMenuStrip.Enabled = !scanning;

            if (_txtSource != null) _txtSource.Enabled = !scanning;
            if (_txtRoot != null) _txtRoot.Enabled = !scanning;
            if (_btnBrowseSource != null) _btnBrowseSource.Enabled = !scanning;
            if (_btnBrowseTarget != null) _btnBrowseTarget.Enabled = !scanning;
            if (_numMax != null) _numMax.Enabled = !scanning;
            if (_numScanLimit != null) _numScanLimit.Enabled = !scanning;
            if (_chkRecursive != null) _chkRecursive.Enabled = !scanning;
            if (_cmbScanMode != null) _cmbScanMode.Enabled = !scanning;
            if (_txtGroupTemplate != null) _txtGroupTemplate.Enabled = !scanning;

            if (_btnPreview != null)
                _btnPreview.Text = scanning ? L("Main.StopScan") : L("Main.ScanPreview");
            if (_menuPreviewItem != null)
                _menuPreviewItem.Text = scanning ? L("Menu.StopScan") : L("Menu.ScanPreview");

            if (_btnExecute != null && scanning)
                _btnExecute.Enabled = false;

            if (_progressGroup != null)
                _progressGroup.Enabled = true;
            if (_workspaceSplit != null)
                _workspaceSplit.Enabled = true;
            if (_filterBar != null)
                _filterBar.Enabled = true;
            if (_btnToggleDetails != null)
                _btnToggleDetails.Enabled = true;

            UseWaitCursor = false;
            Cursor = Cursors.Default;

            UpdateDetailsFromSelection();
            UpdateMainActionAvailability();
        }

        private void RequestScanCancellation()
        {
            if (!_isScanning || _scanWorker == null)
                return;

            _scanCancelRequested = true;

            if (_scanWorker.WorkerSupportsCancellation &&
                !_scanWorker.CancellationPending)
            {
                _scanWorker.CancelAsync();
            }

            if (_btnPreview != null)
            {
                _btnPreview.Text = L("Main.StoppingScan");
                _btnPreview.Enabled = false;
            }
            if (_menuPreviewItem != null)
            {
                _menuPreviewItem.Text = L("Menu.StoppingScan");
                _menuPreviewItem.Enabled = false;
            }
            if (_lblProgressFile != null)
                _lblProgressFile.Text = L("Status.ScanCancelRequested");
            if (_lblStatus != null)
                _lblStatus.Text = L("Status.ScanCancelRequested");
        }

        private void UpdateScanProgress(
            ScanProgressInfo info)
        {
            if (info == null || _progressBar == null)
                return;

            if (info.Indeterminate || info.Total <= 0)
            {
                if (_progressBar.Style != ProgressBarStyle.Marquee)
                {
                    _progressBar.Style = ProgressBarStyle.Marquee;
                    _progressBar.MarqueeAnimationSpeed = 30;
                }
            }
            else
            {
                if (_progressBar.Style != ProgressBarStyle.Continuous)
                {
                    _progressBar.Style = ProgressBarStyle.Continuous;
                    _progressBar.MarqueeAnimationSpeed = 0;
                }

                int percentage =
                    info.Total > 0
                        ? (int)Math.Round(info.Current * 100.0 / info.Total)
                        : 0;
                percentage = Math.Max(0, Math.Min(100, percentage));
                _progressBar.Value = percentage;
            }

            string text;
            if (info.Stage == ScanProgressStage.Searching)
            {
                text = info.Total > 0 && !info.Indeterminate
                    ? LF("Status.ScanSearchingKnown", info.Current, info.Total)
                    : LF("Status.ScanSearchingUnknown", info.Current);
            }
            else if (info.Stage == ScanProgressStage.Filtering)
            {
                text = LF("Status.ScanEarlyStop", info.Current, info.Total, info.Checked);
            }
            else if (info.Stage == ScanProgressStage.OnlineResolving)
            {
                text = LF("Status.ScanOnlineResolving", info.Current, info.Total);
            }
            else if (info.Stage == ScanProgressStage.Rebuilding)
            {
                text = LF("Status.ScanRebuilding", info.Current, info.Total);
            }
            else
            {
                text = LF("Status.ScanPlanning", info.Current, info.Total);
            }

            _lblProgressFile.Text = text;
            _lblProgressPath.Text = info.CurrentPath ?? "";
        }

        private void SetExecutionUiLocked(
            bool locked)
        {
            _isExecuting = locked;
            UseWaitCursor = locked;

            if (MainMenuStrip != null)
                MainMenuStrip.Enabled = !locked;
            if (_headerPanel != null)
                _headerPanel.Enabled = !locked;
            if (_filterBar != null)
                _filterBar.Enabled = !locked;
            if (_workspaceSplit != null)
                _workspaceSplit.Enabled = !locked;
            if (_btnToggleDetails != null)
                _btnToggleDetails.Enabled = !locked;

            if (_progressGroup != null)
                _progressGroup.Enabled = true;
            if (_lblStatus != null)
                _lblStatus.Enabled = true;

            if (locked && _btnExecute != null)
                _btnExecute.Enabled = false;

            UpdateMainActionAvailability();
        }

        private void UpdateExecutionProgress(
            int percentage,
            MoveProgressInfo info)
        {
            if (percentage < 0)
                percentage = 0;
            if (percentage > 100)
                percentage = 100;

            _progressBar.Value =
                percentage;

            if (info == null)
                return;

            _lblProgressFile.Text =
                info.Phase +
                "  " +
                info.Current.ToString() +
                "/" +
                info.Total.ToString() +
                "：" +
                info.FileName;

            string spaceProgress = "";
            if (info.TotalBytes > 0)
            {
                spaceProgress =
                    "  |  " +
                    LF(
                        "Status.ProgressBytes",
                        FormatBytes(info.ProcessedBytes),
                        FormatBytes(info.TotalBytes));

                if (info.TargetFreeBytes >= 0)
                {
                    spaceProgress +=
                        "  |  " +
                        LF(
                            "Status.ProgressFree",
                            FormatBytes(info.TargetFreeBytes));
                }
            }

            _lblProgressPath.Text =
                L("Main.TargetPrefix") +
                " " +
                info.TargetPath +
                spaceProgress;
        }

        private void BuildGrid()
        {
            _grid = new FastDataGridView();
            if (_gridStatusBoldFont == null)
                _gridStatusBoldFont = new Font(Font, FontStyle.Bold);
            if (_gridStatusLinkFont == null)
                _gridStatusLinkFont = new Font(Font, FontStyle.Bold | FontStyle.Underline);
            _grid.Dock = DockStyle.Fill;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.None;
            _grid.GridColor = Color.FromArgb(218, 221, 226);
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _grid.EnableHeadersVisualStyles = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _grid.RowTemplate.Height = 29;
            _grid.RowTemplate.Resizable = DataGridViewTriState.False;
            _grid.ColumnHeadersHeight = 30;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ScrollBars = ScrollBars.Both;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.BackColor = Color.White;
            _grid.DefaultCellStyle.ForeColor = Color.FromArgb(34, 36, 40);
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 241, 255);
            _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 57, 61);
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 249, 250);
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(55, 57, 61);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Regular);
            _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            _grid.VirtualMode = true;

            AddGridColumn("FileName", L("Grid.FileName"), 320);
            AddGridColumn("Author", L("Grid.Author"), 155);
            AddGridColumn("MatchedAs", L("Grid.MatchedAs"), 155);
            AddGridColumn("Status", L("Grid.Status"), 125);
            AddGridColumn("TargetDir", L("Grid.TargetDir"), 300);
            AddGridColumn("FileSize", L("Grid.Size"), 92);
            AddGridColumn("Modified", L("Grid.Modified"), 135);
            _grid.Columns["Status"].AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
            _grid.Columns["FileSize"].AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;
            _grid.Columns["Modified"].AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells;

            GridInteraction.Apply(_grid, "Main.Workspace", _language, true);
            foreach (DataGridViewColumn column in _grid.Columns)
                column.Resizable = DataGridViewTriState.True;

            _gridContextMenu = new ContextMenuStrip();
            _gridContextMenu.ShowImageMargin = false;
            _openFileLocationMenuItem = new ToolStripMenuItem(L("Context.OpenFileLocation"));
            _openFileLocationMenuItem.Click += delegate { OpenSelectedFileLocation(); };
            _copyFullPathMenuItem = new ToolStripMenuItem(L("Context.CopyFullPath"));
            _copyFullPathMenuItem.Click += delegate { CopySelectedFullPaths(); };
            _authorActionMenuItem = new ToolStripMenuItem(L("Details.AssignOtherAuthor"));
            _authorActionMenuItem.Click += delegate { HandleDetailPrimaryAction(); };
            _manageExclusionMenuItem = new ToolStripMenuItem(L("Details.ManageExclusionRules"));
            _manageExclusionMenuItem.Click += delegate { ShowScanExclusionRulesDialog(); };
            _gridActionSeparator = new ToolStripSeparator();
            _blockMenuItem = new ToolStripMenuItem(L("Status.BlockThis"));
            _blockMenuItem.Click += delegate { BlockSelectedArchives(); };
            _gridContextMenu.Items.Add(_openFileLocationMenuItem);
            _gridContextMenu.Items.Add(_copyFullPathMenuItem);
            _gridContextMenu.Items.Add(new ToolStripSeparator());
            _gridContextMenu.Items.Add(_authorActionMenuItem);
            _gridContextMenu.Items.Add(_manageExclusionMenuItem);
            _gridContextMenu.Items.Add(_gridActionSeparator);
            _gridContextMenu.Items.Add(_blockMenuItem);
            UiStyle.StyleMenu(_gridContextMenu);

            _grid.CellDoubleClick += GridCellDoubleClick;
            _grid.CellClick += GridCellClick;
            _grid.CellMouseMove += GridCellMouseMove;
            _grid.CellMouseLeave += GridCellMouseLeave;
            _grid.CellMouseDown += GridCellMouseDown;
            _grid.CellValueNeeded += GridCellValueNeeded;
            _grid.CellFormatting += GridCellFormatting;
            _grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
            _grid.KeyDown += GridKeyDown;
            _grid.SelectionChanged += delegate
            {
                if (!_applyingGridFilter)
                    UpdateDetailsFromSelection();
            };
            _grid.Scroll += delegate { CloseOverlay(true); };
            _grid.ColumnWidthChanged += delegate { CloseOverlay(true); };

            _workspaceSplit.Panel1.Controls.Add(_grid);
        }

        private void AddGridColumn(string name, string header, int width)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.Name = name; col.HeaderText = T(header); col.Width = width;
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

            if (String.Equals(name, "Status", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "FileSize", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "Modified", StringComparison.OrdinalIgnoreCase))
            {
                UiStyle.ConfigureShortColumn(col, name == "Status" ? 92 : 82);
            }
            else
            {
                float weight = 20F;
                int minimum = 120;
                if (String.Equals(name, "FileName", StringComparison.OrdinalIgnoreCase)) { weight = 34F; minimum = 220; }
                else if (String.Equals(name, "TargetDir", StringComparison.OrdinalIgnoreCase)) { weight = 30F; minimum = 220; }
                else if (String.Equals(name, "Author", StringComparison.OrdinalIgnoreCase) ||
                         String.Equals(name, "MatchedAs", StringComparison.OrdinalIgnoreCase)) { weight = 18F; minimum = 120; }
                UiStyle.ConfigureFillColumn(col, weight, minimum);
            }
            _grid.Columns.Add(col);
        }

        private Label NewLabel(string text, int x, int y)
        { Label l = new Label(); l.Text = T(text); l.Location = new Point(x, y); l.AutoSize = true; return l; }
        private Button NewButton(string text, int x, int y, int w, int h)
        { Button b = new Button(); b.Text = T(text); b.Location = new Point(x, y); b.Size = new Size(w, h); return b; }

        private void UpdateGroupTemplatePreview()
        {
            if (_txtGroupTemplate == null ||
                _lblGroupTemplatePreview == null)
            {
                return;
            }

            string normalized;
            string error;

            if (!GroupNaming.TryValidateTemplate(
                    _txtGroupTemplate.Text,
                    out normalized,
                    out error))
            {
                _lblGroupTemplatePreview.Text =
                    T(error);
                _lblGroupTemplatePreview.ForeColor =
                    Color.Firebrick;
                return;
            }

            _lblGroupTemplatePreview.Text =
                L("Dialog.PreviewPrefix") +
                GroupNaming.Render(
                    normalized,
                    1) +
                " / " +
                GroupNaming.Render(
                    normalized,
                    2);

            _lblGroupTemplatePreview.ForeColor =
                Color.DimGray;
        }

        private void RefreshActiveScanExtensions()
        {
            FileTypeProfile profile = GetActiveFileTypeProfile();
            _scanExtensions = profile != null
                ? FileTypeRules.NormalizeExtensions(profile.Extensions)
                : FileTypeRules.GetCompressionDefaults();

            UpdateMainActionAvailability();
            ScheduleScanWarmup(false);
        }

        private void InitializeScanWarmup()
        {
            _warmupDebounceTimer = new System.Windows.Forms.Timer();
            _warmupDebounceTimer.Interval = 650;
            _warmupDebounceTimer.Tick += delegate
            {
                _warmupDebounceTimer.Stop();
                StartScanWarmup();
            };
            // The form handle does not exist while the constructor is still
            // running. BeginInvoke at that point throws and prevents startup.
            // Start the first warmup only after WinForms has shown the form.
            Shown += delegate { ScheduleScanWarmup(false); };
        }

        private void HandlePerformanceSettingsChanged()
        {
            _scanWarmup.Cancel();
            if (ScanPerformanceDiagnostics.WarmupEnabled)
                ScheduleScanWarmup(false);
            UpdateInitialSearchLabel();
        }

        private void ShowScanPerformancePanel()
        {
            if (_scanPerformanceForm == null || _scanPerformanceForm.IsDisposed)
            {
                _scanPerformanceForm = new ScanPerformanceForm(
                    _language,
                    Path.Combine(_appDir, AppFiles.ScanPerformanceLog),
                    delegate(bool diagnostics, bool warmup, bool everything)
                    { _settingsStore.UpdatePerformanceSettings(diagnostics, warmup, everything); });
                _scanPerformanceForm.Shown += delegate { CenterScanPerformancePanel(); };
                _scanPerformanceForm.FormClosed += delegate { _scanPerformanceForm = null; };
                _scanPerformanceForm.Show(this);
            }
            else
            {
                _scanPerformanceForm.Show();
                _scanPerformanceForm.Activate();
            }
        }

        private void CenterScanPerformancePanel()
        {
            if (_scanPerformanceForm == null || _scanPerformanceForm.IsDisposed) return;
            // Run after Show/AutoScale so Width and Height are the real DPI-scaled values.
            try
            {
                _scanPerformanceForm.BeginInvoke(new MethodInvoker(delegate
                {
                    if (_scanPerformanceForm == null || _scanPerformanceForm.IsDisposed) return;
                    Rectangle area = Screen.FromControl(this).WorkingArea;
                    int x = Left + (Width - _scanPerformanceForm.Width) / 2;
                    int y = Top + (Height - _scanPerformanceForm.Height) / 2;
                    x = Math.Max(area.Left, Math.Min(x, area.Right - _scanPerformanceForm.Width));
                    y = Math.Max(area.Top, Math.Min(y, area.Bottom - _scanPerformanceForm.Height));
                    _scanPerformanceForm.Location = new Point(x, y);
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void ScheduleScanWarmup(bool debounce)
        {
            if (_warmupDebounceTimer == null || IsDisposed || Disposing ||
                !IsHandleCreated || _isExecuting)
                return;
            _warmupDebounceTimer.Stop();
            if (debounce)
                _warmupDebounceTimer.Start();
            else
            {
                try { BeginInvoke(new MethodInvoker(StartScanWarmup)); }
                catch (InvalidOperationException) { }
            }
        }

        private void StartScanWarmup()
        {
            if (!ScanPerformanceDiagnostics.WarmupEnabled || _isExecuting || _isScanning ||
                _txtSource == null || _txtRoot == null)
                return;
            ScanWarmupRequest request = BuildWarmupRequest(
                _txtSource.Text.Trim(), _txtRoot.Text.Trim(),
                _chkRecursive != null && _chkRecursive.Checked,
                _scanExtensions);
            if (!Directory.Exists(request.Source) || !Directory.Exists(request.Target))
                return;
            _scanWarmup.Start(request);
        }

        private ScanWarmupRequest BuildWarmupRequest(
            string source, string target, bool recursive, IEnumerable<string> extensions)
        {
            ScanWarmupRequest request = new ScanWarmupRequest();
            request.Source = source;
            request.Target = target;
            request.Recursive = recursive;
            request.Extensions = extensions != null ? new List<string>(extensions) : new List<string>();
            request.BlockedPaths = _blockList.LoadSet();
            request.GroupTemplate = _txtGroupTemplate != null ? _txtGroupTemplate.Text : GroupNaming.DefaultTemplate;
            request.RecognizedGroupTemplates = new List<string>(_recognizedGroupTemplates);
            request.AuthorFolderTemplate = _authorFolderTemplateSetting;
            request.RecognizedAuthorFolderTemplates = new List<string>(_recognizedAuthorFolderTemplates);
            request.Mode = GetSelectedScanMode();
            request.RequestedLimit = _numScanLimit != null ? Decimal.ToInt32(_numScanLimit.Value) : 0;
            request.MaxAuthors = _numMax != null ? Decimal.ToInt32(_numMax.Value) : 20;
            request.SourceVersion = GetCacheFileVersion(AppFiles.ExclusionList) + "|" +
                GetCacheFileVersion(AppFiles.ScanExclusionRules) + "|" +
                GetCacheFileVersion(AppFiles.FileTypeProfiles) + "|Everything=" +
                ScanPerformanceDiagnostics.EverythingEnabled + "|Alias=" +
                GetCacheFileVersion(AppFiles.AuthorAliases) + "|Entity=" +
                GetCacheFileVersion(AppFiles.AuthorEntities) + "|Tag=" +
                GetCacheFileVersion(AppFiles.TagCleaningRules);
            return request;
        }

        private string GetCacheFileVersion(string name)
        {
            try
            {
                string path = Path.Combine(_appDir, name);
                if (!File.Exists(path)) return "0";
                byte[] data = File.ReadAllBytes(path);
                // Stable FNV-1a content fingerprint. Saving an unchanged JSON/INI
                // file must not invalidate a prepared scan snapshot merely because
                // its filesystem timestamp changed.
                UInt64 hash = 14695981039346656037UL;
                for (int i = 0; i < data.Length; i++)
                {
                    hash ^= data[i];
                    hash *= 1099511628211UL;
                }
                return data.Length.ToString() + ":" + hash.ToString("X16");
            }
            catch { return "0"; }
        }

        private FileTypeProfile GetActiveFileTypeProfile()
        {
            return _fileTypeProfileStore.GetCurrentProfile(
                _fileTypeConfig);
        }

        private string GetActiveFileTypeProfileName()
        {
            FileTypeProfile profile = GetActiveFileTypeProfile();
            return profile != null
                ? GetFileTypeProfileDisplayName(profile)
                : L("FileProfile.Archive");
        }

        private void ShowFileTypeDialog()
        {
            using (FileTypeProfileManagerForm dlg = new FileTypeProfileManagerForm(
                _fileTypeProfileStore,
                _fileTypeConfig,
                _language,
                Font))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ResultConfig == null)
                    return;

                try
                {
                    _fileTypeConfig = _fileTypeProfileStore.CloneConfig(dlg.ResultConfig);
                    _fileTypeProfileStore.Save(_fileTypeConfig);
                    RefreshActiveScanExtensions();
                    SavePersistentPaths();

                    FileTypeProfile active = GetActiveFileTypeProfile();
                    if (_lblStatus != null && active != null)
                    {
                        _lblStatus.Text = LF(
                            "Status.FileTypeManagerChanged",
                            GetFileTypeProfileDisplayName(active));
                    }
                }
                catch (Exception ex)
                {
                    UiMessageBox.Show(
                        this,
                        LF("Dialog.FileType.SaveError", ex.Message),
                        L("Dialog.FileType.CannotSave"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void BrowseFolder(
            TextBox target,
            string description)
        {
            using (
                FolderBrowserDialog dlg =
                    new FolderBrowserDialog())
            {
                dlg.Description = description;

                if (Directory.Exists(
                        target.Text.Trim()))
                {
                    dlg.SelectedPath =
                        target.Text.Trim();
                }

                if (dlg.ShowDialog(this) ==
                    DialogResult.OK)
                {
                    target.Text =
                        dlg.SelectedPath;

                    // 用户通过浏览器选中的有效路径立即记忆。
                    SavePersistentPaths();
                }
            }
        }

        private void LoadPersistentPaths()
        {
            UserSettingsData data =
                _settingsStore.Load();

            if (!String.IsNullOrWhiteSpace(
                    data.SourcePath))
            {
                _txtSource.Text =
                    data.SourcePath;
            }

            if (!String.IsNullOrWhiteSpace(
                    data.AuthorRoot))
            {
                _txtRoot.Text =
                    data.AuthorRoot;
            }

            _safetyReserveGb = data.SafetyReserveGb;
            _onlineAuthorLookupEnabled = data.OnlineAuthorLookupEnabled;
            _onlineAuthorProvider = data.OnlineAuthorProvider;
            _saveOnlineAuthorCache = data.SaveOnlineAuthorCache;
            _maxOnlineLookupsPerScan = data.MaxOnlineLookupsPerScan;

            if (data.MaxAuthorsPerGroup >= 1 &&
                data.MaxAuthorsPerGroup <= 999)
            {
                _numMax.Value =
                    data.MaxAuthorsPerGroup;
            }

            _txtGroupTemplate.Text =
                data.GroupTemplate;

            _currentGroupTemplate =
                data.GroupTemplate;

            _recognizedGroupTemplates =
                GroupNaming.NormalizeTemplates(
                    data.GroupTemplate,
                    data.GroupTemplateHistory);

            _authorFolderTemplateSetting =
                data.AuthorFolderTemplate;

            _currentAuthorFolderTemplate =
                data.AuthorFolderTemplate;

            _recognizedAuthorFolderTemplates =
                AuthorFolderNaming.NormalizeTemplates(
                    data.AuthorFolderTemplate,
                    data.AuthorFolderTemplateHistory);

            _fileTypeConfig = _fileTypeProfileStore.LoadOrCreate(data);

            RefreshActiveScanExtensions();
            UpdateGroupTemplatePreview();
        }

        private void SavePersistentPaths()
        {
            UserSettingsData data =
                _settingsStore.UpdateSettings(
                    _txtSource.Text.Trim(),
                    _txtRoot.Text.Trim(),
                    _txtGroupTemplate.Text.Trim(),
                    Decimal.ToInt32(_numMax.Value),
                    _authorFolderTemplateSetting);

            _recognizedGroupTemplates =
                GroupNaming.NormalizeTemplates(
                    data.GroupTemplate,
                    data.GroupTemplateHistory);

            _authorFolderTemplateSetting =
                data.AuthorFolderTemplate;

            _recognizedAuthorFolderTemplates =
                AuthorFolderNaming.NormalizeTemplates(
                    data.AuthorFolderTemplate,
                    data.AuthorFolderTemplateHistory);

            try
            {
                _fileTypeProfileStore.Save(_fileTypeConfig);
            }
            catch
            {
            }

            RefreshActiveScanExtensions();
        }

        private void UpdateInitialSearchLabel()
        {
            if (!ScanPerformanceDiagnostics.EverythingEnabled)
                SetEverythingStatus(L("Status.EverythingDisabled"), Color.DarkOrange,
                    L("Status.EverythingEnableInDiagnostics"), "diagnostics");
            else if (_everything.IsEverythingRunning())
                SetEverythingStatus(L("Status.EverythingAvailable"), Color.DarkGreen, "", "");
            else
                SetEverythingStatus(L("Status.EverythingNotRunning"), Color.DarkOrange,
                    L("Status.EverythingDownloadRecommended"), "download");
        }

        private void SetEverythingStatus(string text, Color color, string linkText, string action)
        {
            if (_lblSearch == null) return;
            _lblSearch.Links.Clear();
            _lblSearch.Text = text ?? "";
            if (!String.IsNullOrWhiteSpace(linkText))
            {
                string suffix = " [" + linkText + "]";
                int start = _lblSearch.Text.Length + 2;
                _lblSearch.Text += suffix;
                _lblSearch.Links.Add(start, linkText.Length, action);
            }
            _lblSearch.ForeColor = color;
            _lblSearch.LinkColor = color;
        }

        private void EverythingStatusLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string action = e != null && e.Link != null ? e.Link.LinkData as string : "";
            if (String.Equals(action, "download", StringComparison.OrdinalIgnoreCase))
            {
                try { Process.Start("https://www.voidtools.com/"); }
                catch (Exception ex) { UiMessageBox.Show(this, ex.Message, L("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            else if (String.Equals(action, "diagnostics", StringComparison.OrdinalIgnoreCase))
                ShowScanPerformancePanel();
        }

        private ScanModeKind GetSelectedScanMode()
        {
            if (_cmbScanMode == null)
                return ScanModeKind.Global;

            switch (_cmbScanMode.SelectedIndex)
            {
                case 1:
                    return ScanModeKind.Exact;
                case 2:
                    return ScanModeKind.NewAuthor;
                case 3:
                    return ScanModeKind.Ambiguous;
                case 4:
                    return ScanModeKind.Unrecognized;
                default:
                    return ScanModeKind.Global;
            }
        }

        private List<PlanItem> GetModeMatchedPlan(
            List<FileInfo> searchedFiles,
            string root,
            int maxAuthors,
            ScanModeKind mode,
            int requestedLimit,
            out int classifiedCount,
            out int matchedCount)
        {
            classifiedCount = searchedFiles.Count;

            List<PlanItem> classifiedPlan =
                _engine.BuildPlan(
                    searchedFiles,
                    root,
                    maxAuthors,
                    _currentGroupTemplate,
                    _recognizedGroupTemplates,
                    _currentAuthorFolderTemplate,
                    _recognizedAuthorFolderTemplates);

            if (mode == ScanModeKind.Global)
            {
                matchedCount = classifiedPlan.Count;
                return classifiedPlan;
            }

            // Classification is decided against the complete candidate set.
            // The filtered list must keep that decision; otherwise rebuilding
            // only the filtered files can remove the very context that made an
            // item ambiguous (or change a new-author relationship).
            List<PlanItem> matchedPlan =
                classifiedPlan.Where(
                    delegate(PlanItem p)
                    {
                        return ScanModeRules.Matches(p, mode);
                    })
                .ToList();

            matchedCount = matchedPlan.Count;

            if (requestedLimit > 0 &&
                matchedPlan.Count > requestedLimit)
            {
                matchedPlan = matchedPlan.Take(requestedLimit).ToList();
            }

            return matchedPlan;
        }

        private static void PreserveFilteredClassification(
            PlanItem rebuilt,
            PlanItem classified)
        {
            if (rebuilt == null || classified == null)
                return;

            rebuilt.Author = classified.Author ?? "";
            rebuilt.MatchedAs = classified.MatchedAs ?? "";
            rebuilt.MatchWhy = classified.MatchWhy ?? "";

            rebuilt.CandidatePaths.Clear();
            rebuilt.CandidateNames.Clear();
            rebuilt.CandidateIsPlanned.Clear();
            foreach (string path in classified.CandidatePaths)
                rebuilt.CandidatePaths.Add(path);
            foreach (string name in classified.CandidateNames)
                rebuilt.CandidateNames.Add(name);
            foreach (bool isPlanned in classified.CandidateIsPlanned)
                rebuilt.CandidateIsPlanned.Add(isPlanned);

            RecognitionVisual originalVisual =
                RecognitionVisualResolver.Resolve(classified);
            string code = originalVisual != null
                ? (originalVisual.Code ?? "").ToLowerInvariant()
                : "";

            if (code == "ambiguous" || code == "unrecognized")
            {
                rebuilt.Status = classified.Status ?? "";
                rebuilt.CanMove = false;
                rebuilt.TargetDir = "";
                rebuilt.TargetPath = "";
                return;
            }

            // New/new-reuse is also a classification result. Preserve the
            // label while retaining the rebuilt target path and move-safety
            // checks from the selected-only planning pass.
            if ((code == "new" || code == "new-reuse") &&
                rebuilt.CanMove)
            {
                rebuilt.Status = classified.Status ?? rebuilt.Status;
            }
        }

        private void ScanPreview()
        {
            if (_isScanning)
            {
                RequestScanCancellation();
                return;
            }

            if (_isExecuting)
                return;

            CloseOverlay(true);
            string source =
                _txtSource.Text.Trim();
            string root =
                _txtRoot.Text.Trim();

            if (!Directory.Exists(source))
            {
                UiMessageBox.Show(
                    this,
                    LF("Status.SourceMissing", source),
                    L("Status.ScanFailed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (!Directory.Exists(root))
            {
                UiMessageBox.Show(
                    this,
                    LF("Status.TargetMissing", root),
                    L("Status.ScanFailed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string normalizedGroupTemplate;
            string groupTemplateError;

            if (!GroupNaming.TryValidateTemplate(
                    _txtGroupTemplate.Text,
                    out normalizedGroupTemplate,
                    out groupTemplateError))
            {
                UiMessageBox.Show(
                    this,
                    T(groupTemplateError),
                    L("Dialog.InvalidGroupTemplate"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _txtGroupTemplate.Focus();
                return;
            }

            if (_scanExtensions == null ||
                _scanExtensions.Count == 0)
            {
                UiMessageBox.Show(
                    this,
                    L("Status.NoExtensions"),
                    L("Status.ScanFailed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Persist the exact settings that define this scan before the
            // background worker starts. While scanning, those inputs are locked
            // so the UI can remain responsive without changing the scan context.
            SavePersistentPaths();

            _currentGroupTemplate =
                normalizedGroupTemplate;

            _recognizedGroupTemplates =
                GroupNaming.NormalizeTemplates(
                    _currentGroupTemplate,
                    _recognizedGroupTemplates);

            _currentAuthorFolderTemplate =
                _authorFolderTemplateSetting;

            _recognizedAuthorFolderTemplates =
                AuthorFolderNaming.NormalizeTemplates(
                    _currentAuthorFolderTemplate,
                    _recognizedAuthorFolderTemplates);

            _currentRoot = root;
            _currentSourceRoot = source;
            _currentMaxAuthors =
                Decimal.ToInt32(
                    _numMax.Value);
            _currentScanMode =
                GetSelectedScanMode();

            ScanRequest request =
                new ScanRequest();
            request.Source = source;
            request.Root = root;
            request.MaxAuthors = _currentMaxAuthors;
            request.RequestedLimit =
                Decimal.ToInt32(
                    _numScanLimit.Value);
            request.Recursive =
                _chkRecursive.Checked;
            request.Mode =
                _currentScanMode;
            request.GroupTemplate =
                _currentGroupTemplate;
            request.RecognizedGroupTemplates =
                new List<string>(
                    _recognizedGroupTemplates);
            request.AuthorFolderTemplate =
                _currentAuthorFolderTemplate;
            request.RecognizedAuthorFolderTemplates =
                new List<string>(
                    _recognizedAuthorFolderTemplates);
            request.Extensions =
                new List<string>(
                    _scanExtensions);
            request.OnlineLookupEnabled = _onlineAuthorLookupEnabled;
            request.OnlineProvider = _onlineAuthorProvider;
            request.SaveOnlineCache = _saveOnlineAuthorCache;
            request.MaxOnlineLookups = _maxOnlineLookupsPerScan;
            request.WarmupRequest = BuildWarmupRequest(
                source, root, request.Recursive, request.Extensions);

            _scanCancelRequested = false;
            _scanResponseTimer = Stopwatch.StartNew();
            ResetScanProgress();
            SetScanUiState(true);
            _lblStatus.Text = L("Status.ScanRunning");

            BackgroundWorker worker =
                new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            _scanWorker = worker;

            worker.DoWork +=
                delegate(
                    object sender,
                    DoWorkEventArgs e)
                {
                    BackgroundWorker bg =
                        (BackgroundWorker)sender;

                    try
                    {
                        e.Result =
                            RunScanInBackground(
                                request,
                                bg);
                    }
                    catch (OperationCanceledException)
                    {
                        e.Cancel = true;
                    }
                };

            worker.ProgressChanged +=
                delegate(
                    object sender,
                    ProgressChangedEventArgs e)
                {
                    BackgroundWorker bg =
                        (BackgroundWorker)sender;
                    if (bg.CancellationPending)
                        return;

                    UpdateScanProgress(
                        e.UserState as ScanProgressInfo);
                };

            worker.RunWorkerCompleted +=
                delegate(
                    object sender,
                    RunWorkerCompletedEventArgs e)
                {
                    bool canceled =
                        e.Cancelled || _scanCancelRequested;
                    _scanCancelRequested = false;
                    _scanWorker = null;
                    SetScanUiState(false);

                    if (_progressBar != null)
                    {
                        _progressBar.Style = ProgressBarStyle.Continuous;
                        _progressBar.MarqueeAnimationSpeed = 0;
                    }

                    if (canceled)
                    {
                        if (_scanResponseTimer != null) { _scanResponseTimer.Stop(); _scanResponseTimer = null; }
                        if (_progressBar != null)
                            _progressBar.Value = 0;
                        _lblProgressFile.Text =
                            L("Status.ScanCanceled");
                        _lblProgressPath.Text = "";
                        _lblStatus.Text =
                            L("Status.ScanCanceledKeepPreview");
                        RefreshExecutionSafetyUi();
                        UpdateScanExcludedLink();
                        return;
                    }

                    if (e.Error != null)
                    {
                        if (_scanResponseTimer != null) { _scanResponseTimer.Stop(); _scanResponseTimer = null; }
                        if (_progressBar != null)
                            _progressBar.Value = 0;
                        _lblProgressFile.Text =
                            L("Status.ScanFailed");
                        _lblProgressPath.Text =
                            e.Error.Message;
                        _lblStatus.Text =
                            LF("Status.ScanFailedDetail", e.Error.Message);

                        RefreshExecutionSafetyUi();
                        UpdateScanExcludedLink();

                        UiMessageBox.Show(
                            this,
                            e.Error.Message,
                            L("Status.ScanFailed"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    ScanRunResult result =
                        e.Result as ScanRunResult;
                    if (result == null)
                    {
                        if (_scanResponseTimer != null) { _scanResponseTimer.Stop(); _scanResponseTimer = null; }
                        _lblStatus.Text =
                            L("Status.ScanFailed");
                        RefreshExecutionSafetyUi();
                        UpdateScanExcludedLink();
                        return;
                    }

                    _plan = result.Plan ?? new List<PlanItem>();
                    _lastScanExcludedItems =
                        result.Search != null && result.Search.ExcludedItems != null
                            ? new List<ScanExcludedItem>(result.Search.ExcludedItems)
                            : new List<ScanExcludedItem>();
                    UpdateScanExcludedLink();

                    _lblProgressFile.Text =
                        L("Status.ScanUpdatingPreview");
                    _lblProgressPath.Text = "";
                    if (_progressBar != null)
                        _progressBar.Value = 100;

                    GridRenderMetrics gridMetrics = RenderGrid(null);
                    if (_scanResponseTimer != null)
                    {
                        _scanResponseTimer.Stop();
                        result.ElapsedMilliseconds = _scanResponseTimer.ElapsedMilliseconds;
                        _scanResponseTimer = null;
                    }

                    if (result.WarmupMetrics != null && ScanPerformanceDiagnostics.Enabled)
                    {
                        ScanPerformanceDiagnostics.Record(new ScanPerformanceEntry
                        {
                            Time = DateTime.Now,
                            WarmupEnabled = ScanPerformanceDiagnostics.WarmupEnabled,
                            EverythingEnabled = ScanPerformanceDiagnostics.EverythingEnabled,
                            Provider = result.WarmupMetrics.Provider,
                            WarmupHit = result.WarmupMetrics.WarmupHit,
                            ReadyBeforeRequest = result.WarmupMetrics.ReadyBeforeRequest,
                            SnapshotHit = result.WarmupMetrics.SnapshotHit,
                            WarmupWaitMs = result.WarmupMetrics.WarmupWaitMs,
                            SnapshotPrepareMs = result.WarmupMetrics.SnapshotPrepareMs,
                            FileDiscoveryMs = result.WarmupMetrics.FileDiscoveryMs,
                            TargetIndexMs = result.WarmupMetrics.TargetIndexMs,
                            CandidatePrepareMs = result.CandidatePrepareMs,
                            AuthorMatchMs = result.RecognitionPlanMs,
                            SortMs = gridMetrics.SortMs,
                            RowBuildMs = gridMetrics.RowBuildMs,
                            AddRowsMs = gridMetrics.AddRowsMs,
                            LayoutMs = gridMetrics.LayoutMs,
                            FinalizeMs = gridMetrics.FinalizeMs,
                            UiApplyMs = gridMetrics.TotalMs,
                            TotalResponseMs = result.ElapsedMilliseconds,
                            CandidateCount = result.CandidateCount,
                            ResultCount = result.Plan.Count
                        });
                    }

                    int movable =
                        _plan.Count(
                            delegate(PlanItem p)
                            {
                                return p.CanMove;
                            });

                    int needsReview = CountUnresolvedItems();
                    int deferred = CountDeferredItems();

                    RefreshExecutionSafetyUi();

                    if (result.Search != null &&
                        result.Search.Backend == "Everything SDK")
                    {
                        SetEverythingStatus(L("Status.EverythingBackend"), Color.DarkGreen, "", "");
                    }
                    else if (!ScanPerformanceDiagnostics.EverythingEnabled)
                        SetEverythingStatus(L("Status.EverythingDisabled"), Color.DarkOrange,
                            L("Status.EverythingEnableInDiagnostics"), "diagnostics");
                    else
                        SetEverythingStatus(L("Status.EverythingNotRunning"), Color.DarkOrange,
                            L("Status.EverythingDownloadRecommended"), "download");

                    int excludedCount = _lastScanExcludedItems != null
                        ? _lastScanExcludedItems.Count
                        : 0;

                    string statusText = deferred > 0
                        ? LF(
                            "Status.ScanCompleteHumanWithDeferred",
                            movable,
                            needsReview,
                            deferred,
                            excludedCount)
                        : LF(
                            "Status.ScanCompleteHuman",
                            movable,
                            needsReview,
                            excludedCount);

                    statusText +=
                        "  " +
                        LF(
                            "Status.ScanElapsed",
                            result.ElapsedMilliseconds);

                    if (request.OnlineLookupEnabled && result.OnlineStats != null)
                    {
                        statusText +=
                            "  " +
                            LF(
                                "Status.OnlineAuthorSummary",
                                result.OnlineStats.LocalRecognized,
                                result.OnlineStats.PendingOnline,
                                result.OnlineStats.OnlineResolved,
                                result.OnlineStats.StillUnresolved);
                    }

                    SetMainStatusLinks(statusText, needsReview, deferred);

                    _lblProgressFile.Text =
                        LF(
                            "Status.ScanProgressComplete",
                            _plan.Count);
                    _lblProgressPath.Text = "";
                    UpdateMainActionAvailability();
                };

            worker.RunWorkerAsync();
        }

        private ScanRunResult RunScanInBackground(
            ScanRequest request,
            BackgroundWorker worker)
        {
            Stopwatch timer =
                Stopwatch.StartNew();

            Func<bool> cancelRequested =
                delegate
                {
                    return worker.CancellationPending;
                };

            Action<ScanProgressInfo> reportProgress =
                delegate(ScanProgressInfo info)
                {
                    if (worker.CancellationPending)
                        throw new OperationCanceledException();

                    worker.ReportProgress(
                        0,
                        info);
                };

            if (worker.CancellationPending)
                throw new OperationCanceledException();

            SearchResult search;
            List<PlanItem> plan;
            int classifiedCount = 0;
            int matchedCount = 0;
            OnlineAuthorResolutionStats onlineStats =
                new OnlineAuthorResolutionStats();
            ScanWarmupMetrics warmupMetrics;
            ScanWarmupRequest warmupRequest = request.WarmupRequest;
            Stopwatch candidateTimer = Stopwatch.StartNew();
            if (ScanPerformanceDiagnostics.WarmupEnabled)
            {
                search = _scanWarmup.GetOrRunSource(
                    warmupRequest, reportProgress, cancelRequested, out warmupMetrics);
            }
            else
            {
                search = _everything.SearchFiles(
                    request.Source, request.Recursive, 0, warmupRequest.BlockedPaths,
                    request.Extensions, reportProgress, cancelRequested);
                warmupMetrics = new ScanWarmupMetrics();
                warmupMetrics.Provider = search.Backend ?? "";
                warmupMetrics.WarmupHit = false;
            }
            candidateTimer.Stop();
            if (!ScanPerformanceDiagnostics.WarmupEnabled)
                warmupMetrics.FileDiscoveryMs = candidateTimer.ElapsedMilliseconds;
            Stopwatch recognitionTimer = Stopwatch.StartNew();
            List<PlanItem> preparedPlan = null;
            long snapshotPrepareMs = 0;
            bool snapshotHit = ScanPerformanceDiagnostics.WarmupEnabled &&
                !request.OnlineLookupEnabled &&
                _scanWarmup.TryGetPreparedPlan(
                    warmupRequest, out preparedPlan, out snapshotPrepareMs);
            warmupMetrics.SnapshotHit = snapshotHit;
            warmupMetrics.SnapshotPrepareMs = snapshotPrepareMs;

            if (request.Mode ==
                ScanModeKind.Global)
            {
                if (request.RequestedLimit > 0 && search.Files.Count > request.RequestedLimit)
                    search.Files = search.Files.Take(request.RequestedLimit).ToList();

                plan = snapshotHit
                    ? (request.RequestedLimit > 0
                        ? preparedPlan.Take(request.RequestedLimit).ToList()
                        : preparedPlan)
                    : _engine.BuildPlan(
                        search.Files,
                        request.Root,
                        request.MaxAuthors,
                        request.GroupTemplate,
                        request.RecognizedGroupTemplates,
                        request.AuthorFolderTemplate,
                        request.RecognizedAuthorFolderTemplates,
                        reportProgress,
                        cancelRequested,
                        ScanProgressStage.Planning);

                if (request.OnlineLookupEnabled)
                {
                    onlineStats = ResolveOnlineAuthors(
                        request,
                        plan,
                        reportProgress,
                        cancelRequested);

                    if (onlineStats.OnlineResolved > 0)
                    {
                        plan = _engine.BuildPlan(
                            search.Files,
                            request.Root,
                            request.MaxAuthors,
                            request.GroupTemplate,
                            request.RecognizedGroupTemplates,
                            request.AuthorFolderTemplate,
                            request.RecognizedAuthorFolderTemplates,
                            reportProgress,
                            cancelRequested,
                            ScanProgressStage.Rebuilding);
                    }
                }

                classifiedCount = plan.Count;
                matchedCount = plan.Count;
            }
            else
            {
                // V1.11.6: filtered modes still enumerate candidates in newest -> oldest
                // order, but author recognition/planning stops as soon as the requested
                // number of matching results has been found. This keeps the scan mode
                // semantics stable while avoiding a full expensive classification pass.
                bool earlyStopped = false;
                if (snapshotHit)
                {
                    plan = ProjectPreparedSnapshot(
                        request, preparedPlan, cancelRequested,
                        out classifiedCount, out matchedCount);
                }
                else
                {
                    plan = BuildFilteredPlanWithEarlyStop(
                        request,
                        search,
                        reportProgress,
                        cancelRequested,
                        out classifiedCount,
                        out matchedCount,
                        out earlyStopped,
                        out onlineStats);
                }

                // Carry the flag to the final result below.
                // CandidateCount records the full lightweight enumeration set; only
                // ClassifiedCount entered author recognition/planning.
                ScanRunResult filteredResult = new ScanRunResult();
                filteredResult.Search = search;
                filteredResult.Plan = plan;
                filteredResult.ClassifiedCount = classifiedCount;
                filteredResult.MatchedCount = matchedCount;
                filteredResult.CandidateCount = search != null && search.Files != null
                    ? search.Files.Count
                    : 0;
                filteredResult.EarlyStopped = earlyStopped;
                filteredResult.OnlineStats = onlineStats;
                filteredResult.WarmupMetrics = warmupMetrics;
                filteredResult.CandidatePrepareMs = candidateTimer.ElapsedMilliseconds;
                recognitionTimer.Stop();
                filteredResult.RecognitionPlanMs = recognitionTimer.ElapsedMilliseconds;

                if (worker.CancellationPending)
                    throw new OperationCanceledException();

                timer.Stop();
                filteredResult.ElapsedMilliseconds = timer.ElapsedMilliseconds;
                return filteredResult;
            }

            if (worker.CancellationPending)
                throw new OperationCanceledException();

            timer.Stop();

            ScanRunResult result =
                new ScanRunResult();
            result.Search = search;
            result.Plan = plan;
            result.ClassifiedCount = classifiedCount;
            result.MatchedCount = matchedCount;
            result.CandidateCount = search != null && search.Files != null ? search.Files.Count : classifiedCount;
            result.EarlyStopped = false;
            result.OnlineStats = onlineStats;
            result.WarmupMetrics = warmupMetrics;
            result.CandidatePrepareMs = candidateTimer.ElapsedMilliseconds;
            recognitionTimer.Stop();
            result.RecognitionPlanMs = recognitionTimer.ElapsedMilliseconds;
            result.ElapsedMilliseconds =
                timer.ElapsedMilliseconds;
            return result;
        }

        private List<PlanItem> ProjectPreparedSnapshot(
            ScanRequest request,
            List<PlanItem> completePlan,
            Func<bool> cancelRequested,
            out int classifiedCount,
            out int matchedCount)
        {
            List<PlanItem> all = completePlan ?? new List<PlanItem>();
            classifiedCount = all.Count;
            List<PlanItem> matched = all.Where(
                delegate(PlanItem item) { return ScanModeRules.Matches(item, request.Mode); })
                .ToList();
            matchedCount = matched.Count;
            if (request.RequestedLimit > 0 && matched.Count > request.RequestedLimit)
                matched = matched.Take(request.RequestedLimit).ToList();

            // New-author destinations depend on the selected executable subset.
            // Rebuild only that subset; the expensive complete classification and
            // file discovery remain reused from the prepared snapshot.
            if (request.Mode != ScanModeKind.NewAuthor || matched.Count == 0)
                return matched;

            Dictionary<string, PlanItem> classifications = matched.ToDictionary(
                delegate(PlanItem item) { return item.SourcePath; },
                StringComparer.OrdinalIgnoreCase);
            List<FileInfo> files = new List<FileInfo>();
            foreach (PlanItem item in matched)
            {
                if (cancelRequested != null && cancelRequested())
                    throw new OperationCanceledException();
                if (File.Exists(item.SourcePath)) files.Add(new FileInfo(item.SourcePath));
            }
            List<PlanItem> rebuilt = _engine.BuildPlan(
                files, request.Root, request.MaxAuthors,
                request.GroupTemplate, request.RecognizedGroupTemplates,
                request.AuthorFolderTemplate, request.RecognizedAuthorFolderTemplates,
                null, cancelRequested, ScanProgressStage.Rebuilding);
            foreach (PlanItem item in rebuilt)
            {
                PlanItem classified;
                if (classifications.TryGetValue(item.SourcePath, out classified))
                    PreserveFilteredClassification(item, classified);
            }
            return rebuilt;
        }

        private List<PlanItem> BuildFilteredPlanWithEarlyStop(
            ScanRequest request,
            SearchResult search,
            Action<ScanProgressInfo> reportProgress,
            Func<bool> cancelRequested,
            out int classifiedCount,
            out int matchedCount,
            out bool earlyStopped,
            out OnlineAuthorResolutionStats onlineStats)
        {
            classifiedCount = 0;
            matchedCount = 0;
            earlyStopped = false;
            onlineStats = new OnlineAuthorResolutionStats();

            List<FileInfo> allFiles = search != null && search.Files != null
                ? search.Files
                : new List<FileInfo>();

            if (allFiles.Count == 0)
                return new List<PlanItem>();

            int requested = Math.Max(0, request.RequestedLimit);
            int totalCandidates = allFiles.Count;
            List<PlanItem> classifiedPlan = new List<PlanItem>();
            List<PlanItem> matchedPlan = new List<PlanItem>();

            // V1.11.6 strict Early Stop:
            // When no real-time online lookup can change the classification,
            // classify candidates sequentially and stop immediately after the
            // requested number of results for the selected scan mode is found.
            // The candidate list itself has already been enumerated in newest ->
            // oldest order by EverythingService, so this preserves scan ordering.
            bool strictEarlyStop =
                requested > 0 &&
                (!request.OnlineLookupEnabled || request.MaxOnlineLookups <= 0);

            if (strictEarlyStop)
            {
                classifiedPlan =
                    _engine.BuildPlanUntilMatches(
                        allFiles,
                        request.Root,
                        request.MaxAuthors,
                        request.GroupTemplate,
                        request.RecognizedGroupTemplates,
                        request.AuthorFolderTemplate,
                        request.RecognizedAuthorFolderTemplates,
                        request.Mode,
                        requested,
                        reportProgress,
                        cancelRequested,
                        ScanProgressStage.Planning);

                classifiedCount = classifiedPlan.Count;
                matchedPlan = classifiedPlan.Where(
                    delegate(PlanItem item)
                    {
                        return ScanModeRules.Matches(item, request.Mode);
                    }).ToList();
                matchedCount = matchedPlan.Count;

                earlyStopped =
                    matchedCount >= requested &&
                    classifiedCount < totalCandidates;
            }
            else
            {
                // Online resolution can change a local classification. In that
                // case, grow the newest-prefix in bounded rounds, resolve only
                // the newly added slice online, and stop as soon as the target
                // count is stable. This still avoids the old unconditional full
                // classification pass in the common case.
                int prefixCount = requested > 0
                    ? Math.Min(totalCandidates, Math.Max(32, requested))
                    : totalCandidates;
                int previousPrefixCount = 0;
                int previousMatchedCount = 0;
                int remainingOnlineLookups = Math.Max(0, request.MaxOnlineLookups);

                while (prefixCount > 0)
                {
                    if (cancelRequested != null && cancelRequested())
                        throw new OperationCanceledException();

                    List<FileInfo> prefixFiles = allFiles.Take(prefixCount).ToList();

                    Action<ScanProgressInfo> planningProgress = reportProgress;
                    if (requested > 0 && reportProgress != null)
                    {
                        int progressPreviousPrefix = previousPrefixCount;
                        int progressPreviousMatched = previousMatchedCount;
                        planningProgress =
                            delegate(ScanProgressInfo info)
                            {
                                if (info == null)
                                    return;

                                if (info.Stage == ScanProgressStage.Planning)
                                {
                                    ScanProgressInfo filteredInfo = new ScanProgressInfo();
                                    filteredInfo.Stage = ScanProgressStage.Filtering;
                                    filteredInfo.Current = Math.Min(progressPreviousMatched, requested);
                                    filteredInfo.Total = requested;
                                    filteredInfo.Checked = Math.Max(progressPreviousPrefix, info.Current);
                                    filteredInfo.CurrentPath = info.CurrentPath ?? "";
                                    filteredInfo.Indeterminate = false;
                                    reportProgress(filteredInfo);
                                }
                                else
                                {
                                    reportProgress(info);
                                }
                            };
                    }

                    classifiedPlan =
                        _engine.BuildPlan(
                            prefixFiles,
                            request.Root,
                            request.MaxAuthors,
                            request.GroupTemplate,
                            request.RecognizedGroupTemplates,
                            request.AuthorFolderTemplate,
                            request.RecognizedAuthorFolderTemplates,
                            planningProgress,
                            cancelRequested,
                            ScanProgressStage.Planning);

                    if (request.OnlineLookupEnabled && remainingOnlineLookups > 0)
                    {
                        List<PlanItem> newSlice = classifiedPlan
                            .Skip(Math.Min(previousPrefixCount, classifiedPlan.Count))
                            .ToList();

                        if (newSlice.Count > 0)
                        {
                            OnlineAuthorResolutionStats roundStats =
                                ResolveOnlineAuthors(
                                    request,
                                    newSlice,
                                    reportProgress,
                                    cancelRequested,
                                    remainingOnlineLookups);

                            MergeOnlineStats(onlineStats, roundStats);
                            remainingOnlineLookups = Math.Max(
                                0,
                                remainingOnlineLookups - Math.Max(0, roundStats.OnlineQueried));

                            if (roundStats.OnlineResolved > 0)
                            {
                                classifiedPlan =
                                    _engine.BuildPlan(
                                        prefixFiles,
                                        request.Root,
                                        request.MaxAuthors,
                                        request.GroupTemplate,
                                        request.RecognizedGroupTemplates,
                                        request.AuthorFolderTemplate,
                                        request.RecognizedAuthorFolderTemplates,
                                        reportProgress,
                                        cancelRequested,
                                        ScanProgressStage.Rebuilding);
                            }
                        }
                    }

                    matchedPlan = classifiedPlan.Where(
                        delegate(PlanItem item)
                        {
                            return ScanModeRules.Matches(item, request.Mode);
                        }).ToList();

                    classifiedCount = classifiedPlan.Count;
                    matchedCount = matchedPlan.Count;

                    if (requested > 0 && reportProgress != null)
                    {
                        ScanProgressInfo filteredInfo = new ScanProgressInfo();
                        filteredInfo.Stage = ScanProgressStage.Filtering;
                        filteredInfo.Current = Math.Min(matchedCount, requested);
                        filteredInfo.Total = requested;
                        filteredInfo.Checked = prefixCount;
                        filteredInfo.CurrentPath = prefixFiles.Count > 0
                            ? prefixFiles[prefixFiles.Count - 1].FullName
                            : "";
                        filteredInfo.Indeterminate = false;
                        reportProgress(filteredInfo);
                    }

                    if (requested <= 0 || matchedCount >= requested || prefixCount >= totalCandidates)
                        break;

                    previousPrefixCount = prefixCount;
                    previousMatchedCount = matchedCount;
                    prefixCount = Math.Min(
                        totalCandidates,
                        Math.Max(prefixCount + 1, prefixCount * 2));
                }

                earlyStopped =
                    requested > 0 &&
                    matchedCount >= requested &&
                    classifiedCount < totalCandidates;
            }

            if (requested > 0 && matchedPlan.Count > requested)
                matchedPlan = matchedPlan.Take(requested).ToList();

            // Only New Author mode needs a second planning pass: selecting a
            // subset changes new-folder group allocation. Exact/Ambiguous/
            // Unrecognized classifications are already final, so rebuilding
            // them merely rescans the target author tree and rereads metadata.
            if (request.Mode != ScanModeKind.NewAuthor)
                return matchedPlan;

            // Rebuild only the selected result set so new-author group allocation
            // is based on executable rows rather than on candidates that were
            // inspected only to discover the requested matches.
            Dictionary<string, PlanItem> classificationByPath =
                new Dictionary<string, PlanItem>(StringComparer.OrdinalIgnoreCase);
            List<FileInfo> matchedFiles = new List<FileInfo>();

            foreach (PlanItem item in matchedPlan)
            {
                if (cancelRequested != null && cancelRequested())
                    throw new OperationCanceledException();

                classificationByPath[item.SourcePath] = item;
                if (File.Exists(item.SourcePath))
                    matchedFiles.Add(new FileInfo(item.SourcePath));
            }

            List<PlanItem> finalPlan =
                _engine.BuildPlan(
                    matchedFiles,
                    request.Root,
                    request.MaxAuthors,
                    request.GroupTemplate,
                    request.RecognizedGroupTemplates,
                    request.AuthorFolderTemplate,
                    request.RecognizedAuthorFolderTemplates,
                    reportProgress,
                    cancelRequested,
                    ScanProgressStage.Rebuilding);

            foreach (PlanItem rebuilt in finalPlan)
            {
                PlanItem classified;
                if (classificationByPath.TryGetValue(rebuilt.SourcePath, out classified))
                    PreserveFilteredClassification(rebuilt, classified);
            }

            return finalPlan;
        }

        private static void MergeOnlineStats(
            OnlineAuthorResolutionStats target,
            OnlineAuthorResolutionStats source)
        {
            if (target == null || source == null)
                return;

            target.LocalRecognized += source.LocalRecognized;
            target.PendingOnline += source.PendingOnline;
            target.OnlineQueried += source.OnlineQueried;
            target.OnlineResolved += source.OnlineResolved;
            target.OnlineAmbiguous += source.OnlineAmbiguous;
            target.OnlineNotFound += source.OnlineNotFound;
            target.CachedSkipped += source.CachedSkipped;
            target.StillUnresolved += source.StillUnresolved;
            target.NewEntities += source.NewEntities;
        }

        private OnlineAuthorResolutionStats ResolveOnlineAuthors(
            ScanRequest request,
            List<PlanItem> plan,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            int maxLookupsOverride = -1)
        {
            IAuthorProvider provider;
            if (String.Equals(
                    request.OnlineProvider,
                    "Danbooru",
                    StringComparison.OrdinalIgnoreCase))
            {
                provider = new DanbooruAuthorProvider();
            }
            else
            {
                return new OnlineAuthorResolutionStats();
            }

            OnlineAuthorResolver resolver =
                new OnlineAuthorResolver(
                    _authorEntityStore,
                    provider,
                    _tagCleaningStore);

            return resolver.Resolve(
                plan,
                maxLookupsOverride >= 0 ? maxLookupsOverride : request.MaxOnlineLookups,
                request.SaveOnlineCache,
                progress,
                cancelRequested);
        }

        private GridRenderMetrics RenderGrid(string selectPath)
        {
            GridRenderMetrics metrics = new GridRenderMetrics();
            Stopwatch totalTimer = Stopwatch.StartNew();
            bool previousGridLoading = GridInteraction.SetBulkLayoutLoading(_grid, true);
            UiStyle.GridAutoSizeSnapshot autoSizeSnapshot = UiStyle.SuspendGridAutoSize(_grid);
            _grid.SuspendLayout();
            try
            {
                _grid.RowCount = 0;

                // Build and sort the full preview only when the preview data itself
                // changes. Status/search switches reuse these row objects and batch
                // attach only the matching subset, so cells are not recreated.
                Stopwatch sortTimer = Stopwatch.StartNew();
                _allGridItems = GetPreviewItems()
                    .OrderByDescending(
                        delegate(PlanItem p)
                        {
                            return p != null ? p.LastWriteTime : DateTime.MinValue;
                        })
                    .ToList();
                sortTimer.Stop();
                metrics.SortMs = sortTimer.ElapsedMilliseconds;

                // VirtualMode keeps only the PlanItem model. DataGridView asks
                // for values only for cells it needs to paint.
                metrics.RowBuildMs = 0;
                metrics.AddRowsMs = 0;
            }
            finally
            {
                Stopwatch layoutTimer = Stopwatch.StartNew();
                // Responsive widths are calculated once for the complete data set.
                // Short columns are then frozen so filter switches do not repeat
                // AllCells measurements and the layout stays visually stable.
                UiStyle.RestoreGridAutoSize(_grid, autoSizeSnapshot);
                FreezeMainGridShortColumnWidths();
                GridInteraction.SetBulkLayoutLoading(_grid, previousGridLoading);
                _grid.ResumeLayout(false);
                layoutTimer.Stop();
                metrics.LayoutMs = layoutTimer.ElapsedMilliseconds;
            }

            Stopwatch finalizeTimer = Stopwatch.StartNew();
            UpdateFilterCounts();
            UpdateViewFilterButtonStyles();
            ApplyCurrentGridFilter(selectPath);
            finalizeTimer.Stop();
            metrics.FinalizeMs = finalizeTimer.ElapsedMilliseconds;
            totalTimer.Stop();
            metrics.TotalMs = totalTimer.ElapsedMilliseconds;
            return metrics;
        }

        private PlanItem GetGridItem(int rowIndex)
        {
            return rowIndex >= 0 && rowIndex < _visibleGridItems.Count
                ? _visibleGridItems[rowIndex]
                : null;
        }

        private void GridCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            PlanItem item = GetGridItem(e.RowIndex);
            if (item == null || e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count)
                return;
            string name = _grid.Columns[e.ColumnIndex].Name;
            RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
            if (name == "FileName") e.Value = item.FileName;
            else if (name == "Author") e.Value = item.Author;
            else if (name == "MatchedAs") e.Value = item.MatchedAs;
            else if (name == "Status") e.Value = GetGridStatusText(item, visual);
            else if (name == "TargetDir") e.Value = item.TargetDir;
            else if (name == "FileSize") e.Value = GetFileSizeText(item);
            else if (name == "Modified") e.Value = item.LastWriteTime == DateTime.MinValue
                ? "" : item.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        }

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                _grid.Columns[e.ColumnIndex].Name != "Status") return;
            PlanItem item = GetGridItem(e.RowIndex);
            if (item == null) return;
            Color color = GetGridStatusColor(item, RecognitionVisualResolver.Resolve(item));
            RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
            bool actionable = visual != null &&
                (String.Equals(visual.Code, "ambiguous", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(visual.Code, "unrecognized", StringComparison.OrdinalIgnoreCase));
            e.CellStyle.ForeColor = color;
            e.CellStyle.SelectionForeColor = color;
            e.CellStyle.Font = actionable && e.RowIndex == _hoveredStatusRow
                ? _gridStatusLinkFont
                : _gridStatusBoldFont;
        }

        private void GridCellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                _grid.Columns[e.ColumnIndex].Name != "Status") return;
            PlanItem item = GetGridItem(e.RowIndex);
            if (item != null)
                e.ToolTipText = GetGridStatusTooltip(item, RecognitionVisualResolver.Resolve(item));
        }

        private void ApplyCurrentGridFilter(string selectPath)
        {
            if (_grid == null)
                return;

            bool previousGridLoading = GridInteraction.SetBulkLayoutLoading(_grid, true);
            _grid.SuspendLayout();
            _applyingGridFilter = true;
            try
            {
                _grid.CurrentCell = null;
                _grid.ClearSelection();

                List<PlanItem> visibleItems =
                    new List<PlanItem>(_allGridItems.Count);
                string search = _txtListSearch != null
                    ? (_txtListSearch.Text ?? "").Trim()
                    : "";

                int selectedIndex = -1;
                foreach (PlanItem item in _allGridItems)
                {
                    bool visible = item != null &&
                        MatchesViewFilter(item) &&
                        MatchesSearchText(item, search);
                    if (!visible)
                        continue;

                    visibleItems.Add(item);

                    if (selectedIndex < 0 &&
                        !String.IsNullOrWhiteSpace(selectPath) &&
                        String.Equals(item.SourcePath, selectPath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = visibleItems.Count - 1;
                    }
                }
                // Virtual DataGridView cells can retain values already requested
                // for the previous index-to-item mapping. Reset the virtual row
                // range before publishing the new model so a status/search switch
                // cannot paint stale rows from the former filter.
                _grid.RowCount = 0;
                _visibleGridItems = visibleItems;
                _grid.RowCount = visibleItems.Count;
                _grid.Invalidate();

                _grid.CurrentCell = null;
                _grid.ClearSelection();

                if (selectedIndex >= 0 && selectedIndex < _grid.RowCount)
                {
                    _grid.Rows[selectedIndex].Selected = true;
                    _grid.CurrentCell = _grid.Rows[selectedIndex].Cells["FileName"];
                }
            }
            finally
            {
                _applyingGridFilter = false;
                GridInteraction.SetBulkLayoutLoading(_grid, previousGridLoading);
                _grid.ResumeLayout(false);
            }

            UpdateDetailsFromSelection();
        }

        private void FreezeMainGridShortColumnWidths()
        {
            if (_grid == null)
                return;

            string[] names = new string[] { "Status", "FileSize", "Modified" };
            foreach (string name in names)
            {
                DataGridViewColumn column = _grid.Columns[name];
                if (column == null)
                    continue;

                int preferred = column.Width;
                try
                {
                    preferred = column.GetPreferredWidth(
                        _grid.VirtualMode
                            ? DataGridViewAutoSizeColumnMode.DisplayedCells
                            : DataGridViewAutoSizeColumnMode.AllCells,
                        true);
                }
                catch { }

                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = Math.Max(
                    column.MinimumWidth,
                    Math.Min(360, preferred));
                column.Resizable = DataGridViewTriState.True;
            }
        }

        private DataGridViewRow GetFirstVisibleGridRow()
        {
            if (_grid == null)
                return null;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Visible)
                    return row;
            }
            return null;
        }

        private void GridCellMouseDown(
            object sender,
            DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right ||
                e.RowIndex < 0)
            {
                return;
            }

            CloseOverlay(true);

            DataGridViewRow row =
                _grid.Rows[e.RowIndex];

            // 右键点击已选中的任意一行时，绝不修改当前多选集合。
            // 旧版在这里设置 CurrentCell，WinForms 会把某些框选/Shift 多选
            // 收缩成单行，所以现在仅在右键未选中行时才改变选择。
            bool clickedInsideSelection = row.Selected;

            if (!clickedInsideSelection)
            {
                _grid.ClearSelection();
                row.Selected = true;

                if (e.ColumnIndex >= 0)
                {
                    _grid.CurrentCell =
                        row.Cells[e.ColumnIndex];
                }
            }

            int count =
                _grid.SelectedRows.Count;

            PlanItem singleItem = count == 1
                ? GetSingleSelectedPlanItem()
                : null;
            _openFileLocationMenuItem.Enabled = singleItem != null &&
                !String.IsNullOrWhiteSpace(singleItem.SourcePath);
            _copyFullPathMenuItem.Enabled = count > 0;

            _authorActionMenuItem.Visible = false;
            _manageExclusionMenuItem.Visible = false;
            if (singleItem != null)
            {
                if (singleItem.IsExcludedPreview)
                {
                    _manageExclusionMenuItem.Visible = true;
                    _manageExclusionMenuItem.Enabled = !_isScanning && !_isExecuting;
                }
                else
                {
                    RecognitionVisual visual = RecognitionVisualResolver.Resolve(singleItem);
                    string code = visual != null ? (visual.Code ?? "").ToLowerInvariant() : "";
                    _authorActionMenuItem.Visible = true;
                    _authorActionMenuItem.Text = code == "unrecognized"
                        ? L("Details.AssignAuthor")
                        : code == "ambiguous"
                            ? L("Details.ChooseAuthor")
                            : L("Details.AssignOtherAuthor");
                    _authorActionMenuItem.Enabled = !_isScanning && !_isExecuting &&
                        (code == "unrecognized" || !String.IsNullOrWhiteSpace(singleItem.Author));
                }
            }

            _blockMenuItem.Text =
                count > 1
                    ? LF("Status.BlockSelectedMany", count)
                    : L("Status.BlockThis");
            bool hasExcludedSelection = false;
            foreach (DataGridViewRow selectedRow in _grid.SelectedRows)
            {
                PlanItem selectedItem = GetGridItem(selectedRow.Index);
                if (selectedItem != null && selectedItem.IsExcludedPreview)
                {
                    hasExcludedSelection = true;
                    break;
                }
            }
            _blockMenuItem.Enabled =
                !_isScanning && !_isExecuting && count > 0 && !hasExcludedSelection;
            _blockMenuItem.Visible = !hasExcludedSelection;
            _gridActionSeparator.Visible = _authorActionMenuItem.Visible ||
                _manageExclusionMenuItem.Visible || _blockMenuItem.Visible;

            _gridContextMenu.Show(
                Cursor.Position);
        }

        private void OpenSelectedFileLocation()
        {
            PlanItem item = GetSingleSelectedPlanItem();
            if (item == null || String.IsNullOrWhiteSpace(item.SourcePath)) return;
            try
            {
                string path = Path.GetFullPath(item.SourcePath);
                if (File.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                {
                    string directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
                    Process.Start("explorer.exe", "\"" + directory + "\"");
                }
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("Common.Error.OpenFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopySelectedFullPaths()
        {
            List<string> paths = new List<string>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                PlanItem item = GetGridItem(row.Index);
                if (item != null && !String.IsNullOrWhiteSpace(item.SourcePath))
                    paths.Add(Path.GetFullPath(item.SourcePath));
            }
            if (paths.Count == 0) return;
            paths.Reverse();
            try
            {
                Clipboard.SetText(String.Join(Environment.NewLine, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("Context.CopyFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BlockSelectedArchives()
        {
            if (_isScanning || _isExecuting)
                return;

            CloseOverlay(true);

            List<string> paths =
                new List<string>();

            foreach (
                DataGridViewRow row
                in _grid.SelectedRows)
            {
                PlanItem item = GetGridItem(row.Index);

                if (item != null &&
                    !item.IsExcludedPreview &&
                    !String.IsNullOrWhiteSpace(
                        item.SourcePath))
                {
                    paths.Add(
                        item.SourcePath);
                }
            }

            if (paths.Count == 0)
                return;

            paths =
                paths.Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            int added =
                _blockList.AddPaths(paths);

            HashSet<string> blockedNow =
                new HashSet<string>(
                    paths.Select(
                        delegate(string p)
                        {
                            return BlockListStore
                                .NormalizePath(p);
                        }),
                    StringComparer.OrdinalIgnoreCase);

            // 屏蔽后立即从当前预览中消失。
            _plan =
                _plan.Where(
                    delegate(PlanItem p)
                    {
                        return !blockedNow.Contains(
                            BlockListStore
                                .NormalizePath(
                                    p.SourcePath));
                    })
                .ToList();

            RenderGrid(null);

            RefreshExecutionSafetyUi();

            _lblStatus.Text =
                LF(
                    "Status.BlockedResult",
                    paths.Count,
                    added);
            ScheduleScanWarmup(false);
        }

        private void ShowBlockListDialog()
        {
            CloseOverlay(true);
            using (BlockListManagerForm dlg = new BlockListManagerForm(_blockList, _language, Font))
            {
                dlg.ShowDialog(this);
            }
            ScheduleScanWarmup(false);
        }

        private void GridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_isScanning || _isExecuting) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            PlanItem p = GetGridItem(e.RowIndex);
            if (p == null || p.IsExcludedPreview) return;
            string column = _grid.Columns[e.ColumnIndex].Name;

            if (column == "FileName") ShowOverlayEditor(e.RowIndex, e.ColumnIndex, p.FileName, false, p);
            else if (column == "Author" || column == "MatchedAs")
            {
                string value = Convert.ToString(row.Cells[e.ColumnIndex].Value);
                ShowOverlayEditor(e.RowIndex, e.ColumnIndex, value, true, p);
            }
        }

        private void GridCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_isScanning || _isExecuting || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (!String.Equals(_grid.Columns[e.ColumnIndex].Name, "Status", StringComparison.OrdinalIgnoreCase))
                return;

            PlanItem item = GetGridItem(e.RowIndex);
            if (item == null || item.IsExcludedPreview)
                return;

            RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
            string code = visual != null ? (visual.Code ?? "") : "";
            if (String.Equals(code, "ambiguous", StringComparison.OrdinalIgnoreCase))
                ChooseManualAuthorFolder(item);
            else if (String.Equals(code, "unrecognized", StringComparison.OrdinalIgnoreCase))
                AssignAuthorNameToUnrecognized(item);
        }

        private void GridCellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            int nextRow = -1;
            bool actionable = false;
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                String.Equals(_grid.Columns[e.ColumnIndex].Name, "Status", StringComparison.OrdinalIgnoreCase))
            {
                PlanItem item = GetGridItem(e.RowIndex);
                RecognitionVisual visual = item != null && !item.IsExcludedPreview
                    ? RecognitionVisualResolver.Resolve(item)
                    : null;
                string code = visual != null ? (visual.Code ?? "") : "";
                actionable = String.Equals(code, "ambiguous", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(code, "unrecognized", StringComparison.OrdinalIgnoreCase);
                if (actionable)
                    nextRow = e.RowIndex;
            }

            if (_hoveredStatusRow != nextRow)
            {
                int previousRow = _hoveredStatusRow;
                _hoveredStatusRow = nextRow;
                if (previousRow >= 0 && previousRow < _grid.RowCount)
                    _grid.InvalidateCell(_grid.Columns["Status"].Index, previousRow);
                if (nextRow >= 0 && nextRow < _grid.RowCount)
                    _grid.InvalidateCell(_grid.Columns["Status"].Index, nextRow);
            }
            _grid.Cursor = actionable ? Cursors.Hand : Cursors.Default;
        }

        private void GridCellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == _hoveredStatusRow)
            {
                int previousRow = _hoveredStatusRow;
                _hoveredStatusRow = -1;
                _grid.Cursor = Cursors.Default;
                if (previousRow >= 0 && previousRow < _grid.RowCount)
                    _grid.InvalidateCell(_grid.Columns["Status"].Index, previousRow);
            }
        }

        private void ShowOverlayEditor(int rowIndex, int columnIndex, string text, bool readOnly, PlanItem item)
        {
            CloseOverlay(true);
            Rectangle rect = _grid.GetCellDisplayRectangle(columnIndex, rowIndex, true);
            _overlayEditor = new GridOverlayTextBox();
            _overlayEditor.Text = text ?? "";
            _overlayEditor.ReadOnly = readOnly;
            _overlayEditor.BorderStyle = BorderStyle.FixedSingle;
            _overlayEditor.Location = new Point(rect.X, rect.Y);
            _overlayEditor.Size = new Size(Math.Max(50, rect.Width), Math.Max(22, rect.Height));
            _overlayEditor.Font = _grid.Font;
            _overlayEditor.ShortcutsEnabled = true;
            _editingItem = item;
            _editingFileName = !readOnly;
            _overlayEditor.KeyDown += OverlayKeyDown;
            _overlayEditor.LostFocus += delegate { CloseOverlay(true); };
            _grid.Controls.Add(_overlayEditor);
            _overlayEditor.BringToFront();
            _overlayEditor.Focus();
            if (!readOnly) _overlayEditor.SelectAll(); else _overlayEditor.Select(0, 0);
        }

        private void OverlayKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            { CloseOverlay(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape)
            { CloseOverlay(false); e.Handled = true; e.SuppressKeyPress = true; }
        }

        private void CloseOverlay(bool commit)
        {
            if (_overlayEditor == null || _overlayClosing) return;
            _overlayClosing = true;
            try
            {
                TextBox editor = _overlayEditor;
                PlanItem item = _editingItem;
                bool fileEdit = _editingFileName;
                string newText = editor.Text;
                _grid.Controls.Remove(editor);
                editor.Dispose();
                _overlayEditor = null; _editingItem = null; _editingFileName = false;
                if (commit && fileEdit && item != null && !String.Equals(item.FileName, newText, StringComparison.Ordinal))
                    RenameArchive(item, newText);
            }
            finally { _overlayClosing = false; }
        }

        private void RenameArchive(PlanItem item, string requestedName)
        {
            try
            {
                string oldPath = item.SourcePath;
                if (!File.Exists(oldPath)) throw new FileNotFoundException(L("Validation.SourceAlreadyGone"), oldPath);
                string newName = ValidateNewArchiveName(oldPath, requestedName);
                if (String.Equals(Path.GetFileName(oldPath), newName, StringComparison.Ordinal)) return;

                string dir = Path.GetDirectoryName(oldPath);
                string newPath = Path.Combine(dir, newName);
                bool sameIgnoreCase = String.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
                if (File.Exists(newPath) && !sameIgnoreCase) throw new IOException(LF("Validation.DuplicateInFolder", newName));

                if (sameIgnoreCase)
                {
                    string temp = Path.Combine(dir, "__AuthorSorterTemp_" + Guid.NewGuid().ToString("N") + Path.GetExtension(oldPath));
                    File.Move(oldPath, temp); File.Move(temp, newPath);
                }
                else File.Move(oldPath, newPath);

                item.SourcePath = newPath; item.FileName = newName;
                item.ManualTargetDir = ""; item.ManualTargetName = ""; item.ManualTargetAuthor = "";
                RefreshPlan(newPath);
                _lblStatus.Text = LF("Status.Renamed", newName);
            }
            catch (Exception ex)
            {
                UiMessageBox.Show(this, ex.Message, L("Dialog.RenameFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                RenderGrid(item.SourcePath);
            }
        }

        private string ValidateNewArchiveName(
            string oldPath,
            string input)
        {
            string name =
                (input ?? "").Trim();

            if (name.Length == 0)
            {
                throw new ArgumentException(
                    L("Validation.EmptyFileName"));
            }

            if (name == "." ||
                name == "..")
            {
                throw new ArgumentException(
                    L("Validation.InvalidFileName"));
            }

            if (name.EndsWith(
                    ".",
                    StringComparison.Ordinal) ||
                name.EndsWith(
                    " ",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    L("Validation.EndDotSpace"));
            }

            if (name.IndexOfAny(
                    Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(
                    L("Validation.InvalidChars"));
            }

            if (!String.Equals(
                    Path.GetFileName(name),
                    name,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    L("Validation.PathNotAllowed"));
            }

            string oldExt =
                Path.GetExtension(
                    oldPath);

            string newExt =
                Path.GetExtension(
                    name);

            if (String.IsNullOrWhiteSpace(
                    newExt))
            {
                name += oldExt;
                newExt =
                    oldExt;
            }

            string oldNormalized;
            string oldError;

            FileTypeRules.TryNormalizeExtension(
                oldExt,
                out oldNormalized,
                out oldError);

            string newNormalized;
            string newError;

            if (!FileTypeRules.TryNormalizeExtension(
                    newExt,
                    out newNormalized,
                    out newError))
            {
                throw new ArgumentException(
                    L("Validation.InvalidExtension"));
            }

            bool keepsOriginal =
                String.Equals(
                    oldNormalized,
                    newNormalized,
                    StringComparison.OrdinalIgnoreCase);

            if (!keepsOriginal &&
                !FileTypeRules.ContainsExtension(
                    _scanExtensions,
                    newNormalized))
            {
                throw new ArgumentException(
                    LF(
                        "Validation.ExtensionNotAllowed",
                        GetActiveFileTypeProfileName(),
                        FormatExtensionSummary(
                            _scanExtensions,
                            12)));
            }

            return name;
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (_isScanning || _isExecuting) return;
            if (e.KeyCode != Keys.Delete || _overlayEditor != null) return;
            List<string> removePaths = new List<string>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                PlanItem p = GetGridItem(row.Index);
                if (p != null && !p.IsExcludedPreview) removePaths.Add(p.SourcePath);
            }
            if (removePaths.Count == 0) return;
            HashSet<string> remove = new HashSet<string>(removePaths, StringComparer.OrdinalIgnoreCase);
            _plan = _plan.Where(delegate(PlanItem p) { return !remove.Contains(p.SourcePath); }).ToList();
            RenderGrid(null);
            RefreshExecutionSafetyUi();
            _lblStatus.Text = LF("Status.RemovedFromPlan", removePaths.Count);
            e.Handled = true; e.SuppressKeyPress = true;
        }

        private void ChooseManualAuthorFolder(PlanItem item)
        {
            if (_isScanning || _isExecuting)
                return;

            if (String.IsNullOrWhiteSpace(item.Author))
            {
                UiMessageBox.Show(this, L("Status.NoAuthorForManual"), L("Status.CannotNormalizeAuthor"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selected = null;
            if (item.CandidatePaths.Count > 0)
            {
                CandidateChoiceResult choice = ShowCandidateDialog(item);
                if (choice.Cancelled) return;
                if (!choice.SelectOther)
                {
                    if (!String.IsNullOrWhiteSpace(choice.NewAuthorName))
                    {
                        ApplyManualNewAuthor(item, choice.NewAuthorName);
                        return;
                    }
                    selected = choice.Path;
                }
            }

            if (selected == null)
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = L("Dialog.ManualFolderDescription");
                    if (!String.IsNullOrWhiteSpace(item.TargetDir) && Directory.Exists(item.TargetDir)) dlg.SelectedPath = item.TargetDir;
                    else if (Directory.Exists(_currentRoot)) dlg.SelectedPath = _currentRoot;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    selected = dlg.SelectedPath;
                }
            }
            ApplyManualAuthorFolder(item, selected);
        }

        private void ApplyManualAuthorFolder(PlanItem item, string selectedFolder)
        {
            if (String.IsNullOrWhiteSpace(selectedFolder)) return;
            string folderName = new DirectoryInfo(selectedFolder).Name;
            List<string> knownGroupTemplates =
                GroupNaming.NormalizeTemplates(
                    _txtGroupTemplate.Text,
                    _recognizedGroupTemplates);

            if (GroupNaming.IsGroupFolderName(
                    folderName,
                    knownGroupTemplates))
            {
                UiMessageBox.Show(
                    this,
                    LF("Dialog.SelectedGroupNotAuthor", folderName),
                    L("Dialog.SelectSpecificAuthor"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string logicalFolderName;
            if (!AuthorFolderNaming.TryExtractAuthor(
                    folderName,
                    _recognizedAuthorFolderTemplates,
                    out logicalFolderName))
            {
                logicalFolderName = folderName;
            }

            _aliasLibrary.MergeAliasGroup(logicalFolderName, new string[] { item.Author });
            item.ManualTargetDir = selectedFolder;
            item.ManualTargetName = logicalFolderName;
            item.ManualTargetAuthor = "";
            string keep = item.SourcePath;
            RefreshPlan(keep);
            _lblStatus.Text = LF("Status.ManualFolderSelected", logicalFolderName, item.Author);
        }

        private void ApplyManualNewAuthor(PlanItem item, string authorName)
        {
            if (item == null || String.IsNullOrWhiteSpace(authorName))
                return;

            string chosenAuthor = authorName.Trim();

            // Persist the user's choice as an alias decision, but deliberately
            // do not persist the temporary group path from the full scan.
            _aliasLibrary.MergeAliasGroup(
                chosenAuthor,
                new string[] { item.Author });

            item.ManualTargetDir = "";
            item.ManualTargetName = chosenAuthor;
            item.ManualTargetAuthor = chosenAuthor;

            string keep = item.SourcePath;
            RefreshPlan(keep);
            _lblStatus.Text =
                LF(
                    "Status.ManualNewAuthorSelected",
                    chosenAuthor,
                    item.Author);
        }

        private sealed class CandidateChoiceResult
        {
            public bool Cancelled;
            public bool SelectOther;
            public string Path;
            public string NewAuthorName;
        }

        private CandidateChoiceResult ShowCandidateDialog(PlanItem item)
        {
            CandidateChoiceResult result = new CandidateChoiceResult();
            using (Form dlg = new Form())
            {
                dlg.Text = L("Dialog.ChooseAuthorFolder.Title");
                dlg.ClientSize = new Size(760, 430);
                dlg.MinimumSize = new Size(680, 390);
                dlg.MaximizeBox = false;
                UiStyle.ApplyDialog(dlg, Font);

                Label label = new Label(); label.Location = new Point(15, 15); label.Size = new Size(710, 55);
                label.Text = LF("Dialog.ChooseAuthorFolder.Info", item.Author);
                dlg.Controls.Add(label);

                ListBox list = new ListBox(); list.Location = new Point(15, 75); list.Size = new Size(710, 245); list.HorizontalScrollbar = true; dlg.Controls.Add(list);
                for (int i = 0; i < item.CandidatePaths.Count; i++)
                {
                    string path = item.CandidatePaths[i] ?? "";
                    bool isPlanned =
                        i < item.CandidateIsPlanned.Count &&
                        item.CandidateIsPlanned[i];

                    string name =
                        i < item.CandidateNames.Count &&
                        !String.IsNullOrWhiteSpace(item.CandidateNames[i])
                            ? item.CandidateNames[i]
                            : (!String.IsNullOrWhiteSpace(path)
                                ? new DirectoryInfo(path).Name
                                : "");

                    if (isPlanned || String.IsNullOrWhiteSpace(path))
                    {
                        list.Items.Add(
                            name +
                            "    →    " +
                            L("Dialog.ChooseAuthorFolder.PlannedNew"));
                    }
                    else
                    {
                        list.Items.Add(name + "    →    " + path);
                    }
                }
                if (list.Items.Count > 0) list.SelectedIndex = 0;

                Button btnUse = UiStyle.NewButton(L("Dialog.ChooseAuthorFolder.Use"), 180, true);
                btnUse.Location = new Point(15, 335);
                Button btnOther = UiStyle.NewButton(L("Dialog.ChooseAuthorFolder.Other"), 150, false);
                btnOther.Location = new Point(210, 335);
                Button btnCancel = UiStyle.NewButton(L("Common.Cancel"), 80, false);
                btnCancel.Location = new Point(645, 335);
                dlg.Controls.Add(btnUse); dlg.Controls.Add(btnOther); dlg.Controls.Add(btnCancel);

                Action useSelected =
                    delegate
                    {
                        int index = list.SelectedIndex;
                        if (index < 0)
                            return;

                        bool isPlanned =
                            index < item.CandidateIsPlanned.Count &&
                            item.CandidateIsPlanned[index];

                        string path =
                            index < item.CandidatePaths.Count
                                ? (item.CandidatePaths[index] ?? "")
                                : "";

                        if (isPlanned || String.IsNullOrWhiteSpace(path))
                        {
                            result.NewAuthorName =
                                index < item.CandidateNames.Count
                                    ? item.CandidateNames[index]
                                    : "";
                        }
                        else
                        {
                            result.Path = path;
                        }

                        dlg.DialogResult = DialogResult.OK;
                        dlg.Close();
                    };

                btnUse.Click += delegate { useSelected(); };
                list.DoubleClick += delegate { useSelected(); };
                btnOther.Click += delegate { result.SelectOther = true; dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                btnCancel.Click += delegate { result.Cancelled = true; dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

                if (dlg.ShowDialog(this) != DialogResult.OK) result.Cancelled = true;
            }
            return result;
        }

        private void RefreshPlan(string keepSelectedPath)
        {
            if (String.IsNullOrWhiteSpace(
                    _currentRoot) ||
                !Directory.Exists(
                    _currentRoot))
            {
                return;
            }

            // 重要：
            // 扫描模式只在点击“扫描预览”的那一刻决定哪些文件进入列表。
            // 后续只重新规划“当前确实可执行的项目 + 本次正在处理的项目”。
            // 尚未确认的歧义/未识别项目继续留在列表中，但不能占用新作者
            // 分组名额，否则一次歧义处理就可能把真实的下一分组从 11 推到 31。
            // 因此：
            // [!] 歧义 -> [★] 手动指定，仍留在歧义模式列表；
            // [×] 无法识别 -> 改名或在处理预览中指定作者后变成可规划状态，仍留在本次扫描列表；
            // 其他仍待确认项目不会参与本次目标目录编号分配。
            List<PlanItem> originalPlan =
                new List<PlanItem>(_plan);

            List<PlanItem> executableSource =
                originalPlan.Where(
                    delegate(PlanItem p)
                    {
                        return
                            p.CanMove ||
                            String.Equals(
                                p.SourcePath,
                                keepSelectedPath,
                                StringComparison.OrdinalIgnoreCase);
                    })
                .ToList();

            List<PlanItem> recalculated =
                _engine.ResolvePlan(
                    executableSource,
                    _currentRoot,
                    _currentMaxAuthors,
                    _currentGroupTemplate,
                    _recognizedGroupTemplates,
                    _currentAuthorFolderTemplate,
                    _recognizedAuthorFolderTemplates);

            Dictionary<string, PlanItem> recalculatedByPath =
                new Dictionary<string, PlanItem>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (PlanItem item in recalculated)
                recalculatedByPath[item.SourcePath] = item;

            List<PlanItem> mergedPlan =
                new List<PlanItem>();

            foreach (PlanItem original in originalPlan)
            {
                PlanItem rebuilt;
                if (recalculatedByPath.TryGetValue(
                        original.SourcePath,
                        out rebuilt))
                {
                    mergedPlan.Add(rebuilt);
                }
                else
                {
                    mergedPlan.Add(original);
                }
            }

            _plan = mergedPlan;

            RenderGrid(
                keepSelectedPath);

            RefreshExecutionSafetyUi();
        }

        private static string FormatBytes(long bytes)
        {
            double value = Math.Max(0L, bytes);
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }

            if (unit == 0)
                return ((long)value).ToString() + " " + units[unit];

            return value.ToString(value >= 10 ? "0.0" : "0.00") + " " + units[unit];
        }

        private long GetSafetyReserveBytes()
        {
            decimal bytes =
                Math.Max(0M, _safetyReserveGb) *
                1024M * 1024M * 1024M;

            if (bytes > Int64.MaxValue)
                return Int64.MaxValue;

            return Decimal.ToInt64(bytes);
        }

        private int CountUnresolvedItems()
        {
            int count = 0;
            foreach (PlanItem item in _plan)
            {
                if (item == null || item.CanMove)
                    continue;

                RecognitionVisual visual = RecognitionVisualResolver.Resolve(item);
                string code = visual != null ? (visual.Code ?? "") : "";
                if (String.Equals(code, "ambiguous", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(code, "unrecognized", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                string a = Path.GetFullPath(left ?? "")
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string b = Path.GetFullPath(right ?? "")
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return String.Equals(
                    (left ?? "").Trim(),
                    (right ?? "").Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        private ExecutionSafetyCheck EvaluateExecutionSafety(
            List<PlanItem> movable,
            bool probeWritable)
        {
            ExecutionSafetyCheck check = new ExecutionSafetyCheck();
            check.ReserveBytes = GetSafetyReserveBytes();

            string targetRoot =
                _txtRoot != null
                    ? (_txtRoot.Text ?? "").Trim()
                    : "";

            check.TargetExists =
                targetRoot.Length > 0 &&
                Directory.Exists(targetRoot);

            if (!check.TargetExists)
            {
                check.Detail = L("Status.SafetyTargetUnavailable");
                return check;
            }

            string currentSource =
                _txtSource != null
                    ? (_txtSource.Text ?? "").Trim()
                    : "";

            if (_plan.Count > 0 &&
                (!PathsEqual(targetRoot, _currentRoot) ||
                 !PathsEqual(currentSource, _currentSourceRoot)))
            {
                check.PlanPathsChanged = true;
            }

            if (movable == null)
                movable = new List<PlanItem>();

            HashSet<string> conflictTargets =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Existing blocked target conflicts are part of the current batch
            // safety state too. Do not allow a partially-conflicted plan to run.
            foreach (PlanItem planned in _plan)
            {
                if (planned == null ||
                    String.IsNullOrWhiteSpace(planned.TargetPath) ||
                    String.Equals(
                        planned.SourcePath,
                        planned.TargetPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(planned.TargetPath) ||
                    Directory.Exists(planned.TargetPath) ||
                    File.Exists(planned.TargetPath + ".moving") ||
                    Directory.Exists(planned.TargetPath + ".moving"))
                {
                    conflictTargets.Add(planned.TargetPath);
                }
            }

            HashSet<string> plannedTargets =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PlanItem item in movable)
            {
                if (item == null)
                    continue;

                if (!File.Exists(item.SourcePath))
                {
                    check.MissingSources++;
                    continue;
                }

                try
                {
                    long length = new FileInfo(item.SourcePath).Length;
                    check.BatchBytes += length;
                    if (!ExecutionSafety.IsSameVolume(item.SourcePath, item.TargetPath))
                        check.RequiredBytes += length;
                }
                catch
                {
                    check.MissingSources++;
                }

                if (File.Exists(item.TargetPath) ||
                    Directory.Exists(item.TargetPath) ||
                    File.Exists(item.TargetPath + ".moving") ||
                    Directory.Exists(item.TargetPath + ".moving"))
                {
                    conflictTargets.Add(item.TargetPath);
                }

                if (!String.IsNullOrWhiteSpace(item.TargetPath) &&
                    !plannedTargets.Add(item.TargetPath))
                {
                    conflictTargets.Add(item.TargetPath);
                }
            }

            check.TargetConflicts = conflictTargets.Count;

            string spaceError;
            check.SpaceKnown =
                ExecutionSafety.TryGetAvailableSpace(
                    targetRoot,
                    out check.FreeBytes,
                    out spaceError);

            if (!check.SpaceKnown)
                check.Detail = spaceError;

            if (probeWritable)
            {
                string writeError;
                check.TargetWritable =
                    ExecutionSafety.TryProbeWritable(
                        targetRoot,
                        out writeError);

                if (!check.TargetWritable)
                    check.Detail = writeError;
            }

            bool enoughSpace =
                check.SpaceKnown &&
                check.FreeBytes >= check.RequiredBytes + check.ReserveBytes;

            check.Passed =
                check.TargetExists &&
                check.TargetWritable &&
                check.SpaceKnown &&
                enoughSpace &&
                check.MissingSources == 0 &&
                check.TargetConflicts == 0 &&
                !check.PlanPathsChanged &&
                movable.Count > 0;

            return check;
        }

        private string BuildSafetyFailureMessage(ExecutionSafetyCheck check)
        {
            StringBuilder message = new StringBuilder();
            message.AppendLine(L("Status.SafetyFailedHeader"));
            message.AppendLine();

            if (!check.TargetExists)
                message.AppendLine("• " + L("Status.SafetyTargetUnavailable"));
            if (!check.TargetWritable)
                message.AppendLine("• " + L("Status.SafetyTargetNotWritable"));
            if (check.PlanPathsChanged)
                message.AppendLine("• " + L("Status.SafetyPathsChanged"));
            if (check.MissingSources > 0)
                message.AppendLine("• " + LF("Status.SafetyMissingSources", check.MissingSources));
            if (check.TargetConflicts > 0)
                message.AppendLine("• " + LF("Status.SafetyTargetConflicts", check.TargetConflicts));
            if (!check.SpaceKnown)
            {
                message.AppendLine("• " + L("Status.SafetySpaceUnavailable"));
            }
            else if (check.FreeBytes < check.RequiredBytes + check.ReserveBytes)
            {
                long deficit =
                    check.RequiredBytes + check.ReserveBytes - check.FreeBytes;

                message.AppendLine("• " + L("Status.SafetySpaceInsufficient"));
                message.AppendLine("  " + LF("Status.SafetyBatch", FormatBytes(check.BatchBytes)));
                message.AppendLine("  " + LF("Status.SafetyRequired", FormatBytes(check.RequiredBytes)));
                message.AppendLine("  " + LF("Status.SafetyAvailable", FormatBytes(check.FreeBytes)));
                message.AppendLine("  " + LF("Status.SafetyReserve", FormatBytes(check.ReserveBytes)));
                message.AppendLine("  " + LF("Status.SafetyDeficit", FormatBytes(deficit)));
            }

            if (!String.IsNullOrWhiteSpace(check.Detail))
            {
                message.AppendLine();
                message.AppendLine(LF("Status.SafetyDetail", check.Detail));
            }

            message.AppendLine();
            message.Append(L("Status.SafetyAction"));
            return message.ToString();
        }

        private void RefreshExecutionSafetyUi()
        {
            if (_lblSpaceStatus == null || _btnExecute == null)
                return;

            _executionSafetyAllowsMove = false;

            List<PlanItem> movable =
                _plan.Where(delegate(PlanItem p) { return p != null && p.CanMove; })
                    .ToList();
            int reviewCount = CountUnresolvedItems();

            UpdatePrimaryActionPresentation();

            // When nothing can be moved but review items exist, the primary
            // button becomes a navigation action. It must not be blocked by
            // destination/space checks because it does not execute file moves.
            if (movable.Count == 0 && reviewCount > 0)
            {
                _lblSpaceStatus.Text = LF("Status.ReviewOnlyAction", reviewCount);
                _lblSpaceStatus.ForeColor = UiStyle.Accent;
                if (!_isExecuting && !_isScanning)
                    _btnExecute.Enabled = true;
                UpdateMainActionAvailability();
                return;
            }

            ExecutionSafetyCheck check =
                EvaluateExecutionSafety(movable, false);

            if (!check.TargetExists)
            {
                _lblSpaceStatus.Text = L("Status.SpaceTargetUnavailable");
                _lblSpaceStatus.ForeColor = UiStyle.Danger;
                if (!_isExecuting && !_isScanning) _btnExecute.Enabled = false;
                UpdateMainActionAvailability();
                return;
            }

            if (check.PlanPathsChanged)
            {
                _lblSpaceStatus.Text = L("Status.SpaceRescanRequired");
                _lblSpaceStatus.ForeColor = UiStyle.Danger;
                if (!_isExecuting && !_isScanning) _btnExecute.Enabled = false;
                UpdateMainActionAvailability();
                return;
            }

            if (!check.SpaceKnown)
            {
                _lblSpaceStatus.Text = L("Status.SpaceUnknown");
                _lblSpaceStatus.ForeColor = UiStyle.Danger;
                if (!_isExecuting && !_isScanning) _btnExecute.Enabled = false;
                UpdateMainActionAvailability();
                return;
            }

            if (movable.Count == 0)
            {
                _lblSpaceStatus.Text =
                    LF("Status.SpaceAvailableOnly", FormatBytes(check.FreeBytes));
                _lblSpaceStatus.ForeColor = UiStyle.Muted;
                if (!_isExecuting && !_isScanning) _btnExecute.Enabled = false;
                UpdateMainActionAvailability();
                return;
            }

            bool enough =
                check.FreeBytes >= check.RequiredBytes + check.ReserveBytes;

            if (check.RequiredBytes == 0)
            {
                _lblSpaceStatus.Text =
                    LF(
                        enough ? "Status.SpaceSameVolume" : "Status.SpaceSameVolumeInsufficient",
                        FormatBytes(check.BatchBytes),
                        FormatBytes(check.FreeBytes));
            }
            else if (check.RequiredBytes < check.BatchBytes)
            {
                _lblSpaceStatus.Text =
                    LF(
                        enough ? "Status.SpaceMixed" : "Status.SpaceMixedInsufficient",
                        FormatBytes(check.BatchBytes),
                        FormatBytes(check.RequiredBytes),
                        FormatBytes(check.FreeBytes));
            }
            else
            {
                _lblSpaceStatus.Text =
                    LF(
                        enough ? "Status.SpaceEnough" : "Status.SpaceInsufficientShort",
                        FormatBytes(check.BatchBytes),
                        FormatBytes(check.FreeBytes));
            }
            _lblSpaceStatus.ForeColor = enough ? Color.DarkGreen : UiStyle.Danger;

            if (!_isExecuting && !_isScanning)
            {
                // Pending review items are deliberately not a hard blocker.
                // Only the files that already have executable plans are checked
                // here; unresolved items stay in place and are skipped this run.
                _executionSafetyAllowsMove =
                    enough &&
                    !check.PlanPathsChanged &&
                    check.MissingSources == 0 &&
                    check.TargetConflicts == 0 &&
                    check.TargetExists &&
                    check.SpaceKnown;
                _btnExecute.Enabled = _executionSafetyAllowsMove;
            }

            UpdateMainActionAvailability();
        }

        private void ExecuteMove()
        {
            if (_isExecuting || _isScanning)
                return;

            CloseOverlay(true);

            List<PlanItem> movable =
                _plan.Where(
                    delegate(PlanItem p)
                    {
                        return p.CanMove;
                    })
                .ToList();

            int reviewCount = CountUnresolvedItems();
            if (movable.Count == 0)
            {
                if (reviewCount > 0)
                    OpenNeedsReview();
                return;
            }

            ExecutionSafetyCheck safetyCheck =
                EvaluateExecutionSafety(movable, true);

            if (!safetyCheck.Passed)
            {
                RefreshExecutionSafetyUi();
                UiMessageBox.Show(
                    this,
                    BuildSafetyFailureMessage(safetyCheck),
                    L("Status.SafetyTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (reviewCount > 0)
            {
                using (ExecutionReviewDialog dlg =
                    new ExecutionReviewDialog(
                        _language,
                        Font,
                        movable.Count,
                        reviewCount))
                {
                    dlg.ShowDialog(this);
                    if (dlg.Choice == ExecutionReviewChoice.HandleReview)
                    {
                        OpenNeedsReview();
                        return;
                    }
                    if (dlg.Choice != ExecutionReviewChoice.OrganizeAndSkip)
                        return;
                }
            }
            else
            {
                using (ExecutionConfirmDialog dlg =
                    new ExecutionConfirmDialog(
                        _language,
                        Font,
                        movable.Count))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.Confirmed)
                        return;
                }
            }

            Dictionary<PlanItem, HistoryItem> historyItems;
            HistorySession historySession =
                CreateHistorySession(
                    movable,
                    out historyItems);

            ResetExecutionProgress();
            _scanWarmup.Cancel();
            SetExecutionUiLocked(true);

            _lblProgressFile.Text =
                LF("Status.ExecutePreparing", movable.Count);
            _lblProgressPath.Text = "";
            _lblStatus.Text =
                L("Status.ExecuteRunning");

            BackgroundWorker worker =
                new BackgroundWorker();

            worker.WorkerReportsProgress =
                true;

            worker.DoWork +=
                delegate(
                    object sender,
                    DoWorkEventArgs e)
                {
                    BackgroundWorker bg =
                        (BackgroundWorker)sender;

                    MoveRunResult result =
                        new MoveRunResult();

                    int total =
                        movable.Count;

                    Dictionary<PlanItem, long> fileLengths =
                        new Dictionary<PlanItem, long>();
                    long totalBytes = 0;
                    long remainingCrossBytes = 0;
                    long reserveBytes = GetSafetyReserveBytes();

                    foreach (PlanItem item in movable)
                    {
                        long length = 0;
                        try
                        {
                            if (File.Exists(item.SourcePath))
                                length = new FileInfo(item.SourcePath).Length;
                        }
                        catch
                        {
                            length = 0;
                        }

                        fileLengths[item] = length;
                        totalBytes += length;
                        if (!ExecutionSafety.IsSameVolume(item.SourcePath, item.TargetPath))
                            remainingCrossBytes += length;
                    }

                    for (
                        int i = 0;
                        i < total;
                        i++)
                    {
                        PlanItem p =
                            movable[i];

                        HistoryItem historyItem = null;
                        historyItems.TryGetValue(
                            p,
                            out historyItem);

                        long fileLength =
                            fileLengths.ContainsKey(p)
                                ? fileLengths[p]
                                : 0;
                        bool crossVolume =
                            !ExecutionSafety.IsSameVolume(
                                p.SourcePath,
                                p.TargetPath);

                        long currentFree = -1;
                        string currentSpaceError;
                        ExecutionSafety.TryGetAvailableSpace(
                            p.TargetPath,
                            out currentFree,
                            out currentSpaceError);

                        int before =
                            (int)Math.Floor(
                                i * 100.0 /
                                total);

                        MoveProgressInfo beforeInfo =
                            new MoveProgressInfo();

                        beforeInfo.Current = i + 1;
                        beforeInfo.Total = total;
                        beforeInfo.FileName =
                            p.FileName;
                        beforeInfo.SourcePath =
                            p.SourcePath;
                        beforeInfo.TargetPath =
                            p.TargetPath;
                        beforeInfo.Phase =
                            L("Status.Moving");
                        beforeInfo.ProcessedBytes =
                            result.ProcessedBytes;
                        beforeInfo.TotalBytes = totalBytes;
                        beforeInfo.TargetFreeBytes = currentFree;

                        bg.ReportProgress(
                            before,
                            beforeInfo);

                        // Before every cross-volume copy, ensure the complete
                        // remaining batch still fits. If another application
                        // consumed target space after preflight, stop before
                        // starting another file.
                        if (crossVolume)
                        {
                            long freeNow;
                            string freeError;
                            bool freeKnown =
                                ExecutionSafety.TryGetAvailableSpace(
                                    p.TargetPath,
                                    out freeNow,
                                    out freeError);

                            if (!freeKnown ||
                                freeNow < remainingCrossBytes + reserveBytes)
                            {
                                result.Aborted = true;
                                result.AbortMessage =
                                    freeKnown
                                        ? LF(
                                            "Status.ExecutionSpaceStopped",
                                            FormatBytes(remainingCrossBytes),
                                            FormatBytes(freeNow),
                                            FormatBytes(reserveBytes))
                                        : LF(
                                            "Status.ExecutionSpaceReadFailed",
                                            freeError);

                                MoveProgressInfo stopInfo =
                                    new MoveProgressInfo();
                                stopInfo.Current = i + 1;
                                stopInfo.Total = total;
                                stopInfo.FileName = p.FileName;
                                stopInfo.SourcePath = p.SourcePath;
                                stopInfo.TargetPath = p.TargetPath;
                                stopInfo.Phase = L("Status.NotRunSafetyStop");
                                stopInfo.ProcessedBytes = result.ProcessedBytes;
                                stopInfo.TotalBytes = totalBytes;
                                stopInfo.TargetFreeBytes = freeKnown ? freeNow : -1;
                                bg.ReportProgress(before, stopInfo);

                                for (int j = i; j < total; j++)
                                {
                                    HistoryItem notRun = null;
                                    historyItems.TryGetValue(movable[j], out notRun);
                                    if (notRun != null)
                                    {
                                        notRun.ResultCode = "not-run";
                                        notRun.ResultMessage = result.AbortMessage;
                                    }
                                }

                                result.Skipped += total - i;
                                break;
                            }
                        }

                        bool stopAfterCurrent = false;
                        string originalStatus = p.Status;
                        string resultPhase = p.Status;

                        try
                        {
                            if (!File.Exists(
                                    p.SourcePath))
                            {
                                p.Status =
                                    L("Status.SourceMissingDuringMove");
                                p.CanMove = false;
                                resultPhase = p.Status;
                                result.Failed++;
                                result.Aborted = true;
                                result.AbortMessage = p.Status;
                                stopAfterCurrent = true;

                                if (historyItem != null)
                                {
                                    historyItem.ResultCode = "failed";
                                    historyItem.ResultMessage = p.Status;
                                }
                            }
                            else if (File.Exists(
                                         p.TargetPath) ||
                                     Directory.Exists(
                                         p.TargetPath) ||
                                     File.Exists(
                                         p.TargetPath + ".moving") ||
                                     Directory.Exists(
                                         p.TargetPath + ".moving"))
                            {
                                p.Status =
                                    L("Status.TargetExistsDuringMove");
                                p.CanMove = false;
                                resultPhase = p.Status;
                                result.Skipped++;
                                result.Aborted = true;
                                result.AbortMessage = p.Status;
                                stopAfterCurrent = true;

                                if (historyItem != null)
                                {
                                    historyItem.ResultCode = "skipped";
                                    historyItem.ResultMessage = p.Status;
                                }
                            }
                            else
                            {
                                if (!Directory.Exists(
                                        p.TargetDir))
                                {
                                    Directory.CreateDirectory(
                                        p.TargetDir);
                                }

                                ExecutionSafety.SafeMoveFile(
                                    p.SourcePath,
                                    p.TargetPath,
                                    reserveBytes,
                                    crossVolume
                                        ? remainingCrossBytes
                                        : 0L);

                                p.SourcePath =
                                    p.TargetPath;
                                p.Status =
                                    L("Status.Moved");
                                p.CanMove =
                                    false;
                                resultPhase = p.Status;
                                result.Success++;
                                result.ProcessedBytes += fileLength;

                                if (crossVolume)
                                    remainingCrossBytes = Math.Max(0L, remainingCrossBytes - fileLength);

                                if (historyItem != null)
                                {
                                    historyItem.ResultCode = "moved";
                                    historyItem.ResultMessage = p.Status;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            string failureText =
                                LF("Status.FailedPrefix", ex.Message);

                            // SafeMoveFile keeps the source authoritative on
                            // failure. Preserve the original recognition status
                            // so the item can be retried after the cause is fixed.
                            p.Status = originalStatus;
                            p.CanMove =
                                File.Exists(p.SourcePath) &&
                                !File.Exists(p.TargetPath) &&
                                !Directory.Exists(p.TargetPath) &&
                                !File.Exists(p.TargetPath + ".moving") &&
                                !Directory.Exists(p.TargetPath + ".moving");
                            resultPhase = failureText;
                            result.Failed++;
                            result.Aborted = true;
                            result.AbortMessage =
                                LF("Status.ExecutionSafetyStopped", ex.Message);
                            stopAfterCurrent = true;

                            if (historyItem != null)
                            {
                                historyItem.ResultCode = "failed";
                                historyItem.ResultMessage = failureText;
                            }
                        }

                        result.ProcessedCount++;

                        long freeAfter = -1;
                        string freeAfterError;
                        ExecutionSafety.TryGetAvailableSpace(
                            p.TargetPath,
                            out freeAfter,
                            out freeAfterError);

                        int after =
                            (int)Math.Round(
                                (i + 1) * 100.0 /
                                total);

                        MoveProgressInfo afterInfo =
                            new MoveProgressInfo();

                        afterInfo.Current = i + 1;
                        afterInfo.Total = total;
                        afterInfo.FileName =
                            p.FileName;
                        afterInfo.SourcePath =
                            p.SourcePath;
                        afterInfo.TargetPath =
                            p.TargetPath;
                        afterInfo.Phase =
                            resultPhase;
                        afterInfo.ProcessedBytes =
                            result.ProcessedBytes;
                        afterInfo.TotalBytes = totalBytes;
                        afterInfo.TargetFreeBytes = freeAfter;

                        bg.ReportProgress(
                            after,
                            afterInfo);

                        if (stopAfterCurrent)
                        {
                            int remaining = total - (i + 1);
                            if (remaining > 0)
                            {
                                result.Skipped += remaining;
                                for (int j = i + 1; j < total; j++)
                                {
                                    HistoryItem notRun = null;
                                    historyItems.TryGetValue(movable[j], out notRun);
                                    if (notRun != null)
                                    {
                                        notRun.ResultCode = "not-run";
                                        notRun.ResultMessage = result.AbortMessage;
                                    }
                                }
                            }
                            break;
                        }
                    }

                    e.Result = result;
                };

            worker.ProgressChanged +=
                delegate(
                    object sender,
                    ProgressChangedEventArgs e)
                {
                    UpdateExecutionProgress(
                        e.ProgressPercentage,
                        e.UserState
                            as MoveProgressInfo);
                };

            worker.RunWorkerCompleted +=
                delegate(
                    object sender,
                    RunWorkerCompletedEventArgs e)
                {
                    SetExecutionUiLocked(false);
                    _engine.InvalidateTargetDirectoryCache();
                    ScheduleScanWarmup(false);

                    if (e.Error != null)
                    {
                        _lblProgressFile.Text =
                            L("Status.ExecuteUnhandled");
                        _lblProgressPath.Text =
                            e.Error.Message;
                        _lblStatus.Text =
                            LF("Status.ExecuteFailed", e.Error.Message);

                        RefreshExecutionSafetyUi();

                        UiMessageBox.Show(
                            this,
                            e.Error.Message,
                            L("Status.ExecuteFailedTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    MoveRunResult result =
                        e.Result
                            as MoveRunResult;

                    int success =
                        result != null
                            ? result.Success
                            : 0;
                    int skipped =
                        result != null
                            ? result.Skipped
                            : 0;
                    int failed =
                        result != null
                            ? result.Failed
                            : movable.Count;

                    historySession.SuccessCount = success;
                    historySession.SkippedCount = skipped;
                    historySession.FailedCount = failed;

                    if (result != null && result.Aborted)
                    {
                        int progress =
                            movable.Count > 0
                                ? (int)Math.Round(
                                    result.ProcessedCount * 100.0 / movable.Count)
                                : 0;
                        _progressBar.Value = Math.Max(0, Math.Min(99, progress));
                        _lblProgressFile.Text = L("Status.ExecutionStopped");
                    }
                    else
                    {
                        _progressBar.Value = 100;
                        _lblProgressFile.Text =
                            LF(
                                "Status.ExecuteComplete",
                                success,
                                skipped,
                                failed);
                    }
                    _lblProgressPath.Text =
                        "";

                    RenderGrid(null);

                    bool historySaved = false;
                    string historyError = "";

                    try
                    {
                        _historyStore.AddSession(
                            historySession);
                        historySaved = true;

                        _lblStatus.Text =
                            LF(
                                "Status.CompleteWithHistory",
                                success,
                                skipped,
                                failed);
                    }
                    catch (Exception historyEx)
                    {
                        historyError =
                            historyEx.Message;

                        _lblStatus.Text =
                            LF(
                                "Status.CompleteHistoryFailed",
                                success,
                                skipped,
                                failed,
                                historyError);
                    }

                    RefreshExecutionSafetyUi();

                    string message;
                    string messageTitle;
                    MessageBoxIcon messageIcon;

                    if (result != null && result.Aborted)
                    {
                        message =
                            LF(
                                "Status.ExecutionStoppedMessage",
                                success,
                                skipped,
                                failed,
                                result.AbortMessage);
                        messageTitle = L("Status.ExecutionStoppedTitle");
                        messageIcon = MessageBoxIcon.Warning;
                    }
                    else
                    {
                        message =
                            historySaved
                                ? LF(
                                    "Status.CompleteMessage",
                                    success,
                                    skipped,
                                    failed)
                                : LF(
                                    "Status.CompleteMessageHistoryFailed",
                                    success,
                                    skipped,
                                    failed,
                                    historyError);
                        messageTitle = L("Status.CompleteTitle");
                        messageIcon =
                            historySaved
                                ? MessageBoxIcon.Information
                                : MessageBoxIcon.Warning;
                    }

                    UiMessageBox.Show(
                        this,
                        message,
                        messageTitle,
                        MessageBoxButtons.OK,
                        messageIcon);
                };

            worker.RunWorkerAsync();
        }

        private HistorySession CreateHistorySession(
            List<PlanItem> items,
            out Dictionary<PlanItem, HistoryItem> map)
        {
            HistorySession session =
                new HistorySession();

            session.Id =
                Guid.NewGuid().ToString("N");
            session.ExecutedAt =
                DateTime.Now.ToString("o");
            session.SourceRoot =
                _txtSource != null
                    ? (_txtSource.Text ?? "").Trim()
                    : "";
            session.TargetRoot =
                _txtRoot != null
                    ? (_txtRoot.Text ?? "").Trim()
                    : "";
            session.ScanRange =
                _currentScanMode.ToString();

            FileTypeProfile profile = GetActiveFileTypeProfile();
            if (profile != null)
            {
                session.FileTypeProfileId = profile.Id ?? "";
                session.FileTypeProfileName = GetFileTypeProfileDisplayName(profile);
            }

            map =
                new Dictionary<PlanItem, HistoryItem>();

            foreach (PlanItem p in items)
            {
                RecognitionVisual visual =
                    RecognitionVisualResolver.Resolve(
                        p);

                HistoryItem item =
                    new HistoryItem();

                item.FileName =
                    p.FileName ?? "";
                item.SourcePath =
                    p.SourcePath ?? "";
                item.TargetPath =
                    p.TargetPath ?? "";
                item.Author =
                    p.Author ?? "";
                item.MatchedAs =
                    p.MatchedAs ?? "";
                item.RecognitionCode =
                    visual != null
                        ? visual.Code ?? ""
                        : "";
                item.MatchWhy =
                    p.MatchWhy ?? "";
                item.ResultCode =
                    "";
                item.ResultMessage =
                    "";

                session.Items.Add(item);
                map[p] = item;
            }

            session.TotalCount =
                session.Items.Count;

            return session;
        }

        private void ShowHistory()
        {
            CloseOverlay(true);

            using (HistoryForm dlg =
                new HistoryForm(
                    _historyStore,
                    _language,
                    Font))
            {
                dlg.ShowDialog(this);
            }
        }

        private void OpenAliasLibrary()
        {
            CloseOverlay(true);
            using (AliasManagerForm dlg = new AliasManagerForm(_aliasLibrary, _language, Font))
            {
                dlg.ShowDialog(this);
            }
            ScheduleScanWarmup(false);
        }
    }
}
