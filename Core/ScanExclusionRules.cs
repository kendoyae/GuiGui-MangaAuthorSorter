using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal static class ScanExclusionScope
    {
        public const string FileName = "FileName";
        public const string FolderName = "FolderName";

        public static string Normalize(string value)
        {
            return String.Equals(value, FolderName, StringComparison.OrdinalIgnoreCase)
                ? FolderName
                : FileName;
        }
    }

    internal static class ScanExclusionMatchType
    {
        public const string Exact = "Exact";
        public const string Contains = "Contains";
        public const string StartsWith = "StartsWith";
        public const string EndsWith = "EndsWith";
        public const string Regex = "Regex";

        public static string Normalize(string value)
        {
            if (String.Equals(value, Contains, StringComparison.OrdinalIgnoreCase)) return Contains;
            if (String.Equals(value, StartsWith, StringComparison.OrdinalIgnoreCase)) return StartsWith;
            if (String.Equals(value, EndsWith, StringComparison.OrdinalIgnoreCase)) return EndsWith;
            if (String.Equals(value, Regex, StringComparison.OrdinalIgnoreCase)) return Regex;
            return Exact;
        }
    }

    internal sealed class ScanExclusionRule
    {
        public bool Enabled { get; set; }
        public string Name { get; set; }
        public string Example { get; set; }
        public string Scope { get; set; }
        public string MatchType { get; set; }
        public string Pattern { get; set; }

        public ScanExclusionRule()
        {
            Enabled = true;
            Name = "";
            Example = "";
            Scope = ScanExclusionScope.FileName;
            MatchType = ScanExclusionMatchType.Contains;
            Pattern = "";
        }

        public ScanExclusionRule Clone()
        {
            ScanExclusionRule r = new ScanExclusionRule();
            r.Enabled = Enabled;
            r.Name = Name ?? "";
            r.Example = Example ?? "";
            r.Scope = ScanExclusionScope.Normalize(Scope);
            r.MatchType = ScanExclusionMatchType.Normalize(MatchType);
            r.Pattern = Pattern ?? "";
            return r;
        }
    }

    internal sealed class ScanExclusionRuleConfig
    {
        public int Version { get; set; }
        public bool GlobalEnabled { get; set; }
        public List<ScanExclusionRule> Rules { get; set; }

        public ScanExclusionRuleConfig()
        {
            Version = 1;
            GlobalEnabled = true;
            Rules = new List<ScanExclusionRule>();
        }
    }

    internal sealed class ScanExclusionMatch
    {
        public string RuleName = "";
        public string Scope = "";
        public string MatchedText = "";
        public string MatchedPath = "";
    }

    // Global scan gate. It runs before author parsing and online resolution.
    // Matching is case-insensitive for ordinary text rules. Regex rules use a
    // short timeout so a custom expression cannot freeze a large scan.
    internal sealed class ScanExclusionRuleStore
    {
        private sealed class CompiledRule
        {
            public ScanExclusionRule Rule;
            public Regex Regex;
        }

        private readonly string _path;
        private readonly object _sync = new object();
        private bool _globalEnabled = true;
        private List<ScanExclusionRule> _rules;
        private List<CompiledRule> _compiled;
        // Many archives share the same parent folder. Cache the result of the
        // ancestor-folder exclusion walk so Everything scans do not rebuild
        // DirectoryInfo/GetParent chains for every file in that folder.
        private readonly Dictionary<string, ScanExclusionMatch> _folderMatchCache =
            new Dictionary<string, ScanExclusionMatch>(StringComparer.OrdinalIgnoreCase);

        public string Path { get { return _path; } }

        public ScanExclusionRuleStore(string path)
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
                        SaveInternal(true, GetDefaultRules());
                    }
                    catch
                    {
                        SetCache(true, GetDefaultRules());
                    }
                }
                else
                {
                    EnsureCache();
                }
            }
        }

        public bool IsGloballyEnabled()
        {
            lock (_sync)
            {
                EnsureCache();
                return _globalEnabled;
            }
        }

        public List<ScanExclusionRule> LoadRules()
        {
            lock (_sync)
            {
                EnsureCache();
                return _rules.Select(delegate(ScanExclusionRule r) { return r.Clone(); }).ToList();
            }
        }

        public void Save(bool globalEnabled, IEnumerable<ScanExclusionRule> rules)
        {
            List<ScanExclusionRule> normalized = NormalizeRules(rules);
            string error;
            if (!ValidateRules(normalized, out error))
                throw new InvalidOperationException(error);

            lock (_sync)
            {
                SaveInternal(globalEnabled, normalized);
            }
        }

        public void RestoreDefaults()
        {
            Save(true, GetDefaultRules());
        }

        public bool TryMatchFilePath(
            string filePath,
            string sourceRoot,
            out ScanExclusionMatch match)
        {
            match = null;
            if (String.IsNullOrWhiteSpace(filePath)) return false;

            lock (_sync)
            {
                EnsureCache();
                if (!_globalEnabled) return false;

                string fileName = "";
                try { fileName = System.IO.Path.GetFileName(filePath) ?? ""; } catch { }

                CompiledRule hit = FindMatch(fileName, ScanExclusionScope.FileName);
                if (hit != null)
                {
                    match = CreateMatch(hit, fileName);
                    return true;
                }

                string directory = "";
                try { directory = System.IO.Path.GetDirectoryName(filePath) ?? ""; } catch { }
                return TryMatchFolderPathInternal(directory, sourceRoot, out match);
            }
        }

        public bool TryMatchFolderPath(
            string folderPath,
            string sourceRoot,
            out ScanExclusionMatch match)
        {
            lock (_sync)
            {
                EnsureCache();
                if (!_globalEnabled)
                {
                    match = null;
                    return false;
                }
                return TryMatchFolderPathInternal(folderPath, sourceRoot, out match);
            }
        }

        private bool TryMatchFolderPathInternal(
            string folderPath,
            string sourceRoot,
            out ScanExclusionMatch match)
        {
            match = null;
            if (String.IsNullOrWhiteSpace(folderPath)) return false;

            string current;
            string root;
            try
            {
                current = System.IO.Path.GetFullPath(folderPath)
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                root = String.IsNullOrWhiteSpace(sourceRoot)
                    ? ""
                    : System.IO.Path.GetFullPath(sourceRoot)
                        .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return false;
            }

            string cacheKey = root + "\n" + current;
            ScanExclusionMatch cached;
            if (_folderMatchCache.TryGetValue(cacheKey, out cached))
            {
                match = CloneMatch(cached);
                return match != null;
            }

            while (!String.IsNullOrWhiteSpace(current))
            {
                if (root.Length > 0 && String.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                    break;

                string name = "";
                try { name = new DirectoryInfo(current).Name; } catch { }
                CompiledRule hit = FindMatch(name, ScanExclusionScope.FolderName);
                if (hit != null)
                {
                    match = CreateMatch(hit, name);
                    match.MatchedPath = current;
                    _folderMatchCache[cacheKey] = CloneMatch(match);
                    return true;
                }

                string parent;
                try
                {
                    DirectoryInfo info = Directory.GetParent(current);
                    parent = info != null ? info.FullName : "";
                }
                catch
                {
                    parent = "";
                }

                if (String.IsNullOrWhiteSpace(parent) ||
                    String.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;

                if (root.Length > 0 &&
                    !parent.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                    break;

                current = parent;
            }

            _folderMatchCache[cacheKey] = null;
            return false;
        }

        private static ScanExclusionMatch CloneMatch(ScanExclusionMatch source)
        {
            if (source == null) return null;
            ScanExclusionMatch copy = new ScanExclusionMatch();
            copy.RuleName = source.RuleName ?? "";
            copy.Scope = source.Scope ?? "";
            copy.MatchedText = source.MatchedText ?? "";
            copy.MatchedPath = source.MatchedPath ?? "";
            return copy;
        }

        private CompiledRule FindMatch(string text, string scope)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            foreach (CompiledRule c in _compiled)
            {
                if (c == null || c.Rule == null || !c.Rule.Enabled) continue;
                if (!String.Equals(ScanExclusionScope.Normalize(c.Rule.Scope), scope, StringComparison.OrdinalIgnoreCase)) continue;
                if (Matches(c, text)) return c;
            }
            return null;
        }

        private static bool Matches(CompiledRule compiled, string text)
        {
            string type = ScanExclusionMatchType.Normalize(compiled.Rule.MatchType);
            string pattern = (compiled.Rule.Pattern ?? "").Trim();
            if (pattern.Length == 0) return false;

            if (type == ScanExclusionMatchType.Exact)
                return String.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);
            if (type == ScanExclusionMatchType.Contains)
                return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            if (type == ScanExclusionMatchType.StartsWith)
                return text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
            if (type == ScanExclusionMatchType.EndsWith)
                return text.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
            if (type == ScanExclusionMatchType.Regex && compiled.Regex != null)
            {
                try { return compiled.Regex.IsMatch(text); }
                catch (RegexMatchTimeoutException) { return false; }
            }
            return false;
        }

        private static ScanExclusionMatch CreateMatch(CompiledRule compiled, string text)
        {
            ScanExclusionMatch match = new ScanExclusionMatch();
            match.RuleName = String.IsNullOrWhiteSpace(compiled.Rule.Name)
                ? compiled.Rule.Pattern
                : compiled.Rule.Name.Trim();
            match.Scope = ScanExclusionScope.Normalize(compiled.Rule.Scope);
            match.MatchedText = text ?? "";
            return match;
        }

        private void EnsureCache()
        {
            if (_rules != null && _compiled != null) return;

            if (!File.Exists(_path))
            {
                SetCache(true, GetDefaultRules());
                return;
            }

            try
            {
                string json = File.ReadAllText(_path, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = Int32.MaxValue;
                ScanExclusionRuleConfig config = serializer.Deserialize<ScanExclusionRuleConfig>(json);
                if (config == null)
                {
                    SetCache(true, GetDefaultRules());
                    return;
                }
                SetCache(config.GlobalEnabled, NormalizeRules(config.Rules));
            }
            catch
            {
                SetCache(true, GetDefaultRules());
            }
        }

        private void SaveInternal(bool globalEnabled, IEnumerable<ScanExclusionRule> rules)
        {
            List<ScanExclusionRule> normalized = NormalizeRules(rules);
            ScanExclusionRuleConfig config = new ScanExclusionRuleConfig();
            config.Version = 1;
            config.GlobalEnabled = globalEnabled;
            config.Rules = normalized.Select(delegate(ScanExclusionRule r) { return r.Clone(); }).ToList();

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            string json = serializer.Serialize(config);

            string directory = System.IO.Path.GetDirectoryName(_path);
            if (!String.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, json, new UTF8Encoding(true));
            SetCache(globalEnabled, normalized);
        }

        private void SetCache(bool globalEnabled, IEnumerable<ScanExclusionRule> rules)
        {
            _globalEnabled = globalEnabled;
            _rules = NormalizeRules(rules);
            _compiled = CompileRules(_rules);
            _folderMatchCache.Clear();
        }

        private static List<CompiledRule> CompileRules(IEnumerable<ScanExclusionRule> rules)
        {
            List<CompiledRule> result = new List<CompiledRule>();
            if (rules == null) return result;

            foreach (ScanExclusionRule rule in rules)
            {
                if (rule == null) continue;
                CompiledRule c = new CompiledRule();
                c.Rule = rule.Clone();
                if (String.Equals(ScanExclusionMatchType.Normalize(rule.MatchType), ScanExclusionMatchType.Regex, StringComparison.OrdinalIgnoreCase) &&
                    !String.IsNullOrWhiteSpace(rule.Pattern))
                {
                    try
                    {
                        c.Regex = new Regex(
                            rule.Pattern,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromMilliseconds(200));
                    }
                    catch
                    {
                        c.Regex = null;
                    }
                }
                result.Add(c);
            }
            return result;
        }

        private static List<ScanExclusionRule> NormalizeRules(IEnumerable<ScanExclusionRule> rules)
        {
            List<ScanExclusionRule> result = new List<ScanExclusionRule>();
            if (rules == null) return result;
            foreach (ScanExclusionRule source in rules)
            {
                if (source == null) continue;
                ScanExclusionRule r = source.Clone();
                r.Name = (r.Name ?? "").Trim();
                r.Example = (r.Example ?? "").Trim();
                r.Scope = ScanExclusionScope.Normalize(r.Scope);
                r.MatchType = ScanExclusionMatchType.Normalize(r.MatchType);
                r.Pattern = (r.Pattern ?? "").Trim();
                result.Add(r);
            }
            return result;
        }

        public static bool ValidateRules(IEnumerable<ScanExclusionRule> rules, out string error)
        {
            error = "";
            if (rules == null) return true;

            foreach (ScanExclusionRule rule in rules)
            {
                if (rule == null) continue;
                if (String.IsNullOrWhiteSpace(rule.Name))
                {
                    error = "规则名称不能为空。";
                    return false;
                }
                if (String.IsNullOrWhiteSpace(rule.Pattern))
                {
                    error = "规则内容不能为空：" + rule.Name;
                    return false;
                }
                if (String.Equals(ScanExclusionMatchType.Normalize(rule.MatchType), ScanExclusionMatchType.Regex, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        new Regex(
                            rule.Pattern,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromMilliseconds(200));
                    }
                    catch (Exception ex)
                    {
                        error = "正则表达式无效：" + rule.Name + "\r\n" + ex.Message;
                        return false;
                    }
                }
            }
            return true;
        }

        public static List<ScanExclusionRule> GetDefaultRules()
        {
            List<ScanExclusionRule> result = new List<ScanExclusionRule>();

            ScanExclusionRule aiEnglish = new ScanExclusionRule();
            aiEnglish.Enabled = false;
            aiEnglish.Name = "AI Generated";
            aiEnglish.Example = "[AI Generated] sample.zip";
            aiEnglish.Scope = ScanExclusionScope.FileName;
            aiEnglish.MatchType = ScanExclusionMatchType.Contains;
            aiEnglish.Pattern = "AI Generated";
            result.Add(aiEnglish);

            ScanExclusionRule aiCjk = new ScanExclusionRule();
            aiCjk.Enabled = false;
            aiCjk.Name = "AI 生成作品";
            aiCjk.Example = "[AI生成] sample.zip";
            aiCjk.Scope = ScanExclusionScope.FileName;
            aiCjk.MatchType = ScanExclusionMatchType.Contains;
            aiCjk.Pattern = "AI生成";
            result.Add(aiCjk);

            return result;
        }
    }
}
