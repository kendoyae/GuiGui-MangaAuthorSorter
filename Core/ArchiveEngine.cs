using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MangaAuthorSorter
{
    internal sealed class ArchiveEngine
    {
        private readonly AliasLibrary _aliasLibrary;
        private readonly AuthorEntityStore _entityStore;
        private readonly TagCleaningRuleStore _tagCleaningStore;
        private readonly object _targetCacheGate = new object();
        private string _targetCacheKey = "";
        private List<AuthorFolder> _cachedAuthorFolders;
        private SortedDictionary<int, string> _cachedAuthorGroups;

        public ArchiveEngine(AliasLibrary aliasLibrary)
            : this(aliasLibrary, null, null)
        {
        }

        public ArchiveEngine(AliasLibrary aliasLibrary, AuthorEntityStore entityStore)
            : this(aliasLibrary, entityStore, null)
        {
        }

        public ArchiveEngine(
            AliasLibrary aliasLibrary,
            AuthorEntityStore entityStore,
            TagCleaningRuleStore tagCleaningStore)
        {
            _aliasLibrary = aliasLibrary;
            _entityStore = entityStore;
            _tagCleaningStore = tagCleaningStore;
        }

        public List<PlanItem> BuildPlan(
            List<FileInfo> files,
            string authorRoot,
            int maxAuthors)
        {
            return BuildPlan(
                files,
                authorRoot,
                maxAuthors,
                GroupNaming.DefaultTemplate,
                new string[]
                {
                    GroupNaming.DefaultTemplate
                },
                AuthorFolderNaming.DefaultTemplate,
                new string[]
                {
                    AuthorFolderNaming.DefaultTemplate
                });
        }

        public List<PlanItem> BuildPlan(
            List<FileInfo> files,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates)
        {
            return BuildPlan(
                files,
                authorRoot,
                maxAuthors,
                currentGroupTemplate,
                recognizedGroupTemplates,
                currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates,
                null,
                null,
                ScanProgressStage.Planning);
        }

        public List<PlanItem> BuildPlan(
            List<FileInfo> files,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            ScanProgressStage stage)
        {
            List<PlanItem> initial =
                new List<PlanItem>();

            if (files != null)
            {
                foreach (FileInfo f in files)
                {
                    ThrowIfCanceled(cancelRequested);
                    initial.Add(
                        NewBlankItem(f));
                }
            }

            return ResolvePlan(
                initial,
                authorRoot,
                maxAuthors,
                currentGroupTemplate,
                recognizedGroupTemplates,
                currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates,
                progress,
                cancelRequested,
                stage);
        }

        public List<PlanItem> BuildPlanUntilMatches(
            List<FileInfo> files,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            ScanModeKind stopMode,
            int stopAfterMatches,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            ScanProgressStage stage)
        {
            // Keep filtered scans truly lazy. Older versions materialized every
            // candidate into a PlanItem here, which read LastWriteTime/Length
            // for the entire candidate set before Early Stop could take effect.
            IEnumerable<PlanItem> initial =
                EnumerateBlankItems(files, cancelRequested);

            return ResolvePlan(
                initial,
                authorRoot,
                maxAuthors,
                currentGroupTemplate,
                recognizedGroupTemplates,
                currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates,
                progress,
                cancelRequested,
                stage,
                stopMode,
                Math.Max(0, stopAfterMatches));
        }

        public List<PlanItem> ResolvePlan(
            IEnumerable<PlanItem> sourceItems,
            string authorRoot,
            int maxAuthors)
        {
            return ResolvePlan(
                sourceItems,
                authorRoot,
                maxAuthors,
                GroupNaming.DefaultTemplate,
                new string[]
                {
                    GroupNaming.DefaultTemplate
                },
                AuthorFolderNaming.DefaultTemplate,
                new string[]
                {
                    AuthorFolderNaming.DefaultTemplate
                });
        }

        public List<PlanItem> ResolvePlan(
            IEnumerable<PlanItem> sourceItems,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates)
        {
            return ResolvePlan(
                sourceItems,
                authorRoot,
                maxAuthors,
                currentGroupTemplate,
                recognizedGroupTemplates,
                currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates,
                null,
                null,
                ScanProgressStage.Planning);
        }

        public List<PlanItem> ResolvePlan(
            IEnumerable<PlanItem> sourceItems,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            ScanProgressStage stage,
            ScanModeKind? stopMode = null,
            int stopAfterMatches = 0)
        {
            ThrowIfCanceled(cancelRequested);

            if (!Directory.Exists(authorRoot))
            {
                throw new DirectoryNotFoundException(
                    "迁移位置不存在：" +
                    authorRoot);
            }

            string normalizedCurrent;
            string templateError;

            if (!GroupNaming.TryValidateTemplate(
                    currentGroupTemplate,
                    out normalizedCurrent,
                    out templateError))
            {
                normalizedCurrent =
                    GroupNaming.DefaultTemplate;
            }

            List<string> recognized =
                GroupNaming.NormalizeTemplates(
                    normalizedCurrent,
                    recognizedGroupTemplates);

            string normalizedAuthorFolderTemplate;
            string authorFolderTemplateError;

            if (!AuthorFolderNaming.TryValidateTemplate(
                    currentAuthorFolderTemplate,
                    out normalizedAuthorFolderTemplate,
                    out authorFolderTemplateError))
            {
                normalizedAuthorFolderTemplate =
                    AuthorFolderNaming.DefaultTemplate;
            }

            List<string> recognizedAuthorFolders =
                AuthorFolderNaming.NormalizeTemplates(
                    normalizedAuthorFolderTemplate,
                    recognizedAuthorFolderTemplates);

            List<AliasGroup> aliasGroups =
                _aliasLibrary.Load();

            AuthorEntityIndex entityIndex =
                _entityStore != null
                    ? _entityStore.LoadIndex()
                    : null;

            ThrowIfCanceled(cancelRequested);

            List<AuthorFolder> existing;
            SortedDictionary<int, string> groups;
            GetTargetDirectoryCache(
                authorRoot, normalizedCurrent, recognized, normalizedAuthorFolderTemplate,
                recognizedAuthorFolders, cancelRequested, out existing, out groups);

            ThrowIfCanceled(cancelRequested);

            // V1.9.6: Treat author folders planned earlier in the same scan as
            // searchable author identities too. This makes [creator] and
            // [circle (creator)] converge on the same planned folder even
            // before that folder physically exists on disk.
            List<AuthorFolder> searchableAuthors =
                new List<AuthorFolder>(existing);
            HashSet<string> roundTargetPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<int, int> groupCounts = new Dictionary<int, int>();

            foreach (KeyValuePair<int, string> pair in groups)
            {
                int count = 0;
                try { count = Directory.GetDirectories(pair.Value).Length; } catch { }
                groupCounts[pair.Key] = count;
            }

            int highest = groups.Count > 0 ? groups.Keys.Max() : 1;
            if (!groupCounts.ContainsKey(highest)) groupCounts[highest] = 0;

            Dictionary<string, string> newTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<PlanItem> resolved = new List<PlanItem>();
            int stopMatchCount = 0;

            bool useEarlyStop =
                stopMode.HasValue && stopAfterMatches > 0;

            // Full scans keep a materialized list so progress has a stable
            // denominator. Filtered Early Stop scans remain lazy so unused
            // candidates never incur file metadata reads.
            IEnumerable<PlanItem> sourceSequence;
            int planTotal = 0;
            if (useEarlyStop)
            {
                sourceSequence =
                    sourceItems ?? Enumerable.Empty<PlanItem>();
            }
            else
            {
                List<PlanItem> sourceList =
                    sourceItems as List<PlanItem> ??
                    (sourceItems ?? Enumerable.Empty<PlanItem>()).ToList();
                sourceSequence = sourceList;
                planTotal = sourceList.Count;
            }

            int planCurrent = 0;

            if (useEarlyStop)
            {
                ReportFilterProgress(
                    progress,
                    0,
                    stopAfterMatches,
                    0,
                    "");
            }
            else
            {
                ReportPlanProgress(
                    progress,
                    stage,
                    0,
                    planTotal,
                    "");
            }

            foreach (PlanItem original in sourceSequence)
            {
                ThrowIfCanceled(cancelRequested);
                planCurrent++;

                if ((!stopMode.HasValue || stopAfterMatches <= 0) &&
                    (planCurrent == 1 ||
                     (planCurrent % 32) == 0 ||
                     planCurrent == planTotal))
                {
                    ReportPlanProgress(
                        progress,
                        stage,
                        planCurrent,
                        planTotal,
                        original != null ? original.SourcePath : "");
                }

                if (original == null ||
                    String.IsNullOrWhiteSpace(original.SourcePath) ||
                    !File.Exists(original.SourcePath))
                {
                    continue;
                }

                // Reuse metadata already captured by BuildPlan/Everything.
                // V1.11.21 recreated FileInfo and reread LastWriteTime/Length
                // here for every item, doubling filesystem metadata work.
                PlanItem p = CloneBlankItem(original);
                p.ManualTargetDir = original.ManualTargetDir ?? "";
                p.ManualTargetName = original.ManualTargetName ?? "";
                p.ManualTargetAuthor = original.ManualTargetAuthor ?? "";

                List<string> authorCandidates =
                    AuthorRules.GetAuthorCandidatesFromFileName(
                        Path.GetFileNameWithoutExtension(p.FileName),
                        _tagCleaningStore);
                string author =
                    authorCandidates.Count > 0
                        ? authorCandidates[0]
                        : null;

                // A user may explicitly name an author from the context panel
                // even when the file name cannot be parsed. Manual identity is
                // authoritative for this item and lets the normal planner decide
                // whether it maps to an existing folder or a new author.
                if (String.IsNullOrWhiteSpace(author) &&
                    !String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    author = p.ManualTargetAuthor.Trim();
                    authorCandidates.Add(author);
                }

                p.Author = author ?? "";

                if (String.IsNullOrWhiteSpace(author))
                {
                    p.Status = "无法识别作者";
                    if (AddResolvedAndShouldStop(
                            resolved,
                            p,
                            stopMode,
                            stopAfterMatches,
                            ref stopMatchCount,
                            progress,
                            planCurrent))
                        break;
                    continue;
                }

                bool manualTargetAvailable =
                    !String.IsNullOrWhiteSpace(p.ManualTargetDir) &&
                    (Directory.Exists(p.ManualTargetDir) ||
                     roundTargetPaths.Contains(p.ManualTargetDir));

                if (manualTargetAvailable)
                {
                    p.MatchedAs = !String.IsNullOrWhiteSpace(p.ManualTargetName) ? p.ManualTargetName : new DirectoryInfo(p.ManualTargetDir).Name;
                    p.MatchWhy = "手动指定作者文件夹；已写入作者别名库";
                    p.TargetDir = p.ManualTargetDir;
                    p.TargetPath = Path.Combine(p.TargetDir, p.FileName);
                    FinalizeMoveState(p, "手动指定作者文件夹");
                    if (AddResolvedAndShouldStop(
                            resolved,
                            p,
                            stopMode,
                            stopAfterMatches,
                            ref stopMatchCount,
                            progress,
                            planCurrent))
                        break;
                    continue;
                }

                // A planned-new-author choice from an ambiguity dialog stores
                // only the chosen author identity, never the temporary group
                // number produced by the full classification pass. Resolve that
                // identity again against the current (actually executable) plan.
                string matchAuthor =
                    !String.IsNullOrWhiteSpace(p.ManualTargetAuthor)
                        ? p.ManualTargetAuthor
                        : author;

                AuthorMatchResult match;
                if (!String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    match =
                        FindExistingAuthor(
                            matchAuthor,
                            searchableAuthors,
                            aliasGroups,
                            entityIndex);
                }
                else
                {
                    match =
                        FindExistingAuthorCandidates(
                            authorCandidates,
                            searchableAuthors,
                            aliasGroups,
                            entityIndex);
                }

                if (match.Type == AuthorMatchType.Ambiguous)
                {
                    p.MatchWhy = match.Why;
                    p.Status = "同作者识别有歧义，跳过";
                    if (AddResolvedAndShouldStop(
                            resolved,
                            p,
                            stopMode,
                            stopAfterMatches,
                            ref stopMatchCount,
                            progress,
                            planCurrent))
                        break;
                    continue;
                }

                if (match.Type == AuthorMatchType.Choice)
                {
                    p.MatchWhy = match.Why;
                    p.Status = "作者候选需要确认";
                    p.CanMove = false;
                    bool hasPlannedCandidate = false;
                    foreach (AuthorFolder c in match.Candidates)
                    {
                        bool plannedCandidate =
                            roundTargetPaths.Contains(c.AuthorPath);
                        if (plannedCandidate)
                            hasPlannedCandidate = true;

                        // Do not leak a temporary full-scan destination such as
                        // "漫画作者31" into a filtered ambiguity result. A planned
                        // candidate keeps only its author identity and receives
                        // a real destination after the user confirms it.
                        p.CandidatePaths.Add(
                            plannedCandidate ? "" : c.AuthorPath);
                        p.CandidateNames.Add(c.AuthorName);
                        p.CandidateIsPlanned.Add(plannedCandidate);
                    }

                    if (hasPlannedCandidate &&
                        !String.IsNullOrWhiteSpace(match.Society) &&
                        !String.IsNullOrWhiteSpace(match.Creator))
                    {
                        p.MatchWhy =
                            "社团名「" + match.Society +
                            "」与作者名「" + match.Creator +
                            "」分别命中不同作者候选，其中包含本轮新作者；请使用“指定作者...”确认";
                    }

                    if (AddResolvedAndShouldStop(
                            resolved,
                            p,
                            stopMode,
                            stopAfterMatches,
                            ref stopMatchCount,
                            progress,
                            planCurrent))
                        break;
                    continue;
                }

                string targetDir, matchedAs, matchWhy, statusText;

                if (match.Type == AuthorMatchType.Matched)
                {
                    targetDir = match.Folder.AuthorPath;
                    matchedAs = match.Folder.AuthorName;

                    if (roundTargetPaths.Contains(targetDir))
                    {
                        // The match points to a folder planned earlier in this
                        // same scan, not to an on-disk existing author folder.
                        // Keep it classified as a new author while reusing the
                        // same destination.
                        matchWhy = "本轮扫描已识别为同一新作者";
                        statusText = "本轮新作者（复用目录）";
                    }
                    else
                    {
                        matchWhy = match.Why;
                        statusText = "匹配到已有作者";
                    }
                }
                else
                {
                    bool aliasAmbiguous;
                    AliasGroup aliasGroup = _aliasLibrary.GetGroupForName(matchAuthor, aliasGroups, out aliasAmbiguous);
                    string canonical, identity;

                    if (aliasGroup != null && !aliasAmbiguous)
                    {
                        canonical = aliasGroup.Canonical;
                        identity = "alias:" + AuthorRules.NormalizeText(canonical);
                    }
                    else
                    {
                        AuthorEntityMatch entityMatch =
                            entityIndex != null
                                ? entityIndex.Resolve(matchAuthor)
                                : new AuthorEntityMatch();

                        if (entityMatch.Found && entityMatch.Entity != null &&
                            !String.IsNullOrWhiteSpace(entityMatch.Entity.CanonicalName))
                        {
                            canonical = entityMatch.Entity.CanonicalName;
                            identity = "entity:" + entityMatch.Entity.Id.ToString();
                        }
                        else
                        {
                            canonical = matchAuthor;
                            identity = "normal:" + AuthorRules.NormalizeLoose(matchAuthor);
                        }
                    }

                    if (newTargets.ContainsKey(identity))
                    {
                        targetDir = newTargets[identity];
                        matchedAs = canonical;
                        matchWhy = "本轮扫描已识别为同一新作者";
                        statusText = "本轮新作者（复用目录）";
                    }
                    else
                    {
                        int groupNumber = highest;
                        while (GetCount(groupCounts, groupNumber) >= maxAuthors)
                        {
                            groupNumber++;
                            if (!groupCounts.ContainsKey(groupNumber)) groupCounts[groupNumber] = 0;
                        }
                        if (groupNumber > highest) highest = groupNumber;

                        string groupName =
                            GroupNaming.Render(
                                normalizedCurrent,
                                groupNumber);
                        string groupPath =
                            Path.Combine(
                                authorRoot,
                                groupName);

                        string authorFolderName =
                            AuthorFolderNaming.Render(
                                normalizedAuthorFolderTemplate,
                                canonical);

                        targetDir =
                            Path.Combine(
                                groupPath,
                                authorFolderName);
                        newTargets[identity] = targetDir;
                        groupCounts[groupNumber] = GetCount(groupCounts, groupNumber) + 1;
                        matchedAs = canonical;
                        matchWhy = !String.Equals(canonical, matchAuthor, StringComparison.Ordinal)
                            ? "别名库统一为：" + canonical
                            : "未找到已有作者，按新作者处理";
                        statusText =
                            "新作者，计划放入 " +
                            groupName;

                        AddRoundAuthorTarget(
                            searchableAuthors,
                            roundTargetPaths,
                            groupNumber,
                            canonical,
                            matchAuthor,
                            targetDir);
                    }
                }

                if (!String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    matchWhy = "手动指定作者；已写入作者别名库；目标目录按当前列表重新规划";
                    statusText = "手动指定作者";
                }

                p.MatchedAs = matchedAs;
                p.MatchWhy = matchWhy;
                p.TargetDir = targetDir;
                p.TargetPath = Path.Combine(targetDir, p.FileName);
                FinalizeMoveState(p, statusText);
                if (AddResolvedAndShouldStop(
                            resolved,
                            p,
                            stopMode,
                            stopAfterMatches,
                            ref stopMatchCount,
                            progress,
                            planCurrent))
                    break;
            }

            ThrowIfCanceled(cancelRequested);
            if (stopMode.HasValue && stopAfterMatches > 0)
            {
                ReportFilterProgress(
                    progress,
                    Math.Min(stopMatchCount, stopAfterMatches),
                    stopAfterMatches,
                    planCurrent,
                    "");
            }
            else
            {
                ReportPlanProgress(
                    progress,
                    stage,
                    planCurrent,
                    planTotal,
                    "");
            }

            return resolved;
        }

        private static bool AddResolvedAndShouldStop(
            List<PlanItem> resolved,
            PlanItem item,
            ScanModeKind? stopMode,
            int stopAfterMatches,
            ref int stopMatchCount,
            Action<ScanProgressInfo> progress,
            int checkedCount)
        {
            resolved.Add(item);

            if (!stopMode.HasValue || stopAfterMatches <= 0)
                return false;

            if (ScanModeRules.Matches(item, stopMode.Value))
                stopMatchCount++;

            bool reachedTarget =
                stopMatchCount >= stopAfterMatches;

            if (checkedCount == 1 ||
                (checkedCount % 16) == 0 ||
                reachedTarget)
            {
                ReportFilterProgress(
                    progress,
                    Math.Min(stopMatchCount, stopAfterMatches),
                    stopAfterMatches,
                    checkedCount,
                    item != null ? item.SourcePath : "");
            }

            return reachedTarget;
        }

        private static void ReportFilterProgress(
            Action<ScanProgressInfo> progress,
            int matched,
            int target,
            int checkedCount,
            string path)
        {
            if (progress == null)
                return;

            ScanProgressInfo info = new ScanProgressInfo();
            info.Stage = ScanProgressStage.Filtering;
            info.Current = Math.Max(0, matched);
            info.Total = Math.Max(0, target);
            info.Checked = Math.Max(0, checkedCount);
            info.CurrentPath = path ?? "";
            info.Indeterminate = target <= 0;
            progress(info);
        }

        private static void ThrowIfCanceled(
            Func<bool> cancelRequested)
        {
            if (cancelRequested != null &&
                cancelRequested())
            {
                throw new OperationCanceledException();
            }
        }

        private static void ReportPlanProgress(
            Action<ScanProgressInfo> progress,
            ScanProgressStage stage,
            int current,
            int total,
            string path)
        {
            if (progress == null)
                return;

            ScanProgressInfo info =
                new ScanProgressInfo();
            info.Stage = stage;
            info.Current = current;
            info.Total = total;
            info.CurrentPath = path ?? "";
            info.Indeterminate = total <= 0;
            progress(info);
        }

        private static void AddRoundAuthorTarget(
            List<AuthorFolder> searchableAuthors,
            HashSet<string> roundTargetPaths,
            int groupNumber,
            string canonical,
            string originalAuthor,
            string targetDir)
        {
            if (String.IsNullOrWhiteSpace(targetDir))
                return;

            if (!roundTargetPaths.Add(targetDir))
                return;

            AuthorFolder f = new AuthorFolder();
            f.GroupNumber = groupNumber;
            f.AuthorName = canonical ?? "";
            f.AuthorPath = targetDir;
            f.PreferredIdentity =
                AuthorRules.GetPreferredAuthorIdentity(
                    f.AuthorName);

            IndexAuthorFolderName(f, f.AuthorName);

            // Also remember the source identity that created this planned
            // folder. This is useful when the canonical folder name came from
            // an alias but a later composite candidate uses the original name.
            IndexAuthorFolderName(f, originalAuthor);

            searchableAuthors.Add(f);
        }

        private static int GetCount(Dictionary<int, int> d, int key)
        {
            int v;
            return d.TryGetValue(key, out v) ? v : 0;
        }

        public void PrepareTargetDirectoryCache(
            string root,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            Func<bool> cancelRequested)
        {
            string normalizedGroup;
            string error;
            if (!GroupNaming.TryValidateTemplate(currentGroupTemplate, out normalizedGroup, out error))
                normalizedGroup = GroupNaming.DefaultTemplate;
            string normalizedAuthor;
            if (!AuthorFolderNaming.TryValidateTemplate(currentAuthorFolderTemplate, out normalizedAuthor, out error))
                normalizedAuthor = AuthorFolderNaming.DefaultTemplate;
            List<string> groups = GroupNaming.NormalizeTemplates(normalizedGroup, recognizedGroupTemplates);
            List<string> authors = AuthorFolderNaming.NormalizeTemplates(normalizedAuthor, recognizedAuthorFolderTemplates);
            List<AuthorFolder> ignoredAuthors;
            SortedDictionary<int, string> ignoredGroups;
            GetTargetDirectoryCache(root, normalizedGroup, groups, normalizedAuthor, authors,
                cancelRequested, out ignoredAuthors, out ignoredGroups);
        }

        private void GetTargetDirectoryCache(
            string root,
            string groupTemplate,
            IEnumerable<string> recognizedGroups,
            string authorTemplate,
            IEnumerable<string> recognizedAuthors,
            Func<bool> cancelRequested,
            out List<AuthorFolder> authors,
            out SortedDictionary<int, string> groups)
        {
            string key = Path.GetFullPath(root).TrimEnd('\\', '/').ToUpperInvariant() + "|" +
                groupTemplate + "|" + String.Join(";", recognizedGroups ?? new string[0]) + "|" +
                authorTemplate + "|" + String.Join(";", recognizedAuthors ?? new string[0]);
            lock (_targetCacheGate)
            {
                if (String.Equals(key, _targetCacheKey, StringComparison.Ordinal) &&
                    _cachedAuthorFolders != null && _cachedAuthorGroups != null)
                {
                    authors = new List<AuthorFolder>(_cachedAuthorFolders);
                    groups = new SortedDictionary<int, string>(_cachedAuthorGroups);
                    return;
                }
            }
            ThrowIfCanceled(cancelRequested);
            List<AuthorFolder> builtAuthors = GetExistingAuthorFolders(root, recognizedGroups, recognizedAuthors);
            ThrowIfCanceled(cancelRequested);
            SortedDictionary<int, string> builtGroups = GetAuthorGroups(root, groupTemplate);
            ThrowIfCanceled(cancelRequested);
            lock (_targetCacheGate)
            {
                _targetCacheKey = key;
                _cachedAuthorFolders = builtAuthors;
                _cachedAuthorGroups = builtGroups;
            }
            authors = new List<AuthorFolder>(builtAuthors);
            groups = new SortedDictionary<int, string>(builtGroups);
        }

        public void InvalidateTargetDirectoryCache()
        {
            lock (_targetCacheGate)
            {
                _targetCacheKey = "";
                _cachedAuthorFolders = null;
                _cachedAuthorGroups = null;
            }
        }

        private static void FinalizeMoveState(PlanItem p, string normalStatus)
        {
            if (String.Equals(p.SourcePath, p.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                p.Status = "文件已在目标作者文件夹";
                p.CanMove = false;
            }
            else if (File.Exists(p.TargetPath))
            {
                p.Status = "目标已有同名文件，跳过";
                p.CanMove = false;
            }
            else
            {
                p.Status = normalStatus;
                p.CanMove = true;
            }
        }

        private static IEnumerable<PlanItem> EnumerateBlankItems(
            IEnumerable<FileInfo> files,
            Func<bool> cancelRequested)
        {
            if (files == null)
                yield break;

            foreach (FileInfo file in files)
            {
                ThrowIfCanceled(cancelRequested);
                if (file == null)
                    continue;
                yield return NewBlankItem(file);
            }
        }

        private static PlanItem CloneBlankItem(PlanItem source)
        {
            PlanItem p = new PlanItem();
            if (source == null)
                return p;

            p.FileName = source.FileName ?? "";
            p.SourcePath = source.SourcePath ?? "";
            p.LastWriteTime = source.LastWriteTime;
            p.FileSize = source.FileSize;
            return p;
        }

        private static PlanItem NewBlankItem(FileInfo file)
        {
            PlanItem p = new PlanItem();
            p.FileName = file.Name;
            p.SourcePath = file.FullName;
            p.LastWriteTime = file.LastWriteTime;
            try { p.FileSize = file.Length; } catch { p.FileSize = -1; }
            return p;
        }

        private static SortedDictionary<int, string>
            GetAuthorGroups(
                string root,
                string template)
        {
            SortedDictionary<int, string> result =
                new SortedDictionary<int, string>();

            if (!Directory.Exists(root))
                return result;

            foreach (string dir in
                     Directory.GetDirectories(root))
            {
                string name =
                    new DirectoryInfo(dir).Name;

                int number;

                if (!GroupNaming.TryParseGroupNumber(
                        template,
                        name,
                        out number))
                {
                    continue;
                }

                result[number] =
                    dir;
            }

            return result;
        }

        private static List<AuthorFolder>
            GetExistingAuthorFolders(
                string root,
                IEnumerable<string> templates,
                IEnumerable<string> authorFolderTemplates)
        {
            List<AuthorFolder> items =
                new List<AuthorFolder>();

            if (!Directory.Exists(root))
                return items;

            List<string> recognized =
                GroupNaming.NormalizeTemplates(
                    GroupNaming.DefaultTemplate,
                    templates);

            HashSet<string> seenGroupPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string groupPath in
                     Directory.GetDirectories(root))
            {
                DirectoryInfo groupInfo =
                    new DirectoryInfo(
                        groupPath);

                int groupNumber = 0;
                bool matched = false;

                foreach (string template
                         in recognized)
                {
                    if (GroupNaming.TryParseGroupNumber(
                            template,
                            groupInfo.Name,
                            out groupNumber))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                    continue;

                if (!seenGroupPaths.Add(
                        groupInfo.FullName))
                {
                    continue;
                }

                string[] dirs;

                try
                {
                    dirs =
                        Directory.GetDirectories(
                            groupInfo.FullName);
                }
                catch
                {
                    continue;
                }

                foreach (string dir in dirs)
                {
                    DirectoryInfo info =
                        new DirectoryInfo(dir);

                    AuthorFolder f =
                        new AuthorFolder();

                    f.GroupNumber =
                        groupNumber;

                    string logicalAuthor;
                    if (!AuthorFolderNaming.TryExtractAuthor(
                            info.Name,
                            authorFolderTemplates,
                            out logicalAuthor))
                    {
                        logicalAuthor =
                            info.Name;
                    }

                    f.AuthorName =
                        logicalAuthor;
                    f.AuthorPath =
                        info.FullName;
                    f.PreferredIdentity =
                        AuthorRules.GetPreferredAuthorIdentity(
                            logicalAuthor);

                    IndexAuthorFolderName(f, logicalAuthor);

                    // Keep the physical folder name searchable as a fallback,
                    // so older folders still match even if their naming style
                    // predates the current settings.
                    if (!String.Equals(
                            logicalAuthor,
                            info.Name,
                            StringComparison.Ordinal))
                    {
                        IndexAuthorFolderName(f, info.Name);
                    }

                    items.Add(f);
                }
            }

            return items;
        }

        private AuthorMatchResult FindExistingAuthorCandidates(
            List<string> authors,
            List<AuthorFolder> existing,
            List<AliasGroup> aliasGroups,
            AuthorEntityIndex entityIndex)
        {
            if (authors == null || authors.Count == 0)
                return Result(AuthorMatchType.NotFound, null, "未找到同作者目录");

            List<AuthorMatchResult> matched = new List<AuthorMatchResult>();
            List<AuthorMatchResult> hardChoices = new List<AuthorMatchResult>();
            List<AuthorMatchResult> cautiousChoices = new List<AuthorMatchResult>();

            foreach (string candidate in authors)
            {
                if (String.IsNullOrWhiteSpace(candidate)) continue;
                AuthorMatchResult r =
                    FindExistingAuthor(candidate, existing, aliasGroups, entityIndex);

                if (r.Type == AuthorMatchType.Ambiguous)
                {
                    // A genuinely ambiguous identity must never be silently
                    // overridden by another tag from the same filename.
                    return r;
                }

                if (r.Type == AuthorMatchType.Matched)
                {
                    matched.Add(r);
                    continue;
                }

                if (r.Type == AuthorMatchType.Choice)
                {
                    if ((r.Why ?? "").StartsWith(
                            "发现疑似命名差异的作者目录",
                            StringComparison.Ordinal))
                    {
                        cautiousChoices.Add(r);
                    }
                    else
                    {
                        hardChoices.Add(r);
                    }
                }
            }

            List<AuthorFolder> strongFolders = new List<AuthorFolder>();
            foreach (AuthorMatchResult r in matched)
                if (r.Folder != null) strongFolders.Add(r.Folder);
            foreach (AuthorMatchResult r in hardChoices)
                strongFolders.AddRange(r.Candidates);
            strongFolders = UniqueFolders(strongFolders);

            if (strongFolders.Count == 1 && matched.Count > 0)
            {
                // If all strong evidence converges on one folder, use the first
                // concrete match. This covers multiple leading tags that are
                // aliases/roles of the same existing author folder.
                foreach (AuthorMatchResult r in matched)
                {
                    if (r.Folder != null &&
                        String.Equals(
                            r.Folder.AuthorPath,
                            strongFolders[0].AuthorPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return r;
                    }
                }
            }

            if (strongFolders.Count > 1)
            {
                AuthorMatchResult conflict = Result(
                    AuthorMatchType.Choice,
                    null,
                    "多个前置身份标签或社团/作者候选分别匹配到不同作者文件夹；请双击“识别依据”选择");
                conflict.Candidates = strongFolders;
                return conflict;
            }

            if (matched.Count > 0)
                return matched[0];

            if (hardChoices.Count > 0)
            {
                List<AuthorFolder> folders = new List<AuthorFolder>();
                foreach (AuthorMatchResult r in hardChoices)
                    folders.AddRange(r.Candidates);
                folders = UniqueFolders(folders);

                AuthorMatchResult choice = Result(
                    AuthorMatchType.Choice,
                    null,
                    hardChoices.Count == 1
                        ? hardChoices[0].Why
                        : "多个前置身份标签或社团/作者候选需要确认；请双击“识别依据”选择");
                choice.Candidates = folders;
                if (hardChoices.Count == 1)
                {
                    choice.Society = hardChoices[0].Society;
                    choice.Creator = hardChoices[0].Creator;
                }
                return choice;
            }

            if (cautiousChoices.Count > 0)
            {
                List<AuthorFolder> folders = new List<AuthorFolder>();
                foreach (AuthorMatchResult r in cautiousChoices)
                    folders.AddRange(r.Candidates);
                folders = UniqueFolders(folders);

                AuthorMatchResult cautious = Result(
                    AuthorMatchType.Choice,
                    null,
                    "发现疑似命名差异的作者目录（中点 / 横线 / 波浪线 / 外层引号 / 多余右括号等）；请使用“指定作者...”确认");
                cautious.Candidates = folders;
                return cautious;
            }

            return Result(AuthorMatchType.NotFound, null, "未找到同作者目录");
        }

        private AuthorMatchResult FindExistingAuthor(
            string author,
            List<AuthorFolder> existing,
            List<AliasGroup> aliasGroups,
            AuthorEntityIndex entityIndex)
        {
            bool aliasAmbiguous;
            AliasGroup candidateGroup = _aliasLibrary.GetGroupForName(author, aliasGroups, out aliasAmbiguous);
            if (aliasAmbiguous) return Result(AuthorMatchType.Ambiguous, null, "作者候选同时命中多个自定义别名组");

            if (candidateGroup != null)
            {
                List<AuthorFolder> hits = new List<AuthorFolder>();
                foreach (AuthorFolder folder in existing)
                {
                    bool fa;
                    AliasGroup fg = _aliasLibrary.GetGroupForName(folder.AuthorName, aliasGroups, out fa);
                    if (fg != null && !fa && String.Equals(fg.Canonical, candidateGroup.Canonical, StringComparison.Ordinal)) hits.Add(folder);
                }
                hits = UniqueFolders(hits);
                if (hits.Count == 1) return Result(AuthorMatchType.Matched, hits[0], "作者别名库：" + candidateGroup.Canonical);
                if (hits.Count > 1) return Result(AuthorMatchType.Ambiguous, null, "自定义别名组对应多个现有作者目录");
            }

            StructuredAuthorParts structured = AuthorRules.GetStructuredAuthorParts(author);
            if (structured != null)
            {
                string creatorLabel = JoinNames(structured.Creators);

                // Explicit "circle (author)" may correspond to an older folder
                // written as "circle author". Only accept this automatically
                // when the entire flat folder identity is exactly the two
                // explicit components; this is formatting normalization, not a
                // generic whitespace deletion rule.
                if (structured.Creators.Count == 1)
                {
                    string flat = AuthorRules.NormalizeText(
                        structured.Society + " " + structured.Creators[0]);
                    List<AuthorFolder> flatHits =
                        FindFoldersByExactDirectNorm(flat, existing);
                    if (flatHits.Count == 1)
                    {
                        return Result(
                            AuthorMatchType.Matched,
                            flatHits[0],
                            "社团 / 作者结构格式标准化匹配：「" +
                            structured.Society + " (" + structured.Creators[0] +
                            ")」↔「" + flatHits[0].AuthorName + "」");
                    }
                    if (flatHits.Count > 1)
                        return Result(AuthorMatchType.Ambiguous, null, "同一个结构化作者候选可匹配多个已有目录");
                }

                List<AuthorFolder> societyHits =
                    FindSocietyFoldersForCandidate(
                        structured.Society,
                        existing);

                List<AuthorFolder> creatorHits = new List<AuthorFolder>();
                List<string> matchedCreators = new List<string>();
                foreach (string creator in structured.Creators)
                {
                    List<AuthorFolder> one =
                        FindCreatorFoldersForCandidate(
                            creator,
                            existing);
                    if (one.Count > 0)
                    {
                        matchedCreators.Add(creator);
                        creatorHits.AddRange(one);
                    }
                }
                creatorHits = UniqueFolders(creatorHits);

                List<AuthorFolder> union = UniqueFolders(societyHits, creatorHits);
                string matchedCreatorLabel =
                    matchedCreators.Count > 0
                        ? JoinNames(matchedCreators)
                        : creatorLabel;

                if (societyHits.Count > 0 && creatorHits.Count > 0)
                {
                    if (union.Count == 1)
                    {
                        string sourceCore =
                            AuthorRules.GetDirectMatchIdentity(author);
                        string folderCore =
                            AuthorRules.GetDirectMatchIdentity(union[0].AuthorName);
                        string sourceNorm =
                            AuthorRules.NormalizeText(sourceCore);
                        string folderNorm =
                            AuthorRules.NormalizeText(folderCore);

                        if (!String.Equals(
                                sourceCore,
                                folderCore,
                                StringComparison.OrdinalIgnoreCase) &&
                            String.Equals(
                                sourceNorm,
                                folderNorm,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return Result(
                                AuthorMatchType.Matched,
                                union[0],
                                "作者目录名称标准化匹配（空白 / 全半角 / 括号容错）");
                        }

                        return Result(
                            AuthorMatchType.Matched,
                            union[0],
                            "社团名「" + structured.Society +
                            "」和作者名「" + matchedCreatorLabel +
                            "」均指向同一作者目录");
                    }
                    if (union.Count > 1)
                    {
                        AuthorMatchResult r = Result(
                            AuthorMatchType.Choice,
                            null,
                            "社团名「" + structured.Society +
                            "」与作者名「" + matchedCreatorLabel +
                            "」分别匹配到不同作者文件夹；请双击“识别依据”选择");
                        r.Candidates = union;
                        r.Society = structured.Society;
                        r.Creator = matchedCreatorLabel;
                        return r;
                    }
                }

                if (societyHits.Count == 1 && creatorHits.Count == 0)
                    return Result(AuthorMatchType.Matched, societyHits[0], "仅社团名「" + structured.Society + "」匹配到已有作者目录");

                if (creatorHits.Count == 1 && societyHits.Count == 0)
                    return Result(AuthorMatchType.Matched, creatorHits[0], "仅作者名「" + matchedCreatorLabel + "」匹配到已有作者目录");

                if (societyHits.Count > 1 || creatorHits.Count > 1)
                {
                    AuthorMatchResult r = Result(
                        AuthorMatchType.Choice,
                        null,
                        "「" + structured.Society + " (" + creatorLabel + ")」匹配到多个候选作者文件夹；请双击“识别依据”选择");
                    r.Candidates = union;
                    r.Society = structured.Society;
                    r.Creator = creatorLabel;
                    return r;
                }
            }

            // Plain identity tags are matched role-aware before the older union
            // aliases. This lets [あめのまち] match the society part of
            // "あめのまち (すわっぷきのこ)" and keeps the UI reason accurate.
            List<AuthorFolder> directHits = FindDirectFoldersForCandidate(author, existing);
            if (directHits.Count == 1)
            {
                string sourceCore = AuthorRules.GetDirectMatchIdentity(author);
                string folderCore = AuthorRules.GetDirectMatchIdentity(directHits[0].AuthorName);
                string why = String.Equals(sourceCore, folderCore, StringComparison.OrdinalIgnoreCase)
                    ? "作者核心名称完全一致"
                    : "作者目录名称标准化匹配（空白 / 全半角 / 括号容错）";
                return Result(AuthorMatchType.Matched, directHits[0], why);
            }
            if (directHits.Count > 1)
                return Result(AuthorMatchType.Ambiguous, null, "同一个作者候选可匹配多个已有目录");

            List<AuthorFolder> societyRoleHits = FindFoldersByRoleNorm(author, existing, 1);
            if (societyRoleHits.Count == 1)
                return Result(AuthorMatchType.Matched, societyRoleHits[0], "仅社团名「" + author + "」匹配到已有作者目录");
            if (societyRoleHits.Count > 1)
                return ChoiceForRole(author, societyRoleHits, true);

            List<AuthorFolder> creatorRoleHits = FindFoldersByRoleNorm(author, existing, 2);
            if (creatorRoleHits.Count == 1)
                return Result(AuthorMatchType.Matched, creatorRoleHits[0], "仅作者名「" + author + "」匹配到已有作者目录");
            if (creatorRoleHits.Count > 1)
                return ChoiceForRole(author, creatorRoleHits, false);

            List<AuthorFolder> segmentHits = FindFoldersByRoleNorm(author, existing, 3);
            if (segmentHits.Count == 1)
                return Result(AuthorMatchType.Matched, segmentHits[0], "社团名称段「" + author + "」匹配到已有作者目录");
            if (segmentHits.Count > 1)
            {
                AuthorMatchResult r = Result(
                    AuthorMatchType.Choice,
                    null,
                    "社团名称段「" + author + "」匹配到多个作者文件夹；请双击“识别依据”选择");
                r.Candidates = segmentHits;
                r.Society = author;
                return r;
            }

            HashSet<string> authorNorms = new HashSet<string>(AuthorRules.GetNorms(author), StringComparer.OrdinalIgnoreCase);
            List<AuthorFolder> genericHits = new List<AuthorFolder>();
            foreach (AuthorFolder folder in existing)
            {
                bool matchedNorm = false;
                foreach (string n in folder.Norms)
                {
                    if (authorNorms.Contains(n))
                    {
                        matchedNorm = true;
                        break;
                    }
                }
                if (matchedNorm) genericHits.Add(folder);
            }
            genericHits = UniqueFolders(genericHits);

            if (genericHits.Count == 1)
                return Result(AuthorMatchType.Matched, genericHits[0], "作者目录名称标准化匹配（空白 / 全半角 / 括号容错）");

            if (genericHits.Count > 1) return Result(AuthorMatchType.Ambiguous, null, "同一个作者候选可匹配多个已有目录");

            if (entityIndex != null)
            {
                AuthorEntityMatch entityMatch = entityIndex.Resolve(author);
                if (entityMatch.Ambiguous)
                {
                    return Result(
                        AuthorMatchType.Ambiguous,
                        null,
                        "本地作者实体库中同一名称对应多个作者实体");
                }

                if (entityMatch.Found && entityMatch.Entity != null)
                {
                    List<AuthorFolder> entityHits =
                        FindFoldersForEntityMatch(entityMatch, existing);

                    if (entityHits.Count == 1)
                    {
                        return Result(
                            AuthorMatchType.Matched,
                            entityHits[0],
                            "作者实体库：" + entityMatch.Entity.CanonicalName +
                            "（" + (entityMatch.Entity.Source ?? "") + "）");
                    }

                    if (entityHits.Count > 1)
                    {
                        AuthorMatchResult entityChoice = Result(
                            AuthorMatchType.Choice,
                            null,
                            "作者实体「" + entityMatch.Entity.CanonicalName +
                            "」可对应多个已有作者文件夹；请双击“识别依据”选择");
                        entityChoice.Candidates = entityHits;
                        return entityChoice;
                    }
                }
            }

            List<AuthorFolder> cautiousHits = FindCautiousFoldersForCandidate(author, existing);
            if (cautiousHits.Count > 0)
            {
                AuthorMatchResult cautious = Result(
                    AuthorMatchType.Choice,
                    null,
                    "发现疑似命名差异的作者目录（中点 / 横线 / 波浪线 / 外层引号 / 多余右括号等）；请使用“指定作者...”确认");
                cautious.Candidates = cautiousHits;
                return cautious;
            }

            return Result(AuthorMatchType.NotFound, null, "未找到同作者目录");
        }

        private static AuthorMatchResult ChoiceForRole(
            string candidate,
            List<AuthorFolder> folders,
            bool society)
        {
            AuthorMatchResult r = Result(
                AuthorMatchType.Choice,
                null,
                (society ? "社团名「" : "作者名「") + candidate +
                "」匹配到多个作者文件夹；请双击“识别依据”选择");
            r.Candidates = UniqueFolders(folders);
            if (society) r.Society = candidate;
            else r.Creator = candidate;
            return r;
        }

        private static string JoinNames(List<string> names)
        {
            if (names == null || names.Count == 0) return "";
            return String.Join(" / ", names.ToArray());
        }

        private static void IndexAuthorFolderName(AuthorFolder folder, string name)
        {
            if (folder == null || String.IsNullOrWhiteSpace(name)) return;

            foreach (string n in AuthorRules.GetNorms(name))
                folder.Norms.Add(n);
            foreach (string n in AuthorRules.GetCautiousNorms(name))
                folder.CautiousNorms.Add(n);

            string direct = AuthorRules.NormalizeText(name);
            if (direct.Length > 0) folder.DirectNorms.Add(direct);

            string core = AuthorRules.NormalizeText(
                AuthorRules.GetDirectMatchIdentity(name));
            if (core.Length > 0) folder.DirectNorms.Add(core);

            string terminalNormalized =
                AuthorRules.NormalizeTerminalPunctuation(
                    AuthorRules.GetDirectMatchIdentity(name));
            if (terminalNormalized.Length > 0)
                folder.DirectNorms.Add(terminalNormalized);

            string cjkWhitespaceNormalized =
                AuthorRules.NormalizeCjkIdentityWhitespace(
                    AuthorRules.GetDirectMatchIdentity(name));
            if (cjkWhitespaceNormalized.Length > 0)
                folder.DirectNorms.Add(cjkWhitespaceNormalized);

            // Index structured identities found either in the whole folder name
            // or inside a bracketed segment such as
            // "Meme50 [circle (creator)]".
            foreach (string alias in AuthorRules.GetNameAliases(name))
            {
                StructuredAuthorParts parts =
                    AuthorRules.GetStructuredAuthorParts(alias);
                if (parts != null)
                {
                    string society = AuthorRules.NormalizeText(parts.Society);
                    if (society.Length > 0) folder.SocietyNorms.Add(society);
                    foreach (string creator in parts.Creators)
                    {
                        string n = AuthorRules.NormalizeText(creator);
                        if (n.Length > 0) folder.CreatorNorms.Add(n);
                    }
                }
            }

            foreach (string segment in AuthorRules.GetSafeNameSegments(name))
            {
                string n = AuthorRules.NormalizeText(segment);
                if (n.Length > 0) folder.SegmentNorms.Add(n);
            }
        }

        private static List<AuthorFolder> FindFoldersByExactDirectNorm(
            string norm,
            List<AuthorFolder> existing)
        {
            List<AuthorFolder> hits = new List<AuthorFolder>();
            if (String.IsNullOrWhiteSpace(norm)) return hits;
            foreach (AuthorFolder folder in existing)
                if (folder.DirectNorms.Contains(norm)) hits.Add(folder);
            return UniqueFolders(hits);
        }

        private static List<AuthorFolder> FindDirectFoldersForCandidate(
            string candidate,
            List<AuthorFolder> existing)
        {
            HashSet<string> norms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string full = AuthorRules.NormalizeText(candidate);
            string core = AuthorRules.NormalizeText(
                AuthorRules.GetDirectMatchIdentity(candidate));
            string terminalNormalized =
                AuthorRules.NormalizeTerminalPunctuation(
                    AuthorRules.GetDirectMatchIdentity(candidate));
            string cjkWhitespaceNormalized =
                AuthorRules.NormalizeCjkIdentityWhitespace(
                    AuthorRules.GetDirectMatchIdentity(candidate));
            if (full.Length > 0) norms.Add(full);
            if (core.Length > 0) norms.Add(core);
            if (terminalNormalized.Length > 0) norms.Add(terminalNormalized);
            if (cjkWhitespaceNormalized.Length > 0) norms.Add(cjkWhitespaceNormalized);

            List<AuthorFolder> hits = new List<AuthorFolder>();
            foreach (AuthorFolder folder in existing)
            {
                foreach (string n in norms)
                {
                    if (folder.DirectNorms.Contains(n))
                    {
                        hits.Add(folder);
                        break;
                    }
                }
            }
            return UniqueFolders(hits);
        }

        // role: 1 = society, 2 = creator, 3 = conservative name segment.
        private static List<AuthorFolder> FindFoldersByRoleNorm(
            string candidate,
            List<AuthorFolder> existing,
            int role)
        {
            HashSet<string> norms = new HashSet<string>(
                AuthorRules.GetNorms(candidate),
                StringComparer.OrdinalIgnoreCase);
            List<AuthorFolder> hits = new List<AuthorFolder>();

            foreach (AuthorFolder folder in existing)
            {
                HashSet<string> target =
                    role == 1 ? folder.SocietyNorms :
                    role == 2 ? folder.CreatorNorms :
                    folder.SegmentNorms;

                bool found = false;
                foreach (string n in norms)
                {
                    if (target.Contains(n))
                    {
                        found = true;
                        break;
                    }
                }
                if (found) hits.Add(folder);
            }
            return UniqueFolders(hits);
        }

        private static List<AuthorFolder> FindSocietyFoldersForCandidate(
            string candidate,
            List<AuthorFolder> existing)
        {
            return UniqueFolders(
                FindDirectFoldersForCandidate(candidate, existing),
                FindFoldersByRoleNorm(candidate, existing, 1),
                FindFoldersByRoleNorm(candidate, existing, 3));
        }

        private static List<AuthorFolder> FindCreatorFoldersForCandidate(
            string candidate,
            List<AuthorFolder> existing)
        {
            return UniqueFolders(
                FindDirectFoldersForCandidate(candidate, existing),
                FindFoldersByRoleNorm(candidate, existing, 2));
        }

        private static AuthorMatchResult Result(AuthorMatchType type, AuthorFolder folder, string why)
        {
            AuthorMatchResult r = new AuthorMatchResult(); r.Type = type; r.Folder = folder; r.Why = why; return r;
        }

        private static List<AuthorFolder> FindFoldersForCandidate(string candidate, List<AuthorFolder> existing)
        {
            HashSet<string> norms = new HashSet<string>(AuthorRules.GetNorms(candidate), StringComparer.OrdinalIgnoreCase);
            List<AuthorFolder> hits = new List<AuthorFolder>();
            foreach (AuthorFolder folder in existing)
            {
                bool matched = false;
                foreach (string n in folder.Norms) if (norms.Contains(n)) { matched = true; break; }
                if (matched) hits.Add(folder);
            }
            return UniqueFolders(hits);
        }

        private static List<AuthorFolder> FindFoldersForEntityMatch(
            AuthorEntityMatch entityMatch,
            List<AuthorFolder> existing)
        {
            List<AuthorFolder> hits = new List<AuthorFolder>();
            if (entityMatch == null || !entityMatch.Found) return hits;

            foreach (string name in entityMatch.IdentityNames ?? new List<string>())
            {
                if (String.IsNullOrWhiteSpace(name)) continue;
                hits.AddRange(FindDirectFoldersForCandidate(name, existing));
                hits.AddRange(FindFoldersByRoleNorm(name, existing, 1));
                hits.AddRange(FindFoldersByRoleNorm(name, existing, 2));
                hits.AddRange(FindFoldersByRoleNorm(name, existing, 3));
                hits.AddRange(FindFoldersForCandidate(name, existing));
            }

            return UniqueFolders(hits);
        }

        private static List<AuthorFolder> FindCautiousFoldersForCandidate(string candidate, List<AuthorFolder> existing)
        {
            HashSet<string> norms = new HashSet<string>(
                AuthorRules.GetCautiousNorms(candidate),
                StringComparer.OrdinalIgnoreCase);
            List<AuthorFolder> hits = new List<AuthorFolder>();

            if (norms.Count == 0) return hits;

            foreach (AuthorFolder folder in existing)
            {
                bool matched = false;
                foreach (string n in folder.CautiousNorms)
                {
                    if (norms.Contains(n))
                    {
                        matched = true;
                        break;
                    }
                }
                if (matched) hits.Add(folder);
            }

            return UniqueFolders(hits);
        }

        private static List<AuthorFolder> UniqueFolders(params List<AuthorFolder>[] lists)
        {
            List<AuthorFolder> r = new List<AuthorFolder>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (List<AuthorFolder> list in lists)
                foreach (AuthorFolder f in list)
                    if (seen.Add(f.AuthorPath)) r.Add(f);
            return r;
        }
    }
}
