using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MangaAuthorSorter
{
    // One diagnostic accumulator per explicit scan. No mutable global timers:
    // the independent simulation window can run at the same time.
    internal sealed class ArchivePlanDiagnostics
    {
        public long IdentitySourceMs, TargetDirectoryMs, InitialIndexMs;
        public long PrepareRecognitionMs, PlanLoopMs, IncrementalIndexMs;
        public int InitialBuilds, IncrementalAdds, NewAuthorFolders;
        public readonly HashSet<string> RecognizedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> UniqueAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int UniqueAuthorCount { get { return UniqueAuthors.Count; } }
    }

    internal sealed class ArchiveEngine
    {
        private sealed class PreparedRecognition
        {
            public List<string> Candidates = new List<string>();
            public AuthorMatchResult Scored;
        }

        private readonly AuthorEntityStore _entityStore;
        private readonly TagCleaningRuleStore _tagCleaningStore;
        private readonly object _targetCacheGate = new object();
        private string _targetCacheKey = "";
        private List<AuthorFolder> _cachedAuthorFolders;
        private SortedDictionary<int, string> _cachedAuthorGroups;
        private readonly object _identityCacheGate = new object();
        private string _publicCacheVersion = "";
        private long _identityRevision = -1;
        private long _entityCacheStamp = -1;
        private List<AliasGroup> _cachedAliasGroups;
        private AuthorEntityIndex _cachedEntityIndex;

        public AuthorRecognitionMode RecognitionMode { get; set; }
        public IParsedMetadataCache ParsedMetadataCache { get; set; }
        public IRecognitionFactCache RecognitionFactCache { get; set; }
        public IDestinationIndexCache DestinationIndexCache { get; set; }

        public CacheDiagnosticsSnapshot GetCacheDiagnostics()
        {
            CacheDiagnosticsSnapshot snapshot = new CacheDiagnosticsSnapshot();
            ICacheLayerDiagnostics parsed = ParsedMetadataCache as ICacheLayerDiagnostics;
            ICacheLayerDiagnostics recognition = RecognitionFactCache as ICacheLayerDiagnostics;
            ICacheLayerDiagnostics destination = DestinationIndexCache as ICacheLayerDiagnostics;
            if (parsed != null) { snapshot.ParsedHits = parsed.HitCount; snapshot.ParsedMisses = parsed.MissCount; }
            if (recognition != null) { snapshot.RecognitionHits = recognition.HitCount; snapshot.RecognitionMisses = recognition.MissCount; }
            if (destination != null) { snapshot.DestinationHits = destination.HitCount; snapshot.DestinationMisses = destination.MissCount; }
            return snapshot;
        }

        public ArchiveEngine(AuthorEntityStore entityStore, TagCleaningRuleStore tagCleaningStore)
        {
            _entityStore = entityStore;
            _tagCleaningStore = tagCleaningStore;
            RecognitionMode = AuthorRecognitionMode.Classic;
            List<AliasGroup> startupAliases;
            AuthorEntityIndex startupEntities;
            GetIdentitySourceCache(out startupAliases, out startupEntities);
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
            ScanProgressStage stage,
            ArchivePlanDiagnostics diagnostics = null)
        {
            return BuildPlanUsingIndex(files, null, authorRoot, maxAuthors,
                currentGroupTemplate, recognizedGroupTemplates, currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates, progress, cancelRequested, stage, diagnostics);
        }

        // FileInfo still transports paths for legacy callers; metadata comes
        // from Everything's/SourceIndex's own snapshot, not a second disk stat.
        public List<PlanItem> BuildPlanUsingIndex(
            List<FileInfo> files,
            IDictionary<string, SourceIndexFileSnapshot> metadataByPath,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            ScanProgressStage stage,
            ArchivePlanDiagnostics diagnostics = null)
        {
            List<PlanItem> initial = new List<PlanItem>();

            if (files != null)
            {
                foreach (FileInfo f in files)
                {
                    ThrowIfCanceled(cancelRequested);
                    SourceIndexFileSnapshot metadata;
                    if (metadataByPath != null && f != null &&
                        metadataByPath.TryGetValue(f.FullName, out metadata) && metadata != null)
                    {
                        initial.Add(new PlanItem {
                            FileName = f.Name, SourcePath = f.FullName,
                            FileSize = metadata.FileSize,
                            LastWriteTime = metadata.LastWriteTimeUtc == DateTime.MinValue
                                ? DateTime.MinValue : metadata.LastWriteTimeUtc.ToLocalTime()
                        });
                    }
                    else
                        initial.Add(NewBlankItem(f));
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
                stage, null, 0, diagnostics);
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
            int stopAfterMatches = 0,
            ArchivePlanDiagnostics diagnostics = null)
        {
            return ResolvePlanCore(
                sourceItems, authorRoot, maxAuthors, currentGroupTemplate,
                recognizedGroupTemplates, currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates, progress, cancelRequested, stage,
                stopMode, stopAfterMatches, null, diagnostics);
        }

        internal List<PlanItem> ResolvePlanVirtual(
            IEnumerable<PlanItem> sourceItems,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            SimulationArchiveEnvironment simulation,
            Func<bool> cancelRequested)
        {
            return ResolvePlanVirtual(
                sourceItems, authorRoot, maxAuthors, currentGroupTemplate,
                recognizedGroupTemplates, currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates, simulation, null, cancelRequested);
        }

        internal List<PlanItem> ResolvePlanVirtual(
            IEnumerable<PlanItem> sourceItems,
            string authorRoot,
            int maxAuthors,
            string currentGroupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string currentAuthorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            SimulationArchiveEnvironment simulation,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            if (simulation == null) throw new ArgumentNullException("simulation");
            return ResolvePlanCore(
                sourceItems, authorRoot, maxAuthors, currentGroupTemplate,
                recognizedGroupTemplates, currentAuthorFolderTemplate,
                recognizedAuthorFolderTemplates, progress, cancelRequested,
                ScanProgressStage.Planning, null, 0, simulation, null);
        }

        private List<PlanItem> ResolvePlanCore(
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
            ScanModeKind? stopMode,
            int stopAfterMatches,
            SimulationArchiveEnvironment simulation,
            ArchivePlanDiagnostics diagnostics)
        {
            ThrowIfCanceled(cancelRequested);

            if (simulation == null && !Directory.Exists(authorRoot))
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

            List<AliasGroup> aliasGroups;
            AuthorEntityIndex entityIndex;
            long phaseTick = diagnostics != null ? Stopwatch.GetTimestamp() : 0;
            GetIdentitySourceCache(out aliasGroups, out entityIndex);
            if (diagnostics != null) diagnostics.IdentitySourceMs += ElapsedMs(phaseTick);


            ThrowIfCanceled(cancelRequested);

            phaseTick = diagnostics != null ? Stopwatch.GetTimestamp() : 0;
            List<AuthorFolder> existing;
            SortedDictionary<int, string> groups;
            if (simulation != null)
            {
                existing = new List<AuthorFolder>(simulation.ExistingAuthors ?? new List<AuthorFolder>());
                groups = simulation.Groups != null
                    ? new SortedDictionary<int, string>(simulation.Groups)
                    : new SortedDictionary<int, string>();
            }
            else
            {
                GetTargetDirectoryCache(
                    authorRoot, normalizedCurrent, recognized, normalizedAuthorFolderTemplate,
                    recognizedAuthorFolders, cancelRequested, out existing, out groups);
            }

            ThrowIfCanceled(cancelRequested);

            if (diagnostics != null) diagnostics.TargetDirectoryMs += ElapsedMs(phaseTick);

            // V1.9.6: Treat author folders planned earlier in the same scan as
            // searchable author identities too. This makes [creator] and
            // [circle (creator)] converge on the same planned folder even
            // before that folder physically exists on disk.
            List<AuthorFolder> searchableAuthors =
                new List<AuthorFolder>(existing);
            phaseTick = diagnostics != null ? Stopwatch.GetTimestamp() : 0;
            AuthorIndex authorIndex = AuthorIndex.Build(searchableAuthors, aliasGroups, entityIndex);
            if (diagnostics != null)
            {
                diagnostics.InitialIndexMs += ElapsedMs(phaseTick);
                diagnostics.InitialBuilds++;
            }
            AuthorScoringRules.Context scoringContext =
                RecognitionMode == AuthorRecognitionMode.Scoring
                    ? AuthorScoringRules.CreateContext(aliasGroups, entityIndex, _tagCleaningStore)
                    : null;
            HashSet<string> roundTargetPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            Dictionary<int, int> groupCounts = new Dictionary<int, int>();

            foreach (KeyValuePair<int, string> pair in groups)
            {
                int count = existing.Count(delegate(AuthorFolder folder)
                {
                    return folder != null && folder.GroupNumber == pair.Key;
                });
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
            HashSet<string> scanActivities = useEarlyStop ? new HashSet<string>()
                : FileNameStructure.DiscoverActivities(sourceSequence.Select(x => Path.GetFileNameWithoutExtension(x.FileName ?? "")));
            phaseTick = diagnostics != null ? Stopwatch.GetTimestamp() : 0;
            Dictionary<string, PreparedRecognition> preparedRecognitions =
                useEarlyStop
                    ? new Dictionary<string, PreparedRecognition>(StringComparer.OrdinalIgnoreCase)
                    : PrepareRecognitionsInParallel(sourceSequence, searchableAuthors, aliasGroups, entityIndex, cancelRequested);
            if (diagnostics != null) diagnostics.PrepareRecognitionMs += ElapsedMs(phaseTick);


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

            phaseTick = diagnostics != null ? Stopwatch.GetTimestamp() : 0;
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
                    (simulation == null && !File.Exists(original.SourcePath)))
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

                PreparedRecognition prepared;
                if (diagnostics != null) diagnostics.RecognizedFiles.Add(p.SourcePath);
                preparedRecognitions.TryGetValue(p.SourcePath ?? "", out prepared);
                List<string> authorCandidates = prepared != null
                    ? new List<string>(prepared.Candidates)
                    : GetAuthorCandidates(p);
                string author =
                    authorCandidates.Count > 0
                        ? authorCandidates[0]
                        : null;
                FileNameStructure fileStructure = FileNameStructure.Parse(Path.GetFileNameWithoutExtension(p.FileName ?? ""), scanActivities);
                AuthorEntityMatch prefixEntity = entityIndex != null && fileStructure.Prefix.Length > 0
                    ? entityIndex.Resolve(fileStructure.Prefix) : null;
                if (prefixEntity != null && prefixEntity.Found && prefixEntity.Entity != null && prefixEntity.Entity.UserConfirmed)
                {
                    // An explicit user decision outranks inferred syntax.
                    authorCandidates.Add(fileStructure.Prefix);
                    author = authorCandidates[0];
                }
                else if (fileStructure.SuspectedActivity)
                {
                    // Scan-dependent evidence is applied after the filename cache
                    // and is never written back as a permanent parsing fact.
                    if (!FileNameStructure.Parse(Path.GetFileNameWithoutExtension(p.FileName ?? "")).SuspectedActivity)
                        authorCandidates = AuthorRules.GetAuthorCandidatesFromFileName(fileStructure.IdentityText, _tagCleaningStore);
                    author = authorCandidates.Count > 0 ? authorCandidates[0] : null;
                }
                AuthorMatchResult scoredMatch = prepared != null ? prepared.Scored : null;

                // Deterministic fallback for untagged legacy files: an exact,
                // complete existing folder identity at the filename start is
                // strong evidence. Boundary checks prevent partial-name hits.
                if (String.IsNullOrWhiteSpace(author))
                {
                    string fileBaseName = Path.GetFileNameWithoutExtension(p.FileName);
                    foreach (AuthorFolder folder in authorIndex.FindCandidateFolders(fileBaseName))
                    {
                        if (folder != null &&
                            (AuthorRules.StartsWithCompleteIdentity(fileBaseName, folder.AuthorName) ||
                             AuthorRules.StartsWithConventionDescriptionThenIdentity(fileBaseName, folder.AuthorName)))
                        {
                            authorCandidates.Add(folder.AuthorName);
                        }
                    }
                    authorCandidates = authorCandidates
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    author = authorCandidates.Count > 0 ? authorCandidates[0] : null;
                }

                // The classic rules always run first. Scoring is only allowed
                // to rescue a filename for which those rules found no author.
                if (String.IsNullOrWhiteSpace(author) &&
                    RecognitionMode == AuthorRecognitionMode.Scoring &&
                    String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    string fileBaseName = Path.GetFileNameWithoutExtension(p.FileName);
                    if (scoredMatch == null || fileStructure.SuspectedActivity) scoredMatch = scoringContext.Match(fileStructure.IdentityText, searchableAuthors);
                    p.RecognitionScore = scoredMatch.Score;
                    p.RecognitionRunnerUpScore = scoredMatch.RunnerUpScore;
                    if (scoredMatch.Type == AuthorMatchType.NotFound && scoredMatch.Score > 0)
                    {
                        p.MatchWhy = scoredMatch.Why;
                        p.EvidenceKind = RecognitionEvidenceKind.Scoring;
                    }
                    if (scoredMatch.Type == AuthorMatchType.Matched && scoredMatch.Folder != null)
                    {
                        author = !String.IsNullOrWhiteSpace(scoredMatch.Folder.PreferredIdentity)
                            ? scoredMatch.Folder.PreferredIdentity
                            : scoredMatch.Folder.AuthorName;
                        authorCandidates.Clear();
                        authorCandidates.Add(author);
                    }
                    else if (scoredMatch.Type == AuthorMatchType.Choice &&
                             scoredMatch.Candidates.Count > 0)
                    {
                        AuthorFolder best = scoredMatch.Candidates[0];
                        author = !String.IsNullOrWhiteSpace(best.PreferredIdentity)
                            ? best.PreferredIdentity
                            : best.AuthorName;
                        authorCandidates.Clear();
                        authorCandidates.Add(author);
                    }
                }

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
                if (diagnostics != null && !String.IsNullOrWhiteSpace(p.Author))
                    diagnostics.UniqueAuthors.Add(AuthorRules.NormalizeText(p.Author));

                if (String.IsNullOrWhiteSpace(author))
                {
                    p.Status = "无法识别作者";
                    p.StatusCode = PlanStatusCode.Unrecognized;
                    p.EvidenceKind = RecognitionEvidenceKind.Unrecognized;
                    if (_entityStore != null && fileStructure.WorkTitle.Length > 0)
                    {
                        foreach (string name in _entityStore.GetWorkTitleCandidates(fileStructure.WorkTitle))
                        {
                            p.CandidateNames.Add(name);
                            p.CandidatePaths.Add("");
                            p.CandidateIsPlanned.Add(false);
                        }
                        if (p.CandidateNames.Count > 0) p.MatchWhy = "Local work metadata: " + String.Join(", ", p.CandidateNames) + "; confirmation required";
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

                bool manualTargetAvailable =
                    !String.IsNullOrWhiteSpace(p.ManualTargetDir) &&
                    ((simulation != null
                        ? existing.Any(delegate(AuthorFolder f)
                          { return f != null && String.Equals(f.AuthorPath, p.ManualTargetDir, StringComparison.OrdinalIgnoreCase); })
                        : Directory.Exists(p.ManualTargetDir)) ||
                     roundTargetPaths.Contains(p.ManualTargetDir));

                if (manualTargetAvailable)
                {
                    p.MatchedAs = !String.IsNullOrWhiteSpace(p.ManualTargetName) ? p.ManualTargetName : new DirectoryInfo(p.ManualTargetDir).Name;
                    p.MatchWhy = "手动指定作者文件夹；已写入作者别名库";
                    p.EvidenceKind = RecognitionEvidenceKind.Manual;
                    p.TargetDir = p.ManualTargetDir;
                    p.TargetPath = Path.Combine(p.TargetDir, p.FileName);
                    FinalizeMoveState(
                        p, "手动指定作者文件夹", PlanStatusCode.ManualFolder,
                        simulation != null ? simulation.ExistingTargetFiles : null);
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
                if (scoredMatch != null &&
                    scoredMatch.Type != AuthorMatchType.NotFound &&
                    String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    match = scoredMatch;
                }
                else if (!String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    match =
                        FindExistingAuthor(
                            matchAuthor,
                            searchableAuthors,
                            aliasGroups,
                            entityIndex,
                            authorIndex);
                }
                else
                {
                    match =
                        FindExistingAuthorCandidates(
                            authorCandidates,
                            searchableAuthors,
                            aliasGroups,
                            entityIndex,
                            authorIndex);
                }

                // A classic NotFound result means the extracted identity would
                // become a new author. Only now may scoring try to map it back
                // to an existing local author. Existing classic matches and
                // ambiguities are never overridden.
                if (match.Type == AuthorMatchType.NotFound &&
                    scoredMatch == null &&
                    RecognitionMode == AuthorRecognitionMode.Scoring &&
                    String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    string fileBaseName = Path.GetFileNameWithoutExtension(p.FileName);
                    scoredMatch = scoringContext.Match(fileBaseName, searchableAuthors);
                    p.RecognitionScore = scoredMatch.Score;
                    p.RecognitionRunnerUpScore = scoredMatch.RunnerUpScore;
                    if (scoredMatch.Type != AuthorMatchType.NotFound)
                    {
                        match = scoredMatch;
                        if (scoredMatch.Type == AuthorMatchType.Matched && scoredMatch.Folder != null)
                        {
                            author = !String.IsNullOrWhiteSpace(scoredMatch.Folder.PreferredIdentity)
                                ? scoredMatch.Folder.PreferredIdentity
                                : scoredMatch.Folder.AuthorName;
                            p.Author = author;
                        }
                    }
                    else if (scoredMatch.Score > 0)
                    {
                        p.MatchWhy = scoredMatch.Why;
                        p.EvidenceKind = RecognitionEvidenceKind.Scoring;
                    }
                }

                // "Choice" is meaningful only when at least two distinct
                // destination folders remain. A single candidate is a unique
                // match and must never open an ambiguity dialog.
                if (match.Type == AuthorMatchType.Choice)
                {
                    List<AuthorFolder> uniqueCandidates =
                        UniqueFolders(match.Candidates);
                    if (uniqueCandidates.Count == 1)
                    {
                        match = Result(
                            AuthorMatchType.Matched,
                            uniqueCandidates[0],
                            "候选目录去重后唯一，已自动匹配");
                    }
                    else
                    {
                        match.Candidates = uniqueCandidates;
                    }
                }

                if (match.Type == AuthorMatchType.Ambiguous)
                {
                    p.MatchWhy = match.Why;
                    p.Status = "同作者识别有歧义，跳过";
                    p.StatusCode = PlanStatusCode.Ambiguous;
                    p.EvidenceKind = RecognitionEvidenceKind.Ambiguous;
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
                    p.StatusCode = PlanStatusCode.CandidateConfirmation;
                    p.EvidenceKind = RecognitionEvidenceKind.Ambiguous;
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
                        p.StatusCode = PlanStatusCode.NewAuthorReuse;
                        p.EvidenceKind = RecognitionEvidenceKind.NewAuthorReuse;
                    }
                    else
                    {
                        matchWhy = match.Why;
                        statusText = "匹配到已有作者";
                        p.StatusCode = PlanStatusCode.Matched;
                        p.EvidenceKind = match.EvidenceKind;
                    }
                }
                else
                {
                    bool aliasAmbiguous;
                    AliasGroup aliasGroup = authorIndex.ResolveAlias(matchAuthor, out aliasAmbiguous);
                    string canonical, identity;
                    bool publicDatabaseHit = false;
                    bool personalEntityHit = false;

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
                            canonical = GetEntityCanonicalIdentity(
                                matchAuthor,
                                entityMatch);
                            publicDatabaseHit = entityMatch.FromPublicDatabase;
                            personalEntityHit = !entityMatch.FromPublicDatabase;
                            identity = (entityMatch.FromPublicDatabase ? "public-entity:" : "entity:") +
                                entityMatch.Entity.Id.ToString();
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
                        p.StatusCode = PlanStatusCode.NewAuthorReuse;
                        p.EvidenceKind = RecognitionEvidenceKind.NewAuthorReuse;
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
                        matchWhy = publicDatabaseHit
                            ? "公共作者库命中：" + canonical + "；未找到已有作者目录，按新作者处理"
                            : personalEntityHit
                                ? "个人作者实体命中：" + canonical + "；未找到已有作者目录，按新作者处理"
                                : !String.Equals(canonical, matchAuthor, StringComparison.Ordinal)
                                    ? "别名库统一为：" + canonical
                                    : "未找到已有作者，按新作者处理";
                        statusText =
                            "新作者，计划放入 " +
                            groupName;
                        p.StatusCode = PlanStatusCode.NewAuthor;
                        p.StatusArgument = groupName;
                        p.EvidenceKind = RecognitionEvidenceKind.NewAuthor;

                        long addTick = diagnostics != null ? Stopwatch.GetTimestamp() : 0;
                        AuthorFolder addedFolder = AddRoundAuthorTarget(
                            searchableAuthors,
                            roundTargetPaths,
                            groupNumber,
                            canonical,
                            matchAuthor,
                            targetDir);
                        if (addedFolder != null)
                        {
                            // A planned folder affects later matches immediately,
                            // without rebuilding every existing name/alias bucket.
                            authorIndex.AddPlannedFolder(addedFolder);
                            if (diagnostics != null)
                            {
                                diagnostics.NewAuthorFolders++;
                                diagnostics.IncrementalAdds++;
                            }
                        }
                        if (diagnostics != null) diagnostics.IncrementalIndexMs += ElapsedMs(addTick);
                    }
                }

                if (!String.IsNullOrWhiteSpace(p.ManualTargetAuthor))
                {
                    matchWhy = "手动指定作者；已写入作者别名库；目标目录按当前列表重新规划";
                    statusText = "手动指定作者";
                    p.StatusCode = PlanStatusCode.ManualAuthor;
                    p.EvidenceKind = RecognitionEvidenceKind.Manual;
                }

                p.MatchedAs = matchedAs;
                p.MatchWhy = matchWhy;
                p.TargetDir = targetDir;
                p.TargetPath = Path.Combine(targetDir, p.FileName);
                FinalizeMoveState(
                    p, statusText, p.StatusCode,
                    simulation != null ? simulation.ExistingTargetFiles : null);
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

            if (diagnostics != null) diagnostics.PlanLoopMs += ElapsedMs(phaseTick);
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

            foreach (PlanItem item in resolved)
            {
                FileNameStructure structure = FileNameStructure.Parse(Path.GetFileNameWithoutExtension(item.FileName ?? ""), scanActivities);
                if (!structure.SuspectedActivity) continue;
                string explanation = "[structure] " + structure.Prefix + ": " + structure.Reason + "; confidence=" + structure.PrefixConfidence;
                AuthorEntityMatch confirmedPrefix = entityIndex != null ? entityIndex.Resolve(structure.Prefix) : null;
                if (confirmedPrefix != null && confirmedPrefix.Found && confirmedPrefix.Entity != null && confirmedPrefix.Entity.UserConfirmed)
                    explanation += "; user-confirmed identity retained";
                else explanation += "; prefix excluded from author candidates";
                if (structure.Creators.Count > 0) explanation += "; artist=" + String.Join(", ", structure.Creators) + "; circle=" + structure.Society;
                explanation += "; selected=" + (String.IsNullOrWhiteSpace(item.Author) ? "pending confirmation; title=" + structure.WorkTitle : item.Author);
                item.MatchWhy = explanation + (String.IsNullOrWhiteSpace(item.MatchWhy) ? "" : "; " + item.MatchWhy);
            }
            if (RecognitionFactCache != null)
            {
                foreach (PlanItem item in resolved) RecognitionFactCache.Store(item);
                RecognitionFactCache.Flush();
            }

            return resolved;
        }

        private static long ElapsedMs(long since)
        {
            return (long)((Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency);
        }

        private Dictionary<string, PreparedRecognition> PrepareRecognitionsInParallel(
            IEnumerable<PlanItem> source,
            List<AuthorFolder> folders,
            List<AliasGroup> aliases,
            AuthorEntityIndex entities,
            Func<bool> cancelRequested)
        {
            List<PlanItem> items = (source ?? Enumerable.Empty<PlanItem>()).Where(delegate(PlanItem x)
            { return x != null && !String.IsNullOrWhiteSpace(x.SourcePath); }).ToList();
            ConcurrentDictionary<string, PreparedRecognition> prepared =
                new ConcurrentDictionary<string, PreparedRecognition>(StringComparer.OrdinalIgnoreCase);
            int workers = Math.Max(2, Math.Min(8, Environment.ProcessorCount - 1));
            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = workers };
            ThreadLocal<AuthorScoringRules.Context> scoring = RecognitionMode == AuthorRecognitionMode.Scoring
                ? new ThreadLocal<AuthorScoringRules.Context>(delegate { return AuthorScoringRules.CreateContext(aliases, entities, _tagCleaningStore); })
                : null;
            try
            {
                Parallel.ForEach(items, options, delegate(PlanItem item)
                {
                    if (cancelRequested != null && cancelRequested()) throw new OperationCanceledException();
                    string fileBase = Path.GetFileNameWithoutExtension(item.FileName ?? "");
                    PreparedRecognition value = new PreparedRecognition();
                    RecognitionCacheEntry recognition;
                    List<string> cachedCandidates;
                    if (RecognitionFactCache != null &&
                        RecognitionFactCache.TryGet(item.SourcePath, item.FileName, out recognition))
                    {
                        value.Candidates = new List<string> { recognition.MatchedAuthor };
                    }
                    else if (ParsedMetadataCache != null &&
                        ParsedMetadataCache.TryGetAuthorCandidates(item.SourcePath, item.FileName, out cachedCandidates))
                    {
                        value.Candidates = cachedCandidates;
                    }
                    else
                    {
                        value.Candidates = AuthorRules.GetAuthorCandidatesFromFileName(fileBase, _tagCleaningStore);
                        if (ParsedMetadataCache != null)
                            ParsedMetadataCache.StoreAuthorCandidates(item.SourcePath, item.FileName, value.Candidates);
                    }
                    if (value.Candidates.Count == 0 && scoring != null)
                        value.Scored = scoring.Value.Match(fileBase, folders);
                    prepared[item.SourcePath] = value;
                });
            }
            catch (AggregateException ex)
            {
                if (ex.Flatten().InnerExceptions.All(delegate(Exception inner) { return inner is OperationCanceledException; }))
                    throw new OperationCanceledException();
                throw;
            }
            finally
            {
                if (scoring != null) scoring.Dispose();
                if (ParsedMetadataCache != null) ParsedMetadataCache.Flush();
            }
            return new Dictionary<string, PreparedRecognition>(prepared, StringComparer.OrdinalIgnoreCase);
        }

        private List<string> GetAuthorCandidates(PlanItem item)
        {
            RecognitionCacheEntry recognition;
            List<string> candidates;
            if (RecognitionFactCache != null &&
                RecognitionFactCache.TryGet(item.SourcePath, item.FileName, out recognition))
                return new List<string> { recognition.MatchedAuthor };
            if (ParsedMetadataCache != null &&
                ParsedMetadataCache.TryGetAuthorCandidates(item.SourcePath, item.FileName, out candidates))
                return candidates;
            candidates = AuthorRules.GetAuthorCandidatesFromFileName(
                Path.GetFileNameWithoutExtension(item.FileName), _tagCleaningStore);
            if (ParsedMetadataCache != null)
            {
                ParsedMetadataCache.StoreAuthorCandidates(item.SourcePath, item.FileName, candidates);
                ParsedMetadataCache.Flush();
            }
            return candidates;
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

        private static AuthorFolder AddRoundAuthorTarget(
            List<AuthorFolder> searchableAuthors,
            HashSet<string> roundTargetPaths,
            int groupNumber,
            string canonical,
            string originalAuthor,
            string targetDir)
        {
            if (String.IsNullOrWhiteSpace(targetDir))
                return null;

            if (!roundTargetPaths.Add(targetDir))
                return null;

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
            return f;
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
            if (DestinationIndexCache != null)
            {
                List<DestinationFolderIndexEntry> persisted;
                if (DestinationIndexCache.TryLoad(root, key, out persisted))
                {
                    List<AuthorFolder> loadedAuthors = new List<AuthorFolder>();
                    SortedDictionary<int, string> loadedGroups = new SortedDictionary<int, string>();
                    foreach (DestinationFolderIndexEntry entry in persisted)
                    {
                        if (entry.IsGroup)
                        {
                            loadedGroups[entry.GroupNumber] = entry.FullPath;
                            continue;
                        }
                        AuthorFolder folder = new AuthorFolder
                        {
                            GroupNumber = entry.GroupNumber,
                            AuthorName = !String.IsNullOrWhiteSpace(entry.LogicalAuthor)
                                ? entry.LogicalAuthor : entry.FolderName,
                            AuthorPath = entry.FullPath
                        };
                        folder.PreferredIdentity = AuthorRules.GetPreferredAuthorIdentity(folder.AuthorName);
                        IndexAuthorFolderName(folder, folder.AuthorName);
                        if (!String.Equals(folder.AuthorName, entry.FolderName, StringComparison.Ordinal))
                            IndexAuthorFolderName(folder, entry.FolderName);
                        loadedAuthors.Add(folder);
                    }
                    lock (_targetCacheGate)
                    {
                        _targetCacheKey = key;
                        _cachedAuthorFolders = loadedAuthors;
                        _cachedAuthorGroups = loadedGroups;
                    }
                    authors = new List<AuthorFolder>(loadedAuthors);
                    groups = new SortedDictionary<int, string>(loadedGroups);
                    return;
                }
            }
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
            if (DestinationIndexCache != null)
                DestinationIndexCache.Store(root, key, builtGroups, builtAuthors);
            authors = new List<AuthorFolder>(builtAuthors);
            groups = new SortedDictionary<int, string>(builtGroups);
        }

        public void InvalidateTargetDirectoryCache()
        {
            string previousRoot = "";
            lock (_targetCacheGate)
            {
                int separator = (_targetCacheKey ?? "").IndexOf('|');
                previousRoot = separator > 0 ? _targetCacheKey.Substring(0, separator) : "";
                _targetCacheKey = "";
                _cachedAuthorFolders = null;
                _cachedAuthorGroups = null;
            }
            if (DestinationIndexCache != null && previousRoot.Length > 0)
                DestinationIndexCache.Invalidate(previousRoot);
        }

        private void GetIdentitySourceCache(out List<AliasGroup> aliases, out AuthorEntityIndex entities)
        {
            string publicVersion = _entityStore != null ? _entityStore.PublicDatabaseVersion : "";
            long entityStamp = GetFileStamp(_entityStore != null ? _entityStore.Path : "");
            lock (_identityCacheGate)
            {
                if (_entityStore != null && (_cachedEntityIndex == null || entityStamp != _entityCacheStamp || publicVersion != _publicCacheVersion || _identityRevision != _entityStore.IdentityRevision))
                {
                    _cachedEntityIndex = _entityStore.LoadIndex();
                    _entityCacheStamp = entityStamp;
                    _cachedAliasGroups = _cachedEntityIndex.AliasGroups;
                    _publicCacheVersion = publicVersion;
                    _identityRevision = _entityStore.IdentityRevision;
                }
                aliases = _cachedAliasGroups ?? new List<AliasGroup>();
                entities = _cachedEntityIndex;
            }
        }

        public Func<string, string> CreateIdentityDependencyResolver()
        {
            List<AliasGroup> aliases; AuthorEntityIndex index;
            GetIdentitySourceCache(out aliases, out index);
            return index == null ? (Func<string, string>)(name => "") : index.GetDependencyVersion;
        }

        private static long GetFileStamp(string path)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
                FileInfo info = new FileInfo(path);
                return info.LastWriteTimeUtc.Ticks ^ info.Length;
            }
            catch { return -1; }
        }

        private static void FinalizeMoveState(
            PlanItem p,
            string normalStatus,
            PlanStatusCode normalStatusCode,
            ISet<string> virtualExistingTargetFiles = null)
        {
            if (String.Equals(p.SourcePath, p.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                p.Status = "文件已在目标作者文件夹";
                p.StatusCode = PlanStatusCode.AlreadyInTarget;
                p.CanMove = false;
            }
            else if (virtualExistingTargetFiles != null
                ? virtualExistingTargetFiles.Contains(p.TargetPath ?? "")
                : File.Exists(p.TargetPath))
            {
                p.Status = "目标已有同名文件，跳过";
                p.StatusCode = PlanStatusCode.TargetExists;
                p.CanMove = false;
            }
            else
            {
                p.Status = normalStatus;
                p.StatusCode = normalStatusCode;
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
            AuthorEntityIndex entityIndex,
            AuthorIndex authorIndex)
        {
            if (authors == null || authors.Count == 0)
                return Result(AuthorMatchType.NotFound, null, "未找到同作者目录");

            // Once the first leading identity is uniquely known by the local
            // entity database it is authoritative. Later bracket tags are
            // commonly work/series metadata and must not create a false author
            // conflict with the confirmed first tag.
            string primaryAuthor = authors[0] ?? "";
            if (entityIndex != null && !String.IsNullOrWhiteSpace(primaryAuthor))
            {
                AuthorEntityMatch primaryEntity = entityIndex.Resolve(primaryAuthor);
                if (primaryEntity.Found && !primaryEntity.Ambiguous)
                {
                    return FindExistingAuthor(
                        primaryAuthor,
                        existing,
                        aliasGroups,
                        entityIndex,
                        authorIndex);
                }
            }

            List<AuthorMatchResult> matched = new List<AuthorMatchResult>();
            List<AuthorMatchResult> hardChoices = new List<AuthorMatchResult>();
            List<AuthorMatchResult> cautiousChoices = new List<AuthorMatchResult>();

            foreach (string candidate in authors)
            {
                if (String.IsNullOrWhiteSpace(candidate)) continue;
                AuthorMatchResult r =
                    FindExistingAuthor(candidate, existing, aliasGroups, entityIndex, authorIndex);

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
            AuthorEntityIndex entityIndex,
            AuthorIndex authorIndex)
        {
            // A complete local-folder identity is the strongest possible
            // evidence. Resolve it before aliases or structured society/creator
            // expansion so an exact "A (B)" folder cannot become ambiguous
            // merely because A or B also appears in another folder.
            string completeIdentity = AuthorRules.NormalizeText(
                AuthorRules.GetDirectMatchIdentity(author));
            List<AuthorFolder> completeHits =
                authorIndex.FindExactDirect(completeIdentity);
            if (completeHits.Count == 1)
            {
                return Result(
                    AuthorMatchType.Matched,
                    completeHits[0],
                    "作者完整身份命中已有本地作者目录");
            }
            if (completeHits.Count > 1)
            {
                AuthorMatchResult completeChoice = Result(
                    AuthorMatchType.Choice,
                    null,
                    "作者完整身份对应多个本地作者目录");
                completeChoice.Candidates = completeHits;
                return completeChoice;
            }

            bool aliasAmbiguous;
            AliasGroup candidateGroup = authorIndex.ResolveAlias(author, out aliasAmbiguous);
            if (aliasAmbiguous)
                return Result(AuthorMatchType.Ambiguous, null, "Reason.EntityConflict");

            if (candidateGroup != null)
            {
                List<AuthorFolder> hits = new List<AuthorFolder>();
                foreach (AuthorFolder folder in existing)
                {
                    bool fa;
                    AliasGroup fg = authorIndex.ResolveAlias(folder.AuthorName, out fa);
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
                        authorIndex.FindExactDirect(flat);
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
                    UniqueFolders(
                        authorIndex.FindDirect(structured.Society),
                        authorIndex.FindRole(structured.Society, 1),
                        authorIndex.FindRole(structured.Society, 3));

                List<AuthorFolder> creatorHits = new List<AuthorFolder>();
                List<string> matchedCreators = new List<string>();
                foreach (string creator in structured.Creators)
                {
                    List<AuthorFolder> one =
                        UniqueFolders(
                            authorIndex.FindDirect(creator),
                            authorIndex.FindRole(creator, 2));
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
            List<AuthorFolder> directHits = authorIndex.FindDirect(author);
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
            {
                AuthorMatchResult directChoice = Result(
                    AuthorMatchType.Choice,
                    null,
                    "同一个作者候选可匹配多个已有目录；请选择实际归属");
                directChoice.Candidates = directHits;
                return directChoice;
            }

            List<AuthorFolder> societyRoleHits = authorIndex.FindRole(author, 1);
            if (societyRoleHits.Count == 1)
                return Result(AuthorMatchType.Matched, societyRoleHits[0], "仅社团名「" + author + "」匹配到已有作者目录");
            if (societyRoleHits.Count > 1)
                return ChoiceForRole(author, societyRoleHits, true);

            List<AuthorFolder> creatorRoleHits = authorIndex.FindRole(author, 2);
            if (creatorRoleHits.Count == 1)
                return Result(AuthorMatchType.Matched, creatorRoleHits[0], "仅作者名「" + author + "」匹配到已有作者目录");
            if (creatorRoleHits.Count > 1)
                return ChoiceForRole(author, creatorRoleHits, false);

            List<AuthorFolder> segmentHits = authorIndex.FindRole(author, 3);
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

            List<AuthorFolder> genericHits = authorIndex.FindGeneric(author);

            if (genericHits.Count == 1)
                return Result(AuthorMatchType.Matched, genericHits[0], "作者目录名称标准化匹配（空白 / 全半角 / 括号容错）");

            if (genericHits.Count > 1)
            {
                AuthorMatchResult genericChoice = Result(
                    AuthorMatchType.Choice,
                    null,
                    "同一个作者候选可匹配多个已有目录；请选择实际归属");
                genericChoice.Candidates = genericHits;
                return genericChoice;
            }

            if (entityIndex != null)
            {
                AuthorEntityMatch entityMatch = entityIndex.Resolve(author);
                if (entityMatch.Ambiguous)
                {
                    return Result(
                        AuthorMatchType.Ambiguous,
                        null,
                        "Reason.EntityConflict");
                }

                if (entityMatch.Found && entityMatch.Entity != null)
                {
                    List<AuthorFolder> entityHits =
                        FindFoldersForEntityMatch(entityMatch, authorIndex);

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

            List<AuthorFolder> cautiousHits = authorIndex.FindCautious(author);
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

        private static string GetEntityCanonicalIdentity(
            string sourceIdentity,
            AuthorEntityMatch entityMatch)
        {
            if (entityMatch == null || entityMatch.Entity == null)
                return sourceIdentity ?? "";

            string artist = entityMatch.Entity.CanonicalName ?? "";
            StructuredAuthorParts parts =
                AuthorRules.GetStructuredAuthorParts(sourceIdentity);
            if (parts == null || String.IsNullOrWhiteSpace(parts.Society))
                return artist;

            HashSet<string> societyNorms = new HashSet<string>(
                AuthorRules.GetNorms(parts.Society),
                StringComparer.OrdinalIgnoreCase);
            List<CircleEntityRecord> matchedGroups =
                new List<CircleEntityRecord>();

            foreach (CircleEntityRecord group in entityMatch.RelatedGroups)
            {
                if (group == null) continue;
                bool matched = false;
                foreach (string name in new string[]
                {
                    group.CanonicalName,
                    group.RomanName,
                    group.EHGroupTag,
                    group.NHGroupTag
                })
                {
                    foreach (string norm in AuthorRules.GetNorms(name ?? ""))
                    {
                        if (!societyNorms.Contains(norm)) continue;
                        matched = true;
                        break;
                    }
                    if (matched) break;
                }
                if (matched) matchedGroups.Add(group);
            }

            if (matchedGroups.Count != 1)
                return artist;

            string groupName =
                !String.IsNullOrWhiteSpace(matchedGroups[0].CanonicalName)
                    ? matchedGroups[0].CanonicalName
                    : parts.Society;
            return groupName + " (" + artist + ")";
        }

        internal static void IndexAuthorFolderName(AuthorFolder folder, string name)
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



        // role: 1 = society, 2 = creator, 3 = conservative name segment.



        private static AuthorMatchResult Result(AuthorMatchType type, AuthorFolder folder, string why)
        {
            AuthorMatchResult r = new AuthorMatchResult();
            r.Type = type;
            r.Folder = folder;
            r.Why = why;
            r.EvidenceKind = ClassifyEvidence(type, why);
            return r;
        }

        private static RecognitionEvidenceKind ClassifyEvidence(AuthorMatchType type, string why)
        {
            string value = why ?? "";
            if (type == AuthorMatchType.Ambiguous || type == AuthorMatchType.Choice) return RecognitionEvidenceKind.Ambiguous;
            if (value.StartsWith("评分识别：", StringComparison.Ordinal)) return RecognitionEvidenceKind.Scoring;
            if (value.StartsWith("作者实体库：", StringComparison.Ordinal)) return RecognitionEvidenceKind.Entity;
            if (value.StartsWith("作者别名库：", StringComparison.Ordinal) || value.StartsWith("别名库统一为：", StringComparison.Ordinal)) return RecognitionEvidenceKind.Alias;
            if (value.StartsWith("仅社团名", StringComparison.Ordinal) || value.StartsWith("社团名称段", StringComparison.Ordinal)) return RecognitionEvidenceKind.Society;
            if (value.StartsWith("仅作者名", StringComparison.Ordinal)) return RecognitionEvidenceKind.Author;
            if (value.IndexOf("标准化匹配", StringComparison.Ordinal) >= 0) return RecognitionEvidenceKind.Normalized;
            if (type == AuthorMatchType.Matched) return RecognitionEvidenceKind.Direct;
            return RecognitionEvidenceKind.None;
        }


        private static List<AuthorFolder> FindFoldersForEntityMatch(
            AuthorEntityMatch entityMatch,
            AuthorIndex authorIndex)
        {
            List<AuthorFolder> hits = new List<AuthorFolder>();
            if (entityMatch == null || !entityMatch.Found) return hits;

            foreach (string name in entityMatch.IdentityNames ?? new List<string>())
            {
                if (String.IsNullOrWhiteSpace(name)) continue;
                hits.AddRange(authorIndex.FindDirect(name));
                hits.AddRange(authorIndex.FindRole(name, 1));
                hits.AddRange(authorIndex.FindRole(name, 2));
                hits.AddRange(authorIndex.FindRole(name, 3));
                hits.AddRange(authorIndex.FindGeneric(name));
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
