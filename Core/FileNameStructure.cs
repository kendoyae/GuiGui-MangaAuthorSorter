using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MangaAuthorSorter
{
    // Syntax facts are independent of entity data. Statistical evidence is
    // deliberately scoped to one scan and never becomes a cleaning rule.
    internal sealed class FileNameStructure
    {
        public const string RuleVersion = "6";
        private static readonly ConcurrentDictionary<string, FileNameStructure> SyntaxCache =
            new ConcurrentDictionary<string, FileNameStructure>(StringComparer.Ordinal);
        public string Original = "";
        public string Prefix = "";
        public string IdentityText = "";
        public string WorkTitle = "";
        public string Reason = "";
        public int PrefixLength;
        public int PrefixConfidence;
        public readonly List<string> Creators = new List<string>();
        public string Society = "";
        public bool SuspectedActivity { get { return PrefixConfidence >= 80; } }

        public static FileNameStructure Parse(string text, ISet<string> activities = null)
        {
            string key = text ?? "";
            FileNameStructure syntax;
            if (!SyntaxCache.TryGetValue(key, out syntax))
            {
                syntax = ParseCore(key, null);
                // Bound memory during long scans. Eviction changes no facts.
                if (SyntaxCache.Count >= 8192) SyntaxCache.Clear();
                SyntaxCache.TryAdd(key, syntax);
            }
            if (!syntax.SuspectedActivity && activities != null &&
                activities.Contains(AuthorRules.NormalizeText(syntax.Prefix))) return ParseCore(key, activities);
            return syntax;
        }

        private static FileNameStructure ParseCore(string text, ISet<string> activities)
        {
            FileNameStructure result = new FileNameStructure { Original = text ?? "", IdentityText = text ?? "" };
            string normalized = result.Original.Normalize(NormalizationForm.FormKC);
            Match prefix = Regex.Match(normalized, @"^\s*\(([^()]{1,80})\)\s*");
            if (!prefix.Success) return result;
            result.Prefix = prefix.Groups[1].Value.Trim();
            // FormKC can change string length; find the boundary in raw text.
            Match raw = Regex.Match(result.Original, @"^\s*[（(][^()（）]{1,80}[)）]\s*");
            if (!raw.Success) return result;
            result.PrefixLength = raw.Length;
            string rest = result.Original.Substring(raw.Length);
            Match identity = Regex.Match(rest, @"^[\[［【]([^\]］】]+)[\]］】]");
            StructuredAuthorParts parts = identity.Success
                ? AuthorRules.GetStructuredAuthorParts(identity.Groups[1].Value) : null;
            if (parts != null)
            {
                result.Society = parts.Society;
                result.Creators.AddRange(parts.Creators);
                result.PrefixConfidence = 90;
                result.Reason = "leading parentheses before [circle (artist)]";
            }
            else if (Regex.IsMatch(result.Prefix,
                @"(?:同人祭|同人誌即売会|同人即売会|例大祭|コミックマーケット|Comic\s*Market|COMITIA|COMIC1|コミティア|コミケ|^(?:C|AC)\s*\d+)", RegexOptions.IgnoreCase))
            {
                result.PrefixConfidence = 85;
                result.Reason = "activity naming pattern";
            }
            else if (activities != null && activities.Contains(AuthorRules.NormalizeText(result.Prefix)))
            {
                result.PrefixConfidence = 80;
                result.Reason = "same prefix before at least three distinct circle/artist pairs in this scan";
            }
            if (result.SuspectedActivity)
            {
                result.IdentityText = rest;
                string title = identity.Success ? rest.Substring(identity.Length).Trim() : rest.Trim();
                // Suffix fields describe parody, translation and editions.
                result.WorkTitle = Regex.Split(title, @"\s*[\[［【(（]")[0].Trim();
            }
            return result;
        }

        public static HashSet<string> DiscoverActivities(IEnumerable<string> names)
        {
            Dictionary<string, HashSet<string>> evidence = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names ?? Enumerable.Empty<string>())
            {
                FileNameStructure parsed = Parse(name);
                if (parsed.Prefix.Length == 0 || parsed.Creators.Count == 0) continue;
                string key = AuthorRules.NormalizeText(parsed.Prefix);
                HashSet<string> identities;
                if (!evidence.TryGetValue(key, out identities)) evidence[key] = identities = new HashSet<string>();
                // Count distinct complete circle/artist pairs, never file frequency.
                identities.Add(AuthorRules.NormalizeText(parsed.Society) + "|" + String.Join("|", parsed.Creators.Select(AuthorRules.NormalizeText)));
            }
            return new HashSet<string>(evidence.Where(x => x.Value.Count >= 3).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        }
    }
}
