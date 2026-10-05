using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal static class TagCleaningMatchType
    {
        public const string Exact = "Exact";
        public const string Contains = "Contains";
        public const string Regex = "Regex";

        public static bool IsValid(string value)
        {
            return String.Equals(value, Exact, StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, Contains, StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(value, Regex, StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string value)
        {
            if (String.Equals(value, Contains, StringComparison.OrdinalIgnoreCase)) return Contains;
            if (String.Equals(value, Regex, StringComparison.OrdinalIgnoreCase)) return Regex;
            return Exact;
        }
    }

    internal static class TagCleaningVisualRuleType
    {
        public const string Exact = "Exact";
        public const string Contains = "Contains";
        public const string StartsWith = "StartsWith";
        public const string EndsWith = "EndsWith";
        public const string PrefixDigits = "PrefixDigits";
        public const string NumericDate = "NumericDate";
        public const string CjkDate = "CjkDate";
        public const string AdvancedRegex = "AdvancedRegex";

        public static string Normalize(TagCleaningRule rule)
        {
            if (rule == null) return Exact;
            string value = rule.EditorType ?? "";
            if (String.Equals(value, Exact, StringComparison.OrdinalIgnoreCase)) return Exact;
            if (String.Equals(value, Contains, StringComparison.OrdinalIgnoreCase)) return Contains;
            if (String.Equals(value, StartsWith, StringComparison.OrdinalIgnoreCase)) return StartsWith;
            if (String.Equals(value, EndsWith, StringComparison.OrdinalIgnoreCase)) return EndsWith;
            if (String.Equals(value, PrefixDigits, StringComparison.OrdinalIgnoreCase)) return PrefixDigits;
            if (String.Equals(value, NumericDate, StringComparison.OrdinalIgnoreCase)) return NumericDate;
            if (String.Equals(value, CjkDate, StringComparison.OrdinalIgnoreCase)) return CjkDate;
            if (String.Equals(value, AdvancedRegex, StringComparison.OrdinalIgnoreCase)) return AdvancedRegex;

            string matchType = TagCleaningMatchType.Normalize(rule.MatchType);
            if (matchType == TagCleaningMatchType.Exact) return Exact;
            if (matchType == TagCleaningMatchType.Contains) return Contains;
            return AdvancedRegex;
        }
    }

    internal sealed class TagCleaningRule
    {
        public bool Enabled { get; set; }
        public string MatchType { get; set; }
        public string Pattern { get; set; }
        // Built-in rules use a language key so their names follow the current UI language.
        // Custom rules use Name directly. Neither field affects matching behavior.
        public string NameKey { get; set; }
        public string Name { get; set; }
        public string Example { get; set; }
        // Visual editor metadata. Matching still uses MatchType + Pattern so
        // older versions and the scan engine remain compatible.
        public string EditorType { get; set; }
        public string EditorValue { get; set; }
        public int MinDigits { get; set; }
        public int MaxDigits { get; set; }

        public TagCleaningRule()
        {
            Enabled = true;
            MatchType = TagCleaningMatchType.Exact;
            Pattern = "";
            NameKey = "";
            Name = "";
            Example = "";
            EditorType = "";
            EditorValue = "";
            MinDigits = 0;
            MaxDigits = 0;
        }

        public TagCleaningRule Clone()
        {
            TagCleaningRule r = new TagCleaningRule();
            r.Enabled = Enabled;
            r.MatchType = MatchType ?? TagCleaningMatchType.Exact;
            r.Pattern = Pattern ?? "";
            r.NameKey = NameKey ?? "";
            r.Name = Name ?? "";
            r.Example = Example ?? "";
            r.EditorType = EditorType ?? "";
            r.EditorValue = EditorValue ?? "";
            r.MinDigits = MinDigits;
            r.MaxDigits = MaxDigits;
            return r;
        }
    }

    internal sealed class TagCleaningRuleConfig
    {
        public int Version { get; set; }
        public List<TagCleaningRule> Rules { get; set; }

        public TagCleaningRuleConfig()
        {
            Version = 7;
            Rules = new List<TagCleaningRule>();
        }
    }

    // User-editable gate between filename tag extraction and author matching.
    // Rules only decide whether a bracket tag is metadata/noise. They never
    // rename files, rewrite folder names, or perform fuzzy author matching.
    internal sealed class TagCleaningRuleStore
    {
        private const int CurrentConfigVersion = 7;
        private const string CombinedEventPattern = @"^(?:ぷに(?:ケット|けっと)\s*\d{1,4}|FF\s*\d{1,4}|SC\s*\d{1,4}|サンクリ(?:\s*\d{1,4}|\s*\d{4}\s+(?:Spring|Summer|Autumn|Winter))|エアコミケ\s*\d{1,4}|COMIC1\s*[☆★＊*]?\s*\d{1,4}|関西コミティア\s*\d{1,4})$";

        private sealed class CompiledRule
        {
            public string Type = TagCleaningMatchType.Exact;
            public string Pattern = "";
            public Regex Regex;
        }

        private readonly string _path;
        private readonly object _sync = new object();
        private List<TagCleaningRule> _cachedRules;
        private List<CompiledRule> _compiledRules;

        public string Path { get { return _path; } }

        public TagCleaningRuleStore(string path)
        {
            _path = path;
        }

        public void EnsureExists()
        {
            lock (_sync)
            {
                if (!File.Exists(_path))
                {
                    try
                    {
                        SaveInternal(GetDefaultRules());
                    }
                    catch
                    {
                        // Portable/single-EXE folders can be read-only. The
                        // in-memory defaults still keep scanning safe.
                        SetCache(GetDefaultRules());
                    }
                }
                else
                {
                    EnsureCache();
                }
            }
        }

        public List<TagCleaningRule> LoadRules()
        {
            lock (_sync)
            {
                EnsureCache();
                return _cachedRules.Select(delegate(TagCleaningRule r) { return r.Clone(); }).ToList();
            }
        }

        public void Save(IEnumerable<TagCleaningRule> rules)
        {
            List<TagCleaningRule> normalized = NormalizeRules(rules);
            string error;
            if (!ValidateRules(normalized, out error))
                throw new InvalidOperationException(error);

            lock (_sync)
            {
                SaveInternal(normalized);
            }
        }

        public void RestoreDefaults()
        {
            Save(GetDefaultRules());
        }

        public bool IsMetadataTag(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return true;
            lock (_sync)
            {
                EnsureCache();
                return MatchesCompiled(text, _compiledRules);
            }
        }

        public static bool MatchesDefaultRules(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return true;
            List<CompiledRule> compiled = CompileRules(GetDefaultRules());
            return MatchesCompiled(text, compiled);
        }

        public static List<TagCleaningRule> GetDefaultRules()
        {
            List<TagCleaningRule> rules = new List<TagCleaningRule>();

            // Translation/localization markers. These intentionally use
            // Contains to preserve the behavior of older versions while making
            // every rule visible and removable in Settings.
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "中国翻訳", "中国翻訳");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "中国翻译", "中国翻译");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "中文翻訳", "中文翻訳");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "中文翻译", "中文翻译");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "翻訳", "中国翻訳");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "翻译", "中国翻译");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "漢化", "个人漢化");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "汉化", "个人汉化");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "个人汉化", "个人汉化");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "個人漢化", "個人漢化");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "机翻", "机翻");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "機翻", "機翻");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "AI翻译", "AI翻译");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "AI翻訳", "AI翻訳");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Translation", "uncensored", "uncensored");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Language", "chinese", "Chinese");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Language", "中国語", "中国語");
            AddContains(rules, "Dialog.TagCleaning.RuleName.Language", "中国语", "中国语");
            AddContains(rules, "Dialog.TagCleaning.RuleName.TranslationGroup", "白杨汉化组", "白杨汉化组");
            AddContains(rules, "Dialog.TagCleaning.RuleName.TranslationGroup", "白楊漢化組", "白楊漢化組");
            AddContains(rules, "Dialog.TagCleaning.RuleName.OtherMetadata", "成年コミック", "成年コミック");
            AddContains(rules, "Dialog.TagCleaning.RuleName.OtherMetadata", "同人CG集", "同人CG集");
            AddContains(rules, "Dialog.TagCleaning.RuleName.OtherMetadata", "Comic", "Comic");

            // Edition/source/status labels that are commonly placed in the
            // same [] prefix area as an author/circle tag.
            AddExact(rules, "Dialog.TagCleaning.RuleName.Language", "中文", "中文");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Edition", "DL版", "DL版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Edition", "WEB版", "WEB版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Edition", "電子版", "電子版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Edition", "电子版", "电子版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Source", "FANTIA版", "FANTIA版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Source", "FANBOX版", "FANBOX版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Source", "FANTIA", "FANTIA");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Source", "Pixiv", "Pixiv");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Source", "ニジエ", "ニジエ");
            AddExact(rules, "Dialog.TagCleaning.RuleName.EditStatus", "無修正", "無修正");
            AddExact(rules, "Dialog.TagCleaning.RuleName.EditStatus", "无修正", "无修正");
            AddExact(rules, "Dialog.TagCleaning.RuleName.EditStatus", "修正版", "修正版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.OtherMetadata", "空気系", "空気系");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Edition", "廉価版", "廉価版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Edition", "廉价版", "廉价版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Collection", "合刊", "合刊");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Collection", "合集", "合集");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Collection", "総集編", "総集編");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Collection", "総集版", "総集版");
            AddExact(rules, "Dialog.TagCleaning.RuleName.OtherMetadata", "同人誌", "同人誌");
            AddExact(rules, "Dialog.TagCleaning.RuleName.OtherMetadata", "同人志", "同人志");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Deleted", "原稿格己删除", "原稿格己删除");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Deleted", "原稿格已删除", "原稿格已删除");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Deleted", "原稿已删除", "原稿已删除");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Deleted", "原稿削除", "原稿削除");
            AddExact(rules, "Dialog.TagCleaning.RuleName.Deleted", "削除済み", "削除済み");

            // Date/issue prefixes. Do not exclude arbitrary pure numbers:
            // numeric creator names (for example "774") are legitimate.
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.NumericDate", TagCleaningVisualRuleType.NumericDate, "", 1, 4, @"^\d{2,4}[.\-_/]\d{1,2}(?:[.\-_/]\d{1,2})?$", "2014.11, 18.07, 2024-10-03");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.JapaneseDate", TagCleaningVisualRuleType.CjkDate, "", 1, 4, @"^\d{4}年\d{1,2}月(?:\d{1,2}日)?$", "2024年10月, 2024年10月3日");

            // Convention/event labels. These are presented as a visual
            // "prefix + digits" rule; users never need to edit the regex.
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.ComicMarket", TagCleaningVisualRuleType.PrefixDigits, "C|コミケ", 1, 4, @"^(?:C|コミケ)\s*\d{1,4}$", "C97, C103, コミケ103");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.Comitia", TagCleaningVisualRuleType.PrefixDigits, "COMITIA|コミティア", 1, 4, @"^(?:COMITIA|コミティア)\s*\d{1,4}$", "COMITIA145, コミティア150");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.ComicNumber", TagCleaningVisualRuleType.PrefixDigits, "COMIC", 1, 4, @"^COMIC\s*\d{1,4}$", "COMIC 123");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.Puniket", TagCleaningVisualRuleType.PrefixDigits, "ぷにケット|ぷにけっと", 1, 4, @"^ぷに(?:ケット|けっと)\s*\d{1,4}$", "ぷにケット47, ぷにけっと38");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.FFEvent", TagCleaningVisualRuleType.PrefixDigits, "FF", 1, 4, @"^FF\s*\d{1,4}$", "FF18, FF41");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.SCEvent", TagCleaningVisualRuleType.PrefixDigits, "SC", 1, 4, @"^SC\s*\d{1,4}$", "SC61, SC46");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.Suncre", TagCleaningVisualRuleType.AdvancedRegex, "", 1, 4, @"^サンクリ(?:\s*\d{1,4}|\s*\d{4}\s+(?:Spring|Summer|Autumn|Winter))$", "サンクリ61, サンクリ2021 Summer");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.AirComiket", TagCleaningVisualRuleType.PrefixDigits, "エアコミケ", 1, 4, @"^エアコミケ\s*\d{1,4}$", "エアコミケ2, エアコミケ3");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.ComicOne", TagCleaningVisualRuleType.AdvancedRegex, "", 1, 4, @"^COMIC1\s*[☆★＊*]?\s*\d{1,4}$", "COMIC1☆14, COMIC1☆15");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.KansaiComitia", TagCleaningVisualRuleType.PrefixDigits, "関西コミティア", 1, 4, @"^関西コミティア\s*\d{1,4}$", "関西コミティア48");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.ComicTreasure", TagCleaningVisualRuleType.PrefixDigits, "こみトレ", 1, 4, @"^こみトレ\s*\d{1,4}$", "こみトレ14, こみトレ23, こみトレ40");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.ToraFestival", TagCleaningVisualRuleType.PrefixDigits, "とら祭り", 1, 4, @"^とら祭り\s*\d{1,4}$", "とら祭り2010");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.CSPEvent", TagCleaningVisualRuleType.PrefixDigits, "CSP", 1, 4, @"^CSP\s*\d{1,4}$", "CSP6");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.Reitaisai", TagCleaningVisualRuleType.PrefixDigits, "例大祭", 1, 4, @"^例大祭\s*\d{1,4}$", "例大祭8");
            AddVisualRegex(rules, "Dialog.TagCleaning.RuleName.RagnaFestival", TagCleaningVisualRuleType.PrefixDigits, "ラグフェス", 1, 4, @"^ラグフェス\s*\d{1,4}$", "ラグフェス29");

            return rules;
        }

        public static bool ValidateRule(TagCleaningRule rule, out string error)
        {
            error = "";
            if (rule == null)
            {
                error = "Rule is empty.";
                return false;
            }

            if (!TagCleaningMatchType.IsValid(rule.MatchType))
            {
                error = "Unknown match type.";
                return false;
            }

            if (String.IsNullOrWhiteSpace(rule.Pattern))
            {
                error = "Pattern cannot be empty.";
                return false;
            }

            if (rule.Pattern.Trim().Length > 500)
            {
                error = "Pattern is too long.";
                return false;
            }

            if (String.Equals(rule.MatchType, TagCleaningMatchType.Regex, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateRules(List<TagCleaningRule> rules, out string error)
        {
            error = "";
            foreach (TagCleaningRule rule in rules)
            {
                if (!ValidateRule(rule, out error)) return false;
            }
            return true;
        }

        private void EnsureCache()
        {
            if (_cachedRules != null && _compiledRules != null) return;
            List<TagCleaningRule> rules = null;
            try
            {
                if (File.Exists(_path))
                {
                    string json = File.ReadAllText(_path, Encoding.UTF8);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = Int32.MaxValue;
                    TagCleaningRuleConfig config = serializer.Deserialize<TagCleaningRuleConfig>(json);
                    if (config != null && config.Rules != null)
                    {
                        rules = NormalizeRules(config.Rules);
                        if (config.Version < CurrentConfigVersion)
                        {
                            if (config.Version < 4)
                                AppendMissingRules(rules, GetVersion4AddedRules());
                            if (config.Version < 5)
                                ReplaceCombinedEventRule(rules);
                            if (config.Version < 6)
                                AppendMissingRules(rules, GetVersion6AddedRules());
                            if (config.Version < 7)
                                AppendMissingRules(rules, GetVersion7AddedRules());
                            try
                            {
                                SaveInternal(rules);
                                return;
                            }
                            catch
                            {
                                // A read-only portable folder can still use
                                // the migrated rule set for this session.
                            }
                        }
                    }
                }
            }
            catch
            {
                rules = null;
            }

            if (rules == null)
                rules = GetDefaultRules();

            string error;
            if (!ValidateRules(rules, out error))
                rules = GetDefaultRules();

            SetCache(rules);
        }

        private void SaveInternal(List<TagCleaningRule> rules)
        {
            TagCleaningRuleConfig config = new TagCleaningRuleConfig();
            config.Version = CurrentConfigVersion;
            config.Rules = NormalizeRules(rules);

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            string json = serializer.Serialize(config);
            json = PrettyJson(json);

            string dir = System.IO.Path.GetDirectoryName(_path);
            if (!String.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(_path))
            {
                try
                {
                    File.Replace(temp, _path, null);
                }
                catch
                {
                    File.Delete(_path);
                    File.Move(temp, _path);
                }
            }
            else
            {
                File.Move(temp, _path);
            }
            SetCache(config.Rules);
        }

        private void SetCache(List<TagCleaningRule> rules)
        {
            _cachedRules = NormalizeRules(rules);
            _compiledRules = CompileRules(_cachedRules);
        }

        private static List<TagCleaningRule> NormalizeRules(IEnumerable<TagCleaningRule> rules)
        {
            List<TagCleaningRule> result = new List<TagCleaningRule>();
            if (rules == null) return result;

            // V1.11.1/V1.11.2 configurations did not contain readable names or
            // examples. Match old built-in rules by type + pattern and enrich
            // them automatically, so upgrades do not require Restore Defaults.
            List<TagCleaningRule> defaults = GetDefaultRules();

            foreach (TagCleaningRule source in rules)
            {
                if (source == null) continue;
                TagCleaningRule rule = new TagCleaningRule();
                rule.Enabled = source.Enabled;
                rule.MatchType = TagCleaningMatchType.Normalize(source.MatchType);
                rule.Pattern = (source.Pattern ?? "").Trim();
                rule.NameKey = (source.NameKey ?? "").Trim();
                rule.Name = (source.Name ?? "").Trim();
                rule.Example = (source.Example ?? "").Trim();
                rule.EditorType = (source.EditorType ?? "").Trim();
                rule.EditorValue = (source.EditorValue ?? "").Trim();
                rule.MinDigits = source.MinDigits > 0 ? source.MinDigits : 1;
                rule.MaxDigits = source.MaxDigits >= rule.MinDigits ? source.MaxDigits : Math.Max(4, rule.MinDigits);
                if (rule.Pattern.Length == 0) continue;

                TagCleaningRule builtIn = defaults.FirstOrDefault(delegate(TagCleaningRule d)
                {
                    return String.Equals(TagCleaningMatchType.Normalize(d.MatchType), rule.MatchType, StringComparison.OrdinalIgnoreCase) &&
                           String.Equals((d.Pattern ?? "").Trim(), rule.Pattern, StringComparison.OrdinalIgnoreCase);
                });
                if (builtIn != null)
                {
                    if (rule.NameKey.Length == 0 && rule.Name.Length == 0)
                    {
                        rule.NameKey = builtIn.NameKey ?? "";
                        rule.Name = builtIn.Name ?? "";
                    }
                    if (rule.Example.Length == 0) rule.Example = builtIn.Example ?? "";
                    if (rule.EditorType.Length == 0) rule.EditorType = builtIn.EditorType ?? "";
                    if (rule.EditorValue.Length == 0) rule.EditorValue = builtIn.EditorValue ?? "";
                    if (source.MinDigits <= 0) rule.MinDigits = builtIn.MinDigits;
                    if (source.MaxDigits <= 0) rule.MaxDigits = builtIn.MaxDigits;
                }

                // A legacy custom exact/contains rule is still understandable
                // even when the user never supplied an example.
                if (rule.Example.Length == 0 && rule.MatchType != TagCleaningMatchType.Regex)
                    rule.Example = rule.Pattern;

                if (rule.EditorType.Length == 0)
                {
                    if (rule.MatchType == TagCleaningMatchType.Exact)
                    {
                        rule.EditorType = TagCleaningVisualRuleType.Exact;
                        rule.EditorValue = rule.Pattern;
                    }
                    else if (rule.MatchType == TagCleaningMatchType.Contains)
                    {
                        rule.EditorType = TagCleaningVisualRuleType.Contains;
                        rule.EditorValue = rule.Pattern;
                    }
                    else
                    {
                        rule.EditorType = TagCleaningVisualRuleType.AdvancedRegex;
                    }
                }

                result.Add(rule);
            }
            return result;
        }

        private static List<TagCleaningRule> GetVersion4AddedRules()
        {
            List<TagCleaningRule> defaults = GetDefaultRules();
            return defaults.Where(delegate(TagCleaningRule rule)
            {
                string pattern = rule.Pattern ?? "";
                return String.Equals(pattern, "成年コミック", StringComparison.Ordinal) ||
                       String.Equals(pattern, "同人CG集", StringComparison.Ordinal);
            }).Select(delegate(TagCleaningRule rule) { return rule.Clone(); }).ToList();
        }

        private static void ReplaceCombinedEventRule(List<TagCleaningRule> rules)
        {
            TagCleaningRule combined = rules.FirstOrDefault(delegate(TagCleaningRule rule)
            {
                return String.Equals(TagCleaningMatchType.Normalize(rule.MatchType), TagCleaningMatchType.Regex, StringComparison.OrdinalIgnoreCase) &&
                       String.Equals((rule.Pattern ?? "").Trim(), CombinedEventPattern, StringComparison.Ordinal);
            });
            bool enabled = combined == null || combined.Enabled;
            if (combined != null) rules.Remove(combined);

            List<TagCleaningRule> eventRules = GetDefaultRules().Where(delegate(TagCleaningRule rule)
            {
                string key = rule.NameKey ?? "";
                return key == "Dialog.TagCleaning.RuleName.Puniket" ||
                       key == "Dialog.TagCleaning.RuleName.FFEvent" ||
                       key == "Dialog.TagCleaning.RuleName.SCEvent" ||
                       key == "Dialog.TagCleaning.RuleName.Suncre" ||
                       key == "Dialog.TagCleaning.RuleName.AirComiket" ||
                       key == "Dialog.TagCleaning.RuleName.ComicOne" ||
                       key == "Dialog.TagCleaning.RuleName.KansaiComitia";
            }).Select(delegate(TagCleaningRule rule)
            {
                TagCleaningRule copy = rule.Clone();
                copy.Enabled = enabled;
                return copy;
            }).ToList();
            AppendMissingRules(rules, eventRules);
        }

        private static List<TagCleaningRule> GetVersion6AddedRules()
        {
            return GetDefaultRules().Where(delegate(TagCleaningRule rule)
            {
                return rule.NameKey == "Dialog.TagCleaning.RuleName.ComicTreasure" ||
                       rule.NameKey == "Dialog.TagCleaning.RuleName.ToraFestival";
            }).Select(delegate(TagCleaningRule rule) { return rule.Clone(); }).ToList();
        }

        private static List<TagCleaningRule> GetVersion7AddedRules()
        {
            return GetDefaultRules().Where(delegate(TagCleaningRule rule)
            {
                return (rule.MatchType == TagCleaningMatchType.Contains &&
                        String.Equals(rule.Pattern, "Comic", StringComparison.Ordinal)) ||
                       rule.NameKey == "Dialog.TagCleaning.RuleName.CSPEvent" ||
                       rule.NameKey == "Dialog.TagCleaning.RuleName.Reitaisai" ||
                       rule.NameKey == "Dialog.TagCleaning.RuleName.RagnaFestival";
            }).Select(delegate(TagCleaningRule rule) { return rule.Clone(); }).ToList();
        }

        private static void AppendMissingRules(List<TagCleaningRule> rules, IEnumerable<TagCleaningRule> additions)
        {
            foreach (TagCleaningRule addition in additions ?? new List<TagCleaningRule>())
            {
                bool exists = rules.Any(delegate(TagCleaningRule current)
                {
                    return String.Equals(TagCleaningMatchType.Normalize(current.MatchType), TagCleaningMatchType.Normalize(addition.MatchType), StringComparison.OrdinalIgnoreCase) &&
                           String.Equals((current.Pattern ?? "").Trim(), (addition.Pattern ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                });
                if (!exists) rules.Add(addition.Clone());
            }
        }

        private static List<CompiledRule> CompileRules(IEnumerable<TagCleaningRule> rules)
        {
            List<CompiledRule> result = new List<CompiledRule>();
            foreach (TagCleaningRule rule in rules ?? new List<TagCleaningRule>())
            {
                if (rule == null || !rule.Enabled || String.IsNullOrWhiteSpace(rule.Pattern)) continue;
                CompiledRule compiled = new CompiledRule();
                compiled.Type = TagCleaningMatchType.Normalize(rule.MatchType);
                compiled.Pattern = rule.Pattern.Trim();
                if (compiled.Type == TagCleaningMatchType.Regex)
                {
                    try
                    {
                        compiled.Regex = new Regex(compiled.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
                    }
                    catch
                    {
                        continue;
                    }
                }
                result.Add(compiled);
            }
            return result;
        }

        private static bool MatchesCompiled(string text, List<CompiledRule> rules)
        {
            string value = (text ?? "").Normalize(NormalizationForm.FormKC).Trim();
            if (value.Length == 0) return true;

            foreach (CompiledRule rule in rules ?? new List<CompiledRule>())
            {
                if (rule.Type == TagCleaningMatchType.Exact)
                {
                    if (String.Equals(value, rule.Pattern, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (rule.Type == TagCleaningMatchType.Contains)
                {
                    if (value.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                else if (rule.Type == TagCleaningMatchType.Regex && rule.Regex != null)
                {
                    try
                    {
                        if (rule.Regex.IsMatch(value)) return true;
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        // A user-defined expression must never stall a large scan.
                        continue;
                    }
                }
            }
            return false;
        }

        private static void AddExact(List<TagCleaningRule> rules, string nameKey, string pattern, string example)
        {
            TagCleaningRule rule = NewRule(TagCleaningMatchType.Exact, nameKey, pattern, example);
            rule.EditorType = TagCleaningVisualRuleType.Exact;
            rule.EditorValue = pattern ?? "";
            rules.Add(rule);
        }

        private static void AddContains(List<TagCleaningRule> rules, string nameKey, string pattern, string example)
        {
            TagCleaningRule rule = NewRule(TagCleaningMatchType.Contains, nameKey, pattern, example);
            rule.EditorType = TagCleaningVisualRuleType.Contains;
            rule.EditorValue = pattern ?? "";
            rules.Add(rule);
        }

        private static void AddVisualRegex(List<TagCleaningRule> rules, string nameKey, string editorType, string editorValue, int minDigits, int maxDigits, string pattern, string example)
        {
            TagCleaningRule rule = NewRule(TagCleaningMatchType.Regex, nameKey, pattern, example);
            rule.EditorType = editorType ?? TagCleaningVisualRuleType.AdvancedRegex;
            rule.EditorValue = editorValue ?? "";
            rule.MinDigits = minDigits;
            rule.MaxDigits = maxDigits;
            rules.Add(rule);
        }

        private static TagCleaningRule NewRule(string type, string nameKey, string pattern, string example)
        {
            TagCleaningRule rule = new TagCleaningRule();
            rule.Enabled = true;
            rule.MatchType = type;
            rule.Pattern = pattern;
            rule.NameKey = nameKey ?? "";
            rule.Name = "";
            rule.Example = example ?? "";
            return rule;
        }

        public static bool ApplyVisualDefinition(TagCleaningRule rule, out string error)
        {
            error = "";
            if (rule == null)
            {
                error = "Rule is empty.";
                return false;
            }

            string editorType = TagCleaningVisualRuleType.Normalize(rule);
            string value = (rule.EditorValue ?? "").Trim();
            int minDigits = Math.Max(1, rule.MinDigits);
            int maxDigits = Math.Max(minDigits, rule.MaxDigits);
            rule.EditorType = editorType;
            rule.MinDigits = minDigits;
            rule.MaxDigits = maxDigits;

            if (editorType == TagCleaningVisualRuleType.Exact)
            {
                if (value.Length == 0) { error = "Match text cannot be empty."; return false; }
                rule.MatchType = TagCleaningMatchType.Exact;
                rule.Pattern = value;
            }
            else if (editorType == TagCleaningVisualRuleType.Contains)
            {
                if (value.Length == 0) { error = "Match text cannot be empty."; return false; }
                rule.MatchType = TagCleaningMatchType.Contains;
                rule.Pattern = value;
            }
            else if (editorType == TagCleaningVisualRuleType.StartsWith)
            {
                if (value.Length == 0) { error = "Start text cannot be empty."; return false; }
                rule.MatchType = TagCleaningMatchType.Regex;
                rule.Pattern = "^" + Regex.Escape(value);
            }
            else if (editorType == TagCleaningVisualRuleType.EndsWith)
            {
                if (value.Length == 0) { error = "End text cannot be empty."; return false; }
                rule.MatchType = TagCleaningMatchType.Regex;
                rule.Pattern = Regex.Escape(value) + "$";
            }
            else if (editorType == TagCleaningVisualRuleType.PrefixDigits)
            {
                string[] raw = value.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                List<string> prefixes = raw.Select(delegate(string x) { return x.Trim(); }).Where(delegate(string x) { return x.Length > 0; }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (prefixes.Count == 0) { error = "Prefix cannot be empty."; return false; }
                if (maxDigits > 12) { error = "Maximum digit count cannot exceed 12."; return false; }
                string prefixPattern = String.Join("|", prefixes.Select(delegate(string x) { return Regex.Escape(x); }).ToArray());
                rule.EditorValue = String.Join("|", prefixes.ToArray());
                rule.MatchType = TagCleaningMatchType.Regex;
                rule.Pattern = "^(?:" + prefixPattern + @")\s*\d{" + minDigits.ToString() + "," + maxDigits.ToString() + "}$";
            }
            else if (editorType == TagCleaningVisualRuleType.NumericDate)
            {
                rule.MatchType = TagCleaningMatchType.Regex;
                rule.Pattern = @"^\d{2,4}[.\-_/]\d{1,2}(?:[.\-_/]\d{1,2})?$";
            }
            else if (editorType == TagCleaningVisualRuleType.CjkDate)
            {
                rule.MatchType = TagCleaningMatchType.Regex;
                rule.Pattern = @"^\d{4}年\d{1,2}月(?:\d{1,2}日)?$";
            }
            else
            {
                rule.EditorType = TagCleaningVisualRuleType.AdvancedRegex;
                rule.MatchType = TagCleaningMatchType.Regex;
                if (String.IsNullOrWhiteSpace(rule.Pattern)) { error = "Regular expression cannot be empty."; return false; }
            }

            return ValidateRule(rule, out error);
        }

        public static bool TestRule(TagCleaningRule source, string text, out bool matched, out string error)
        {
            matched = false;
            error = "";
            if (source == null) { error = "Rule is empty."; return false; }
            TagCleaningRule rule = source.Clone();
            if (!ApplyVisualDefinition(rule, out error)) return false;
            rule.Enabled = true;
            List<TagCleaningRule> single = new List<TagCleaningRule>();
            single.Add(rule);
            matched = MatchesCompiled(text, CompileRules(single));
            return true;
        }

        // JavaScriptSerializer emits compact JSON. A small formatter keeps the
        // portable config readable/editable without adding third-party DLLs.
        private static string PrettyJson(string json)
        {
            if (String.IsNullOrEmpty(json)) return json ?? "";
            StringBuilder sb = new StringBuilder();
            bool inString = false;
            bool escaped = false;
            int indent = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    sb.Append(c);
                }
                else if (c == '{' || c == '[')
                {
                    sb.Append(c);
                    sb.AppendLine();
                    indent++;
                    AppendIndent(sb, indent);
                }
                else if (c == '}' || c == ']')
                {
                    sb.AppendLine();
                    indent = Math.Max(0, indent - 1);
                    AppendIndent(sb, indent);
                    sb.Append(c);
                }
                else if (c == ',')
                {
                    sb.Append(c);
                    sb.AppendLine();
                    AppendIndent(sb, indent);
                }
                else if (c == ':')
                {
                    sb.Append(": ");
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private static void AppendIndent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++) sb.Append("  ");
        }
    }
}
