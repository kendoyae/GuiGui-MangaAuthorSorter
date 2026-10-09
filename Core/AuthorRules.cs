using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MangaAuthorSorter
{
    internal static class AuthorRules
    {
        private static readonly Regex BracketRegex = new Regex(@"[\[［](.*?)[\]］]|【(.*?)】", RegexOptions.Compiled);
        private static readonly Regex LeadingBracketRegex = new Regex(@"\G\s*(?:[\[［](.*?)[\]］]|【(.*?)】)", RegexOptions.Compiled);
        private static readonly Regex CompositeRegex = new Regex(@"^\s*(.*?)\s*[\(（]\s*(.*?)\s*[\)）]\s*$", RegexOptions.Compiled);
        private static readonly Regex CreatorSeparatorRegex = new Regex(@"\s*(?:,|，|、|&|＆|/|／|\+|＋)\s*", RegexOptions.Compiled);
        private static readonly Regex ParenthesizedMetadataRegex = new Regex(
            @"\G\s*\\?\s*[\(（]\s*([^\)）]{1,80})\s*[\)）]\s*",
            RegexOptions.Compiled);

        // Safe normalization used for automatic matching.
        // It only fixes differences that are very likely to be input / Unicode
        // formatting mistakes. It intentionally does NOT delete internal spaces
        // or arbitrary punctuation, so names such as "John Smith" and
        // "JohnSmith" remain different identities.
        public static string NormalizeText(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "";

            string n = text.Normalize(NormalizationForm.FormKC);
            n = RemoveZeroWidthCharacters(n);
            n = n.Replace('（', '(').Replace('）', ')');
            n = n.Replace('［', '[').Replace('］', ']');
            n = n.Replace('【', '[').Replace('】', ']');

            // Normalize all ordinary whitespace to one ASCII space first.
            n = Regex.Replace(n, @"[\s　]+", " ").Trim();

            // Spaces immediately around bracket boundaries are presentation
            // differences, not identity differences: A (B) == A(B).
            n = Regex.Replace(n, @"\s*([\(\)\[\]])\s*", "$1");

            // Repair exactly one missing closing bracket. This covers common
            // legacy folders such as "自家発電処 (flanvia" without making
            // broad fuzzy assumptions.
            n = RepairSingleMissingClosingBracket(n);

            return n.Trim().ToLowerInvariant();
        }

        // Kept for compatibility with older call sites. From V1.10.24 onward
        // "loose" no longer strips punctuation or all spaces because that could
        // silently merge genuinely different authors.
        public static string NormalizeLoose(string text)
        {
            return NormalizeText(text);
        }

        // Cautious normalization is never used for automatic matching. It is
        // only used to surface a possible existing folder that requires user
        // confirmation. This covers visually similar punctuation variants, an
        // extra trailing bracket, and outer quote-style differences.
        public static string NormalizeCautious(string text)
        {
            string n = NormalizeText(text);
            if (n.Length == 0) return "";

            n = n.Replace('・', '·').Replace('･', '·').Replace('•', '·');
            n = Regex.Replace(n, "[‐‑‒–—―−－]", "-");
            n = Regex.Replace(n, "[~～〜]", "~");
            n = StripOuterQuotePair(n);
            n = RemoveSingleExtraTrailingClosingBracket(n);

            if (n.EndsWith(".", StringComparison.Ordinal) &&
                !n.EndsWith("..", StringComparison.Ordinal))
            {
                n = n.Substring(0, n.Length - 1);
            }

            return n.Trim().ToLowerInvariant();
        }

        private static string RemoveZeroWidthCharacters(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\u200B", "")
                .Replace("\u200C", "")
                .Replace("\u200D", "")
                .Replace("\u2060", "")
                .Replace("\uFEFF", "");
        }

        private static string RepairSingleMissingClosingBracket(string value)
        {
            if (String.IsNullOrEmpty(value)) return value ?? "";

            Stack<char> stack = new Stack<char>();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '(' || c == '[')
                {
                    stack.Push(c);
                    continue;
                }

                if (c == ')' || c == ']')
                {
                    if (stack.Count == 0) return value;
                    char open = stack.Pop();
                    if ((open == '(' && c != ')') ||
                        (open == '[' && c != ']'))
                    {
                        return value;
                    }
                }
            }

            if (stack.Count != 1) return value;
            char missingFor = stack.Pop();
            return value + (missingFor == '(' ? ")" : "]");
        }

        private static string RemoveSingleExtraTrailingClosingBracket(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length < 2) return value ?? "";
            char last = value[value.Length - 1];
            if (last != ')' && last != ']') return value;

            string candidate = value.Substring(0, value.Length - 1);
            int paren = 0;
            int square = 0;
            for (int i = 0; i < candidate.Length; i++)
            {
                char c = candidate[i];
                if (c == '(') paren++;
                else if (c == ')') paren--;
                else if (c == '[') square++;
                else if (c == ']') square--;
                if (paren < 0 || square < 0) return value;
            }

            if (paren == 0 && square == 0) return candidate;
            return value;
        }

        private static string StripOuterQuotePair(string value)
        {
            if (String.IsNullOrEmpty(value)) return value ?? "";
            string n = value.Trim();
            for (int i = 0; i < 2 && n.Length >= 2; i++)
            {
                char first = n[0];
                char last = n[n.Length - 1];
                bool quoted =
                    (first == '\"' && last == '\"') ||
                    (first == '\'' && last == '\'') ||
                    (first == '“' && last == '”') ||
                    (first == '‘' && last == '’') ||
                    (first == '「' && last == '」') ||
                    (first == '『' && last == '』');
                if (!quoted) break;
                n = n.Substring(1, n.Length - 2).Trim();
            }
            return n;
        }

        // Direct-match identity removes only a whole-name display wrapper.
        // It never removes parentheses inside a composite name such as
        // "circle (author)". This lets legacy folders like [author],
        // 【author】 or (author) count as a direct match when the core
        // author name itself is unchanged.
        public static string GetDirectMatchIdentity(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "";

            string value = text.Trim();
            for (int i = 0; i < 2; i++)
            {
                if (value.Length < 2) break;

                char first = value[0];
                char last = value[value.Length - 1];
                bool wrapped =
                    (first == '[' && last == ']') ||
                    (first == '［' && last == '］') ||
                    (first == '【' && last == '】') ||
                    (first == '(' && last == ')') ||
                    (first == '（' && last == '）');

                if (!wrapped) break;

                string inner = value.Substring(1, value.Length - 2).Trim();
                if (inner.Length == 0) break;
                value = inner;
            }

            return value;
        }

        public static bool IsMultiAuthorText(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            return text.IndexOf(',') >= 0 || text.IndexOf('，') >= 0 ||
                   text.IndexOf('&') >= 0 || text.IndexOf('＆') >= 0 ||
                   text.IndexOf('、') >= 0 || text.IndexOf('/') >= 0 || text.IndexOf('／') >= 0 ||
                   text.IndexOf('+') >= 0 || text.IndexOf('＋') >= 0;
        }

        // Parses "circle (author)" and "circle (author A, author B)".
        // A single missing closing parenthesis is repaired with the same strict
        // rule used by safe normalization, which covers filenames such as
        // "木鈴亭 (木鈴カケル, コウリ" without enabling fuzzy text matching.
        public static StructuredAuthorParts GetStructuredAuthorParts(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;

            string repaired = PrepareStructureText(name);
            // Legacy folder names sometimes wrap the whole identity in []/【】
            // and may omit the final square bracket. PrepareStructureText repairs
            // one missing closer; unwrap that display-only shell before parsing
            // the inner "circle (creator)" structure.
            repaired = GetDirectMatchIdentity(repaired);
            Match m = CompositeRegex.Match(repaired);
            if (!m.Success) return null;

            string society = m.Groups[1].Value.Trim();
            string inside = m.Groups[2].Value.Trim();
            // A plus immediately before the parenthesized creator is commonly
            // used as a visual separator: "Xration+(mil)" == "Xration (mil)".
            // Strip only this terminal separator; plus signs elsewhere remain
            // meaningful multi-author delimiters.
            society = society.TrimEnd(' ', '+', '＋');
            if (society.Length == 0 || inside.Length == 0) return null;

            StructuredAuthorParts result = new StructuredAuthorParts();
            result.Society = society;

            string[] pieces = CreatorSeparatorRegex.Split(inside);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string piece in pieces)
            {
                string creator = (piece ?? "").Trim();
                if (creator.Length == 0) continue;
                string key = NormalizeText(creator);
                if (key.Length > 0 && seen.Add(key)) result.Creators.Add(creator);
            }

            if (result.Creators.Count == 0) return null;
            return result;
        }

        private static string PrepareStructureText(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "";
            string n = text.Normalize(NormalizationForm.FormKC);
            n = RemoveZeroWidthCharacters(n);
            n = n.Replace('（', '(').Replace('）', ')');
            n = Regex.Replace(n, @"[\s　]+", " ").Trim();
            n = RepairSingleMissingClosingBracket(n);
            return n.Trim();
        }

        // Conservative segment extraction for legacy folder names such as
        // "雨と棘 雨がっぱ少女群". Only 2-3 whitespace-separated segments
        // are accepted, every segment must be at least two characters, and
        // every segment must contain Japanese/CJK text. Latin personal names
        // such as "John Smith" are intentionally not split.
        public static List<string> GetSafeNameSegments(string name)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(name)) return result;

            string raw = PrepareStructureText(name);
            if (raw.IndexOf('(') >= 0 || raw.IndexOf(')') >= 0 ||
                raw.IndexOf('[') >= 0 || raw.IndexOf(']') >= 0 ||
                raw.IndexOf('【') >= 0 || raw.IndexOf('】') >= 0)
            {
                return result;
            }

            string[] pieces = Regex.Split(raw, @"[\s　]+");
            if (pieces.Length < 2 || pieces.Length > 3) return result;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string piece in pieces)
            {
                string value = (piece ?? "").Trim();
                if (value.Length < 2 || !ContainsJapaneseOrCjk(value)) return new List<string>();
                string key = NormalizeText(value);
                if (key.Length > 0 && seen.Add(key)) result.Add(value);
            }
            return result;
        }

        private static bool ContainsJapaneseOrCjk(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= '\u3040' && c <= '\u30ff') ||
                    (c >= '\u3400' && c <= '\u4dbf') ||
                    (c >= '\u4e00' && c <= '\u9fff') ||
                    (c >= '\uf900' && c <= '\ufaff'))
                {
                    return true;
                }
            }
            return false;
        }

        public static List<string> GetNameAliases(string name)
        {
            List<string> priority = new List<string>();
            List<string> fallback = new List<string>();
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(name)) return result;

            string raw = name.Trim();
            MatchCollection brackets = BracketRegex.Matches(raw);
            if (brackets.Count > 0)
            {
                foreach (Match m in brackets)
                {
                    string inner = m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim();
                    if (inner.Length == 0) continue;
                    priority.Add(inner);
                    AddCompositeAliases(inner, priority);
                    AddPlainMultiAuthorAliases(inner, priority);
                    foreach (string segment in GetSafeNameSegments(inner))
                        priority.Add(segment);
                    foreach (string segment in GetCjkWhitespaceIdentitySegments(inner))
                        priority.Add(segment);
                }

                Match tail = Regex.Match(raw, @"[\]］】]\s*(.+?)\s*$");
                if (tail.Success && tail.Groups[1].Value.Trim().Length > 0)
                    fallback.Add(tail.Groups[1].Value.Trim());

                // 完整目录名作为最后兜底；例如 ※[あらくれた者たち (あらくれ)]。
                fallback.Add(raw);
            }
            else
            {
                priority.Add(raw);
                AddCompositeAliases(raw, priority);
            }

            AddPlainMultiAuthorAliases(raw, priority);
            foreach (string segment in GetSafeNameSegments(raw))
                priority.Add(segment);
            foreach (string segment in GetCjkWhitespaceIdentitySegments(raw))
                priority.Add(segment);

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in Concat(priority, fallback))
            {
                if (String.IsNullOrWhiteSpace(value)) continue;
                string v = value.Trim();
                string key = NormalizeText(v);
                if (key.Length == 0) key = v;
                if (seen.Add(key)) result.Add(v);
            }
            return result;
        }

        private static IEnumerable<string> Concat(List<string> a, List<string> b)
        {
            foreach (string s in a) yield return s;
            foreach (string s in b) yield return s;
        }

        private static void AddCompositeAliases(string candidate, List<string> output)
        {
            StructuredAuthorParts parts = GetStructuredAuthorParts(candidate);
            if (parts == null) return;
            if (!String.IsNullOrWhiteSpace(parts.Society)) output.Add(parts.Society);
            foreach (string creator in parts.Creators)
                if (!String.IsNullOrWhiteSpace(creator)) output.Add(creator);
        }

        private static void AddPlainMultiAuthorAliases(
            string candidate,
            List<string> output)
        {
            if (String.IsNullOrWhiteSpace(candidate) ||
                !IsMultiAuthorText(candidate) ||
                GetStructuredAuthorParts(candidate) != null)
            {
                return;
            }

            string[] pieces = CreatorSeparatorRegex.Split(candidate.Trim());
            if (pieces.Length < 2)
                return;

            foreach (string piece in pieces)
            {
                string value = (piece ?? "").Trim();
                if (value.Length > 0)
                    output.Add(value);
            }
        }

        // Bracketed identity tags occasionally contain a Japanese/CJK identity
        // followed by an English studio label, e.g. "日本名 SAYA PRODUCTS".
        // Extract only whitespace-delimited CJK-bearing segments; never split a
        // purely Latin personal name such as "John Smith".
        private static List<string> GetCjkWhitespaceIdentitySegments(string name)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(name)) return result;

            string raw = PrepareStructureText(name);
            if (raw.IndexOf('(') >= 0 || raw.IndexOf(')') >= 0 ||
                raw.IndexOf('[') >= 0 || raw.IndexOf(']') >= 0)
            {
                return result;
            }

            string[] pieces = Regex.Split(raw, @"[\s　]+");
            if (pieces.Length < 2 || pieces.Length > 4)
                return result;

            foreach (string piece in pieces)
            {
                string value = (piece ?? "").Trim();
                if (value.Length >= 2 && ContainsJapaneseOrCjk(value))
                    result.Add(value);
            }

            return result;
        }

        public static List<string> GetNorms(string name)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string alias in GetNameAliases(name))
            {
                string a = NormalizeText(alias);
                string b = NormalizeLoose(alias);
                string c = NormalizeTerminalPunctuation(alias);
                string d = NormalizeCjkIdentityWhitespace(alias);
                if (a.Length > 0) result.Add(a);
                if (b.Length > 0) result.Add(b);
                if (c.Length > 0) result.Add(c);
                if (d.Length > 0) result.Add(d);
            }
            return new List<string>(result);
        }

        // Some Japanese/CJK creator names are written with a visual separator,
        // for example "葛籠 くずかご", while the folder uses "葛籠くずかご".
        // Collapse whitespace only when every separated part contains CJK or
        // kana, so ordinary Latin identities such as "John Smith" stay apart.
        public static string NormalizeCjkIdentityWhitespace(string text)
        {
            string n = NormalizeText(text);
            if (n.IndexOf(' ') < 0) return n;

            string[] pieces = n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length < 2 || pieces.Length > 4) return n;
            foreach (string piece in pieces)
                if (!ContainsJapaneseOrCjk(piece)) return n;

            return String.Concat(pieces);
        }

        // A single sentence-ending full stop is presentation punctuation when it
        // follows an otherwise complete creator identity. Remove at most one so
        // ellipses and punctuation-bearing names are not broadly collapsed.
        public static string NormalizeTerminalPunctuation(string text)
        {
            string n = NormalizeText(text);
            if (n.Length <= 1) return n;

            char last = n[n.Length - 1];
            if (last != '.' && last != '。' && last != '．' && last != '｡')
                return n;

            char previous = n[n.Length - 2];
            if (previous == '.' || previous == '。' ||
                previous == '．' || previous == '｡')
            {
                return n;
            }

            return n.Substring(0, n.Length - 1).TrimEnd();
        }

        // Cautious keys are used only to propose a candidate that still
        // requires manual confirmation.
        public static List<string> GetCautiousNorms(string name)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string alias in GetNameAliases(name))
            {
                string n = NormalizeCautious(alias);
                if (n.Length > 0) result.Add(n);
            }
            return new List<string>(result);
        }

        // 别名库只做“字面量”匹配：允许去掉 [] / 【】 包装，
        // 但不会把 社团名 (作者名) 自动拆成两个身份。
        public static List<string> GetLiteralNorms(string name)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(name)) return new List<string>();

            List<string> values = new List<string>();
            string raw = name.Trim();
            values.Add(raw);
            foreach (Match m in BracketRegex.Matches(raw))
            {
                string inner = m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim();
                if (inner.Length > 0) values.Add(inner);
            }

            foreach (string value in values)
            {
                string a = NormalizeText(value);
                string b = NormalizeLoose(value);
                if (a.Length > 0) result.Add(a);
                if (b.Length > 0) result.Add(b);
            }
            return new List<string>(result);
        }

        public static string GetPreferredAuthorIdentity(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "";
            Match m = BracketRegex.Match(name.Trim());
            if (m.Success)
                return m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value.Trim();
            return name.Trim();
        }

        public static CompositeParts GetCompositeAuthorParts(string name)
        {
            StructuredAuthorParts structured = GetStructuredAuthorParts(name);
            if (structured == null || structured.Creators.Count != 1) return null;
            CompositeParts p = new CompositeParts();
            p.Society = structured.Society;
            p.Creator = structured.Creators[0];
            return p;
        }

        public static bool IsMetadataTag(string text)
        {
            return TagCleaningRuleStore.MatchesDefaultRules(text);
        }

        public static bool IsMetadataTag(string text, TagCleaningRuleStore cleaningStore)
        {
            return cleaningStore != null
                ? cleaningStore.IsMetadataTag(text)
                : IsMetadataTag(text);
        }

        // Returns all valid leading identity tags instead of stopping at the
        // first one. Cleaning rules are applied to each extracted [] / ［］ / 【】
        // tag before it is allowed into the author matching chain. A cleaned
        // metadata tag is skipped but does not terminate the prefix scan.
        public static List<string> GetAuthorCandidatesFromFileName(string baseName)
        {
            return GetAuthorCandidatesFromFileName(baseName, null);
        }

        public static List<string> GetAuthorCandidatesFromFileName(
            string baseName,
            TagCleaningRuleStore cleaningStore)
        {
            List<string> result = new List<string>();
            if (String.IsNullOrWhiteSpace(baseName)) return result;

            FileNameStructure structure = FileNameStructure.Parse(baseName);
            baseName = structure.IdentityText;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> leadingChain = GetLeadingIdentityChain(baseName, cleaningStore);
            foreach (string value in leadingChain)
            {
                string candidate = (value ?? "").Trim();
                string key = NormalizeText(candidate);
                if (candidate.Length > 0 && seen.Add(key.Length > 0 ? key : candidate))
                    result.Add(candidate);
            }
            if (result.Count > 0)
                return result;

            int position = 0;
            bool consumedLeadingTag = false;

            // Some archives use leading parentheses for the identity instead
            // of square brackets: (circle) title. Skip known event/publication
            // metadata first, then accept one explicit leading identity.
            while (position < baseName.Length)
            {
                Match leadingParenthesis = ParenthesizedMetadataRegex.Match(baseName, position);
                if (!leadingParenthesis.Success || leadingParenthesis.Index != position)
                    break;

                string value = leadingParenthesis.Groups[1].Value.Trim();
                if (IsMetadataTag(value, cleaningStore) || IsPublicationKindTag(value))
                {
                    position = leadingParenthesis.Index + leadingParenthesis.Length;
                    while (position < baseName.Length && Char.IsWhiteSpace(baseName[position])) position++;
                    continue;
                }

                AddFileNameCandidate(result, seen, value, cleaningStore);
                consumedLeadingTag = true;
                position = leadingParenthesis.Index + leadingParenthesis.Length;
                break;
            }

            // Repair narrowly scoped legacy prefixes before the normal bracket
            // parser: [circle (creator) missing its ], or an identity ending in
            // a stray ]. The extracted text must stay at the filename front.
            if (!consumedLeadingTag && position < baseName.Length)
            {
                string remaining = baseName.Substring(position).TrimStart();
                if (remaining.StartsWith("[", StringComparison.Ordinal) ||
                    remaining.StartsWith("［", StringComparison.Ordinal) ||
                    remaining.StartsWith("【", StringComparison.Ordinal))
                {
                    Match incomplete = Regex.Match(
                        remaining,
                        @"^[\[［【]\s*([^\]］】]{1,100}?[\)）])(?=\s|$)");
                    if (incomplete.Success)
                    {
                        AddFileNameCandidate(result, seen, incomplete.Groups[1].Value, cleaningStore);
                        consumedLeadingTag = true;
                    }
                }
                else
                {
                    int strayClose = remaining.IndexOfAny(new[] { ']', '］', '】' });
                    if (strayClose > 0 && strayClose <= 100)
                    {
                        AddFileNameCandidate(result, seen, remaining.Substring(0, strayClose), cleaningStore);
                        consumedLeadingTag = true;
                    }
                    else
                    {
                        Match structuredPrefix = Regex.Match(
                            remaining,
                            @"^(.{1,60}?[\(（][^\)）]{1,40}[\)）])(?=\s|$)");
                        if (structuredPrefix.Success)
                        {
                            AddFileNameCandidate(result, seen, structuredPrefix.Groups[1].Value, cleaningStore);
                            consumedLeadingTag = true;
                        }
                        else
                        {
                            Match latinPrefix = Regex.Match(
                                remaining,
                                @"^([A-Za-z][A-Za-z0-9_.-]{2,39})(?=\s+[\(（])");
                            if (latinPrefix.Success)
                            {
                                AddFileNameCandidate(result, seen, latinPrefix.Groups[1].Value, cleaningStore);
                                consumedLeadingTag = true;
                            }
                        }
                    }
                }
            }

            // Publication-kind labels can precede the actual identity run:
            // (同人誌) [circle] [creator]. Treat the exact, well-known labels
            // as structure even when an older user cleaning-rule file has not
            // yet acquired the corresponding new default rule.
            int leadingIdentityPosition;
            if (TryAdvancePastParenthesizedMetadata(
                    baseName,
                    position,
                    cleaningStore,
                    out leadingIdentityPosition))
            {
                position = leadingIdentityPosition;
            }

            while (position < baseName.Length)
            {
                Match m = LeadingBracketRegex.Match(baseName, position);
                if (!m.Success || m.Index != position || m.Length <= 0) break;
                consumedLeadingTag = true;

                string candidate = m.Groups[1].Success
                    ? m.Groups[1].Value.Trim()
                    : m.Groups[2].Value.Trim();

                if (candidate.Length > 0 && !IsMetadataTag(candidate, cleaningStore))
                {
                    string key = NormalizeText(candidate);
                    if (key.Length == 0) key = candidate;
                    if (seen.Add(key)) result.Add(candidate);
                }

                position = m.Index + m.Length;

                int metadataPosition;
                if (TryAdvancePastParenthesizedMetadata(
                        baseName,
                        position,
                        cleaningStore,
                        out metadataPosition))
                {
                    position = metadataPosition;
                    continue;
                }

                // Permit another leading identity tag wrapped by presentation
                // punctuation: [author A]([author B]) title. Only advance across
                // these characters when an actual bracket tag follows, so title
                // text is never consumed as identity metadata.
                int probe = position;
                while (probe < baseName.Length &&
                       IsIdentityWrapperSeparator(baseName[probe]))
                {
                    probe++;
                }

                if (probe > position && probe < baseName.Length &&
                    IsIdentityBracketStart(baseName[probe]))
                {
                    position = probe;
                }
            }

            // Preserve legacy behavior for filenames that have text before the
            // first identity bracket, for example event prefixes such as
            // "(C75) [circle (author)] ...". Once a real leading bracket run
            // was consumed, do not scan later title/metadata brackets.
            if (!consumedLeadingTag)
            {
                foreach (Match m in BracketRegex.Matches(baseName))
                {
                    string candidate = m.Groups[1].Success
                        ? m.Groups[1].Value.Trim()
                        : m.Groups[2].Value.Trim();
                    if (candidate.Length == 0 || IsMetadataTag(candidate, cleaningStore)) continue;
                    string key = NormalizeText(candidate);
                    if (key.Length == 0) key = candidate;
                    if (seen.Add(key)) result.Add(candidate);
                    break;
                }
            }

            return result;
        }

        private static void AddFileNameCandidate(
            List<string> result,
            HashSet<string> seen,
            string value,
            TagCleaningRuleStore cleaningStore)
        {
            string candidate = (value ?? "").Trim();
            if (candidate.Length == 0 || IsMetadataTag(candidate, cleaningStore))
                return;
            string key = NormalizeText(candidate);
            if (key.Length == 0) key = candidate;
            if (seen.Add(key)) result.Add(candidate);
        }

        private static List<string> GetLeadingIdentityChain(
            string text,
            TagCleaningRuleStore cleaningStore)
        {
            List<string> identities = new List<string>();
            List<string> sourceFallbacks = new List<string>();
            if (String.IsNullOrWhiteSpace(text)) return identities;

            int position = 0;
            bool parsedToken = false;
            while (position < text.Length)
            {
                while (position < text.Length &&
                       (Char.IsWhiteSpace(text[position]) ||
                        text[position] == '+' || text[position] == '＋' ||
                        text[position] == '\\' || text[position] == '/'))
                {
                    position++;
                }
                if (position >= text.Length) break;

                Match bracket = LeadingBracketRegex.Match(text, position);
                if (bracket.Success && bracket.Index == position && bracket.Length > 0)
                {
                    parsedToken = true;
                    string value = bracket.Groups[1].Success
                        ? bracket.Groups[1].Value.Trim()
                        : bracket.Groups[2].Value.Trim();
                    if (IsMetadataTag(value, cleaningStore))
                    {
                        if (IsSourceIdentityFallback(value))
                            sourceFallbacks.Add(value);
                    }
                    else if (value.Length > 0)
                    {
                        identities.Add(value);
                    }
                    position = bracket.Index + bracket.Length;
                    continue;
                }

                Match parenthesis = ParenthesizedMetadataRegex.Match(text, position);
                if (parenthesis.Success && parenthesis.Index == position && parenthesis.Length > 0)
                {
                    parsedToken = true;
                    string value = parenthesis.Groups[1].Value.Trim();
                    if (IsMetadataTag(value, cleaningStore) ||
                        IsPublicationKindTag(value) ||
                        IsConventionMetadata(value))
                    {
                        position = parenthesis.Index + parenthesis.Length;
                        continue;
                    }

                    // Continue through adjacent identity fields; the title
                    // boundary, rather than the first candidate, ends parsing.
                    if (value.Length > 0)
                        identities.Add(value);
                    position = parenthesis.Index + parenthesis.Length;
                    continue;
                }

                break;
            }

            if (identities.Count == 0 && parsedToken && sourceFallbacks.Count == 1)
                identities.Add(sourceFallbacks[0]);
            return identities;
        }

        private static bool IsSourceIdentityFallback(string value)
        {
            string n = NormalizeText(value);
            return n == NormalizeText("ニジエ") ||
                   n == NormalizeText("Pixiv");
        }

        private static bool IsConventionMetadata(string value)
        {
            string n = (value ?? "").Normalize(NormalizationForm.FormKC).Trim();
            return Regex.IsMatch(
                n,
                @"^(?:C|AC|COMIC|COMITIA|コミケ|コミティア)\s*\d",
                RegexOptions.IgnoreCase);
        }

        private static bool TryAdvancePastParenthesizedMetadata(
            string text,
            int position,
            TagCleaningRuleStore cleaningStore,
            out int nextPosition)
        {
            nextPosition = position;
            if (String.IsNullOrEmpty(text) || position < 0 || position >= text.Length)
                return false;

            int cursor = position;
            bool skipped = false;
            while (cursor < text.Length)
            {
                Match metadata = ParenthesizedMetadataRegex.Match(text, cursor);
                if (!metadata.Success || metadata.Index != cursor || metadata.Length <= 0)
                    break;

                string value = metadata.Groups[1].Value.Trim();
                if (value.Length == 0 ||
                    (!IsMetadataTag(value, cleaningStore) &&
                     !IsPublicationKindTag(value)))
                    break;

                skipped = true;
                cursor = metadata.Index + metadata.Length;
            }

            if (!skipped)
                return false;

            while (cursor < text.Length && Char.IsWhiteSpace(text[cursor]))
                cursor++;

            if (cursor >= text.Length || !IsIdentityBracketStart(text[cursor]))
                return false;

            nextPosition = cursor;
            return true;
        }

        private static bool IsPublicationKindTag(string value)
        {
            string n = NormalizeText(value);
            return n == NormalizeText("同人誌") ||
                   n == NormalizeText("同人志");
        }

        private static bool IsIdentityWrapperSeparator(char value)
        {
            return Char.IsWhiteSpace(value) ||
                value == '(' || value == ')' ||
                value == '（' || value == '）' ||
                value == '\\' || value == '/';
        }

        private static bool IsIdentityBracketStart(char value)
        {
            return value == '[' || value == '［' || value == '【';
        }

        public static string GetAuthorFromFileName(string baseName)
        {
            List<string> candidates = GetAuthorCandidatesFromFileName(baseName);
            return candidates.Count > 0 ? candidates[0] : null;
        }

        public static bool StartsWithCompleteIdentity(string fileBaseName, string identity)
        {
            string file = NormalizeText(fileBaseName);
            string name = NormalizeText(GetDirectMatchIdentity(identity));
            if (file.Length == 0 || name.Length < 2 || !file.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (file.Length == name.Length)
                return true;

            char boundary = file[name.Length];
            return Char.IsWhiteSpace(boundary) ||
                boundary == '(' || boundary == '[' ||
                boundary == '-' || boundary == '_' ||
                boundary == '・' || boundary == '·' ||
                boundary == '／' || boundary == '/';
        }

        // Some legacy names place an unwrapped convention descriptor before
        // the creator, for example "コミケ96ダイジェスト版 荒井啓 [漢化]".
        // Only a complete, already-existing identity immediately after a
        // recognized convention prefix is accepted; arbitrary title text is
        // never searched for author names in the middle.
        public static bool StartsWithConventionDescriptionThenIdentity(
            string fileBaseName,
            string identity)
        {
            string file = (fileBaseName ?? "")
                .Normalize(NormalizationForm.FormKC)
                .Trim();
            if (file.Length == 0) return false;

            Match prefix = Regex.Match(
                file,
                @"^(?:(?:C|コミケ|COMITIA|コミティア|FF|SC|エアコミケ|関西コミティア)\s*\d{1,4}|COMIC1\s*[☆★＊*]?\s*\d{1,4}|サンクリ(?:\s*\d{1,4}|\s*\d{4}\s+(?:Spring|Summer|Autumn|Winter)))[^\s]*\s+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!prefix.Success) return false;

            return StartsWithCompleteIdentity(
                file.Substring(prefix.Length),
                identity);
        }
    }
}
