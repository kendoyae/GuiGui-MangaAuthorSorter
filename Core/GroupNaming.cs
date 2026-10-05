using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MangaAuthorSorter
{
    internal static class GroupNaming
    {
        public const string RuleNumeric = "numeric";
        public const string RuleNumeric2 = "numeric2";
        public const string RuleNumeric3 = "numeric3";
        public const string RuleAlphaUpper = "alpha-upper";
        public const string RuleAlphaLower = "alpha-lower";

        public const string DefaultPrefix = "_漫画作者";
        public const string DefaultSuffix = "";
        public const string DefaultRuleId = RuleNumeric;
        public const string DefaultTemplate = "_漫画作者{n}";

        private sealed class RuleDefinition
        {
            public string Id;
            public string Token;

            public RuleDefinition(string id, string token)
            {
                Id = id;
                Token = token;
            }
        }

        private static readonly RuleDefinition[] Rules =
        {
            new RuleDefinition(RuleNumeric, "{n}"),
            new RuleDefinition(RuleNumeric2, "{n2}"),
            new RuleDefinition(RuleNumeric3, "{n3}"),
            new RuleDefinition(RuleAlphaUpper, "{A}"),
            new RuleDefinition(RuleAlphaLower, "{a}")
        };

        public static List<string> GetRuleIds()
        {
            return Rules.Select(delegate(RuleDefinition x) { return x.Id; }).ToList();
        }

        public static string GetTokenForRule(string ruleId)
        {
            RuleDefinition rule = FindRuleById(ruleId);
            return rule != null ? rule.Token : "{n}";
        }

        public static bool TryBuildTemplate(
            string prefix,
            string ruleId,
            string suffix,
            out string template,
            out string error)
        {
            template = "";
            error = "";

            RuleDefinition rule = FindRuleById(ruleId);
            if (rule == null)
            {
                error = "未知的分组编号规则。";
                return false;
            }

            string before = prefix ?? "";
            string after = suffix ?? "";

            template = before + rule.Token + after;

            string normalized;
            if (!TryValidateTemplate(template, out normalized, out error))
                return false;

            template = normalized;
            return true;
        }

        public static bool TryDecomposeTemplate(
            string template,
            out string prefix,
            out string ruleId,
            out string suffix)
        {
            prefix = DefaultPrefix;
            ruleId = DefaultRuleId;
            suffix = DefaultSuffix;

            string normalized;
            string error;
            if (!TryValidateTemplate(template, out normalized, out error))
                return false;

            RuleDefinition found = null;
            int tokenIndex = -1;

            foreach (RuleDefinition rule in Rules)
            {
                int index = normalized.IndexOf(rule.Token, StringComparison.Ordinal);
                if (index >= 0)
                {
                    found = rule;
                    tokenIndex = index;
                    break;
                }
            }

            if (found == null || tokenIndex < 0)
                return false;

            prefix = normalized.Substring(0, tokenIndex);
            ruleId = found.Id;
            suffix = normalized.Substring(tokenIndex + found.Token.Length);
            return true;
        }

        public static bool TryValidateTemplate(
            string template,
            out string normalized,
            out string error)
        {
            normalized = (template ?? "").Trim();
            error = "";

            if (normalized.Length == 0)
            {
                error = "作者分组名称不能为空。";
                return false;
            }

            RuleDefinition found = null;
            int tokenCount = 0;

            foreach (RuleDefinition rule in Rules)
            {
                int start = 0;
                while (true)
                {
                    int index = normalized.IndexOf(rule.Token, start, StringComparison.Ordinal);
                    if (index < 0)
                        break;

                    tokenCount++;
                    if (found == null)
                        found = rule;

                    start = index + rule.Token.Length;
                }
            }

            if (tokenCount == 0 || found == null)
            {
                error = "作者分组命名必须包含一种编号规则。";
                return false;
            }

            if (tokenCount != 1)
            {
                error = "作者分组命名只能包含一个编号规则。";
                return false;
            }

            string sample = RenderUnchecked(normalized, found, 1);

            if (sample.Length == 0)
            {
                error = "作者分组命名无效。";
                return false;
            }

            if (sample.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "作者分组命名包含 Windows 文件夹名不允许的字符。";
                return false;
            }

            if (sample.EndsWith(".", StringComparison.Ordinal) ||
                sample.EndsWith(" ", StringComparison.Ordinal))
            {
                error = "作者分组名称不能以句点或空格结尾。";
                return false;
            }

            return true;
        }

        public static string Render(string template, int number)
        {
            string normalized;
            string error;

            if (!TryValidateTemplate(template, out normalized, out error))
                normalized = DefaultTemplate;

            RuleDefinition rule = FindRuleInTemplate(normalized);
            if (rule == null)
                rule = FindRuleById(DefaultRuleId);

            return RenderUnchecked(normalized, rule, number);
        }

        public static bool TryParseGroupNumber(
            string template,
            string folderName,
            out int number)
        {
            number = 0;

            string normalized;
            string error;

            if (!TryValidateTemplate(template, out normalized, out error))
                return false;

            RuleDefinition rule = FindRuleInTemplate(normalized);
            if (rule == null)
                return false;

            string capturePattern;
            if (String.Equals(rule.Id, RuleNumeric2, StringComparison.Ordinal))
                capturePattern = "(?<n>[0-9]{2,})";
            else if (String.Equals(rule.Id, RuleNumeric3, StringComparison.Ordinal))
                capturePattern = "(?<n>[0-9]{3,})";
            else if (String.Equals(rule.Id, RuleAlphaUpper, StringComparison.Ordinal))
                capturePattern = "(?<n>[A-Za-z]+)";
            else if (String.Equals(rule.Id, RuleAlphaLower, StringComparison.Ordinal))
                capturePattern = "(?<n>[A-Za-z]+)";
            else
                capturePattern = "(?<n>[0-9]+)";

            string pattern =
                "^" +
                Regex.Escape(normalized)
                    .Replace(Regex.Escape(rule.Token), capturePattern) +
                "$";

            Match match = Regex.Match(
                folderName ?? "",
                pattern,
                RegexOptions.CultureInvariant);

            if (!match.Success)
                return false;

            string value = match.Groups["n"].Value;

            if (String.Equals(rule.Id, RuleAlphaUpper, StringComparison.Ordinal) ||
                String.Equals(rule.Id, RuleAlphaLower, StringComparison.Ordinal))
            {
                return TryAlphaToNumber(value, out number);
            }

            return Int32.TryParse(value, out number) && number >= 0;
        }

        public static List<string> NormalizeTemplates(
            string currentTemplate,
            IEnumerable<string> history)
        {
            List<string> result = new List<string>();

            AddTemplate(result, DefaultTemplate);
            AddTemplate(result, currentTemplate);

            if (history != null)
            {
                foreach (string item in history)
                    AddTemplate(result, item);
            }

            return result;
        }

        public static bool IsGroupFolderName(
            string folderName,
            IEnumerable<string> templates)
        {
            if (templates == null)
                return false;

            foreach (string template in templates)
            {
                int number;
                if (TryParseGroupNumber(template, folderName, out number))
                    return true;
            }

            return false;
        }

        private static string RenderUnchecked(
            string template,
            RuleDefinition rule,
            int number)
        {
            if (number < 1)
                number = 1;

            string value;
            if (String.Equals(rule.Id, RuleNumeric2, StringComparison.Ordinal))
                value = number.ToString("D2");
            else if (String.Equals(rule.Id, RuleNumeric3, StringComparison.Ordinal))
                value = number.ToString("D3");
            else if (String.Equals(rule.Id, RuleAlphaUpper, StringComparison.Ordinal))
                value = NumberToAlpha(number, false);
            else if (String.Equals(rule.Id, RuleAlphaLower, StringComparison.Ordinal))
                value = NumberToAlpha(number, true);
            else
                value = number.ToString();

            return template.Replace(rule.Token, value);
        }

        private static string NumberToAlpha(int number, bool lower)
        {
            if (number < 1)
                number = 1;

            string result = "";
            int value = number;

            while (value > 0)
            {
                value--;
                char ch = (char)('A' + (value % 26));
                result = ch + result;
                value /= 26;
            }

            return lower ? result.ToLowerInvariant() : result;
        }

        private static bool TryAlphaToNumber(string value, out int number)
        {
            number = 0;
            if (String.IsNullOrWhiteSpace(value))
                return false;

            try
            {
                checked
                {
                    int result = 0;
                    foreach (char raw in value)
                    {
                        char ch = Char.ToUpperInvariant(raw);
                        if (ch < 'A' || ch > 'Z')
                            return false;

                        result = result * 26 + (ch - 'A' + 1);
                    }

                    if (result < 1)
                        return false;

                    number = result;
                    return true;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static RuleDefinition FindRuleById(string ruleId)
        {
            return Rules.FirstOrDefault(
                delegate(RuleDefinition x)
                {
                    return String.Equals(
                        x.Id,
                        ruleId,
                        StringComparison.OrdinalIgnoreCase);
                });
        }

        private static RuleDefinition FindRuleInTemplate(string template)
        {
            if (String.IsNullOrEmpty(template))
                return null;

            return Rules.FirstOrDefault(
                delegate(RuleDefinition x)
                {
                    return template.IndexOf(x.Token, StringComparison.Ordinal) >= 0;
                });
        }

        private static void AddTemplate(List<string> list, string template)
        {
            string normalized;
            string error;

            if (!TryValidateTemplate(template, out normalized, out error))
                return;

            if (!list.Any(
                    delegate(string x)
                    {
                        return String.Equals(
                            x,
                            normalized,
                            StringComparison.Ordinal);
                    }))
            {
                list.Add(normalized);
            }
        }
    }
}
