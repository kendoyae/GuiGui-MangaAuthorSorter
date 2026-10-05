using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class LanguagePackInfo
    {
        public string Name = "";
        public string Code = "";
        public string Author = "";
        public int Version = 1;
        public string FilePath = "";
        public Dictionary<string, string> Strings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public override string ToString()
        {
            return String.IsNullOrWhiteSpace(Name) ? Code : Name;
        }
    }

    internal sealed class LanguagePackCheck
    {
        public string Name = "";
        public string Code = "";
        public int Translated;
        public int Total;

        public int Percent
        {
            get
            {
                if (Total <= 0) return 100;
                return (int)Math.Round((Translated * 100.0) / Total);
            }
        }
    }

    internal sealed class LanguageManager
    {
        private const int BuiltInLanguageVersion = 45;

        private readonly string _directory;
        private readonly Dictionary<string, string> _zh =
            CreateZhStrings();
        private readonly Dictionary<string, string> _en =
            CreateEnStrings();
        private readonly Dictionary<string, string> _de =
            GermanLanguagePack.Create();

        private readonly Dictionary<string, string> _zhValueToKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly List<LanguagePackInfo> _packs =
            new List<LanguagePackInfo>();

        private LanguagePackInfo _current;

        public LanguageManager(string directory)
        {
            _directory = directory;

            foreach (KeyValuePair<string, string> pair in _zh)
            {
                if (!_zhValueToKey.ContainsKey(pair.Value))
                    _zhValueToKey[pair.Value] = pair.Key;
            }
        }

        public string DirectoryPath { get { return _directory; } }
        public string CurrentCode { get { return _current != null ? _current.Code : "zh-CN"; } }
        public string CurrentName { get { return _current != null ? _current.Name : "简体中文"; } }

        public void Initialize(string preferredCode)
        {
            EnsureDefaultFiles();
            ReloadPacks();

            string selected = (preferredCode ?? "").Trim();

            if (selected.Length == 0 || FindPack(selected) == null)
            {
                string system = CultureInfo.CurrentUICulture.Name;
                if (FindPack(system) != null)
                {
                    selected = system;
                }
                else
                {
                    string neutral = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    LanguagePackInfo neutralPack = _packs.FirstOrDefault(
                        delegate(LanguagePackInfo p)
                        {
                            return p.Code.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase);
                        });

                    if (neutralPack != null)
                        selected = neutralPack.Code;
                    else if (String.Equals(neutral, "zh", StringComparison.OrdinalIgnoreCase))
                        selected = "zh-CN";
                    else
                        selected = "en-US";
                }
            }

            _current = FindPack(selected) ?? FindPack("en-US") ?? FindPack("zh-CN");

            if (_current == null)
            {
                _current = new LanguagePackInfo();
                _current.Code = "zh-CN";
                _current.Name = "简体中文";
                _current.Author = "Built-in";
                _current.Strings = new Dictionary<string, string>(_zh, StringComparer.Ordinal);
            }

            UiMessageBox.Configure(this);
        }

        public IList<LanguagePackInfo> GetPacks()
        {
            return _packs.OrderBy(
                delegate(LanguagePackInfo p)
                {
                    if (String.Equals(p.Code, "zh-CN", StringComparison.OrdinalIgnoreCase)) return "0_" + p.Name;
                    if (String.Equals(p.Code, "en-US", StringComparison.OrdinalIgnoreCase)) return "1_" + p.Name;
                    if (String.Equals(p.Code, "de-DE", StringComparison.OrdinalIgnoreCase)) return "2_" + p.Name;
                    return "3_" + p.Name;
                }).ToList();
        }

        public string Get(string key)
        {
            if (String.IsNullOrWhiteSpace(key))
                return "";

            string value;

            if (_current != null &&
                _current.Strings != null &&
                _current.Strings.TryGetValue(key, out value) &&
                !String.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            LanguagePackInfo english = FindPack("en-US");
            if (english != null &&
                english.Strings.TryGetValue(key, out value) &&
                !String.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (_en.TryGetValue(key, out value) &&
                !String.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            LanguagePackInfo chinese = FindPack("zh-CN");
            if (chinese != null &&
                chinese.Strings.TryGetValue(key, out value) &&
                !String.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (_zh.TryGetValue(key, out value) &&
                !String.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return key;
        }

        public string Format(string key, params object[] args)
        {
            try
            {
                return String.Format(CultureInfo.CurrentCulture, Get(key), args ?? new object[0]);
            }
            catch
            {
                return Get(key);
            }
        }

        // Compatibility bridge for V1.7-era UI text.
        // New UI code should prefer Get("Some.Key") directly.
        public string TranslateSource(string source)
        {
            if (source == null)
                return "";

            string key;
            if (_zhValueToKey.TryGetValue(source, out key))
                return Get(key);

            return source;
        }

        public List<LanguagePackCheck> CheckPacks()
        {
            List<LanguagePackCheck> result = new List<LanguagePackCheck>();
            int total = _zh.Count;

            foreach (LanguagePackInfo pack in GetPacks())
            {
                int translated = 0;
                foreach (string key in _zh.Keys)
                {
                    string value;
                    if (pack.Strings != null &&
                        pack.Strings.TryGetValue(key, out value) &&
                        !String.IsNullOrWhiteSpace(value))
                    {
                        translated++;
                    }
                }

                result.Add(new LanguagePackCheck
                {
                    Name = pack.Name,
                    Code = pack.Code,
                    Translated = translated,
                    Total = total
                });
            }

            return result;
        }

        private LanguagePackInfo FindPack(string code)
        {
            return _packs.FirstOrDefault(
                delegate(LanguagePackInfo p)
                {
                    return String.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase);
                });
        }

        private void ReloadPacks()
        {
            _packs.Clear();

            if (Directory.Exists(_directory))
            {
                foreach (string file in Directory.GetFiles(_directory, "*.json"))
                {
                    if (String.Equals(Path.GetFileName(file), "_template.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        LanguagePackInfo pack = LoadPack(file);
                        if (pack == null || String.IsNullOrWhiteSpace(pack.Code))
                            continue;

                        LanguagePackInfo existing = FindPack(pack.Code);
                        if (existing != null)
                            _packs.Remove(existing);

                        _packs.Add(pack);
                    }
                    catch
                    {
                        // A broken third-party language pack must never block startup.
                    }
                }
            }

            EnsureInMemoryBuiltin("zh-CN", "简体中文", "Official", _zh);
            EnsureInMemoryBuiltin("en-US", "English", "Official", _en);
            EnsureInMemoryBuiltin("de-DE", "Deutsch", "Official", _de);
        }

        private void EnsureInMemoryBuiltin(
            string code,
            string name,
            string author,
            Dictionary<string, string> strings)
        {
            LanguagePackInfo pack = FindPack(code);
            if (pack == null)
            {
                pack = new LanguagePackInfo();
                pack.Code = code;
                pack.Name = name;
                pack.Author = author;
                pack.Version = 1;
                pack.Strings = new Dictionary<string, string>(strings, StringComparer.Ordinal);
                _packs.Add(pack);
                return;
            }

            foreach (KeyValuePair<string, string> pair in strings)
            {
                string value;
                if (!pack.Strings.TryGetValue(pair.Key, out value) ||
                    String.IsNullOrWhiteSpace(value))
                {
                    pack.Strings[pair.Key] = pair.Value;
                }
            }
        }

        private LanguagePackInfo LoadPack(string path)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;

            Dictionary<string, object> root =
                serializer.Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(path, Encoding.UTF8));

            if (root == null)
                return null;

            LanguagePackInfo pack = new LanguagePackInfo();
            pack.FilePath = path;

            object metaObject;
            if (root.TryGetValue("meta", out metaObject))
            {
                Dictionary<string, object> meta =
                    metaObject as Dictionary<string, object>;

                if (meta != null)
                {
                    pack.Name = ReadString(meta, "name");
                    pack.Code = ReadString(meta, "code");
                    pack.Author = ReadString(meta, "author");

                    object version;
                    if (meta.TryGetValue("version", out version))
                    {
                        int parsed;
                        if (Int32.TryParse(Convert.ToString(version, CultureInfo.InvariantCulture), out parsed))
                            pack.Version = parsed;
                    }
                }
            }

            object stringsObject;
            if (root.TryGetValue("strings", out stringsObject))
            {
                Dictionary<string, object> values =
                    stringsObject as Dictionary<string, object>;

                if (values != null)
                {
                    foreach (KeyValuePair<string, object> pair in values)
                    {
                        pack.Strings[pair.Key] =
                            pair.Value == null ? "" : Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                    }
                }
            }

            if (String.IsNullOrWhiteSpace(pack.Name))
                pack.Name = pack.Code;

            return pack;
        }

        private static string ReadString(
            Dictionary<string, object> source,
            string key)
        {
            object value;
            if (!source.TryGetValue(key, out value) || value == null)
                return "";

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private void EnsureDefaultFiles()
        {
            try
            {
                Directory.CreateDirectory(_directory);

                string zhPath = Path.Combine(_directory, "zh-CN.json");
                string enPath = Path.Combine(_directory, "en-US.json");
                string dePath = Path.Combine(_directory, "de-DE.json");
                string templatePath = Path.Combine(_directory, "_template.json");
                string readmePath = Path.Combine(_directory, "README.txt");

                // 官方语言包由程序维护版本。未来新增多语言字段时，
                // 只要把新 Key 同时加入 CreateZhStrings/CreateEnStrings，
                // 启动时会自动把官方语言 JSON 同步到最新结构。
                SynchronizeOfficialPack(
                    zhPath,
                    "简体中文",
                    "zh-CN",
                    "Official",
                    _zh);

                SynchronizeOfficialPack(
                    enPath,
                    "English",
                    "en-US",
                    "Official",
                    _en);

                SynchronizeOfficialPack(
                    dePath,
                    "Deutsch",
                    "de-DE",
                    "Official",
                    _de);

                // 翻译模板只补充“缺少的 Key”，不删除旧 Key，方便社区翻译者
                // 在升级后直接看到新增字段；第三方语言包本身不会被程序改写。
                SynchronizeTemplate(templatePath);

                if (!File.Exists(readmePath))
                {
                    File.WriteAllText(
                        readmePath,
                        "Language Packs / 语言包说明\r\n\r\n" +
                        "Copy _template.json, rename it to a locale code such as ja-JP.json, translate only values under strings, and save as UTF-8.\r\n" +
                        "UI labels and buttons should be accurate, natural and concise; do not impose character-count limits or shrink fonts to fit long languages.\r\n" +
                        "Buttons use fixed height plus text-measured width; DataGridView short enum columns use automatic sizing and long text columns use Fill. Headers stay on one line.\r\n" +
                        "Deutsch (de-DE) is an official language pack and also the long-text UI test locale. Keep UI translations accurate, natural and as concise as possible. See Docs/UI_I18N_RULES.md in the source package.\r\n\r\n" +
                        "复制 _template.json，改名为 ja-JP.json、ko-KR.json、fr-FR.json 等语言地区代码；只翻译 strings 右侧内容，并统一使用 UTF-8 保存。\r\n" +
                        "UI 翻译应准确、自然、简洁，不限制字符数，也禁止通过缩小字体解决长语言。按钮按文本实际像素宽度自适应；表格短枚举列自动宽度，长文本列 Fill，表头保持单行。\r\n" +
                        "de-DE 为正式德语语言包，同时作为长文本 UI 测试语言。多语言翻译必须准确、自然并尽量短小精炼。\r\n",
                        new UTF8Encoding(true));
                }
            }
            catch
            {
                // Language files are optional at runtime because built-in fallbacks remain available.
            }
        }

        private void SynchronizeOfficialPack(
            string path,
            string name,
            string code,
            string author,
            Dictionary<string, string> strings)
        {
            bool rewrite = !File.Exists(path);

            if (!rewrite)
            {
                try
                {
                    LanguagePackInfo existing = LoadPack(path);
                    rewrite =
                        existing == null ||
                        existing.Version < BuiltInLanguageVersion ||
                        !String.Equals(existing.Code, code, StringComparison.OrdinalIgnoreCase) ||
                        strings.Keys.Any(
                            delegate(string key)
                            {
                                return existing.Strings == null ||
                                    !existing.Strings.ContainsKey(key);
                            });
                }
                catch
                {
                    rewrite = true;
                }
            }

            if (rewrite)
            {
                WritePackFile(
                    path,
                    name,
                    code,
                    author,
                    strings,
                    BuiltInLanguageVersion);
            }
        }

        private void SynchronizeTemplate(string path)
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.Ordinal);

            string name = "";
            string code = "";
            string author = "";
            bool changed = !File.Exists(path);

            if (File.Exists(path))
            {
                try
                {
                    LanguagePackInfo existing = LoadPack(path);
                    if (existing != null)
                    {
                        name = existing.Name ?? "";
                        code = existing.Code ?? "";
                        author = existing.Author ?? "";

                        if (existing.Strings != null)
                        {
                            foreach (KeyValuePair<string, string> pair in existing.Strings)
                                values[pair.Key] = pair.Value ?? "";
                        }

                        if (existing.Version < BuiltInLanguageVersion)
                            changed = true;
                    }
                }
                catch
                {
                    values.Clear();
                    changed = true;
                }
            }

            foreach (string key in _zh.Keys)
            {
                if (!values.ContainsKey(key))
                {
                    values[key] = "";
                    changed = true;
                }
            }

            if (changed)
            {
                WritePackFile(
                    path,
                    name,
                    code,
                    author,
                    values,
                    BuiltInLanguageVersion);
            }
        }

        private static void WritePackFile(
            string path,
            string name,
            string code,
            string author,
            Dictionary<string, string> strings,
            int version)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;

            Dictionary<string, object> meta = new Dictionary<string, object>();
            meta["name"] = name;
            meta["code"] = code;
            meta["author"] = author;
            meta["version"] = version;

            Dictionary<string, object> root = new Dictionary<string, object>();
            root["meta"] = meta;
            root["strings"] = strings;

            string json = serializer.Serialize(root);
            File.WriteAllText(path, json, new UTF8Encoding(true));
        }

        private static Dictionary<string, string> CreateZhStrings()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "App.Title", "归归" },
                { "App.Name", "归归" },
                { "Common.Browse", "浏览..." },
                { "Common.Save", "保存" },
                { "Common.Cancel", "取消" },
                { "Common.OK", "确定" },
                { "Common.Close", "关闭" },
                { "Common.Edit", "编辑" },
                { "Common.Yes", "是" },
                { "Common.No", "否" },
                { "Common.DeleteSelected", "删除选中" },
                { "Common.Error.OpenFailed", "打开失败" },
                { "Common.Error.SaveFailed", "保存失败" },
                { "Common.NotSet", "未设置" },
                { "Common.EmptyValue", "—" },
                { "GridMenu.AutoFit", "自动调整列宽" },
                { "GridMenu.ResetWidths", "恢复默认列宽" },
                { "GridMenu.Columns", "显示/隐藏列" },
                { "DataFile.AuthorAliases.Title", "归归 - 作者别名库" },
                { "DataFile.AuthorAliases.Format", "格式：标准作者名|别名1|别名2|别名3" },
                { "DataFile.AuthorAliases.SameAuthor", "同一行只填写确认属于同一作者的名称。" },
                { "DataFile.AuthorAliases.AutoUpdate", "手动指定作者文件夹时，程序会自动更新此文件。" },
                { "DataFile.ExclusionList.Title", "归归 - 排除列表" },
                { "DataFile.ExclusionList.PathRule", "每行填写一个文件的完整路径。" },
                { "DataFile.ExclusionList.Description", "此列表中的文件不会参与扫描。" },
                { "DataFile.ExclusionList.ManageHint", "建议通过程序中的“排除列表”功能管理此文件。" },
                { "Main.SourcePath", "原始位置：" },
                { "Main.TargetPath", "迁移位置：" },
                { "Main.ScanMode", "扫描范围：" },
                { "Main.ScanPreview", "扫描" },
                { "Main.StopScan", "停止扫描" },
                { "Main.StoppingScan", "正在停止…" },
                { "Main.Execute", "整理" },
                { "Action.HandleReviewCount", "待确认（{0}）" },
                { "Main.ScanCount", "数量：" },
                { "Main.ScanCountHint", "0 = 全部；筛选扫描时表示目标结果数；最新 → 最旧" },
                { "Main.Recursive", "扫描子目录" },
                { "Main.FileType", "文件类型方案：" },
                { "Main.OperationHint", "提示：双击“文件名”可重命名；双击“识别依据”可指定作者；Delete 仅从当前预览移除。修改后的文件不会自动退出当前列表。" },
                { "Main.InitialStatus", "第 1 步：请选择原始位置。" },
                { "Status.GuideSelectSource", "第 1 步：请选择原始位置。" },
                { "Status.GuideSelectTarget", "第 2 步：请选择迁移位置。" },
                { "Status.GuideScanPreview", "下一步：点击“扫描”开始识别。" },
                { "Main.ExecutionProgress", "执行进度" },
                { "Main.Waiting", "等待执行。" },
                { "Main.TargetPrefix", "目标：" },
                { "Nav.Review", "待确认" },
                { "Nav.History", "历史记录" },
                { "History.Title", "历史记录" },
                { "History.Empty", "暂无整理历史。完成一次整理后，本次操作会自动保存到这里。" },
                { "History.Time", "执行时间" },
                { "History.Total", "文件数" },
                { "History.Success", "成功" },
                { "History.Skipped", "跳过" },
                { "History.Failed", "失败" },
                { "History.Source", "原始位置" },
                { "History.Target", "迁移位置" },
                { "History.FileName", "文件名" },
                { "History.Author", "识别作者" },
                { "History.Matched", "匹配作者" },
                { "History.Status", "状态" },
                { "History.Result", "执行结果" },
                { "History.ResultMessage", "结果说明" },
                { "History.SourcePath", "原始文件" },
                { "History.TargetPath", "目标文件" },
                { "History.Reason", "识别依据" },
                { "History.ExportCsv", "导出 CSV..." },
                { "History.DeleteRecord", "删除此记录" },
                { "History.ClearAll", "清空历史" },
                { "History.DeleteConfirm", "确定删除选中的这次历史记录吗？\r\n不会删除或移动任何实际文件。" },
                { "History.ClearConfirm", "确定清空全部历史记录吗？\r\n不会删除或移动任何实际文件。" },
                { "History.ExportSuccess", "已导出：\r\n{0}" },
                { "History.ExportFailed", "导出失败：{0}" },
                { "History.LoadFailed", "读取历史记录失败" },
                { "History.Summary", "文件类型方案：{0}    扫描范围：{1}    文件：{2}    成功：{3}    跳过：{4}    失败：{5}" },
                { "History.ResultMoved", "已移动" },
                { "History.ResultSkipped", "已跳过" },
                { "History.ResultFailed", "失败" },
                { "Filter.All", "全部" },
                { "Filter.Matched", "已匹配" },
                { "Filter.NewAuthor", "新作者" },
                { "Filter.Ambiguous", "歧义" },
                { "Filter.Unrecognized", "未识别" },
                { "Filter.Excluded", "已排除" },
                { "Filter.Duplicate", "重复" },
                { "Filter.ViewLabel", "视图：" },
                { "Filter.Search", "搜索：" },
                { "Details.Title", "处理预览" },
                { "Details.Empty", "选择一个文件后，在这里查看处理方式和需要执行的操作。" },
                { "Details.Multiple", "已选择 {0} 个文件。可通过右键菜单执行批量操作。" },
                { "Details.FileName", "文件名" },
                { "Details.Status", "状态" },
                { "Details.Author", "识别作者" },
                { "Details.Matched", "匹配作者" },
                { "Details.Reason", "识别依据" },
                { "Details.Target", "目标位置" },
                { "Details.Source", "原始文件" },
                { "Details.Process", "处理方式" },
                { "Details.Result", "处理结果" },
                { "Details.MultipleCandidates", "多个候选" },
                { "Details.NoActionNeeded", "可直接整理" },
                { "Details.Process.Existing", "归入已有作者" },
                { "Details.Process.NewFolder", "新建作者文件夹" },
                { "Details.Process.NewReuse", "复用本次作者文件夹" },
                { "Details.Process.Ambiguous", "等待用户确认" },
                { "Details.Process.Unrecognized", "等待用户指定" },
                { "Details.Process.Excluded", "跳过" },
                { "Details.Process.Manual", "按指定作者归档" },
                { "Details.Process.AlreadyThere", "无需移动" },
                { "Details.Process.TargetExists", "等待处理冲突" },
                { "Details.Result.Ready", "可直接整理" },
                { "Details.Result.NewFolder", "将创建作者文件夹" },
                { "Details.Result.Ambiguous", "需要确认作者" },
                { "Details.Result.Unrecognized", "需要指定作者或跳过" },
                { "Details.Result.Excluded", "不会参与整理" },
                { "Details.Result.ExcludedRule", "不会参与整理\r\n规则：{0}" },
                { "Details.Result.TargetExists", "需要处理同名文件" },
                { "Details.Result.AlreadyThere", "无需移动" },
                { "Details.Candidates", "候选：{0}" },
                { "Details.AmbiguousContext", "找到多个可能的作者文件夹。\r\n{0}" },
                { "Details.AmbiguousContextNoCandidates", "找到多个可能的作者匹配，请选择作者后再整理。" },
                { "Details.UnrecognizedContext", "程序无法确定该文件的作者，不会自动整理。" },
                { "Details.ExcludedContext", "命中扫描排除规则：{0}\r\n该项目不会参与整理。" },
                { "Details.TargetExistsContext", "目标位置已有同名文件，当前项目不会自动移动。" },
                { "Details.AssignAuthor", "指定作者..." },
                { "Details.AssignOtherAuthor", "指定其他作者..." },
                { "Details.ChooseAuthor", "选择作者..." },
                { "Details.ManageExclusionRules", "排除规则..." },
                { "Details.Exclude", "排除此文件" },
                { "Details.AlreadyExcluded", "已排除" },
                { "Details.AmbiguousHint", "检测到多个可能的作者匹配。请点击“指定作者...”确认并锁定此文件的归档作者。" },
                { "Details.UnrecognizedHint", "未能自动识别作者。请先双击主列表中的“文件名”修正作者信息；识别成功后可再指定作者。" },
                { "Details.ExcludedHint", "该项目命中全局扫描排除规则，仅供查看，不参与作者识别、在线查询或整理。" },
                { "Details.Toggle", "详情" },
                { "Details.Show", "显示详情" },
                { "Details.Hide", "隐藏详情" },
                { "Dialog.ManualAuthorName.Title", "指定作者" },
                { "Dialog.ManualAuthorName.Description", "文件名未能识别出作者。请输入确认后的作者名称，程序会重新规划为已有作者或新作者。" },
                { "Dialog.ManualAuthorName.Label", "作者名：" },
                { "Dialog.ManualAuthorName.Empty", "作者名不能为空。" },
                { "Dialog.ManualAuthorName.Invalid", "作者名包含 Windows 文件夹名称不允许的字符，或以句点/空格结尾。" },
                { "Status.ManualAuthorNamed", "已指定作者：{0}；当前项目已重新规划。" },
                { "Dialog.ExecuteWithReview.Title", "还有 {0} 个文件需要确认" },
                { "Dialog.ExecuteWithReview.Message", "{0} 个文件已经可以正常整理。\r\n{1} 个文件存在歧义或无法确定作者，本次整理会保持原位并暂时跳过。" },
                { "Dialog.ExecuteWithReview.OrganizeAndSkip", "整理 {0} 个，跳过 {1} 个" },
                { "Dialog.ExecuteWithReview.HandleReview", "处理待确认" },
                { "ScanMode.Global", "全部文件" },
                { "ScanMode.Global.Desc", "全部文件" },
                { "ScanMode.Exact", "已匹配" },
                { "ScanMode.Exact.Desc", "已匹配已有作者" },
                { "ScanMode.New", "新作者" },
                { "ScanMode.New.Desc", "新作者" },
                { "ScanMode.Ambiguous", "歧义" },
                { "ScanMode.Ambiguous.Desc", "存在多个匹配结果" },
                { "ScanMode.Unrecognized", "未识别" },
                { "ScanMode.Unrecognized.Desc", "无法识别作者" },
                { "Menu.File", "文件(&F)" },
                { "Menu.ScanPreview", "扫描(&S)" },
                { "Menu.StopScan", "停止扫描(&S)" },
                { "Menu.StoppingScan", "正在停止扫描…" },
                { "Menu.Execute", "整理(&E)" },
                { "Menu.OpenSource", "打开原始位置" },
                { "Menu.OpenTarget", "打开迁移位置" },
                { "Menu.Exit", "退出(&X)" },
                { "Menu.Settings", "设置(&S)" },
                { "Menu.ArchiveSettings", "归档设置..." },
                { "Menu.FileTypeManager", "文件类型方案..." },
                { "Menu.AliasLibrary", "作者别名库..." },
                { "Menu.BlockList", "排除列表..." },
                { "Menu.Language", "语言" },
                { "Menu.Tools", "工具(&T)" },
                { "Menu.RecheckEverything", "检测 Everything" },
                { "Menu.OpenAppFolder", "打开程序文件夹" },
                { "Menu.OpenLanguageFolder", "打开语言文件夹" },
                { "Menu.CheckLanguagePacks", "检查语言包..." },
                { "Menu.Help", "帮助(&H)" },
                { "Menu.Readme", "使用说明" },
                { "Menu.CheckUpdates", "检查更新" },
                { "Menu.UpdateAvailable", "发现新版本 {0}" },
                { "Menu.About", "关于" },
                { "Menu.Support", "支持项目..." },
                { "Dialog.ArchiveSettings.Title", "归档设置" },
                { "Dialog.ArchiveSettings.Description", "设置作者分组、目录命名和安全预留；下次扫描生效。" },
                { "Dialog.ArchiveSettings.GroupTitle", "作者分组" },
                { "Dialog.ArchiveSettings.Preview", "命名预览：" },
                { "Dialog.ArchiveSettings.MaxAuthors", "每组作者上限：" },
                { "Dialog.ArchiveSettings.GroupTemplate", "分组命名模板：" },
                { "Dialog.ArchiveSettings.GroupPrefix", "名称前缀：" },
                { "Dialog.ArchiveSettings.NumberingRule", "编号规则：" },
                { "Dialog.ArchiveSettings.GroupSuffix", "名称后缀：" },
                { "Dialog.ArchiveSettings.AuthorFolderTitle", "作者目录名称" },
                { "Dialog.ArchiveSettings.AuthorFolderHint", "用 {author} 表示作者名；模板必须且只能包含一个 {author}，仅影响新建目录。" },
                { "Dialog.ArchiveSettings.AuthorTemplate", "命名模板：" },
                { "Dialog.ArchiveSettings.AuthorPreview", "目录预览：" },
                { "Dialog.ArchiveSettings.AuthorSample", "も" },
                { "Dialog.ArchiveSettings.SafetyTitle", "执行安全" },
                { "Dialog.ArchiveSettings.SafetyReserve", "目标盘预留空间：" },
                { "GroupNumbering.Numeric", "数字 1, 2, 3..." },
                { "GroupNumbering.Numeric2", "两位数字 01, 02, 03..." },
                { "GroupNumbering.Numeric3", "三位数字 001, 002, 003..." },
                { "GroupNumbering.AlphaUpper", "大写字母 A, B, C...AA" },
                { "GroupNumbering.AlphaLower", "小写字母 a, b, c...aa" },
                { "Dialog.PreviewPrefix", "预览：" },
                { "Dialog.InvalidGroupTemplate", "分组命名无效" },
                { "Dialog.InvalidAuthorFolderStyle", "作者目录样式无效" },
                { "Dialog.FileType.Title", "文件类型方案" },
                { "Dialog.FileType.Info", "文件类型方案用于筛选扫描时纳入归档列表的文件。每个方案保存一组扩展名，可按需添加压缩包、视频或其他格式；点击“保存并使用”后，下次扫描生效。" },
                { "Dialog.FileType.ProfileGroup", "文件类型方案" },
                { "Dialog.FileType.New", "新建方案" },
                { "Dialog.FileType.Rename", "重命名" },
                { "Dialog.FileType.Duplicate", "复制方案" },
                { "Dialog.FileType.Delete", "删除方案" },
                { "Dialog.FileType.RestoreSystem", "恢复内置方案" },
                { "Dialog.FileType.ExtensionsGroup", "文件扩展名" },
                { "Dialog.FileType.Clear", "清空后缀" },
                { "Dialog.FileType.RestoreProfile", "恢复默认" },
                { "Dialog.FileType.AddExtension", "添加后缀：" },
                { "Dialog.FileType.Add", "添加" },
                { "Dialog.FileType.ExtensionHint", "支持 mp4、.mp4、*.mp4；自动规范并去重。" },
                { "Dialog.FileType.SaveUse", "保存并使用" },
                { "Dialog.ProfileName", "方案名称：" },
                { "Dialog.BlockList.Title", "排除列表" },
                { "Dialog.BlockList.Info", "列表中的文件不会参与扫描。移除排除记录后，对应文件会在下一次扫描重新出现。" },
                { "Dialog.BlockList.FileName", "文件名" },
                { "Dialog.BlockList.FullPath", "完整路径" },
                { "Dialog.BlockList.Status", "当前状态" },
                { "Dialog.BlockList.FileExists", "文件存在" },
                { "Dialog.BlockList.FileMissing", "文件不存在" },
                { "Grid.FileName", "文件名" },
                { "Grid.Author", "识别作者" },
                { "Grid.MatchedAs", "匹配作者" },
                { "Grid.Tag", "标记" },
                { "Grid.Reason", "识别依据" },
                { "Grid.TargetDir", "目标文件夹" },
                { "Grid.Status", "状态" },
                { "GridStatus.AlreadyThere", "已在目标文件夹" },
                { "GridStatus.TargetExists", "目标已存在" },
                { "GridStatus.Excluded", "已排除" },
                { "Grid.Size", "大小" },
                { "Grid.Modified", "修改时间" },
                { "Grid.TagTooltip", "彩色识别标记：精确、标准化、别名、社团、作者、新作者、歧义、手动；[×] 表示无法识别。" },
                { "Recognition.Exact", "精确" },
                { "Recognition.Normalized", "标准化" },
                { "Recognition.Alias", "别名" },
                { "Recognition.Society", "社团" },
                { "Recognition.Author", "作者" },
                { "Recognition.New", "新作者" },
                { "Recognition.Ambiguous", "歧义" },
                { "Recognition.Manual", "手动" },
                { "Recognition.Unrecognized", "无法识别" },
                { "Recognition.ManualAssigned", "手动指定" },
                { "Recognition.AmbiguousChoice", "歧义 / 需选择" },
                { "Recognition.AliasMatch", "别名匹配" },
                { "Recognition.SocietyMatch", "社团匹配" },
                { "Recognition.AuthorMatch", "作者匹配" },
                { "Recognition.ExactMatch", "直接匹配" },
                { "Recognition.DirectMatch", "直接匹配" },
                { "Recognition.NormalizedMatch", "标准化" },
                { "Recognition.NormalizedShort", "标准化" },
                { "Recognition.NewReuse", "新作者复用" },
                { "Recognition.UnrecognizedShort", "未识别" },
                { "FileProfile.Archive", "压缩文件" },
                { "FileProfile.Video", "视频文件" },
                { "FileProfile.Unnamed", "未命名方案" },
                { "Status.ModeChanged", "已切换扫描范围；当前预览列表保持不变。点击旁边的“扫描”后，新范围才会重新筛选。" },
                { "Status.ArchiveSettingsChanged", "归档设置已修改；当前预览列表保持不变。下一次点击“扫描”时生效。" },
                { "Status.EverythingRechecked", "已检测 Everything 状态。" },
                { "Status.EverythingAvailable", "Everything：已连接" },
                { "Status.EverythingUnavailable", "Everything 官方 SDK：不可用" },
                { "Status.EverythingNotRunning", "Everything：未运行，使用文件系统扫描" },
                { "Status.EverythingDownloadRecommended", "推荐前往官网下载" },
                { "Status.EverythingDisabled", "Everything：已关闭" },
                { "Status.EverythingEnableInDiagnostics", "可在性能诊断中开启" },
                { "Status.LanguageRestart", "语言已保存。程序将重新启动并应用新语言。" },
                { "Status.LanguagePacksTitle", "语言包检查" },
                { "Status.NoLanguagePacks", "没有发现可用语言包。" },
                { "Status.ExecutingCloseBlocked", "正在执行文件移动，请等待当前整理完成后再关闭程序。" },
                { "Status.ExecutingTitle", "正在执行" },
                { "Status.ScanningCloseBlocked", "扫描正在后台运行，已请求停止。请等待扫描停止后再关闭程序。" },
                { "Status.ScanningTitle", "正在扫描" },
                { "Status.FileTypeChanged", "已切换文件类型方案为“{0}”；当前预览保持不变，下次点击“扫描”时生效。" },
                { "Status.ArchiveSettingsSaved", "归档设置已保存：每组最多 {0} 个作者；分组示例“{1} / {2}”；作者目录示例“{3}”。当前预览保持不变，下次扫描时生效。" },
                { "Status.PathMissing", "{0}不存在或尚未设置。" },
                { "Status.CannotOpen", "无法打开" },
                { "Status.ReadmeMissing", "未找到 README.txt。" },
                { "Status.ScanFailed", "扫描失败" },
                { "Status.ScanFailedDetail", "扫描失败：{0}" },
                { "Status.ScanPreparing", "正在准备扫描…" },
                { "Status.ScanRunning", "正在后台扫描；窗口可正常移动、最小化并查看当前预览。" },
                { "Status.ScanSearchingKnown", "正在扫描文件 {0} / {1}" },
                { "Status.ScanSearchingUnknown", "正在枚举文件… 已检查 {0} 个" },
                { "Status.ScanPlanning", "正在识别作者并规划 {0} / {1}" },
                { "Status.ScanEarlyStop", "正在按当前扫描范围查找：已找到 {0} / {1}；已检查 {2} 个候选文件" },
                { "Status.ScanRebuilding", "正在生成筛选后的预览 {0} / {1}" },
                { "Status.ScanCancelRequested", "正在安全停止扫描…" },
                { "Status.ScanCanceled", "扫描已停止。" },
                { "Status.ScanCanceledKeepPreview", "扫描已停止；停止前的临时结果不会覆盖当前预览。" },
                { "Status.ScanUpdatingPreview", "扫描完成，正在更新预览列表…" },
                { "Status.ScanProgressComplete", "扫描完成：预览 {0} 个文件。" },
                { "Status.ScanCompleteHuman", "扫描完成：{0} 个可整理，{1} 个需要确认，{2} 个已排除。" },
                { "Status.ScanCompleteHumanWithDeferred", "扫描完成：{0} 个可整理，{1} 个需要确认，{2} 个暂不整理，{3} 个已排除。" },
                { "Status.NeedsReviewLinkText", "{0} 个需要确认" },
                { "Status.DeferredLinkText", "{0} 个暂不整理" },
                { "Status.ReviewOnlyAction", "{0} 个文件需要确认" },
                { "Status.ScanCompleteV1116", "扫描完成：{0}；预览 {1} 个，可整理 {2} 个，需确认/跳过 {3} 个，已排除 {4} 项。" },
                { "Status.ScanFilteredEarlyStop", "{0}：已检查 {1}/{2} 个候选；已找到 {3}/{4}，达到目标后提前停止；可整理 {5} 个；已排除 {6} 项。" },
                { "Status.ScanFilteredV1116", "{0}：已检查 {1}/{2} 个候选；符合当前范围 {3} 个；预览 {4} 个，可整理 {5} 个；已排除 {6} 项。" },
                { "Status.SourceMissing", "原始位置不存在：\r\n{0}" },
                { "Status.TargetMissing", "迁移位置不存在：\r\n{0}" },
                { "Status.NoExtensions", "至少需要设置一种扫描文件类型。" },
                { "Status.ScanComplete", "扫描完成：{0}；预览 {1} 个，可移动 {2} 个，需确认/跳过 {3} 个。" },
                { "Status.ScanElapsed", "扫描响应：{0} ms" },
                { "Status.ScanFiltered", "{0}：已检查 {1} 个未排除文件；符合当前模式 {2} 个；本次预览 {3} 个（扫描数量：{4}）" },
                { "Status.All", "全部" },
                { "Status.EverythingBackend", "Everything：运行中" },
                { "Status.FileSystemBackend", "文件系统扫描（{0}）" },
                { "Status.BlockSelectedMany", "排除选中文件（{0} 个）" },
                { "Status.BlockThis", "排除此文件" },
                { "Status.BlockedResult", "已排除 {0} 个文件，其中新增 {1} 条记录。文件已从当前预览移除，后续扫描也会自动跳过。" },
                { "Status.BlockRemoved", "已移除 {0} 条排除记录；对应文件会在下一次扫描重新出现。" },
                { "Status.Renamed", "文件已重命名为：{0}；已重新识别作者并刷新目标文件夹。" },
                { "Status.RenameFailed", "重命名失败" },
                { "Status.RemovedFromPlan", "已从本次扫描列表移除 {0} 个文件。实际文件未删除、未移动、未改名。" },
                { "Status.NoAuthorForManual", "这个文件当前没有识别出作者。请先双击“文件名”修改文件名，使它能识别出作者。" },
                { "Status.CannotNormalizeAuthor", "无法确定标准作者" },
                { "Status.ManualFolderSelected", "已指定作者文件夹：{0}；复合作者名“{1}”已按本次选择写入作者别名库。" },
                { "Status.ManualNewAuthorSelected", "已指定作者：{0}；复合作者名“{1}”已按本次选择写入作者别名库。目标目录已按当前列表重新规划。" },
                { "Status.ExecuteConfirmBody", "准备整理 {0} 个文件。\r\n\r\n已通过执行前安全检查。目标冲突、源文件缺失、路径变化或空间不足会阻止执行。\r\n\r\n跨盘文件会先复制为 .moving 临时文件，校验完成后才删除源文件。" },
                { "Status.ExecuteConfirmTitle", "确认执行" },
                { "Status.ExecutePreparing", "准备执行，共 {0} 个文件。" },
                { "Status.ExecuteRunning", "正在整理，请勿关闭程序或移动这些源文件。" },
                { "Status.Moving", "正在移动" },
                { "Status.SourceMissingDuringMove", "失败：源文件不存在" },
                { "Status.TargetExistsDuringMove", "跳过：目标已有同名文件" },
                { "Status.Moved", "已移动" },
                { "Status.FailedPrefix", "失败：{0}" },
                { "Status.ExecuteUnhandled", "执行过程中发生意外错误。" },
                { "Status.ExecuteFailed", "执行失败：{0}" },
                { "Status.ExecuteFailedTitle", "执行失败" },
                { "Status.ExecuteComplete", "执行完成：成功 {0} 个；跳过 {1} 个；失败 {2} 个。" },
                { "Status.SpaceWaiting", "等待扫描" },
                { "Status.SpaceTargetUnavailable", "目标路径不可用" },
                { "Status.SpaceUnknown", "空间未知" },
                { "Status.SpaceAvailableOnly", "可用 {0}" },
                { "Status.SpaceEnough", "本批 {0} / 可用 {1}" },
                { "Status.SpaceSameVolume", "本批 {0} / 可用 {1} · 同盘" },
                { "Status.SpaceSameVolumeInsufficient", "本批 {0} / 可用 {1} · 低于安全预留" },
                { "Status.SpaceMixed", "本批 {0} / 需复制 {1} / 可用 {2}" },
                { "Status.SpaceMixedInsufficient", "本批 {0} / 需复制 {1} / 可用 {2} · 空间不足（含预留）" },
                { "Status.SpaceInsufficientShort", "本批 {0} / 可用 {1} · 空间不足（含预留）" },
                { "Status.SafetyTitle", "执行前安全检查" },
                { "Status.SafetyFailedHeader", "执行前安全检查未通过：" },
                { "Status.SafetyTargetUnavailable", "目标路径不存在或不可用。" },
                { "Status.SafetyTargetNotWritable", "目标路径不可写。" },
                { "Status.SafetyMissingSources", "有 {0} 个源文件已经不存在。" },
                { "Status.SafetyTargetConflicts", "有 {0} 个目标文件或 .moving 临时文件存在冲突。" },
                { "Status.SafetyUnresolved", "仍有 {0} 个歧义或未识别项目需要确认。" },
                { "Status.SafetySpaceUnavailable", "无法读取目标磁盘剩余空间。" },
                { "Status.SafetySpaceInsufficient", "目标磁盘空间不足。" },
                { "Status.SafetyBatch", "本批待迁移：{0}" },
                { "Status.SafetyRequired", "实际需新增空间：{0}" },
                { "Status.SafetyAvailable", "目标盘可用：{0}" },
                { "Status.SafetyReserve", "安全预留：{0}" },
                { "Status.SafetyDeficit", "当前缺少：{0}" },
                { "Status.SafetyDetail", "详细信息：{0}" },
                { "Status.SafetyAction", "请修复以上问题、释放目标磁盘空间、减少本批文件，或更换迁移位置后重试。" },
                { "Status.SafetyPathsChanged", "原始位置或迁移位置已在扫描后发生变化，请重新“扫描”。" },
                { "Status.SpaceRescanRequired", "路径已变化，请重新扫描" },
                { "Status.ProgressBytes", "已迁移 {0} / {1}" },
                { "Status.ProgressFree", "目标盘可用 {0}" },
                { "Status.ExecutionSpaceStopped", "目标盘空间发生变化，剩余批次无法安全完成。剩余需复制 {0}，当前可用 {1}，安全预留 {2}。" },
                { "Status.ExecutionSpaceReadFailed", "无法重新读取目标盘剩余空间：{0}" },
                { "Status.NotRunSafetyStop", "安全检查停止，未执行" },
                { "Status.ExecutionSafetyStopped", "安全停止：{0}" },
                { "Status.ExecutionStopped", "整理已安全停止。" },
                { "Status.ExecutionStoppedTitle", "整理已安全停止" },
                { "Status.ExecutionStoppedMessage", "整理已安全停止。\r\n\r\n成功 {0} 个；未执行/跳过 {1} 个；失败 {2} 个。\r\n\r\n原因：{3}\r\n\r\n已完成的文件保持有效；尚未执行的源文件不会被删除。" },
                { "Status.CompleteTitle", "完成" },
                { "Status.LanguagePackLine", "{0} ({1})：{2}/{3}，{4}%" },
                { "Status.LanguageChanged", "当前语言：{0}" },
                { "Reason.Manual", "手动指定作者文件夹；已写入作者别名库" },
                { "Reason.ManualNewAuthor", "手动指定作者；已写入作者别名库；目标目录按当前列表重新规划" },
                { "Reason.NewReuse", "本次扫描已识别为同一新作者" },
                { "Reason.AliasUnified", "别名库统一为：{0}" },
                { "Reason.NewAuthor", "未找到已有作者，按新作者处理" },
                { "Reason.AliasLibrary", "作者别名库：{0}" },
                { "Reason.SocietyAndAuthorSame", "社团名「{0}」和作者名「{1}」均匹配到同一作者文件夹" },
                { "Reason.SocietyAndAuthorDifferent", "社团名「{0}」与作者名「{1}」匹配到不同作者文件夹，请使用“指定作者...”确认" },
                { "Reason.SocietyAndAuthorDifferentWithPlanned", "社团名「{0}」与作者名「{1}」命中不同作者候选，其中包含本轮新作者；请选择实际归属作者。" },
                { "Reason.SocietyOnly", "仅社团名「{0}」匹配到已有作者文件夹" },
                { "Reason.AuthorOnly", "仅作者名「{0}」匹配到已有作者文件夹" },
                { "Reason.StructuredNormalized", "社团「{0}」+ 作者「{1}」与已有文件夹「{2}」仅结构格式不同，按标准化匹配" },
                { "Reason.SocietySegment", "社团名称段「{0}」匹配到已有作者文件夹" },
                { "Reason.SocietySegmentMultiple", "社团名称段「{0}」匹配到多个作者文件夹，请使用“指定作者...”确认" },
                { "Reason.SocietyMultiple", "社团名「{0}」匹配到多个作者文件夹，请使用“指定作者...”确认" },
                { "Reason.AuthorMultiple", "作者名「{0}」匹配到多个作者文件夹，请使用“指定作者...”确认" },
                { "Reason.StructuredMultiple", "同一结构化作者候选可匹配多个已有作者文件夹" },
                { "Reason.MultiTagConflict", "多个前置身份标签或社团/作者候选分别匹配到不同作者文件夹，请使用“指定作者...”确认" },
                { "Reason.MultiTagChoice", "多个前置身份标签或社团/作者候选需要确认，请使用“指定作者...”确认" },
                { "Reason.CompositeMultiple", "「{0} ({1})」匹配到多个候选作者文件夹，请使用“指定作者...”确认" },
                { "Reason.Exact", "去除外层命名样式后，作者名称完全一致" },
                { "Reason.Normalized", "社团名 / 括号作者名 / 空格标点标准化匹配" },
                { "Reason.NormalizedSafe", "作者目录名称经安全标准化后匹配（空白、全半角、括号容错）" },
                { "Reason.CautiousCandidate", "发现疑似命名差异的作者目录，请使用“指定作者...”确认" },
                { "Reason.AliasAmbiguous", "作者候选同时命中多个自定义别名组" },
                { "Reason.AliasMultipleDirs", "自定义别名组对应多个已有作者文件夹" },
                { "Reason.GenericMultiple", "同一作者候选可匹配多个已有作者文件夹" },
                { "Reason.NotFound", "未找到对应作者文件夹" },
                { "PlanStatus.Unrecognized", "无法识别作者" },
                { "PlanStatus.Ambiguous", "同作者识别有歧义，跳过" },
                { "PlanStatus.NeedChoice", "社团/作者均有匹配，需要选择" },
                { "PlanStatus.CandidateConfirm", "作者候选需要确认" },
                { "PlanStatus.Matched", "匹配到已有作者" },
                { "PlanStatus.NewReuse", "本次新作者（复用文件夹）" },
                { "PlanStatus.NewToGroup", "新作者，将放入 {0}" },
                { "PlanStatus.AlreadyThere", "文件已在目标作者文件夹" },
                { "PlanStatus.TargetExists", "目标已有同名文件，跳过" },
                { "PlanStatus.Manual", "手动指定作者文件夹" },
                { "PlanStatus.ManualAuthor", "手动指定作者" },
                { "PlanStatus.Excluded", "扫描规则排除" },
                { "Reason.ScanExcluded", "命中扫描排除规则“{0}”（作用范围：{1}）。" },
                { "Reason.ScanExcludedFolder", "命中扫描排除规则“{0}”（作用范围：{1}）；整个子目录树已跳过。" },
                { "Validation.EmptyFileName", "文件名不能为空。" },
                { "Validation.InvalidFileName", "文件名无效。" },
                { "Validation.EndDotSpace", "Windows 文件名不能以句点或空格结尾。" },
                { "Validation.InvalidChars", "文件名包含 Windows 不允许的字符。" },
                { "Validation.PathNotAllowed", "这里只能编辑文件名，不能填写路径。" },
                { "Validation.InvalidExtension", "新的文件扩展名无效。" },
                { "Validation.ExtensionNotAllowed", "如果修改扩展名，新扩展名必须属于当前文件类型方案。\r\n当前方案：{0}\r\n允许的扩展名：{1}" },
                { "Validation.SourceGone", "源文件已经不存在。" },
                { "Validation.DuplicateFile", "同一文件夹中已经存在同名文件：{0}" },
                { "About.Title", "关于归归" },
                { "About.Body", "归归 / MangaAuthorSorter\r\n开发者：kendo" },
                { "About.ProductName", "归归 / MangaAuthorSorter" },
                { "About.Tagline", "漫画作者识别与归档工具" },
                { "About.Author", "开发者：" },
                { "About.Version", "版本：" },
                { "About.ProjectTitle", "项目" },
                { "About.GitHub", "GitHub" },
                { "About.ProjectHint", "查看源码、版本发布、问题反馈与项目动态。" },
                { "About.CommunityTitle", "社群反馈" },
                { "About.QQGroup", "归归反馈QQ群" },
                { "About.CopyGroup", "复制群号" },
                { "About.Copied", "已复制" },
                { "About.Developer", "开发者：" },
                { "About.CheckUpdates", "检查更新" },
                { "Update.Checking", "正在检查更新…" },
                { "Update.AvailableTitle", "发现新版本" },
                { "Update.VersionSummary", "当前版本：{0}    最新版本：{1}" },
                { "Update.PublishedAt", "发布时间：" },
                { "Update.NoNotes", "此版本没有提供更新说明。" },
                { "Update.Download", "前往下载" },
                { "Update.Later", "稍后" },
                { "Update.UpToDate", "当前已是最新版本。\r\n当前版本：{0}" },
                { "Update.NoRelease", "当前暂无可用的正式发布版本。" },
                { "Update.Unavailable", "暂时无法检查更新，请稍后重试。" },
                { "Update.Timeout", "检查更新超时，请稍后重试。" },
                { "Update.InvalidVersion", "无法识别线上版本信息。" },
                { "Update.OpenFailed", "无法打开下载页面，请稍后重试。" },
                { "About.UpdateUnavailableTitle", "检查更新" },
                { "About.UpdateUnavailableMessage", "当前版本尚未配置更新分发地址，检查与安装更新功能暂未启用。" },
                { "About.FeedbackTitle", "社群反馈" },
                { "About.QQFeedbackGroup", "归归反馈QQ群" },
                { "About.QQCopiedOpening", "QQ群号已复制，正在打开加群申请…" },
                { "About.QQClientOpening", "正在通过 QQ 客户端打开加群申请…" },
                { "About.QQWebFallbackOpening", "已在浏览器中打开 QQ 群加入页面" },
                { "About.QQCopiedOnly", "QQ群号已复制" },
                { "About.QQJoinNotConfigured", "QQ群号已复制；直接加群链接尚未配置。" },
                { "About.QQOpenFailedTitle", "无法打开 QQ" },
                { "About.QQOpenFailed", "QQ群号 {0} 已复制到剪贴板，但未能打开 QQ 加群申请。请在 QQ 中搜索该群号申请加入。" },
                { "About.QQOpenFailedNoCopy", "未能打开 QQ 加群申请，也无法复制群号。请在 QQ 中手动搜索群号：{0}" },
                { "About.GitHubFeedback", "GitHub 反馈" },
                { "About.License", "许可证" },
                { "License.Title", "许可证与相关说明" },
                { "License.Description", "按类别查看软件许可、第三方组件、外部服务与网络访问说明。" },
                { "License.OpenNoticeFile", "打开完整说明" },
                { "License.NoticeMissing", "未找到 LICENSE_NOTICES.txt。" },
                { "License.Section.Application", "软件许可" },
                { "License.Section.Everything", "Everything / voidtools" },
                { "License.Section.Danbooru", "Danbooru 在线服务" },
                { "License.Section.Microsoft", "Windows / .NET Framework" },
                { "License.Section.External", "外部平台" },
                { "License.Section.Network", "网络访问" },
                { "License.Body.Application", "Copyright © 2026 kendo.\r\n\r\n当前发行包未声明单独的开源许可证。除随发行包另行提供的许可证明确允许外，保留所有权利。" },
                { "License.Body.Everything", "归归可选使用 Everything 官方 SDK 接口加速文件搜索。SDK 不可用时会自动回退到普通文件系统扫描。\r\n\r\nEverything 与 Everything SDK 由 David Carpenter / voidtools 开发，是独立第三方软件；归归与 voidtools 不存在隶属、赞助或官方背书关系。\r\n\r\nhttps://www.voidtools.com/\r\nhttps://www.voidtools.com/support/everything/sdk/" },
                { "License.Body.Danbooru", "启用在线作者解析时，归归可向 Danbooru 的公开接口发送作者查询字符串并读取公开返回数据。\r\n\r\nDanbooru 是独立第三方服务，其内容、可用性、服务条款和隐私政策由 Danbooru 自行管理。\r\n\r\nhttps://danbooru.donmai.us/" },
                { "License.Body.Microsoft", "归归是 Windows 桌面应用，使用 Microsoft Windows 与 .NET Framework 提供的系统 API 和运行时组件。\r\n\r\nMicrosoft、Windows 与 .NET 相关名称和商标归其各自权利人所有。" },
                { "License.Body.External", "软件中的 GitHub、QQ、爱发电、Ko-fi、voidtools、Danbooru 等外部入口仅用于项目、反馈、支持或可选功能。\r\n\r\n各平台的名称、商标、内容与服务由相应权利人负责。" },
                { "License.Body.Network", "归归的本地扫描与整理功能不要求持续联网。\r\n\r\n只有在用户主动使用在线作者解析、Everything SDK 自动获取、打开项目 / 反馈 / 支持链接等可选功能时，程序才会进行相应网络访问或调用系统浏览器 / 外部应用。" },
                { "Support.Title", "支持项目" },
                { "Support.Subtitle", "归归 为免费软件，捐赠支持与否不影响使用和更新。" },
                { "Support.Methods", "支持方式" },
                { "Support.WeChat", "微信赞赏" },
                { "Support.Alipay", "支付宝" },
                { "Support.QrComingSoon", "二维码后续补充" },
                { "Support.OnlineSupport", "在线支持" },
                { "Support.Afdian", "爱发电" },
                { "Support.KoFi", "Ko-fi" },
                { "Support.Hint", "点击按钮会使用默认浏览器打开对应支持页面。" },
                { "Support.VoluntaryNote", "所有支持均为自愿，不影响软件任何功能或更新。" },
                { "Dialog.FileType.NoEditable", "没有可编辑的模式。" },
                { "Dialog.FileType.ProfileDetail", "模式：{0}\r\n配置 ID：{1}；后缀数量：{2}" },
                { "Dialog.FileType.InvalidExtension", "扩展名无效" },
                { "Dialog.FileType.NewTitle", "新建文件类型" },
                { "Dialog.FileType.CustomDefault", "自定义方案" },
                { "Dialog.FileType.EmptyName", "方案名称不能为空。" },
                { "Dialog.FileType.CannotCreate", "无法新建方案" },
                { "Dialog.FileType.DuplicateName", "已经存在同名方案。" },
                { "Dialog.FileType.RenameTitle", "重命名文件类型" },
                { "Dialog.FileType.CannotRename", "无法重命名方案" },
                { "Dialog.FileType.CopySuffix", " 副本" },
                { "Dialog.FileType.KeepOne", "至少需要保留一个文件类型方案。" },
                { "Dialog.FileType.CannotDelete", "无法删除" },
                { "Dialog.FileType.DeleteConfirm", "确定删除方案“{0}”吗？\r\n该方案中的扩展名配置也会一并删除。" },
                { "Dialog.FileType.DeleteTitle", "删除文件类型" },
                { "Dialog.FileType.RestoreConfirm", "恢复内置方案会重新建立“压缩文件”和“视频文件”，并恢复这两个方案的默认扩展名。\r\n\r\n用户自行创建的其他方案不会删除。\r\n\r\n是否继续？" },
                { "Dialog.FileType.RestoreTitle", "恢复内置方案" },
                { "Dialog.FileType.CannotSave", "无法保存" },
                { "Dialog.FileType.EmptyExtensions", "所选方案“{0}”没有任何文件扩展名。\r\n请至少添加一种扩展名，或选择其他方案后再保存。" },
                { "Dialog.FileType.CannotUse", "无法使用此方案" },
                { "Dialog.FileType.SaveError", "保存文件类型方案失败：\r\n{0}" },
                { "Status.FileTypeManagerChanged", "文件类型方案已切换为“{0}”；当前预览保持不变，下次点击“扫描”时生效。" },
                { "Validation.ExtensionEmpty", "扩展名不能为空。" },
                { "Validation.ExtensionTooLong", "扩展名过长。" },
                { "Validation.ExtensionChars", "扩展名只能包含英文字母、数字、下划线、加号或减号，例如：mp4、mkv、cbz。" },
                { "GroupNaming.Empty", "作者分组名称不能为空。" },
                { "GroupNaming.RequireToken", "作者分组命名必须包含一种编号规则。" },
                { "GroupNaming.OneToken", "作者分组命名只能包含一个编号规则。" },
                { "GroupNaming.UnknownRule", "未知的分组编号规则。" },
                { "GroupNaming.Invalid", "作者分组命名无效。" },
                { "GroupNaming.InvalidChars", "作者分组命名包含 Windows 文件夹名不允许的字符。" },
                { "GroupNaming.EndDotSpace", "作者分组名称不能以句点或空格结尾。" },
                { "AuthorFolderNaming.RequireToken", "命名模板必须包含固定占位符 {author}。" },
                { "AuthorFolderNaming.OneToken", "命名模板只能包含一个 {author}。" },
                { "AuthorFolderNaming.Invalid", "作者目录命名模板无效。" },
                { "AuthorFolderNaming.InvalidChars", "命名模板包含 Windows 文件夹名不允许使用的字符。" },
                { "AuthorFolderNaming.EndDotSpace", "生成的作者目录名称不能以句点或空格结尾。" },
                { "AuthorFolderNaming.TooLong", "生成的作者目录名称不能超过 255 个字符。" },
                { "AuthorFolderNaming.ReservedName", "命名模板会生成 Windows 保留设备名（如 CON、PRN、AUX、NUL、COM1 或 LPT1），请更换样式。" },
                { "Dialog.SelectSpecificAuthor", "请选择具体作者文件夹" },
                { "Dialog.SelectedGroupNotAuthor", "你选中的是作者分组目录“{0}”。请继续进入其中某个具体作者文件夹后再确认。" },
                { "Dialog.ChooseAuthorFolder.Title", "选择这个文件归属的作者文件夹" },
                { "Dialog.ChooseAuthorFolder.Info", "识别到“{0}”存在多个候选归属。候选可能是已有作者文件夹，也可能是本轮全量识别出的新作者。\r\n请选择本次这个文件实际归属哪一个作者：" },
                { "Dialog.ChooseAuthorFolder.PlannedNew", "本轮新作者（确认后按当前列表重新分配目录）" },
                { "Dialog.ChooseAuthorFolder.Use", "使用选中的作者" },
                { "Dialog.ChooseAuthorFolder.Other", "选择其他文件夹..." },
                { "Dialog.ManualFolderDescription", "请选择这个文件实际所属的具体作者文件夹。所选文件夹名会与当前识别作者写入作者别名库。" },
                { "Status.CompleteWithHistory", "整理完成：成功 {0} 个；跳过 {1} 个；失败 {2} 个。历史记录已保存。" },
                { "Status.CompleteHistoryFailed", "整理完成：成功 {0} 个；跳过 {1} 个；失败 {2} 个；历史记录保存失败：{3}" },
                { "Status.CompleteMessage", "整理完成。\r\n成功：{0}\r\n跳过：{1}\r\n失败：{2}\r\n\r\n本次操作已保存到历史记录。" },
                { "Status.CompleteMessageHistoryFailed", "整理完成。\r\n成功：{0}\r\n跳过：{1}\r\n失败：{2}\r\n\r\n历史记录保存失败：{3}" },
                { "Dialog.BlockList.DeleteSelected", "移除选中记录" },
                { "Validation.SourceAlreadyGone", "源文件已经不存在。" },
                { "Validation.DuplicateInFolder", "同一文件夹中已经存在同名文件：{0}" },
                { "Dialog.RenameFailed", "重命名失败" },
                { "Dialog.FileType.ExtensionInvalidTitle", "扩展名无效" },
                { "Main.SourceName", "原始位置" },
                { "Main.TargetName", "迁移位置" },
                { "SearchDetail.EverythingFast", "Everything 官方 SDK 高速扫描" },
                { "SearchDetail.EverythingFastZero", "Everything 官方 SDK 高速扫描（0 个结果）" },
                { "SearchDetail.FallbackNoResults", "Everything 未返回结果，但检测到符合当前文件类型方案的本地文件；本次已自动改用文件系统扫描" },
                { "SearchDetail.FallbackFailed", "Everything 已运行，但 SDK 查询失败；本次已自动改用文件系统扫描：{0}" },
                { "SearchDetail.FileSystem", "Everything 未运行，使用文件系统扫描" },
                { "SearchDetail.SDKQueryError", "Everything SDK 查询失败，错误代码：{0}" },
                { "Common.Search", "搜索：" },
                { "AliasManager.Title", "作者别名库" },
                { "AliasManager.Description", "管理已确认属于同一作者的名称。修改会直接保存到作者别名库，并用于后续扫描。" },
                { "AliasManager.Canonical", "标准作者名" },
                { "AliasManager.Aliases", "别名" },
                { "AliasManager.Count", "别名数" },
                { "AliasManager.Add", "新增别名组" },
                { "AliasManager.Edit", "编辑" },
                { "AliasManager.Delete", "删除" },
                { "AliasManager.Summary", "当前显示 {0} 组，共 {1} 组" },
                { "AliasManager.DeleteConfirm", "确定删除选中的 {0} 个别名组吗？" },
                { "AliasManager.SaveFailed", "保存作者别名库失败" },
                { "AliasManager.AddTitle", "新增作者别名组" },
                { "AliasManager.EditTitle", "编辑作者别名组" },
                { "AliasManager.AliasesOnePerLine", "别名（使用 | 分隔）" },
                { "AliasManager.EditHint", "可一次填写多个别名，例如：KENDO01 | kwendo1 | ケンドー。标准作者名无需重复，保存时程序会自动去重。" },
                { "AliasManager.CanonicalRequired", "标准作者名不能为空。" },
                { "AliasManager.AliasRequired", "至少需要填写一个不同于标准作者名的别名。" },
                { "BlockList.RemoveMissing", "移除失效记录" },
                { "BlockList.Summary", "当前显示 {0} 条，共 {1} 条" },
                { "History.Description", "按每次整理操作保存历史，可查看文件明细并按需导出 CSV。" },
                { "History.SessionList", "整理记录" },
                { "History.ItemList", "本次文件明细" },
                { "LanguageCheck.Description", "检查已安装语言包的字段完整度；缺失字段会自动回退到英文或简体中文。" },
                { "LanguageCheck.Language", "语言" },
                { "LanguageCheck.Code", "代码" },
                { "LanguageCheck.Translated", "已翻译" },
                { "LanguageCheck.Completion", "完成度" },
                { "Menu.TagCleaningRules", "标签清洗规则..." },
                { "Menu.ScanExclusionRules", "扫描排除规则..." },
                { "Status.ScanExclusionRulesSaved", "扫描排除规则已保存；当前预览保持不变，下次扫描时生效。" },
                { "Status.ScanExcludedLink", "规则排除：{0}" },
                { "Dialog.ScanExclusion.Title", "扫描排除规则" },
                { "Dialog.ScanExclusion.Description", "在文件进入作者解析、在线查询和归档预览之前按规则直接跳过。支持文件名和文件夹名；英文匹配默认忽略大小写。" },
                { "Dialog.ScanExclusion.GlobalEnable", "扫描时启用全局排除规则" },
                { "Dialog.ScanExclusion.Enabled", "启用" },
                { "Dialog.ScanExclusion.RuleName", "规则名称" },
                { "Dialog.ScanExclusion.Example", "示例" },
                { "Dialog.ScanExclusion.Scope", "作用范围" },
                { "Dialog.ScanExclusion.ScopeFile", "文件名" },
                { "Dialog.ScanExclusion.ScopeFolder", "文件夹名" },
                { "Dialog.ScanExclusion.MatchType", "匹配方式" },
                { "Dialog.ScanExclusion.Pattern", "规则内容" },
                { "Dialog.ScanExclusion.TypeContains", "包含" },
                { "Dialog.ScanExclusion.TypeExact", "精确匹配" },
                { "Dialog.ScanExclusion.TypeStartsWith", "开头是" },
                { "Dialog.ScanExclusion.TypeEndsWith", "结尾是" },
                { "Dialog.ScanExclusion.TypeRegex", "高级：正则表达式" },
                { "Dialog.ScanExclusion.SummaryContains", "包含“{0}”" },
                { "Dialog.ScanExclusion.SummaryExact", "完全等于“{0}”" },
                { "Dialog.ScanExclusion.SummaryStartsWith", "以“{0}”开头" },
                { "Dialog.ScanExclusion.SummaryEndsWith", "以“{0}”结尾" },
                { "Dialog.ScanExclusion.Hint", "命中后直接跳过，不进入作者识别、在线查询或文件移动。默认规则仅提供示例且保持关闭，由用户自行启用。" },
                { "Dialog.ScanExclusion.Summary", "共 {0} 条规则" },
                { "Dialog.ScanExclusion.Add", "新增规则" },
                { "Dialog.ScanExclusion.RestoreDefaults", "恢复系统默认" },
                { "Dialog.ScanExclusion.AddTitle", "新增排除规则" },
                { "Dialog.ScanExclusion.EditTitle", "编辑排除规则" },
                { "Dialog.ScanExclusion.EnableRule", "启用此规则" },
                { "Dialog.ScanExclusion.EditHint", "普通规则不需要正则：固定关键词优先使用包含、精确、开头或结尾。文件夹规则命中后会跳过整个子目录树。" },
                { "Dialog.ScanExclusion.NameRequired", "规则名称不能为空。" },
                { "Dialog.ScanExclusion.PatternRequired", "规则内容不能为空。" },
                { "Dialog.ScanExclusion.Duplicate", "已存在相同作用范围、匹配方式和规则内容。" },
                { "Dialog.ScanExclusion.DeleteConfirm", "确定删除选中的 {0} 条扫描排除规则吗？" },
                { "Dialog.ScanExclusion.DeleteTitle", "删除排除规则" },
                { "Dialog.ScanExclusion.RestoreConfirm", "恢复系统默认会替换当前规则列表，包括尚未保存的自定义修改。\r\n\r\n系统默认规则只作为示例提供，并保持关闭。是否继续？" },
                { "Dialog.ScanExclusion.SaveFailed", "保存扫描排除规则失败：\r\n{0}" },
                { "Dialog.ScanExclusion.InvalidTitle", "无法使用此规则" },
                { "Dialog.ScanExclusion.LiveTest", "实时测试" },
                { "Dialog.ScanExclusion.TestEmpty", "输入一个文件名或文件夹名即可测试。" },
                { "Dialog.ScanExclusion.TestInvalid", "⚠ 当前规则无效：{0}" },
                { "Dialog.ScanExclusion.TestMatched", "✓ 会被当前规则排除" },
                { "Dialog.ScanExclusion.TestNotMatched", "○ 不会被当前规则排除" },
                { "Dialog.ScanExcluded.Title", "本次扫描规则排除记录" },
                { "Dialog.ScanExcluded.Description", "本次扫描有 {0} 个文件或目录被全局扫描排除规则跳过。文件夹规则会直接跳过整个子目录树。" },
                { "Dialog.ScanExcluded.Item", "文件 / 文件夹" },
                { "Dialog.ScanExcluded.Scope", "作用范围" },
                { "Dialog.ScanExcluded.Rule", "命中规则" },
                { "Dialog.ScanExcluded.Path", "路径" },
                { "Menu.OnlineAuthorSettings", "在线作者解析..." },
                { "Recognition.EntityMatch", "实体匹配" },
                { "Reason.EntityLibrary", "作者实体库：{0}" },
                { "Reason.EntityAmbiguous", "本地作者实体库中同一名称对应多个作者实体" },
                { "Status.ScanOnlineResolving", "在线解析作者 {0} / {1}" },
                { "Status.OnlineAuthorSummary", "本地识别 {0}｜待在线解析 {1}｜在线已解析 {2}｜仍未解析 {3}" },
                { "Status.OnlineAuthorEnabled", "已启用在线作者解析：{0}，每次扫描最多查询 {1} 个身份。" },
                { "Status.OnlineAuthorDisabled", "已关闭在线作者解析。" },
                { "Dialog.OnlineAuthor.Title", "在线作者解析" },
                { "Dialog.OnlineAuthor.Description", "仅对本地仍为新作者/未解析的身份去重后查询公共数据源；查询文本会发送给所选数据源，结果可按设置保存到本地作者实体库。" },
                { "Dialog.OnlineAuthor.Enable", "未识别 / 新作者自动在线查询" },
                { "Dialog.OnlineAuthor.Provider", "公共作者数据源：" },
                { "Dialog.OnlineAuthor.SaveCache", "保存查询结果到本地作者实体库" },
                { "Dialog.OnlineAuthor.LocalLibrary", "本地缓存：" },
                { "Dialog.OnlineAuthor.MaxLookups", "每次扫描最多查询：" },
                { "Dialog.OnlineAuthor.AutoRule", "自动采用规则：" },
                { "Dialog.OnlineAuthor.Policy", "仅唯一、精确命中作者名或其他作者名时自动采用；多个候选保持歧义，社团名单独命中不会当作作者。" },
                { "Dialog.OnlineAuthor.OpenLibrary", "打开作者实体库" },
                { "Status.TagCleaningRulesSaved", "标签清洗规则已保存；当前预览保持不变，下次扫描时生效。" },
                { "Dialog.TagCleaning.Title", "标签清洗规则" },
                { "Dialog.TagCleaning.Description", "清洗文件名前置 [] / ［］ / 【】 标签中的日期、版本、翻译信息等非作者内容。命中规则的标签不会进入作者匹配或在线查询。" },
                { "Dialog.TagCleaning.Enabled", "启用" },
                { "Dialog.TagCleaning.MatchType", "匹配方式" },
                { "Dialog.TagCleaning.Pattern", "规则内容" },
                { "Dialog.TagCleaning.RuleName", "规则名称" },
                { "Dialog.TagCleaning.Example", "示例" },
                { "Dialog.TagCleaning.CustomRule", "自定义规则" },
                { "Dialog.TagCleaning.NameRequired", "规则名称不能为空。" },
                { "Dialog.TagCleaning.RuleName.Translation", "翻译 / 汉化标记" },
                { "Dialog.TagCleaning.RuleName.Language", "语言标记" },
                { "Dialog.TagCleaning.RuleName.TranslationGroup", "汉化组 / 翻译组" },
                { "Dialog.TagCleaning.RuleName.Edition", "版本 / 发行形式" },
                { "Dialog.TagCleaning.RuleName.Source", "来源平台" },
                { "Dialog.TagCleaning.RuleName.EditStatus", "修正状态" },
                { "Dialog.TagCleaning.RuleName.OtherMetadata", "其他元数据" },
                { "Dialog.TagCleaning.RuleName.Collection", "合刊 / 合集" },
                { "Dialog.TagCleaning.RuleName.Deleted", "删除 / 原稿状态" },
                { "Dialog.TagCleaning.RuleName.NumericDate", "数字日期 / 期号" },
                { "Dialog.TagCleaning.RuleName.JapaneseDate", "中文 / 日文日期" },
                { "Dialog.TagCleaning.RuleName.ComicMarket", "Comic Market 活动编号" },
                { "Dialog.TagCleaning.RuleName.Comitia", "COMITIA 活动编号" },
                { "Dialog.TagCleaning.RuleName.ComicNumber", "COMIC 编号" },
                { "Dialog.TagCleaning.RuleDescription", "规则说明" },
                { "Dialog.TagCleaning.VisualRuleType", "规则类型" },
                { "Dialog.TagCleaning.RuleParameters", "规则参数" },
                { "Dialog.TagCleaning.VisualTypeExact", "完全等于" },
                { "Dialog.TagCleaning.VisualTypeContains", "包含文字" },
                { "Dialog.TagCleaning.VisualTypeStartsWith", "以文字开头" },
                { "Dialog.TagCleaning.VisualTypeEndsWith", "以文字结尾" },
                { "Dialog.TagCleaning.VisualTypePrefixDigits", "前缀 + 数字" },
                { "Dialog.TagCleaning.VisualTypeNumericDate", "数字日期 / 期号" },
                { "Dialog.TagCleaning.VisualTypeCjkDate", "中文 / 日文日期" },
                { "Dialog.TagCleaning.VisualTypeAdvancedRegex", "高级：正则表达式" },
                { "Dialog.TagCleaning.ValueExact", "当整个标签完全等于以下文字时清洗" },
                { "Dialog.TagCleaning.ValueContains", "只要标签中包含以下文字就清洗" },
                { "Dialog.TagCleaning.ValueStartsWith", "当标签以以下文字开头时清洗" },
                { "Dialog.TagCleaning.ValueEndsWith", "当标签以以下文字结尾时清洗" },
                { "Dialog.TagCleaning.ValuePrefixDigits", "编号前缀（多个前缀用 | 分隔，例如 C|コミケ）" },
                { "Dialog.TagCleaning.DigitRange", "数字位数" },
                { "Dialog.TagCleaning.NumericDateHint", "自动识别例如 2014.11、18.07、2024-10-03；不需要填写表达式。" },
                { "Dialog.TagCleaning.CjkDateHint", "自动识别例如 2024年10月、2024年10月3日；不需要填写表达式。" },
                { "Dialog.TagCleaning.AdvancedRegexLabel", "正则表达式（仅高级用户）" },
                { "Dialog.TagCleaning.TestLabel", "实时测试" },
                { "Dialog.TagCleaning.TestEmpty", "输入一个标签即可立即测试，例如 2014.11。不要输入外层 []。" },
                { "Dialog.TagCleaning.TestInvalid", "⚠ 当前规则还不能使用：{0}" },
                { "Dialog.TagCleaning.TestMatched", "✓ 此标签会被当前规则清洗" },
                { "Dialog.TagCleaning.TestNotMatched", "○ 此标签不会被当前规则清洗" },
                { "Dialog.TagCleaning.VisualEditHint", "普通用户只需选择规则类型并填写文字或数字范围；软件会自动生成内部匹配规则。只有特殊需求才使用“高级：正则表达式”。测试内容只用于验证，不会保存到规则中。" },
                { "Dialog.TagCleaning.SummaryExact", "完全等于“{0}”" },
                { "Dialog.TagCleaning.SummaryContains", "包含“{0}”" },
                { "Dialog.TagCleaning.SummaryStartsWith", "以“{0}”开头" },
                { "Dialog.TagCleaning.SummaryEndsWith", "以“{0}”结尾" },
                { "Dialog.TagCleaning.SummaryPrefixDigits", "前缀 {0} + {1}–{2} 位数字" },
                { "Dialog.TagCleaning.SummaryNumericDate", "2–4 位数字 + 日期分隔符 + 月（可选日）" },
                { "Dialog.TagCleaning.SummaryCjkDate", "4 位年份 + 年/月/日格式" },
                { "Dialog.TagCleaning.Hint", "新增和编辑规则默认使用可视化规则，不需要理解正则表达式；主列表显示规则名称、示例和可读说明。清洗只跳过身份候选标签，不会修改文件名。" },
                { "Dialog.TagCleaning.Summary", "共 {0} 条规则" },
                { "Dialog.TagCleaning.Add", "新增规则" },
                { "Dialog.TagCleaning.RestoreDefaults", "恢复系统默认" },
                { "Dialog.TagCleaning.TypeExact", "精确匹配" },
                { "Dialog.TagCleaning.TypeContains", "包含文字" },
                { "Dialog.TagCleaning.TypeRegex", "正则表达式" },
                { "Dialog.TagCleaning.Duplicate", "已经存在相同匹配方式和规则内容。" },
                { "Dialog.TagCleaning.DeleteConfirm", "确定删除选中的 {0} 条标签清洗规则吗？" },
                { "Dialog.TagCleaning.DeleteTitle", "删除清洗规则" },
                { "Dialog.TagCleaning.RestoreConfirm", "恢复系统默认会用内置规则替换当前列表，包括尚未保存的自定义修改。\r\n\r\n是否继续？" },
                { "Dialog.TagCleaning.SaveFailed", "保存标签清洗规则失败：\r\n{0}" },
                { "Dialog.TagCleaning.AddTitle", "新增标签规则" },
                { "Dialog.TagCleaning.EditTitle", "编辑标签规则" },
                { "Dialog.TagCleaning.EnableRule", "参与扫描清洗" },
                { "Dialog.TagCleaning.EditHint", "规则名称和示例用于说明用途，不影响实际匹配。精确匹配：整个标签完全一致；包含文字：标签中出现指定文字即清洗；正则表达式：适合日期、期号、活动编号。" },
                { "Dialog.TagCleaning.InvalidRule", "规则无效：\r\n{0}" },
                { "Dialog.TagCleaning.InvalidTitle", "无法使用此规则" },
                { "FileTypes.More", " … (共 {0} 种)" },
                { "Menu.PerformanceDiagnostics", "性能诊断" },
                { "Performance.Title", "性能诊断" },
                { "Performance.Description", "用于分析扫描、预热、Everything 和界面响应性能。正常使用时无需开启。" },
                { "Performance.Toggle", "记录性能诊断" },
                { "Performance.WarmupToggle", "扫描预热（推荐开启）" },
                { "Performance.EverythingToggle", "Everything（推荐开启）" },
                { "Performance.RecommendedHint", "建议保持扫描预热和 Everything 开启；仅在排查兼容性或比较扫描路径时关闭。" },
                { "Performance.State.On", "已开启：后续实际扫描将记录详细性能信息。" },
                { "Performance.State.Off", "已关闭：不会记录扫描性能明细日志。" },
                { "Performance.Column.Time", "时间" },
                { "Performance.Column.Provider", "提供器" },
                { "Performance.Column.Warmup", "预热" },
                { "Performance.Column.Everything", "Everything" },
                { "Performance.Column.Count", "结果数" },
                { "Performance.Column.Total", "总响应" },
                { "Performance.Column.Status", "状态" },
                { "Performance.Status.Hit", "预热命中" },
                { "Performance.Status.Direct", "即时扫描" },
                { "Performance.CopySelected", "复制选中记录" },
                { "Performance.Copy", "复制诊断信息" },
                { "Performance.Clear", "清空记录" },
                { "Performance.OpenLog", "打开日志位置" },
                { "Performance.ReportTitle", "归归性能诊断" },
                { "Performance.Version", "版本" },
                { "Performance.Time", "时间" },
                { "Performance.Provider", "扫描提供器" },
                { "Performance.Warmup", "预热" },
                { "Performance.Everything", "Everything" },
                { "Performance.WarmupHit", "预热命中" },
                { "Performance.ReadyState", "点击时预热状态" },
                { "Performance.Ready", "已就绪" },
                { "Performance.NotReady", "准备中或未命中" },
                { "Performance.HitType", "命中类型" },
                { "Performance.HitType.Full", "点击前完整命中" },
                { "Performance.HitType.InFlight", "在途预热复用" },
                { "Performance.HitType.None", "未命中" },
                { "Performance.SnapshotHit", "完整快照命中" },
                { "Performance.WarmupWait", "预热等待" },
                { "Performance.SnapshotPrepare", "快照后台准备" },
                { "Performance.FileDiscovery", "文件发现" },
                { "Performance.TargetIndex", "目标索引" },
                { "Performance.CandidatePrepare", "候选准备" },
                { "Performance.AuthorMatch", "作者解析与匹配" },
                { "Performance.Sort", "结果排序" },
                { "Performance.RowBuild", "行模型生成" },
                { "Performance.AddRows", "批量加入行" },
                { "Performance.Layout", "布局恢复" },
                { "Performance.Finalize", "界面收尾" },
                { "Performance.UiApply", "界面应用" },
                { "Performance.TotalResponse", "总响应" },
                { "Performance.Candidates", "候选数" },
                { "Performance.Results", "结果数" },
                { "Performance.Provider.Everything", "Everything 官方 SDK" },
                { "Performance.Provider.System", "传统文件系统" },
            };
        }

        private static Dictionary<string, string> CreateEnStrings()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "App.Title", "MangaAuthorSorter" },
                { "App.Name", "MangaAuthorSorter" },
                { "Common.Browse", "Browse..." },
                { "Common.Save", "Save" },
                { "Common.Cancel", "Cancel" },
                { "Common.OK", "OK" },
                { "Common.Close", "Close" },
                { "Common.Edit", "Edit" },
                { "Common.Yes", "Yes" },
                { "Common.No", "No" },
                { "Common.DeleteSelected", "Delete selected" },
                { "Common.Error.OpenFailed", "Open failed" },
                { "Common.Error.SaveFailed", "Save failed" },
                { "Common.NotSet", "Not set" },
                { "Common.EmptyValue", "—" },
                { "GridMenu.AutoFit", "Auto-fit column widths" },
                { "GridMenu.ResetWidths", "Restore default widths" },
                { "GridMenu.Columns", "Show/hide columns" },
                { "DataFile.AuthorAliases.Title", "MangaAuthorSorter - Author Alias Library" },
                { "DataFile.AuthorAliases.Format", "Format: CanonicalAuthor|Alias1|Alias2|Alias3" },
                { "DataFile.AuthorAliases.SameAuthor", "Put only names confirmed to refer to the same author on one line." },
                { "DataFile.AuthorAliases.AutoUpdate", "The application updates this file when an author folder is assigned manually." },
                { "DataFile.ExclusionList.Title", "MangaAuthorSorter - Exclusion List" },
                { "DataFile.ExclusionList.PathRule", "Enter one absolute file path per line." },
                { "DataFile.ExclusionList.Description", "Files listed here are excluded from scanning." },
                { "DataFile.ExclusionList.ManageHint", "It is recommended to manage this file through the application's Exclusion List feature." },
                { "Main.SourcePath", "Source:" },
                { "Main.TargetPath", "Destination:" },
                { "Main.ScanMode", "Scan range:" },
                { "Main.ScanPreview", "Scan" },
                { "Main.StopScan", "Stop scan" },
                { "Main.StoppingScan", "Stopping…" },
                { "Main.Execute", "Organize" },
                { "Action.HandleReviewCount", "Review ({0})" },
                { "Main.ScanCount", "Count:" },
                { "Main.ScanCountHint", "0 = All; in filtered scans this is the target result count; newest → oldest" },
                { "Main.Recursive", "Scan subfolders" },
                { "Main.FileType", "File type:" },
                { "Main.OperationHint", "Tip: Double-click “File Name” to rename; double-click “Recognition Basis” to assign an author; Delete removes files only from the current preview. Edited files remain in the current list until you scan again." },
                { "Main.InitialStatus", "Step 1: Select the source folder." },
                { "Status.GuideSelectSource", "Step 1: Select the source folder." },
                { "Status.GuideSelectTarget", "Step 2: Select the destination folder." },
                { "Status.GuideScanPreview", "Next: click Scan to start recognition." },
                { "Main.ExecutionProgress", "Execution progress" },
                { "Main.Waiting", "Waiting." },
                { "Main.TargetPrefix", "Target:" },
                { "Nav.Review", "Needs review" },
                { "Nav.History", "History" },
                { "History.Title", "History" },
                { "History.Empty", "No organization history yet. Completed operations will be saved here automatically." },
                { "History.Time", "Time" },
                { "History.Total", "Files" },
                { "History.Success", "Succeeded" },
                { "History.Skipped", "Skipped" },
                { "History.Failed", "Failed" },
                { "History.Source", "Source" },
                { "History.Target", "Destination" },
                { "History.FileName", "File name" },
                { "History.Author", "Detected author" },
                { "History.Matched", "Matched author" },
                { "History.Status", "Status" },
                { "History.Result", "Result" },
                { "History.ResultMessage", "Result details" },
                { "History.SourcePath", "Original file" },
                { "History.TargetPath", "Target file" },
                { "History.Reason", "Recognition reason" },
                { "History.ExportCsv", "Export CSV..." },
                { "History.DeleteRecord", "Delete record" },
                { "History.ClearAll", "Clear history" },
                { "History.DeleteConfirm", "Delete the selected history record?\r\nNo real files will be deleted or moved." },
                { "History.ClearConfirm", "Clear all history records?\r\nNo real files will be deleted or moved." },
                { "History.ExportSuccess", "Exported to:\r\n{0}" },
                { "History.ExportFailed", "Export failed: {0}" },
                { "History.LoadFailed", "Failed to load history" },
                { "History.Summary", "File type profile: {0}    Scan range: {1}    Files: {2}    Succeeded: {3}    Skipped: {4}    Failed: {5}" },
                { "History.ResultMoved", "Moved" },
                { "History.ResultSkipped", "Skipped" },
                { "History.ResultFailed", "Failed" },
                { "Filter.All", "All" },
                { "Filter.Matched", "Matched" },
                { "Filter.NewAuthor", "New" },
                { "Filter.Ambiguous", "Ambiguous" },
                { "Filter.Unrecognized", "Unrecognized" },
                { "Filter.Excluded", "Excluded" },
                { "Filter.Duplicate", "Duplicate" },
                { "Filter.ViewLabel", "View:" },
                { "Filter.Search", "Search:" },
                { "Details.Title", "Processing preview" },
                { "Details.Empty", "Select a file to see how it will be handled and whether any action is needed." },
                { "Details.Multiple", "{0} files selected. Use the context menu for batch actions." },
                { "Details.FileName", "File name" },
                { "Details.Status", "Status" },
                { "Details.Author", "Recognized author" },
                { "Details.Matched", "Matched author" },
                { "Details.Reason", "Recognition basis" },
                { "Details.Target", "Target" },
                { "Details.Source", "Source file" },
                { "Details.Process", "Action" },
                { "Details.Result", "Result" },
                { "Details.MultipleCandidates", "Multiple candidates" },
                { "Details.NoActionNeeded", "Ready to archive" },
                { "Details.Process.Existing", "Use existing author" },
                { "Details.Process.NewFolder", "Create author folder" },
                { "Details.Process.NewReuse", "Reuse new-author folder" },
                { "Details.Process.Ambiguous", "Await confirmation" },
                { "Details.Process.Unrecognized", "Await author assignment" },
                { "Details.Process.Excluded", "Skip" },
                { "Details.Process.Manual", "Use assigned author" },
                { "Details.Process.AlreadyThere", "No move needed" },
                { "Details.Process.TargetExists", "Resolve conflict" },
                { "Details.Result.Ready", "Ready to archive" },
                { "Details.Result.NewFolder", "Author folder will be created" },
                { "Details.Result.Ambiguous", "Author confirmation required" },
                { "Details.Result.Unrecognized", "Assign an author or skip" },
                { "Details.Result.Excluded", "Excluded from archiving" },
                { "Details.Result.ExcludedRule", "Excluded from archiving\r\nRule: {0}" },
                { "Details.Result.TargetExists", "Resolve the duplicate target" },
                { "Details.Result.AlreadyThere", "No move needed" },
                { "Details.Candidates", "Candidates: {0}" },
                { "Details.AmbiguousContext", "Multiple possible author folders were found.\r\n{0}" },
                { "Details.AmbiguousContextNoCandidates", "Multiple possible author matches were found. Choose an author before archiving." },
                { "Details.UnrecognizedContext", "The author could not be determined, so this file will not be archived automatically." },
                { "Details.ExcludedContext", "Matched scan exclusion rule: {0}\r\nThis item will not participate in archiving." },
                { "Details.TargetExistsContext", "A file with the same name already exists at the target. This item will not be moved automatically." },
                { "Details.AssignAuthor", "Assign author..." },
                { "Details.AssignOtherAuthor", "Assign another author..." },
                { "Details.ChooseAuthor", "Choose author..." },
                { "Details.ManageExclusionRules", "Exclusion rules..." },
                { "Details.Exclude", "Exclude file" },
                { "Details.AlreadyExcluded", "Excluded" },
                { "Details.AmbiguousHint", "Multiple possible author matches were found. Click “Assign author...” to confirm and lock the archive author for this file." },
                { "Details.UnrecognizedHint", "The author could not be identified automatically. First double-click the file name in the main list to correct the author information; after recognition succeeds, you can assign an author if needed." },
                { "Details.ExcludedHint", "This item matched a global scan exclusion rule. It is shown for reference only and never enters author recognition, online lookup, or archive execution." },
                { "Details.Toggle", "Details" },
                { "Details.Show", "Show details" },
                { "Details.Hide", "Hide details" },
                { "Dialog.ManualAuthorName.Title", "Assign author" },
                { "Dialog.ManualAuthorName.Description", "No author could be read from the file name. Enter the confirmed author name and the item will be replanned as an existing or new author." },
                { "Dialog.ManualAuthorName.Label", "Author:" },
                { "Dialog.ManualAuthorName.Empty", "Author name cannot be empty." },
                { "Dialog.ManualAuthorName.Invalid", "The author name contains characters Windows does not allow in folder names, or ends with a period/space." },
                { "Status.ManualAuthorNamed", "Assigned author: {0}. The current item has been replanned." },
                { "Dialog.ExecuteWithReview.Title", "Files still need review: {0}" },
                { "Dialog.ExecuteWithReview.Message", "{0} files are ready to organize.\r\n{1} files are ambiguous or unrecognized and will stay in place for this run." },
                { "Dialog.ExecuteWithReview.OrganizeAndSkip", "Organize {0}, skip {1}" },
                { "Dialog.ExecuteWithReview.HandleReview", "Review" },
                { "ScanMode.Global", "All files" },
                { "ScanMode.Global.Desc", "All files" },
                { "ScanMode.Exact", "Matched" },
                { "ScanMode.Exact.Desc", "Matched existing authors" },
                { "ScanMode.New", "New authors" },
                { "ScanMode.New.Desc", "New authors" },
                { "ScanMode.Ambiguous", "Ambiguous" },
                { "ScanMode.Ambiguous.Desc", "Multiple matching results" },
                { "ScanMode.Unrecognized", "Unrecognized" },
                { "ScanMode.Unrecognized.Desc", "Author not identified" },
                { "Menu.File", "File (&F)" },
                { "Menu.ScanPreview", "Scan (&S)" },
                { "Menu.StopScan", "Stop scan (&S)" },
                { "Menu.StoppingScan", "Stopping scan…" },
                { "Menu.Execute", "Organize (&E)" },
                { "Menu.OpenSource", "Open source" },
                { "Menu.OpenTarget", "Open destination" },
                { "Menu.Exit", "Exit (&X)" },
                { "Menu.Settings", "Settings (&S)" },
                { "Menu.ArchiveSettings", "Archive settings..." },
                { "Menu.FileTypeManager", "File type profiles..." },
                { "Menu.AliasLibrary", "Author aliases..." },
                { "Menu.BlockList", "Exclusion list..." },
                { "Menu.Language", "Language" },
                { "Menu.Tools", "Tools (&T)" },
                { "Menu.RecheckEverything", "Check Everything" },
                { "Menu.OpenAppFolder", "Open app folder" },
                { "Menu.OpenLanguageFolder", "Open language folder" },
                { "Menu.CheckLanguagePacks", "Check language packs..." },
                { "Menu.Help", "Help (&H)" },
                { "Menu.Readme", "User guide" },
                { "Menu.CheckUpdates", "Check for updates" },
                { "Menu.UpdateAvailable", "New version {0} available" },
                { "Menu.About", "About" },
                { "Menu.Support", "Support project..." },
                { "Dialog.ArchiveSettings.Title", "Archive settings" },
                { "Dialog.ArchiveSettings.Description", "Configure grouping, folder naming, and safety reserve. Applies on the next scan." },
                { "Dialog.ArchiveSettings.GroupTitle", "Author grouping" },
                { "Dialog.ArchiveSettings.Preview", "Naming preview:" },
                { "Dialog.ArchiveSettings.MaxAuthors", "Max authors per group:" },
                { "Dialog.ArchiveSettings.GroupTemplate", "Group naming template:" },
                { "Dialog.ArchiveSettings.GroupPrefix", "Name prefix:" },
                { "Dialog.ArchiveSettings.NumberingRule", "Numbering rule:" },
                { "Dialog.ArchiveSettings.GroupSuffix", "Name suffix:" },
                { "Dialog.ArchiveSettings.AuthorFolderTitle", "Author folder naming" },
                { "Dialog.ArchiveSettings.AuthorFolderHint", "Use {author} once for the author name. This affects only new author folders." },
                { "Dialog.ArchiveSettings.AuthorTemplate", "Naming template:" },
                { "Dialog.ArchiveSettings.AuthorPreview", "Folder preview:" },
                { "Dialog.ArchiveSettings.AuthorSample", "も" },
                { "Dialog.ArchiveSettings.SafetyTitle", "Execution safety" },
                { "Dialog.ArchiveSettings.SafetyReserve", "Target free-space reserve:" },
                { "GroupNumbering.Numeric", "Numbers 1, 2, 3..." },
                { "GroupNumbering.Numeric2", "2-digit numbers 01, 02, 03..." },
                { "GroupNumbering.Numeric3", "3-digit numbers 001, 002, 003..." },
                { "GroupNumbering.AlphaUpper", "Uppercase letters A, B, C...AA" },
                { "GroupNumbering.AlphaLower", "Lowercase letters a, b, c...aa" },
                { "Dialog.PreviewPrefix", "Preview:" },
                { "Dialog.InvalidGroupTemplate", "Invalid group naming" },
                { "Dialog.InvalidAuthorFolderStyle", "Invalid author-folder naming" },
                { "Dialog.FileType.Title", "File type profiles" },
                { "Dialog.FileType.Info", "File type profiles filter which files enter the archive list during scanning. Each profile stores a set of extensions for archives, videos, or other formats; Save and use applies it to the next scan." },
                { "Dialog.FileType.ProfileGroup", "File type profiles" },
                { "Dialog.FileType.New", "New profile" },
                { "Dialog.FileType.Rename", "Rename" },
                { "Dialog.FileType.Duplicate", "Duplicate" },
                { "Dialog.FileType.Delete", "Delete profile" },
                { "Dialog.FileType.RestoreSystem", "Restore built-in profiles" },
                { "Dialog.FileType.ExtensionsGroup", "Extensions" },
                { "Dialog.FileType.Clear", "Clear extensions" },
                { "Dialog.FileType.RestoreProfile", "Restore defaults" },
                { "Dialog.FileType.AddExtension", "Add extension:" },
                { "Dialog.FileType.Add", "Add" },
                { "Dialog.FileType.ExtensionHint", "Accepts mp4, .mp4, or *.mp4; values are normalized and deduplicated." },
                { "Dialog.FileType.SaveUse", "Save and use" },
                { "Dialog.ProfileName", "Profile name:" },
                { "Dialog.BlockList.Title", "Exclusion list" },
                { "Dialog.BlockList.Info", "Files in this list are excluded from scans. Remove an exclusion entry to make the file appear again on the next scan." },
                { "Dialog.BlockList.FileName", "File name" },
                { "Dialog.BlockList.FullPath", "Full path" },
                { "Dialog.BlockList.Status", "Current status" },
                { "Dialog.BlockList.FileExists", "File exists" },
                { "Dialog.BlockList.FileMissing", "File missing" },
                { "Grid.FileName", "File Name" },
                { "Grid.Author", "Detected Author" },
                { "Grid.MatchedAs", "Matched Author" },
                { "Grid.Tag", "Tag" },
                { "Grid.Reason", "Recognition Basis" },
                { "Grid.TargetDir", "Target Folder" },
                { "Grid.Status", "Status" },
                { "GridStatus.AlreadyThere", "Already in Target" },
                { "GridStatus.TargetExists", "Target Exists" },
                { "GridStatus.Excluded", "Excluded" },
                { "Grid.Size", "Size" },
                { "Grid.Modified", "Modified" },
                { "Grid.TagTooltip", "Colored recognition tags: Exact / Normalized / Alias / Circle / Author / New / Ambiguous / Manual. [×] means unrecognized." },
                { "Recognition.Exact", "Exact" },
                { "Recognition.Normalized", "Normalized" },
                { "Recognition.Alias", "Alias" },
                { "Recognition.Society", "Circle" },
                { "Recognition.Author", "Author" },
                { "Recognition.New", "New" },
                { "Recognition.Ambiguous", "Ambiguous" },
                { "Recognition.Manual", "Manual" },
                { "Recognition.Unrecognized", "Unrecognized" },
                { "Recognition.ManualAssigned", "Manual assignment" },
                { "Recognition.AmbiguousChoice", "Ambiguous / selection required" },
                { "Recognition.AliasMatch", "Alias match" },
                { "Recognition.SocietyMatch", "Circle match" },
                { "Recognition.AuthorMatch", "Author match" },
                { "Recognition.ExactMatch", "Direct match" },
                { "Recognition.DirectMatch", "Direct match" },
                { "Recognition.NormalizedMatch", "Normalized" },
                { "Recognition.NormalizedShort", "Normalized" },
                { "Recognition.NewReuse", "New author reuse" },
                { "Recognition.UnrecognizedShort", "Unrecognized" },
                { "FileProfile.Archive", "Archive files" },
                { "FileProfile.Video", "Video files" },
                { "FileProfile.Unnamed", "Unnamed profile" },
                { "Status.ModeChanged", "Scan mode changed. The current preview remains unchanged. Click the adjacent “Scan” button to rebuild the list with the new mode." },
                { "Status.ArchiveSettingsChanged", "Archive settings changed. The current preview remains unchanged. The changes take effect on the next scan preview." },
                { "Status.EverythingRechecked", "Everything status checked again." },
                { "Status.EverythingAvailable", "Everything: connected" },
                { "Status.EverythingUnavailable", "Everything official SDK: unavailable" },
                { "Status.EverythingNotRunning", "Everything: not running; using file-system scanning" },
                { "Status.EverythingDownloadRecommended", "Download from the official site" },
                { "Status.EverythingDisabled", "Everything: disabled" },
                { "Status.EverythingEnableInDiagnostics", "Enable it in Performance diagnostics" },
                { "Status.LanguageRestart", "Language saved. The application will restart to apply it." },
                { "Status.LanguagePacksTitle", "Language pack check" },
                { "Status.NoLanguagePacks", "No usable language packs were found." },
                { "Status.ExecutingCloseBlocked", "Files are being moved. Wait until the current operation finishes before closing the application." },
                { "Status.ExecutingTitle", "Operation in progress" },
                { "Status.ScanningCloseBlocked", "A background scan is running. A stop request has been sent; close the application after the scan stops." },
                { "Status.ScanningTitle", "Scanning" },
                { "Status.FileTypeChanged", "File type profile changed to “{0}”. The current preview remains unchanged. The change takes effect on the next scan preview." },
                { "Status.ArchiveSettingsSaved", "Archive settings saved: up to {0} authors per group; group examples “{1} / {2}”; author-folder example “{3}”. The current preview remains unchanged; changes take effect on the next scan." },
                { "Status.PathMissing", "{0} does not exist or has not been set." },
                { "Status.CannotOpen", "Cannot open" },
                { "Status.ReadmeMissing", "User guide file not found." },
                { "Status.ScanFailed", "Scan failed" },
                { "Status.ScanFailedDetail", "Scan failed: {0}" },
                { "Status.ScanPreparing", "Preparing scan…" },
                { "Status.ScanRunning", "Scanning in the background. You can move or minimize the window and inspect the current preview." },
                { "Status.ScanSearchingKnown", "Scanning files {0} / {1}" },
                { "Status.ScanSearchingUnknown", "Enumerating files… {0} checked" },
                { "Status.ScanPlanning", "Recognizing authors and planning {0} / {1}" },
                { "Status.ScanEarlyStop", "Searching the selected scan range: found {0} / {1}; checked {2} candidate files" },
                { "Status.ScanRebuilding", "Building filtered preview {0} / {1}" },
                { "Status.ScanCancelRequested", "Stopping scan safely…" },
                { "Status.ScanCanceled", "Scan stopped." },
                { "Status.ScanCanceledKeepPreview", "Scan stopped. Temporary results from the stopped scan did not replace the current preview." },
                { "Status.ScanUpdatingPreview", "Scan complete. Updating the preview list…" },
                { "Status.ScanProgressComplete", "Scan complete: {0} files in the preview." },
                { "Status.ScanCompleteHuman", "Scan complete: {0} organizable, {1} need review, {2} excluded." },
                { "Status.ScanCompleteHumanWithDeferred", "Scan complete: {0} organizable, {1} need review, {2} deferred, {3} excluded." },
                { "Status.NeedsReviewLinkText", "{0} need review" },
                { "Status.DeferredLinkText", "{0} deferred" },
                { "Status.ReviewOnlyAction", "{0} files need review" },
                { "Status.ScanCompleteV1116", "Scan complete: {0}; preview {1}; organizable {2}; needs review / skipped {3}; excluded {4}." },
                { "Status.ScanFilteredEarlyStop", "{0}: checked {1}/{2} candidates; found {3}/{4} and stopped early after reaching the target; organizable {5}; excluded {6}." },
                { "Status.ScanFilteredV1116", "{0}: checked {1}/{2} candidates; {3} matched this range; preview {4}; organizable {5}; excluded {6}." },
                { "Status.SourceMissing", "Source does not exist:\r\n{0}" },
                { "Status.TargetMissing", "Destination does not exist:\r\n{0}" },
                { "Status.NoExtensions", "At least one file extension must be configured for scanning." },
                { "Status.ScanComplete", "Scan complete: {0}; preview {1}; movable {2}; needs review / skipped {3}." },
                { "Status.ScanElapsed", "Scan: {0} ms" },
                { "Status.ScanFiltered", "{0}: checked {1} non-excluded files; {2} matched this mode; {3} shown in this preview (scan limit: {4})" },
                { "Status.All", "All" },
                { "Status.EverythingBackend", "Everything: running" },
                { "Status.FileSystemBackend", "File system ({0})" },
                { "Status.BlockSelectedMany", "Exclude selected files ({0})" },
                { "Status.BlockThis", "Exclude this file" },
                { "Status.BlockedResult", "Excluded {0} files; {1} new entries were added. They were removed from the current preview and will be skipped by future scans." },
                { "Status.BlockRemoved", "Removed {0} exclusion entries. Those files can appear again on the next scan." },
                { "Status.Renamed", "File renamed to: {0}. Author recognition and target folder were refreshed." },
                { "Status.RenameFailed", "Rename failed" },
                { "Status.RemovedFromPlan", "Removed {0} files from the current scan list. The actual files were not deleted, moved, or renamed." },
                { "Status.NoAuthorForManual", "No author can currently be identified for this file. Double-click “File Name” first and rename it so an author can be recognized." },
                { "Status.CannotNormalizeAuthor", "Cannot determine canonical author" },
                { "Status.ManualFolderSelected", "Assigned author folder: {0}. The composite author name “{1}” was written to the alias library based on this choice." },
                { "Status.ManualNewAuthorSelected", "Assigned author: {0}. The composite author name “{1}” was written to the alias library, and the destination was recalculated from the current list." },
                { "Status.ExecuteConfirmBody", "Ready to organize {0} files.\r\n\r\nThe pre-execution safety check passed. Target conflicts, missing sources, changed paths, or insufficient free space will block execution.\r\n\r\nCross-volume files are first copied to a .moving temporary file and verified before the source is deleted." },
                { "Status.ExecuteConfirmTitle", "Confirm operation" },
                { "Status.ExecutePreparing", "Preparing to process {0} files." },
                { "Status.ExecuteRunning", "Organizing files. Do not close the program or move the source files." },
                { "Status.Moving", "Moving" },
                { "Status.SourceMissingDuringMove", "Failed: source file no longer exists" },
                { "Status.TargetExistsDuringMove", "Skipped: target already contains a file with the same name" },
                { "Status.Moved", "Moved" },
                { "Status.FailedPrefix", "Failed: {0}" },
                { "Status.ExecuteUnhandled", "An unexpected error occurred during execution." },
                { "Status.ExecuteFailed", "Execution failed: {0}" },
                { "Status.ExecuteFailedTitle", "Execution failed" },
                { "Status.ExecuteComplete", "Finished: {0} succeeded; {1} skipped; {2} failed." },
                { "Status.SpaceWaiting", "Waiting for scan" },
                { "Status.SpaceTargetUnavailable", "Target path unavailable" },
                { "Status.SpaceUnknown", "Free space unknown" },
                { "Status.SpaceAvailableOnly", "Available {0}" },
                { "Status.SpaceEnough", "Batch {0} / available {1}" },
                { "Status.SpaceSameVolume", "Batch {0} / available {1} · same volume" },
                { "Status.SpaceSameVolumeInsufficient", "Batch {0} / available {1} · below safety reserve" },
                { "Status.SpaceMixed", "Batch {0} / copy {1} / available {2}" },
                { "Status.SpaceMixedInsufficient", "Batch {0} / copy {1} / available {2} · insufficient incl. reserve" },
                { "Status.SpaceInsufficientShort", "Batch {0} / available {1} · insufficient incl. reserve" },
                { "Status.SafetyTitle", "Pre-execution safety check" },
                { "Status.SafetyFailedHeader", "The pre-execution safety check did not pass:" },
                { "Status.SafetyTargetUnavailable", "The target path does not exist or is unavailable." },
                { "Status.SafetyTargetNotWritable", "The target path is not writable." },
                { "Status.SafetyMissingSources", "{0} source file(s) no longer exist." },
                { "Status.SafetyTargetConflicts", "{0} target file(s) or .moving temporary file(s) conflict." },
                { "Status.SafetyUnresolved", "{0} ambiguous or unrecognized item(s) still require confirmation." },
                { "Status.SafetySpaceUnavailable", "Unable to read free space on the target volume." },
                { "Status.SafetySpaceInsufficient", "There is not enough free space on the target volume." },
                { "Status.SafetyBatch", "Batch to move: {0}" },
                { "Status.SafetyRequired", "New space required: {0}" },
                { "Status.SafetyAvailable", "Target available: {0}" },
                { "Status.SafetyReserve", "Safety reserve: {0}" },
                { "Status.SafetyDeficit", "Short by: {0}" },
                { "Status.SafetyDetail", "Details: {0}" },
                { "Status.SafetyAction", "Fix the issues above, free target-disk space, reduce the batch, or choose another target and try again." },
                { "Status.SafetyPathsChanged", "The source or target path changed after the scan. Run Scan Preview again." },
                { "Status.SpaceRescanRequired", "Path changed · scan again" },
                { "Status.ProgressBytes", "Moved {0} / {1}" },
                { "Status.ProgressFree", "Target available {0}" },
                { "Status.ExecutionSpaceStopped", "Target free space changed and the remaining batch can no longer complete safely. Remaining copy size: {0}; available: {1}; reserve: {2}." },
                { "Status.ExecutionSpaceReadFailed", "Unable to re-read target free space: {0}" },
                { "Status.NotRunSafetyStop", "Not run because the safety check stopped execution" },
                { "Status.ExecutionSafetyStopped", "Safety stop: {0}" },
                { "Status.ExecutionStopped", "Organizing stopped safely." },
                { "Status.ExecutionStoppedTitle", "Organizing stopped safely" },
                { "Status.ExecutionStoppedMessage", "Organizing stopped safely.\r\n\r\n{0} succeeded; {1} not run/skipped; {2} failed.\r\n\r\nReason: {3}\r\n\r\nCompleted files remain valid; source files that were not processed are not deleted." },
                { "Status.CompleteTitle", "Complete" },
                { "Status.LanguagePackLine", "{0} ({1}): {2}/{3}, {4}%" },
                { "Status.LanguageChanged", "Current language: {0}" },
                { "Reason.Manual", "Author folder manually assigned; alias library updated" },
                { "Reason.ManualNewAuthor", "Author manually assigned; alias library updated; destination recalculated from the current list" },
                { "Reason.NewReuse", "Recognized as the same new author during this scan" },
                { "Reason.AliasUnified", "Alias library normalized to: {0}" },
                { "Reason.NewAuthor", "No existing author found; treating as a new author" },
                { "Reason.AliasLibrary", "Author alias library: {0}" },
                { "Reason.SocietyAndAuthorSame", "Circle “{0}” and author “{1}” both point to the same author folder" },
                { "Reason.SocietyAndAuthorDifferent", "Circle “{0}” and author “{1}” match different author folders; use “Assign author...” to confirm" },
                { "Reason.SocietyAndAuthorDifferentWithPlanned", "Circle “{0}” and author “{1}” point to different author candidates, including a new author from this scan; choose the actual author." },
                { "Reason.SocietyOnly", "Only circle “{0}” matched an existing author folder" },
                { "Reason.AuthorOnly", "Only author “{0}” matched an existing author folder" },
                { "Reason.StructuredNormalized", "Circle “{0}” + author “{1}” exactly match existing folder “{2}” after structural formatting normalization" },
                { "Reason.SocietySegment", "Circle name segment “{0}” matched an existing author folder" },
                { "Reason.SocietySegmentMultiple", "Circle name segment “{0}” matched multiple author folders; use “Assign author...” to confirm" },
                { "Reason.SocietyMultiple", "Circle “{0}” matched multiple author folders; use “Assign author...” to confirm" },
                { "Reason.AuthorMultiple", "Author “{0}” matched multiple author folders; use “Assign author...” to confirm" },
                { "Reason.StructuredMultiple", "One structured author candidate matches multiple existing author folders" },
                { "Reason.MultiTagConflict", "Multiple leading identity tags or circle/author candidates matched different author folders; use “Assign author...” to confirm" },
                { "Reason.MultiTagChoice", "Multiple leading identity tags or circle/author candidates require confirmation; use “Assign author...” to confirm" },
                { "Reason.CompositeMultiple", "“{0} ({1})” matched multiple candidate author folders; use “Assign author...” to confirm" },
                { "Reason.Exact", "Author name is identical after removing the outer naming wrapper" },
                { "Reason.Normalized", "Matched after normalizing circle name / parenthesized author / spaces and punctuation" },
                { "Reason.NormalizedSafe", "Matched after safe author-folder normalization (whitespace, width, bracket repair)" },
                { "Reason.CautiousCandidate", "A possible author folder was found with punctuation/name-style differences; use “Assign author...” to confirm" },
                { "Reason.AliasAmbiguous", "Author candidate matched multiple custom alias groups" },
                { "Reason.AliasMultipleDirs", "Custom alias group maps to multiple existing author folders" },
                { "Reason.GenericMultiple", "One author candidate matches multiple existing folders" },
                { "Reason.NotFound", "No matching author folder found" },
                { "PlanStatus.Unrecognized", "Unable to identify author" },
                { "PlanStatus.Ambiguous", "Ambiguous author match; skipped" },
                { "PlanStatus.NeedChoice", "Both circle and author matched; selection required" },
                { "PlanStatus.CandidateConfirm", "Author candidate requires confirmation" },
                { "PlanStatus.Matched", "Matched existing author" },
                { "PlanStatus.NewReuse", "New author in this scan (reusing folder)" },
                { "PlanStatus.NewToGroup", "New author; planned for {0}" },
                { "PlanStatus.AlreadyThere", "File is already in the target author folder" },
                { "PlanStatus.TargetExists", "Target already contains a file with the same name; skipped" },
                { "PlanStatus.Manual", "Author folder manually assigned" },
                { "PlanStatus.ManualAuthor", "Author manually assigned" },
                { "PlanStatus.Excluded", "Scan rule excluded" },
                { "Reason.ScanExcluded", "Matched scan exclusion rule “{0}” (scope: {1})." },
                { "Reason.ScanExcludedFolder", "Matched scan exclusion rule “{0}” (scope: {1}); the entire folder subtree was skipped." },
                { "Validation.EmptyFileName", "File name cannot be empty." },
                { "Validation.InvalidFileName", "Invalid file name." },
                { "Validation.EndDotSpace", "Windows file names cannot end with a period or space." },
                { "Validation.InvalidChars", "The file name contains characters that Windows does not allow." },
                { "Validation.PathNotAllowed", "Edit the file name only; a path is not allowed here." },
                { "Validation.InvalidExtension", "The new file extension is invalid." },
                { "Validation.ExtensionNotAllowed", "If you change the extension, the new extension must belong to the current file type profile.\r\nCurrent profile: {0}\r\nAllowed extensions: {1}" },
                { "Validation.SourceGone", "The source file no longer exists." },
                { "Validation.DuplicateFile", "A file with the same name already exists in this folder: {0}" },
                { "About.Title", "About MangaAuthorSorter" },
                { "About.Body", "GuiGui / MangaAuthorSorter\r\nDeveloper: kendo" },
                { "About.ProductName", "GuiGui / MangaAuthorSorter" },
                { "About.Tagline", "Manga author identification and sorting tool" },
                { "About.Author", "Developer:" },
                { "About.Version", "Version:" },
                { "About.ProjectTitle", "Project" },
                { "About.GitHub", "GitHub" },
                { "About.ProjectHint", "Source code, releases, issue reports, and project updates." },
                { "About.CommunityTitle", "Community & feedback" },
                { "About.QQGroup", "QQ Feedback Group" },
                { "About.CopyGroup", "Copy group number" },
                { "About.Copied", "Copied" },
                { "About.Developer", "Developer:" },
                { "About.CheckUpdates", "Check for updates" },
                { "Update.Checking", "Checking for updates…" },
                { "Update.AvailableTitle", "A new version is available" },
                { "Update.VersionSummary", "Current version: {0}    Latest version: {1}" },
                { "Update.PublishedAt", "Published:" },
                { "Update.NoNotes", "No release notes were provided." },
                { "Update.Download", "Go to download" },
                { "Update.Later", "Later" },
                { "Update.UpToDate", "You are using the latest version.\r\nCurrent version: {0}" },
                { "Update.NoRelease", "No official release is currently available." },
                { "Update.Unavailable", "Unable to check for updates right now. Please try again later." },
                { "Update.Timeout", "The update check timed out. Please try again later." },
                { "Update.InvalidVersion", "The online version information could not be recognized." },
                { "Update.OpenFailed", "The download page could not be opened. Please try again later." },
                { "About.UpdateUnavailableTitle", "Check for updates" },
                { "About.UpdateUnavailableMessage", "Update distribution is not configured yet. Update checking and installation are not available in this build." },
                { "About.FeedbackTitle", "Community & feedback" },
                { "About.QQFeedbackGroup", "QQ Feedback Group" },
                { "About.QQCopiedOpening", "Group number copied. Opening the join request…" },
                { "About.QQClientOpening", "Opening the join request in the QQ client…" },
                { "About.QQWebFallbackOpening", "The QQ group join page was opened in your browser." },
                { "About.QQCopiedOnly", "Group number copied." },
                { "About.QQJoinNotConfigured", "Group number copied. A direct QQ join link has not been configured yet." },
                { "About.QQOpenFailedTitle", "Unable to open QQ" },
                { "About.QQOpenFailed", "The QQ group number {0} was copied, but the QQ join request could not be opened. Search for the group number in QQ to join." },
                { "About.QQOpenFailedNoCopy", "The QQ join request could not be opened and the group number could not be copied. Search for group {0} manually in QQ." },
                { "About.GitHubFeedback", "GitHub Feedback" },
                { "About.License", "Licenses" },
                { "License.Title", "Licenses and notices" },
                { "License.Description", "Browse software licensing, third-party components, external services, and network access notices by category." },
                { "License.OpenNoticeFile", "Open full notice" },
                { "License.NoticeMissing", "LICENSE_NOTICES.txt was not found." },
                { "License.Section.Application", "Application license" },
                { "License.Section.Everything", "Everything / voidtools" },
                { "License.Section.Danbooru", "Danbooru online service" },
                { "License.Section.Microsoft", "Windows / .NET Framework" },
                { "License.Section.External", "External platforms" },
                { "License.Section.Network", "Network access" },
                { "License.Body.Application", "Copyright © 2026 kendo.\r\n\r\nThis distribution does not declare a separate open-source license. Unless a separate license is supplied with a release, all rights are reserved by kendo." },
                { "License.Body.Everything", "MangaAuthorSorter can optionally use the official Everything SDK interface to accelerate file search. If the SDK is unavailable, the application falls back to normal filesystem scanning.\r\n\r\nEverything and the Everything SDK are developed by David Carpenter / voidtools and are independent third-party software. MangaAuthorSorter is not affiliated with or endorsed by voidtools.\r\n\r\nhttps://www.voidtools.com/\r\nhttps://www.voidtools.com/support/everything/sdk/" },
                { "License.Body.Danbooru", "When online author resolution is enabled, MangaAuthorSorter may send author-name queries to Danbooru's public API and read public response data.\r\n\r\nDanbooru is an independent third-party service and is not affiliated with or endorsed by MangaAuthorSorter.\r\n\r\nhttps://danbooru.donmai.us/" },
                { "License.Body.Microsoft", "MangaAuthorSorter is a Windows desktop application and uses system APIs/runtime components supplied by Microsoft Windows and .NET Framework.\r\n\r\nMicrosoft, Windows and .NET names and trademarks belong to their respective owners." },
                { "License.Body.External", "External links such as GitHub, QQ, Afdian, Ko-fi, voidtools and Danbooru are provided for project access, feedback, support or optional functionality.\r\n\r\nTheir names, trademarks, content and services belong to their respective owners." },
                { "License.Body.Network", "Local scanning and sorting do not require continuous network access.\r\n\r\nNetwork access or external application/browser launching occurs only when the user invokes optional features such as online author resolution, Everything SDK retrieval, or project / feedback / support links." },
                { "Support.Title", "Support the project" },
                { "Support.Subtitle", "GuiGui is free software; donating or not does not affect use or updates." },
                { "Support.Methods", "Support methods" },
                { "Support.WeChat", "WeChat" },
                { "Support.Alipay", "Alipay" },
                { "Support.QrComingSoon", "QR code coming later" },
                { "Support.OnlineSupport", "Online support" },
                { "Support.Afdian", "Afdian" },
                { "Support.KoFi", "Ko-fi" },
                { "Support.Hint", "Buttons open the corresponding support page in your default browser." },
                { "Support.VoluntaryNote", "All support is voluntary and does not affect any software features or updates." },
                { "Dialog.FileType.NoEditable", "No editable profile is available." },
                { "Dialog.FileType.ProfileDetail", "Profile: {0}\r\nConfig ID: {1}; extensions: {2}" },
                { "Dialog.FileType.InvalidExtension", "Invalid extension" },
                { "Dialog.FileType.NewTitle", "New file type" },
                { "Dialog.FileType.CustomDefault", "Custom profile" },
                { "Dialog.FileType.EmptyName", "Profile name cannot be empty." },
                { "Dialog.FileType.CannotCreate", "Cannot create profile" },
                { "Dialog.FileType.DuplicateName", "A profile with the same name already exists." },
                { "Dialog.FileType.RenameTitle", "Rename file type" },
                { "Dialog.FileType.CannotRename", "Cannot rename profile" },
                { "Dialog.FileType.CopySuffix", " Copy" },
                { "Dialog.FileType.KeepOne", "At least one file type profile must remain." },
                { "Dialog.FileType.CannotDelete", "Cannot delete" },
                { "Dialog.FileType.DeleteConfirm", "Delete profile “{0}”?\r\nIts extension configuration will also be removed." },
                { "Dialog.FileType.DeleteTitle", "Delete file type" },
                { "Dialog.FileType.RestoreConfirm", "Restoring built-in profiles recreates the Archive and Video profiles and restores their default extensions.\r\n\r\nOther user-created profiles will not be deleted.\r\n\r\nContinue?" },
                { "Dialog.FileType.RestoreTitle", "Restore built-in profiles" },
                { "Dialog.FileType.CannotSave", "Cannot save" },
                { "Dialog.FileType.EmptyExtensions", "Profile “{0}” has no file extensions.\r\nAdd at least one extension, or select another profile before saving." },
                { "Dialog.FileType.CannotUse", "Cannot use this profile" },
                { "Dialog.FileType.SaveError", "Failed to save file type configuration:\r\n{0}" },
                { "Status.FileTypeManagerChanged", "File type profile changed to “{0}”. The current preview remains unchanged. The change takes effect on the next scan preview." },
                { "Validation.ExtensionEmpty", "Extension cannot be empty." },
                { "Validation.ExtensionTooLong", "Extension is too long." },
                { "Validation.ExtensionChars", "Extensions may contain only letters, numbers, underscores, plus signs, or hyphens, for example: mp4, mkv, cbz." },
                { "GroupNaming.Empty", "Author-group naming cannot be empty." },
                { "GroupNaming.RequireToken", "Author-group naming must contain one numbering rule." },
                { "GroupNaming.OneToken", "Author-group naming can contain only one numbering rule." },
                { "GroupNaming.UnknownRule", "Unknown group numbering rule." },
                { "GroupNaming.Invalid", "Invalid author-group naming." },
                { "GroupNaming.InvalidChars", "Author-group naming contains characters that Windows folder names do not allow." },
                { "GroupNaming.EndDotSpace", "Author-group names cannot end with a period or space." },
                { "AuthorFolderNaming.RequireToken", "The naming template must contain the fixed {author} token." },
                { "AuthorFolderNaming.OneToken", "The naming template can contain only one {author} token." },
                { "AuthorFolderNaming.Invalid", "Invalid author-folder naming template." },
                { "AuthorFolderNaming.InvalidChars", "The naming template contains characters that Windows folder names do not allow." },
                { "AuthorFolderNaming.EndDotSpace", "Generated author-folder names cannot end with a period or space." },
                { "AuthorFolderNaming.TooLong", "Generated author-folder names cannot exceed 255 characters." },
                { "AuthorFolderNaming.ReservedName", "The naming template would generate a Windows reserved device name such as CON, PRN, AUX, NUL, COM1, or LPT1." },
                { "Dialog.SelectSpecificAuthor", "Select a specific author folder" },
                { "Dialog.SelectedGroupNotAuthor", "You selected the author-group folder “{0}”. Open a specific author folder inside it and confirm again." },
                { "Dialog.ChooseAuthorFolder.Title", "Choose the author folder for this file" },
                { "Dialog.ChooseAuthorFolder.Info", "“{0}” has multiple possible author destinations. A candidate may be an existing author folder or a new author discovered during the full classification pass.\r\nChoose the author this file actually belongs to:" },
                { "Dialog.ChooseAuthorFolder.PlannedNew", "New author in this scan (destination recalculated after confirmation)" },
                { "Dialog.ChooseAuthorFolder.Use", "Use selected author" },
                { "Dialog.ChooseAuthorFolder.Other", "Choose another folder..." },
                { "Dialog.ManualFolderDescription", "Choose the exact author folder this file belongs to. The selected folder name and the currently recognized author will be written to the author alias library." },
                { "Status.CompleteWithHistory", "Completed: {0} succeeded; {1} skipped; {2} failed. History saved." },
                { "Status.CompleteHistoryFailed", "Completed: {0} succeeded; {1} skipped; {2} failed; failed to save history: {3}" },
                { "Status.CompleteMessage", "Organization complete.\r\nSucceeded: {0}\r\nSkipped: {1}\r\nFailed: {2}\r\n\r\nThis operation was saved to History." },
                { "Status.CompleteMessageHistoryFailed", "Organization complete.\r\nSucceeded: {0}\r\nSkipped: {1}\r\nFailed: {2}\r\n\r\nFailed to save history: {3}" },
                { "Dialog.BlockList.DeleteSelected", "Remove selected entries" },
                { "Validation.SourceAlreadyGone", "The source file no longer exists." },
                { "Validation.DuplicateInFolder", "A file with the same name already exists in this folder: {0}" },
                { "Dialog.RenameFailed", "Rename failed" },
                { "Dialog.FileType.ExtensionInvalidTitle", "Invalid extension" },
                { "Main.SourceName", "Source" },
                { "Main.TargetName", "Destination" },
                { "SearchDetail.EverythingFast", "Everything official SDK high-speed mode" },
                { "SearchDetail.EverythingFastZero", "Everything official SDK high-speed mode (0 results)" },
                { "SearchDetail.FallbackNoResults", "Everything returned no results, but matching local files were detected; this scan automatically switched to file-system scanning" },
                { "SearchDetail.FallbackFailed", "Everything is running, but the SDK query failed; this scan switched to file-system scanning: {0}" },
                { "SearchDetail.FileSystem", "Everything is not running; using file-system scanning" },
                { "SearchDetail.SDKQueryError", "Everything SDK query failed with error code: {0}" },
                { "Common.Search", "Search:" },
                { "AliasManager.Title", "Author alias library" },
                { "AliasManager.Description", "Manage names confirmed to refer to the same author. Changes are saved immediately and used in future scans." },
                { "AliasManager.Canonical", "Canonical author" },
                { "AliasManager.Aliases", "Aliases" },
                { "AliasManager.Count", "Aliases" },
                { "AliasManager.Add", "Add alias group" },
                { "AliasManager.Edit", "Edit" },
                { "AliasManager.Delete", "Delete" },
                { "AliasManager.Summary", "Showing {0} groups, {1} total" },
                { "AliasManager.DeleteConfirm", "Delete the selected {0} alias groups?" },
                { "AliasManager.SaveFailed", "Failed to save author aliases" },
                { "AliasManager.AddTitle", "Add author alias group" },
                { "AliasManager.EditTitle", "Edit author alias group" },
                { "AliasManager.AliasesOnePerLine", "Aliases (separate with |)" },
                { "AliasManager.EditHint", "Enter multiple aliases in one line, for example: KENDO01 | kwendo1 | ケンドー. Do not repeat the canonical author; duplicates are removed automatically when saved." },
                { "AliasManager.CanonicalRequired", "Canonical author cannot be empty." },
                { "AliasManager.AliasRequired", "Enter at least one alias different from the canonical author." },
                { "BlockList.RemoveMissing", "Remove missing entries" },
                { "BlockList.Summary", "Showing {0} entries, {1} total" },
                { "History.Description", "History is stored per organization run. Review file details here and export CSV only when needed." },
                { "History.SessionList", "Organization history" },
                { "History.ItemList", "Files in this run" },
                { "LanguageCheck.Description", "Checks installed language packs for missing fields. Missing text falls back to English or Simplified Chinese." },
                { "LanguageCheck.Language", "Language" },
                { "LanguageCheck.Code", "Code" },
                { "LanguageCheck.Translated", "Translated" },
                { "LanguageCheck.Completion", "Completion" },
                { "Menu.TagCleaningRules", "Tag cleaning rules..." },
                { "Menu.ScanExclusionRules", "Scan exclusion rules..." },
                { "Status.ScanExclusionRulesSaved", "Scan exclusion rules were saved. The current preview is unchanged; the new rules apply on the next scan." },
                { "Status.ScanExcludedLink", "Rule excluded: {0}" },
                { "Dialog.ScanExclusion.Title", "Scan exclusion rules" },
                { "Dialog.ScanExclusion.Description", "Skip matching files or folders before author parsing, online lookup, and archive preview. English text matching is case-insensitive by default." },
                { "Dialog.ScanExclusion.GlobalEnable", "Enable global scan exclusion rules during scanning" },
                { "Dialog.ScanExclusion.Enabled", "Enabled" },
                { "Dialog.ScanExclusion.RuleName", "Rule name" },
                { "Dialog.ScanExclusion.Example", "Example" },
                { "Dialog.ScanExclusion.Scope", "Scope" },
                { "Dialog.ScanExclusion.ScopeFile", "File name" },
                { "Dialog.ScanExclusion.ScopeFolder", "Folder name" },
                { "Dialog.ScanExclusion.MatchType", "Match type" },
                { "Dialog.ScanExclusion.Pattern", "Rule value" },
                { "Dialog.ScanExclusion.TypeContains", "Contains" },
                { "Dialog.ScanExclusion.TypeExact", "Exact match" },
                { "Dialog.ScanExclusion.TypeStartsWith", "Starts with" },
                { "Dialog.ScanExclusion.TypeEndsWith", "Ends with" },
                { "Dialog.ScanExclusion.TypeRegex", "Advanced: regular expression" },
                { "Dialog.ScanExclusion.SummaryContains", "Contains “{0}”" },
                { "Dialog.ScanExclusion.SummaryExact", "Exactly equals “{0}”" },
                { "Dialog.ScanExclusion.SummaryStartsWith", "Starts with “{0}”" },
                { "Dialog.ScanExclusion.SummaryEndsWith", "Ends with “{0}”" },
                { "Dialog.ScanExclusion.Hint", "Matched items are skipped before author recognition, online lookup, and moving. Built-in rules are examples only and are disabled by default." },
                { "Dialog.ScanExclusion.Summary", "{0} rules" },
                { "Dialog.ScanExclusion.Add", "Add rule" },
                { "Dialog.ScanExclusion.RestoreDefaults", "Restore defaults" },
                { "Dialog.ScanExclusion.AddTitle", "Add exclusion rule" },
                { "Dialog.ScanExclusion.EditTitle", "Edit exclusion rule" },
                { "Dialog.ScanExclusion.EnableRule", "Enable rule" },
                { "Dialog.ScanExclusion.EditHint", "Most rules do not need regex. Prefer contains, exact, starts with, or ends with for fixed text. Folder rules prune the whole matching subtree." },
                { "Dialog.ScanExclusion.NameRequired", "Rule name cannot be empty." },
                { "Dialog.ScanExclusion.PatternRequired", "Rule value cannot be empty." },
                { "Dialog.ScanExclusion.Duplicate", "A rule with the same scope, match type, and value already exists." },
                { "Dialog.ScanExclusion.DeleteConfirm", "Delete the selected {0} scan exclusion rules?" },
                { "Dialog.ScanExclusion.DeleteTitle", "Delete exclusion rules" },
                { "Dialog.ScanExclusion.RestoreConfirm", "Restoring defaults replaces the current rule list, including unsaved custom changes.\r\n\r\nBuilt-in rules are examples only and remain disabled. Continue?" },
                { "Dialog.ScanExclusion.SaveFailed", "Failed to save scan exclusion rules:\r\n{0}" },
                { "Dialog.ScanExclusion.InvalidTitle", "Cannot use this rule" },
                { "Dialog.ScanExclusion.LiveTest", "Live test" },
                { "Dialog.ScanExclusion.TestEmpty", "Enter a file name or folder name to test." },
                { "Dialog.ScanExclusion.TestInvalid", "⚠ Current rule is invalid: {0}" },
                { "Dialog.ScanExclusion.TestMatched", "✓ This item will be excluded by the current rule" },
                { "Dialog.ScanExclusion.TestNotMatched", "○ This item will not be excluded by the current rule" },
                { "Dialog.ScanExcluded.Title", "Items excluded by scan rules" },
                { "Dialog.ScanExcluded.Description", "{0} files or folders were skipped by global scan exclusion rules in this scan. Folder rules prune the entire matching subtree." },
                { "Dialog.ScanExcluded.Item", "File / folder" },
                { "Dialog.ScanExcluded.Scope", "Scope" },
                { "Dialog.ScanExcluded.Rule", "Matched rule" },
                { "Dialog.ScanExcluded.Path", "Path" },
                { "Menu.OnlineAuthorSettings", "Online author lookup..." },
                { "Recognition.EntityMatch", "Entity match" },
                { "Reason.EntityLibrary", "Author entity library: {0}" },
                { "Reason.EntityAmbiguous", "The same local identity maps to multiple author entities." },
                { "Status.ScanOnlineResolving", "Resolving authors online {0} / {1}" },
                { "Status.OnlineAuthorSummary", "Local recognized {0} | queued online {1} | resolved online {2} | still unresolved {3}" },
                { "Status.OnlineAuthorEnabled", "Online author resolution enabled: {0}, up to {1} identities per scan." },
                { "Status.OnlineAuthorDisabled", "Online author resolution disabled." },
                { "Dialog.OnlineAuthor.Title", "Online author resolution" },
                { "Dialog.OnlineAuthor.Description", "Only deduplicated new/unresolved identities are sent to the selected public source. Results can be saved to the local author entity library according to this setting." },
                { "Dialog.OnlineAuthor.Enable", "Automatically query new / unresolved author identities online" },
                { "Dialog.OnlineAuthor.Provider", "Public data source:" },
                { "Dialog.OnlineAuthor.SaveCache", "Save query results to the local author entity library" },
                { "Dialog.OnlineAuthor.LocalLibrary", "Local cache:" },
                { "Dialog.OnlineAuthor.MaxLookups", "Max queries per scan:" },
                { "Dialog.OnlineAuthor.AutoRule", "Automatic rule:" },
                { "Dialog.OnlineAuthor.Policy", "Only one exact artist-name or other-name result is accepted automatically. Multiple candidates remain ambiguous; a group-name-only hit is never treated as an artist." },
                { "Dialog.OnlineAuthor.OpenLibrary", "Open author entity library" },
                { "Status.TagCleaningRulesSaved", "Tag cleaning rules were saved. The current preview is unchanged; the new rules apply on the next scan." },
                { "Dialog.TagCleaning.Title", "Tag cleaning rules" },
                { "Dialog.TagCleaning.Description", "Filter dates, editions, translation markers, and other non-author content from leading [] / ［］ / 【】 filename tags. Matched tags never enter author matching or online lookup." },
                { "Dialog.TagCleaning.Enabled", "Enabled" },
                { "Dialog.TagCleaning.MatchType", "Match type" },
                { "Dialog.TagCleaning.Pattern", "Rule" },
                { "Dialog.TagCleaning.RuleName", "Rule name" },
                { "Dialog.TagCleaning.Example", "Example" },
                { "Dialog.TagCleaning.CustomRule", "Custom rule" },
                { "Dialog.TagCleaning.NameRequired", "Rule name cannot be empty." },
                { "Dialog.TagCleaning.RuleName.Translation", "Translation / scanlation marker" },
                { "Dialog.TagCleaning.RuleName.Language", "Language marker" },
                { "Dialog.TagCleaning.RuleName.TranslationGroup", "Translation group" },
                { "Dialog.TagCleaning.RuleName.Edition", "Edition / release format" },
                { "Dialog.TagCleaning.RuleName.Source", "Source platform" },
                { "Dialog.TagCleaning.RuleName.EditStatus", "Revision / censor status" },
                { "Dialog.TagCleaning.RuleName.OtherMetadata", "Other metadata" },
                { "Dialog.TagCleaning.RuleName.Collection", "Collection / compilation" },
                { "Dialog.TagCleaning.RuleName.Deleted", "Deleted / manuscript status" },
                { "Dialog.TagCleaning.RuleName.NumericDate", "Numeric date / issue" },
                { "Dialog.TagCleaning.RuleName.JapaneseDate", "CJK date" },
                { "Dialog.TagCleaning.RuleName.ComicMarket", "Comic Market event number" },
                { "Dialog.TagCleaning.RuleName.Comitia", "COMITIA event number" },
                { "Dialog.TagCleaning.RuleName.ComicNumber", "COMIC number" },
                { "Dialog.TagCleaning.RuleDescription", "Rule description" },
                { "Dialog.TagCleaning.VisualRuleType", "Rule type" },
                { "Dialog.TagCleaning.RuleParameters", "Rule parameters" },
                { "Dialog.TagCleaning.VisualTypeExact", "Exactly equals" },
                { "Dialog.TagCleaning.VisualTypeContains", "Contains text" },
                { "Dialog.TagCleaning.VisualTypeStartsWith", "Starts with text" },
                { "Dialog.TagCleaning.VisualTypeEndsWith", "Ends with text" },
                { "Dialog.TagCleaning.VisualTypePrefixDigits", "Prefix + digits" },
                { "Dialog.TagCleaning.VisualTypeNumericDate", "Numeric date / issue" },
                { "Dialog.TagCleaning.VisualTypeCjkDate", "CJK date" },
                { "Dialog.TagCleaning.VisualTypeAdvancedRegex", "Advanced: regular expression" },
                { "Dialog.TagCleaning.ValueExact", "Clean when the whole tag exactly equals this text" },
                { "Dialog.TagCleaning.ValueContains", "Clean when the tag contains this text anywhere" },
                { "Dialog.TagCleaning.ValueStartsWith", "Clean when the tag starts with this text" },
                { "Dialog.TagCleaning.ValueEndsWith", "Clean when the tag ends with this text" },
                { "Dialog.TagCleaning.ValuePrefixDigits", "Number prefix (separate alternatives with |, e.g. C|コミケ)" },
                { "Dialog.TagCleaning.DigitRange", "Digit count" },
                { "Dialog.TagCleaning.NumericDateHint", "Automatically matches examples such as 2014.11, 18.07, and 2024-10-03. No expression is required." },
                { "Dialog.TagCleaning.CjkDateHint", "Automatically matches examples such as 2024年10月 and 2024年10月3日. No expression is required." },
                { "Dialog.TagCleaning.AdvancedRegexLabel", "Regular expression (advanced users only)" },
                { "Dialog.TagCleaning.TestLabel", "Live test" },
                { "Dialog.TagCleaning.TestEmpty", "Enter one tag to test immediately, for example 2014.11. Do not include the outer []." },
                { "Dialog.TagCleaning.TestInvalid", "⚠ This rule is not usable yet: {0}" },
                { "Dialog.TagCleaning.TestMatched", "✓ This tag will be cleaned by the current rule" },
                { "Dialog.TagCleaning.TestNotMatched", "○ This tag will not be cleaned by the current rule" },
                { "Dialog.TagCleaning.VisualEditHint", "Most users only need to choose a rule type and enter text or a digit range; the app generates the internal matching rule automatically. Use “Advanced: regular expression” only for special cases. Test text is not saved." },
                { "Dialog.TagCleaning.SummaryExact", "Exactly equals “{0}”" },
                { "Dialog.TagCleaning.SummaryContains", "Contains “{0}”" },
                { "Dialog.TagCleaning.SummaryStartsWith", "Starts with “{0}”" },
                { "Dialog.TagCleaning.SummaryEndsWith", "Ends with “{0}”" },
                { "Dialog.TagCleaning.SummaryPrefixDigits", "Prefix {0} + {1}–{2} digits" },
                { "Dialog.TagCleaning.SummaryNumericDate", "2–4 digits + date separator + month (optional day)" },
                { "Dialog.TagCleaning.SummaryCjkDate", "4-digit year + 年/月/日 date format" },
                { "Dialog.TagCleaning.Hint", "Add and edit rules visually by default; regular-expression knowledge is not required. The list shows readable names, examples, and descriptions. Cleaning never renames files." },
                { "Dialog.TagCleaning.Summary", "{0} rules" },
                { "Dialog.TagCleaning.Add", "Add rule" },
                { "Dialog.TagCleaning.RestoreDefaults", "Restore defaults" },
                { "Dialog.TagCleaning.TypeExact", "Exact" },
                { "Dialog.TagCleaning.TypeContains", "Contains text" },
                { "Dialog.TagCleaning.TypeRegex", "Regular expression" },
                { "Dialog.TagCleaning.Duplicate", "The same match type and rule already exists." },
                { "Dialog.TagCleaning.DeleteConfirm", "Delete the selected {0} tag cleaning rules?" },
                { "Dialog.TagCleaning.DeleteTitle", "Delete cleaning rules" },
                { "Dialog.TagCleaning.RestoreConfirm", "Restoring defaults replaces the current list, including unsaved custom changes.\r\n\r\nContinue?" },
                { "Dialog.TagCleaning.SaveFailed", "Failed to save tag cleaning rules:\r\n{0}" },
                { "Dialog.TagCleaning.AddTitle", "Add tag rule" },
                { "Dialog.TagCleaning.EditTitle", "Edit tag rule" },
                { "Dialog.TagCleaning.EnableRule", "Use during scan" },
                { "Dialog.TagCleaning.EditHint", "Rule name and example are descriptive only and do not affect matching. Exact matches the whole tag; Contains finds text anywhere; Regex is intended for dates, issue labels, and event numbers." },
                { "Dialog.TagCleaning.InvalidRule", "Invalid rule:\r\n{0}" },
                { "Dialog.TagCleaning.InvalidTitle", "Cannot use this rule" },
                { "FileTypes.More", " … ({0} total)" },
                { "Menu.PerformanceDiagnostics", "Performance diagnostics" },
                { "Performance.Title", "Performance diagnostics" },
                { "Performance.Description", "Analyze scan, warm-up, Everything, and UI response performance. Leave this off during normal use." },
                { "Performance.Toggle", "Record performance diagnostics" },
                { "Performance.WarmupToggle", "Scan warm-up (recommended)" },
                { "Performance.EverythingToggle", "Everything (recommended)" },
                { "Performance.RecommendedHint", "Keep scan warm-up and Everything enabled. Turn them off only for compatibility checks or scan-path comparisons." },
                { "Performance.State.On", "On: future scans will record detailed performance data." },
                { "Performance.State.Off", "Off: scan performance details are not written to disk." },
                { "Performance.Column.Time", "Time" },
                { "Performance.Column.Provider", "Provider" },
                { "Performance.Column.Warmup", "Warm-up" },
                { "Performance.Column.Everything", "Everything" },
                { "Performance.Column.Count", "Results" },
                { "Performance.Column.Total", "Total response" },
                { "Performance.Column.Status", "Status" },
                { "Performance.Status.Hit", "Warm-up hit" },
                { "Performance.Status.Direct", "Direct scan" },
                { "Performance.CopySelected", "Copy selected records" },
                { "Performance.Copy", "Copy diagnostics" },
                { "Performance.Clear", "Clear records" },
                { "Performance.OpenLog", "Open log location" },
                { "Performance.ReportTitle", "MangaAuthorSorter performance diagnostics" },
                { "Performance.Version", "Version" },
                { "Performance.Time", "Time" },
                { "Performance.Provider", "Scan provider" },
                { "Performance.Warmup", "Warm-up" },
                { "Performance.Everything", "Everything" },
                { "Performance.WarmupHit", "Warm-up hit" },
                { "Performance.ReadyState", "Warm-up state at click" },
                { "Performance.Ready", "Ready" },
                { "Performance.NotReady", "Preparing or missed" },
                { "Performance.HitType", "Hit type" },
                { "Performance.HitType.Full", "Complete hit before click" },
                { "Performance.HitType.InFlight", "Reused in-flight warm-up" },
                { "Performance.HitType.None", "No hit" },
                { "Performance.SnapshotHit", "Complete snapshot hit" },
                { "Performance.WarmupWait", "Warm-up wait" },
                { "Performance.SnapshotPrepare", "Background snapshot preparation" },
                { "Performance.FileDiscovery", "File discovery" },
                { "Performance.TargetIndex", "Target index" },
                { "Performance.CandidatePrepare", "Candidate preparation" },
                { "Performance.AuthorMatch", "Author parsing and matching" },
                { "Performance.Sort", "Result sorting" },
                { "Performance.RowBuild", "Row model creation" },
                { "Performance.AddRows", "Batch row insertion" },
                { "Performance.Layout", "Layout restoration" },
                { "Performance.Finalize", "UI finalization" },
                { "Performance.UiApply", "UI application" },
                { "Performance.TotalResponse", "Total response" },
                { "Performance.Candidates", "Candidates" },
                { "Performance.Results", "Results" },
                { "Performance.Provider.Everything", "Everything official SDK" },
                { "Performance.Provider.System", "File system" },
            };
        }
    }
}
