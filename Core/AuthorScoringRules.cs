using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MangaAuthorSorter
{
    // Optional final fallback after the classic extraction/matching chain.
    // It never replaces a result already recognized by the classic rules.
    internal static class AuthorScoringRules
    {
        private const int AutoThreshold = 100;
        private const int ReviewThreshold = 60;
        private const int RequiredLead = 20;

        internal sealed class Identity
        {
            public string Name = "";
            public string Normalized = "";
            public bool ConfirmedAlias;
            public bool FolderIdentity;
        }

        private sealed class PreparedFile
        {
            public string Raw = "";
            public string Normalized = "";
            public List<string> Regions = new List<string>();
            public List<string> MetadataTags = new List<string>();
            public List<string> Creators = new List<string>();
        }

        private sealed class Ranked
        {
            public AuthorFolder Folder;
            public int Score;
            public List<string> Evidence = new List<string>();
        }

        // One context lives for one archive-plan pass. It turns alias/entity
        // discovery and identity normalization into an index build instead of
        // repeating them for every file.
        internal sealed class Context
        {
            private const int MaxScoringCandidates = 24;
            private readonly Dictionary<string, List<string>> _aliasesByNorm =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<AuthorFolder, List<Identity>> _identityCache =
                new Dictionary<AuthorFolder, List<Identity>>();
            private readonly AuthorEntityIndex _entityIndex;
            private readonly TagCleaningRuleStore _cleaningStore;
            private readonly Dictionary<string, HashSet<AuthorFolder>> _gramIndex =
                new Dictionary<string, HashSet<AuthorFolder>>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<AuthorFolder> _indexedFolders = new HashSet<AuthorFolder>();

            internal Context(
                IEnumerable<AliasGroup> aliasGroups,
                AuthorEntityIndex entityIndex,
                TagCleaningRuleStore cleaningStore)
            {
                _entityIndex = entityIndex;
                _cleaningStore = cleaningStore;
                foreach (AliasGroup group in aliasGroups ?? Enumerable.Empty<AliasGroup>())
                {
                    if (group == null || group.Norms == null) continue;
                    foreach (string norm in group.Norms)
                    {
                        List<string> names;
                        if (!_aliasesByNorm.TryGetValue(norm, out names))
                        {
                            names = new List<string>();
                            _aliasesByNorm[norm] = names;
                        }
                        foreach (string name in group.Names ?? new List<string>())
                            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
                    }
                }
            }

            internal AuthorMatchResult Match(string fileBaseName, List<AuthorFolder> folders)
            {
                PreparedFile file = PrepareFile(fileBaseName, _cleaningStore);
                return MatchPrepared(file, GetCandidates(file, folders), this);
            }

            private List<AuthorFolder> GetCandidates(PreparedFile file, List<AuthorFolder> folders)
            {
                foreach (AuthorFolder folder in folders ?? new List<AuthorFolder>())
                {
                    if (folder == null || !_indexedFolders.Add(folder)) continue;
                    HashSet<string> folderGrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Identity identity in GetIdentities(folder))
                        foreach (string gram in GetGrams(identity.Normalized)) folderGrams.Add(gram);
                    foreach (string gram in folderGrams)
                    {
                        HashSet<AuthorFolder> values;
                        if (!_gramIndex.TryGetValue(gram, out values))
                        {
                            values = new HashSet<AuthorFolder>();
                            _gramIndex[gram] = values;
                        }
                        values.Add(folder);
                    }
                }

                Dictionary<AuthorFolder, int> hits = new Dictionary<AuthorFolder, int>();
                HashSet<string> fileGrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string gram in GetGrams(file != null ? file.Normalized : "")) fileGrams.Add(gram);
                foreach (string region in file != null ? file.Regions : new List<string>())
                    foreach (string gram in GetGrams(region)) fileGrams.Add(gram);
                foreach (string gram in fileGrams)
                {
                    HashSet<AuthorFolder> matched;
                    if (!_gramIndex.TryGetValue(gram, out matched)) continue;
                    foreach (AuthorFolder folder in matched)
                    {
                        int count;
                        hits.TryGetValue(folder, out count);
                        hits[folder] = count + 1;
                    }
                }
                return hits.OrderByDescending(delegate(KeyValuePair<AuthorFolder, int> x) { return x.Value; })
                    .ThenBy(delegate(KeyValuePair<AuthorFolder, int> x) { return x.Key.AuthorName; }, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxScoringCandidates)
                    .Select(delegate(KeyValuePair<AuthorFolder, int> x) { return x.Key; })
                    .ToList();
            }

            private static IEnumerable<string> GetGrams(string value)
            {
                string compact = (value ?? "").Replace(" ", "");
                if (compact.Length == 1) { yield return compact; yield break; }
                for (int i = 0; i + 1 < compact.Length; i++) yield return compact.Substring(i, 2);
            }

            internal List<Identity> GetIdentities(AuthorFolder folder)
            {
                List<Identity> identities;
                if (_identityCache.TryGetValue(folder, out identities)) return identities;
                identities = BuildIdentities(folder, _aliasesByNorm, _entityIndex);
                _identityCache[folder] = identities;
                return identities;
            }
        }

        public static Context CreateContext(
            IEnumerable<AliasGroup> aliasGroups,
            AuthorEntityIndex entityIndex,
            TagCleaningRuleStore cleaningStore)
        {
            return new Context(aliasGroups, entityIndex, cleaningStore);
        }

        public static AuthorMatchResult Match(
            string fileBaseName,
            List<AuthorFolder> folders,
            List<AliasGroup> aliasGroups,
            AuthorEntityIndex entityIndex,
            TagCleaningRuleStore cleaningStore)
        {
            return CreateContext(aliasGroups, entityIndex, cleaningStore).Match(fileBaseName, folders);
        }

        private static AuthorMatchResult MatchPrepared(
            PreparedFile file,
            List<AuthorFolder> folders,
            Context context)
        {
            AuthorMatchResult none = NewResult(AuthorMatchType.NotFound, null, "评分识别：没有足够证据");
            string fileBaseName = file != null ? file.Raw : "";
            if (String.IsNullOrWhiteSpace(fileBaseName) || folders == null || folders.Count == 0)
                return none;

            List<Ranked> ranked = new List<Ranked>();
            foreach (AuthorFolder folder in folders)
            {
                if (folder == null || String.IsNullOrWhiteSpace(folder.AuthorName)) continue;
                Ranked item = ScoreFolder(file, folder, context.GetIdentities(folder));
                if (item.Score > 0) ranked.Add(item);
            }

            ranked = ranked
                .OrderByDescending(delegate(Ranked x) { return x.Score; })
                .ThenBy(delegate(Ranked x) { return x.Folder.AuthorName; }, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ranked.Count == 0) return none;

            Ranked first = ranked[0];
            int secondScore = ranked.Count > 1 ? ranked[1].Score : 0;
            string why = "评分识别：" + first.Score + " 分" +
                (secondScore > 0 ? "，第二名 " + secondScore + " 分" : "") +
                "；" + String.Join("；", first.Evidence.ToArray());

            if (first.Score >= AutoThreshold && first.Score - secondScore >= RequiredLead)
            {
                AuthorMatchResult matched = NewResult(AuthorMatchType.Matched, first.Folder, why);
                matched.Score = first.Score;
                matched.RunnerUpScore = secondScore;
                return matched;
            }

            if (first.Score >= ReviewThreshold)
            {
                AuthorMatchResult choice = NewResult(AuthorMatchType.Choice, null, why + "；未达到自动识别门槛，请确认");
                choice.Score = first.Score;
                choice.RunnerUpScore = secondScore;
                foreach (Ranked candidate in ranked)
                {
                    if (candidate.Score < ReviewThreshold) break;
                    if (candidate.Score + RequiredLead < first.Score) break;
                    choice.Candidates.Add(candidate.Folder);
                }
                if (choice.Candidates.Count == 0) choice.Candidates.Add(first.Folder);
                return choice;
            }

            none.Score = first.Score;
            none.RunnerUpScore = secondScore;
            none.Why = why + "；低于待确认门槛";
            return none;
        }

        private static Ranked ScoreFolder(
            PreparedFile file,
            AuthorFolder folder,
            List<Identity> identities)
        {
            Ranked result = new Ranked();
            result.Folder = folder;
            int best = 0;
            List<string> bestEvidence = new List<string>();
            int strongSignals = 0;

            foreach (Identity identity in identities)
            {
                List<string> evidence;
                int score = ScoreIdentity(file, identity, out evidence);
                if (score >= 80) strongSignals++;
                if (score > best)
                {
                    best = score;
                    bestEvidence = evidence;
                }
            }

            if (best > 0 && strongSignals >= 2)
            {
                best += 20;
                bestEvidence.Add("多个独立身份字段一致 +20");
            }

            result.Score = best;
            result.Evidence = bestEvidence;
            return result;
        }

        private static List<Identity> BuildIdentities(
            AuthorFolder folder,
            Dictionary<string, List<string>> aliasesByNorm,
            AuthorEntityIndex entityIndex)
        {
            List<Identity> result = new List<Identity>();
            AddIdentity(result, folder.AuthorName, false, true);
            AddIdentity(result, folder.PreferredIdentity, false, true);

            HashSet<string> aliasNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string norm in folder.Norms)
            {
                List<string> names;
                if (!aliasesByNorm.TryGetValue(norm, out names)) continue;
                foreach (string name in names) aliasNames.Add(name);
            }
            foreach (string name in aliasNames) AddIdentity(result, name, true, false);

            if (entityIndex != null)
            {
                AuthorEntityMatch entity = entityIndex.Resolve(folder.AuthorName);
                if (!entity.Found && !String.IsNullOrWhiteSpace(folder.PreferredIdentity))
                    entity = entityIndex.Resolve(folder.PreferredIdentity);
                if (entity.Found)
                    foreach (string name in entity.IdentityNames)
                        AddIdentity(result, name, true, false);
            }
            return result;
        }

        private static void AddIdentity(List<Identity> output, string name, bool alias, bool folder)
        {
            if (String.IsNullOrWhiteSpace(name)) return;
            string norm = AuthorRules.NormalizeText(AuthorRules.GetDirectMatchIdentity(name));
            if (norm.Length == 0) return;
            Identity existing = output.FirstOrDefault(delegate(Identity x)
            {
                return String.Equals(
                    AuthorRules.NormalizeText(AuthorRules.GetDirectMatchIdentity(x.Name)),
                    norm,
                    StringComparison.OrdinalIgnoreCase);
            });
            if (existing != null)
            {
                existing.ConfirmedAlias = existing.ConfirmedAlias || alias;
                existing.FolderIdentity = existing.FolderIdentity || folder;
                return;
            }
            output.Add(new Identity
            {
                Name = name.Trim(),
                Normalized = norm,
                ConfirmedAlias = alias,
                FolderIdentity = folder
            });
        }

        private static int ScoreIdentity(
            PreparedFile file,
            Identity identity,
            out List<string> evidence)
        {
            evidence = new List<string>();
            string rawName = AuthorRules.GetDirectMatchIdentity(identity.Name).Trim();
            string name = identity.Normalized;
            if (file == null || file.Normalized.Length == 0 || name.Length == 0) return 0;

            int index = FindCompleteIdentity(file.Normalized, name);
            if (index >= 0)
            {
                bool rawExact = FindCompleteIdentity(file.Raw, rawName) >= 0;
                int score = identity.ConfirmedAlias || rawExact ? 100 : 95;
                evidence.Add(identity.ConfirmedAlias
                    ? "已确认别名完整命中 +100"
                    : rawExact
                        ? "本地作者原名完整命中 +100"
                        : "标准化后作者名完整命中 +95");

                if (identity.FolderIdentity)
                {
                    score += 20;
                    evidence.Add("与作者文件夹身份一致 +20");
                }
                if (file.Creators.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    score += 30;
                    evidence.Add("[社团 (作者)] 内部作者字段 +30");
                }
                if (IsAtFileFront(file.Normalized, index))
                {
                    score += 15;
                    evidence.Add("位于文件名前部 +15");
                }
                if (IsBracketed(file.Normalized, index, name.Length))
                {
                    score += 10;
                    evidence.Add("位于高概率括号区域 +10");
                }
                if (file.MetadataTags.Any(delegate(string tag) { return tag.Contains(name); }))
                {
                    score -= 100;
                    evidence.Add("命中清洗/排除标签 -100");
                }
                if (name.Length <= 2)
                {
                    score = Math.Min(score, 75);
                    evidence.Add("作者名过短，限制置信度");
                }
                return Math.Max(0, score);
            }

            int fuzzy = ScoreFuzzyRegions(file.Regions, name, out evidence);
            return name.Length <= 2 ? Math.Min(fuzzy, 40) : fuzzy;
        }

        private static int ScoreFuzzyRegions(List<string> regions, string identity, out List<string> evidence)
        {
            evidence = new List<string>();
            int best = 0;
            foreach (string region in regions ?? new List<string>())
            {
                if (region.Length == 0) continue;
                int score = 0;
                string reason = "";
                if (identity.Length >= 3 && (region.Contains(identity) || identity.Contains(region)))
                {
                    int shorter = Math.Min(region.Length, identity.Length);
                    int longer = Math.Max(region.Length, identity.Length);
                    if (shorter >= 3 && shorter * 100 / longer >= 60)
                    {
                        score = 40;
                        reason = "部分字符串命中 +40";
                    }
                    else if (shorter == 2 && identity.Length >= 3 &&
                             ContainsCjk(region) && shorter * 100 / longer >= 60)
                    {
                        score = 45;
                        reason = "短 CJK 名称部分命中 +45";
                    }
                }

                int distance = Levenshtein(region, identity, 2);
                if (distance == 1 && identity.Length >= 4 && score < 70)
                {
                    score = 70;
                    reason = "编辑距离为 1 +70";
                }
                else if (distance == 2 && identity.Length >= 6 && score < 55)
                {
                    score = 55;
                    reason = "编辑距离为 2 +55";
                }
                if (score > best)
                {
                    best = score;
                    evidence.Clear();
                    evidence.Add(reason);
                }
            }
            return best;
        }

        private static List<string> ExtractRegions(string fileName)
        {
            List<string> result = new List<string>();
            string value = (fileName ?? "").Normalize(NormalizationForm.FormKC);
            foreach (Match match in Regex.Matches(value, @"[\[\(【「『]\s*([^\]\)】」』]{1,80})\s*[\]\)】」』]"))
                result.Add(NormalizeForSearch(match.Groups[1].Value));
            string[] pieces = Regex.Split(value, @"[\s　_\-]+", RegexOptions.CultureInvariant);
            for (int i = 0; i < pieces.Length && i < 4; i++)
                if (!String.IsNullOrWhiteSpace(pieces[i])) result.Add(NormalizeForSearch(pieces[i]));
            return result;
        }

        private static PreparedFile PrepareFile(string fileName, TagCleaningRuleStore cleaningStore)
        {
            PreparedFile result = new PreparedFile();
            result.Raw = FileNameStructure.Parse(fileName).IdentityText;
            result.Normalized = NormalizeForSearch(result.Raw);
            result.Regions = ExtractRegions(result.Raw);
            foreach (string candidate in AuthorRules.GetAuthorCandidatesFromFileName(result.Raw, cleaningStore))
            {
                StructuredAuthorParts parts = AuthorRules.GetStructuredAuthorParts(candidate);
                if (parts != null) result.Creators.AddRange(parts.Creators.Select(AuthorRules.NormalizeText));
            }
            if (cleaningStore != null)
            {
                foreach (Match match in Regex.Matches(result.Raw, @"[\[［【]\s*([^\]］】]{1,100})\s*[\]］】]"))
                {
                    string tag = match.Groups[1].Value.Trim();
                    if (cleaningStore.IsMetadataTag(tag))
                        result.MetadataTags.Add(NormalizeForSearch(tag));
                }
            }
            return result;
        }

        private static string NormalizeForSearch(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            string n = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            n = n.Replace('（', '(').Replace('）', ')')
                 .Replace('［', '[').Replace('］', ']')
                 .Replace('【', '[').Replace('】', ']')
                 .Replace('「', '[').Replace('」', ']')
                 .Replace('『', '[').Replace('』', ']');
            n = Regex.Replace(n, @"[\s　]+", " ").Trim();
            return n;
        }

        private static int FindCompleteIdentity(string file, string name)
        {
            int start = 0;
            while (start <= file.Length - name.Length)
            {
                int index = file.IndexOf(name, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return -1;
                bool left = index == 0 || IsBoundary(file[index - 1]);
                int end = index + name.Length;
                bool right = end == file.Length || IsBoundary(file[end]);
                if (left && right) return index;
                start = index + 1;
            }
            return -1;
        }

        private static bool IsBoundary(char value)
        {
            return Char.IsWhiteSpace(value) || "[]［］【】()（）{}<>「」『』-_.,，、・·/\\+&!！?？:：;；".IndexOf(value) >= 0;
        }

        private static bool ContainsCjk(string value)
        {
            foreach (char c in value ?? "")
                if ((c >= '\u3040' && c <= '\u30ff') ||
                    (c >= '\u3400' && c <= '\u9fff') ||
                    (c >= '\uf900' && c <= '\ufaff')) return true;
            return false;
        }

        private static bool IsAtFileFront(string file, int index)
        {
            for (int i = 0; i < index; i++)
                if (!Char.IsWhiteSpace(file[i]) && "[({<".IndexOf(file[i]) < 0) return false;
            return true;
        }

        private static bool IsBracketed(string file, int index, int length)
        {
            if (index <= 0 || index + length >= file.Length) return false;
            char left = file[index - 1];
            char right = file[index + length];
            return (left == '[' && right == ']') || (left == '(' && right == ')');
        }

        private static int Levenshtein(string a, string b, int limit)
        {
            if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
            int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
            for (int i = 1; i <= a.Length; i++)
            {
                int[] current = new int[b.Length + 1];
                current[0] = i;
                int rowMin = current[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                    rowMin = Math.Min(rowMin, current[j]);
                }
                if (rowMin > limit) return limit + 1;
                previous = current;
            }
            return previous[b.Length];
        }

        private static AuthorMatchResult NewResult(AuthorMatchType type, AuthorFolder folder, string why)
        {
            AuthorMatchResult result = new AuthorMatchResult();
            result.Type = type;
            result.Folder = folder;
            result.Why = why;
            result.EvidenceKind = type == AuthorMatchType.Choice
                ? RecognitionEvidenceKind.Ambiguous
                : RecognitionEvidenceKind.Scoring;
            return result;
        }
    }
}
