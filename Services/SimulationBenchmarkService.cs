using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MangaAuthorSorter
{
    internal sealed class SimulationBenchmarkService
    {
        private sealed class SourceRecord
        {
            public SimulationAuthorSeed Author;
            public PlanItem Item;
        }

        private readonly string _databasePath;

        private readonly AuthorEntityStore _entityStore;
        private readonly TagCleaningRuleStore _tagCleaningStore;
        private readonly AuthorRecognitionMode _recognitionMode;
        private AuthorEntityStore _runEntityStore;

        public SimulationBenchmarkService(
            string databasePath,
            AuthorEntityStore entityStore,
            TagCleaningRuleStore tagCleaningStore,
            AuthorRecognitionMode recognitionMode)
        {
            _databasePath = databasePath ?? "";

            _entityStore = entityStore;
            _tagCleaningStore = tagCleaningStore;
            _recognitionMode = recognitionMode;
        }

        public SimulationBenchmarkReport Run(
            SimulationBenchmarkOptions options,
            int maxAuthors,
            string groupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string authorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            Func<bool> cancelRequested,
            Action<SimulationProgressInfo> progress)
        {
            if (options == null) throw new ArgumentNullException("options");
            ThrowIfCanceled(cancelRequested);
            Report(progress, SimulationProgressStage.Initializing, "", 0, 0, "", true);

            Report(progress, SimulationProgressStage.CheckingDatabase, "", 0, 0, _databasePath, true);
            GuiGuiAuthorIndexDatabase database = new GuiGuiAuthorIndexDatabase(_databasePath);
            PublicAuthorIndexInfo info = database.GetInfo();
            if (!info.Exists)
                throw new FileNotFoundException("GuiGuiAuthorIndex.db 不存在。", _databasePath);
            if (!String.IsNullOrWhiteSpace(info.Error))
                throw new InvalidOperationException(info.Error);
            if (info.Artists <= 0)
                throw new InvalidOperationException("GuiGuiAuthorIndex.db 中没有可用于测试的 Artist 数据。");
            Report(progress, SimulationProgressStage.CheckingDatabase, "", (int)Math.Min(Int32.MaxValue, info.Artists), (int)Math.Min(Int32.MaxValue, info.Artists), _databasePath, false);

            // Isolate the benchmark from the real scan session while still
            // sharing one read-only in-memory author identity index between all
            // rounds of this benchmark. Cold mode lets the first Resolve build
            // it inside the measured scan; warm mode builds it explicitly first.
            _runEntityStore = new AuthorEntityStore(
                "",
                _databasePath);

            PublicAuthorIdentityIndexStats preparedIndex = new PublicAuthorIdentityIndexStats();
            if (options.AuthorIndexCacheMode == SimulationAuthorIndexCacheMode.Warm)
            {
                ThrowIfCanceled(cancelRequested);
                Report(progress, SimulationProgressStage.PreparingAuthorIndex, "", 0, 0, _databasePath, true);
                preparedIndex = _runEntityStore.PreparePublicDatabaseIndex();
                if (preparedIndex == null || !preparedIndex.Ready)
                    throw new InvalidOperationException("GuiGuiAuthorIndex.db 内存身份索引构建失败，无法执行预热索引测试。");
                Report(progress, SimulationProgressStage.PreparingAuthorIndex, "",
                    preparedIndex.Artists, Math.Max(1, preparedIndex.Artists), _databasePath, false);
            }

            Stopwatch preparation = Stopwatch.StartNew();
            int requested = Math.Max(1, Math.Min(100000, options.SourceFileCount));
            int uniqueRequested = Math.Max(1, Math.Min(requested, options.UniqueAuthorCount));
            uniqueRequested = (int)Math.Min((long)uniqueRequested, info.Artists);
            Report(progress, SimulationProgressStage.LoadingAuthors, "", 0, uniqueRequested, "", false);
            List<SimulationAuthorSeed> authors = database.LoadSimulationAuthors(uniqueRequested, options.Seed);
            if (authors.Count == 0)
                throw new InvalidOperationException("未能从 GuiGuiAuthorIndex.db 读取模拟作者。");
            Report(progress, SimulationProgressStage.LoadingAuthors, "", authors.Count, uniqueRequested, "", false);

            Random random = new Random(options.Seed);
            List<SourceRecord> sources = BuildSources(authors, requested, options.NameMode, random, cancelRequested, progress);
            maxAuthors = Math.Max(1, maxAuthors);

            string normalizedGroupTemplate;
            string templateError;
            if (!GroupNaming.TryValidateTemplate(groupTemplate, out normalizedGroupTemplate, out templateError))
                normalizedGroupTemplate = GroupNaming.DefaultTemplate;
            string normalizedAuthorFolderTemplate;
            if (!AuthorFolderNaming.TryValidateTemplate(authorFolderTemplate, out normalizedAuthorFolderTemplate, out templateError))
                normalizedAuthorFolderTemplate = AuthorFolderNaming.DefaultTemplate;

            SimulationArchiveEnvironment environment = BuildEnvironment(
                sources,
                options,
                maxAuthors,
                normalizedGroupTemplate,
                normalizedAuthorFolderTemplate,
                cancelRequested,
                progress);
            preparation.Stop();

            SimulationBenchmarkReport report = new SimulationBenchmarkReport();
            report.DatabasePath = _databasePath;
            report.AvailableArtists = info.Artists;
            report.RequestedFiles = requested;
            report.GeneratedFiles = sources.Count;
            report.UniqueAuthors = authors.Count;
            report.AverageFilesPerAuthor = authors.Count > 0 ? (double)sources.Count / authors.Count : 0.0;
            report.ExistingAuthorFolders = environment.ExistingAuthors.Count;
            report.ExistingTargetFiles = environment.ExistingTargetFiles.Count;
            report.Seed = options.Seed;
            report.AuthorIndexCacheMode = options.AuthorIndexCacheMode;
            report.AuthorIndexBuildMs = preparedIndex.BuildMs;
            report.AuthorIndexArtists = preparedIndex.Artists;
            report.AuthorIndexLookupKeys = preparedIndex.LookupKeys;
            report.AuthorIndexIdentityNames = preparedIndex.IdentityNames;
            report.AuthorIndexRelations = preparedIndex.Relations;
            report.DataPreparationMs = preparation.ElapsedMilliseconds;

            List<PlanItem> sourceItems = sources.Select(delegate(SourceRecord x) { return x.Item; }).ToList();
            List<PlanItem> baseline = null;

            if (options.RunMode == SimulationRunMode.FirstScan ||
                options.RunMode == SimulationRunMode.Complete)
            {
                SimulationRoundResult cold = RunCold(
                    sourceItems, environment, maxAuthors, normalizedGroupTemplate,
                    recognizedGroupTemplates, normalizedAuthorFolderTemplate,
                    recognizedAuthorFolderTemplates, cancelRequested, progress,
                    "首次扫描");
                report.Rounds.Add(cold);
                baseline = cold.Plan;
            }

            if (options.RunMode == SimulationRunMode.CachedRescan ||
                options.RunMode == SimulationRunMode.IncrementalScan)
            {
                // Prime an isolated in-memory baseline. This warm-up is not part
                // of the selected round's timing and never touches production caches.
                Report(progress, SimulationProgressStage.PreparingBaseline, "基线准备", 0, sourceItems.Count, "", true);
                baseline = RunCold(
                    sourceItems, environment, maxAuthors, normalizedGroupTemplate,
                    recognizedGroupTemplates, normalizedAuthorFolderTemplate,
                    recognizedAuthorFolderTemplates, cancelRequested, progress,
                    "基线准备").Plan;
            }

            if (options.RunMode == SimulationRunMode.CachedRescan ||
                options.RunMode == SimulationRunMode.Complete)
            {
                if (baseline == null) baseline = new List<PlanItem>();
                report.Rounds.Add(RunCached(sourceItems, baseline, environment, cancelRequested, progress));
            }

            if (options.RunMode == SimulationRunMode.IncrementalScan ||
                options.RunMode == SimulationRunMode.Complete)
            {
                if (baseline == null) baseline = new List<PlanItem>();
                report.Rounds.Add(RunIncremental(
                    sourceItems, baseline, environment, options.IncrementalChangePercent,
                    maxAuthors, normalizedGroupTemplate, recognizedGroupTemplates,
                    normalizedAuthorFolderTemplate, recognizedAuthorFolderTemplates,
                    cancelRequested, progress));
            }

            Report(progress, SimulationProgressStage.Finalizing, "", 0, 0, "", true);
            PublicAuthorIdentityIndexStats finalIndex = _runEntityStore.GetPublicDatabaseIndexStats();
            if (finalIndex != null && finalIndex.Ready)
            {
                report.AuthorIndexBuildMs = finalIndex.BuildMs;
                report.AuthorIndexArtists = finalIndex.Artists;
                report.AuthorIndexLookupKeys = finalIndex.LookupKeys;
                report.AuthorIndexIdentityNames = finalIndex.IdentityNames;
                report.AuthorIndexRelations = finalIndex.Relations;
            }
            Report(progress, SimulationProgressStage.Completed, "", 1, 1, "", false);
            return report;
        }

        private SimulationRoundResult RunCold(
            List<PlanItem> sourceItems,
            SimulationArchiveEnvironment environment,
            int maxAuthors,
            string groupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string authorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            Func<bool> cancelRequested,
            Action<SimulationProgressInfo> progress,
            string name)
        {
            ThrowIfCanceled(cancelRequested);
            ArchiveEngine engine = NewEngine();
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch recognition = Stopwatch.StartNew();
            Report(progress, SimulationProgressStage.RecognizingAndPlanning, name, 0, sourceItems.Count, "", true);
            Action<ScanProgressInfo> engineProgress = delegate(ScanProgressInfo info)
            {
                if (info == null) return;
                Report(progress, SimulationProgressStage.RecognizingAndPlanning, name,
                    info.Current, info.Total, info.CurrentPath, info.Indeterminate);
            };
            List<PlanItem> plan = engine.ResolvePlanVirtual(
                sourceItems,
                @"Z:\GuiGuiSimulation\Archive",
                maxAuthors,
                groupTemplate,
                recognizedGroupTemplates,
                authorFolderTemplate,
                recognizedAuthorFolderTemplates,
                CloneEnvironment(environment),
                engineProgress,
                cancelRequested);
            recognition.Stop();
            Report(progress, SimulationProgressStage.ConflictAnalysis, name, 0, plan.Count, "", true);
            ApplyBatchTargetConflictMarks(plan);
            long organizeMs;
            int moved, skipped;
            SimulateOrganize(plan, environment, out moved, out skipped, out organizeMs, cancelRequested, progress, name);
            total.Stop();

            SimulationRoundResult result = BuildRound(name, plan, environment);
            result.SourceFiles = sourceItems.Count;
            result.CacheHits = 0;
            result.RecalculatedFiles = sourceItems.Count;
            result.IndexChangedFiles = sourceItems.Count;
            result.SimulatedMoves = moved;
            result.SimulatedSkips = skipped;
            result.RecognitionPlanMs = recognition.ElapsedMilliseconds;
            result.OrganizeMs = organizeMs;
            result.TotalMs = total.ElapsedMilliseconds;
            result.Plan = ClonePlan(plan);
            return result;
        }

        private SimulationRoundResult RunCached(
            List<PlanItem> sourceItems,
            List<PlanItem> baseline,
            SimulationArchiveEnvironment environment,
            Func<bool> cancelRequested,
            Action<SimulationProgressInfo> progress)
        {
            ThrowIfCanceled(cancelRequested);
            Stopwatch total = Stopwatch.StartNew();
            Dictionary<string, PlanItem> cache = BuildPlanCache(baseline);
            Stopwatch lookup = Stopwatch.StartNew();
            List<PlanItem> plan = new List<PlanItem>(sourceItems.Count);
            int hits = 0;
            int cacheCurrent = 0;
            Report(progress, SimulationProgressStage.CacheLookup, "缓存扫描", 0, sourceItems.Count, "", false);
            foreach (PlanItem source in sourceItems)
            {
                ThrowIfCanceled(cancelRequested);
                cacheCurrent++;
                PlanItem cached;
                if (cache.TryGetValue(CacheKey(source), out cached))
                {
                    plan.Add(ClonePlanItem(cached));
                    hits++;
                }
                if (ShouldReport(cacheCurrent, sourceItems.Count))
                    Report(progress, SimulationProgressStage.CacheLookup, "缓存扫描", cacheCurrent, sourceItems.Count, source != null ? source.SourcePath : "", false);
            }
            lookup.Stop();
            Report(progress, SimulationProgressStage.ConflictAnalysis, "缓存扫描", 0, plan.Count, "", true);
            ApplyBatchTargetConflictMarks(plan);
            long organizeMs;
            int moved, skipped;
            SimulateOrganize(plan, environment, out moved, out skipped, out organizeMs, cancelRequested, progress, "缓存扫描");
            total.Stop();

            SimulationRoundResult result = BuildRound("缓存扫描", plan, environment);
            result.SourceFiles = sourceItems.Count;
            result.CacheHits = hits;
            result.RecalculatedFiles = sourceItems.Count - hits;
            result.IndexChangedFiles = 0;
            result.SimulatedMoves = moved;
            result.SimulatedSkips = skipped;
            result.RecognitionPlanMs = lookup.ElapsedMilliseconds;
            result.OrganizeMs = organizeMs;
            result.TotalMs = total.ElapsedMilliseconds;
            result.Plan = ClonePlan(plan);
            return result;
        }

        private SimulationRoundResult RunIncremental(
            List<PlanItem> sourceItems,
            List<PlanItem> baseline,
            SimulationArchiveEnvironment environment,
            decimal changePercent,
            int maxAuthors,
            string groupTemplate,
            IEnumerable<string> recognizedGroupTemplates,
            string authorFolderTemplate,
            IEnumerable<string> recognizedAuthorFolderTemplates,
            Func<bool> cancelRequested,
            Action<SimulationProgressInfo> progress)
        {
            ThrowIfCanceled(cancelRequested);
            int changedCount = (int)Math.Ceiling(sourceItems.Count * (double)Math.Max(0M, changePercent) / 100.0);
            changedCount = Math.Max(1, Math.Min(sourceItems.Count, changedCount));

            Dictionary<string, PlanItem> cache = BuildPlanCache(baseline);
            List<PlanItem> changed = new List<PlanItem>();
            HashSet<int> changedIndexes = new HashSet<int>();
            Report(progress, SimulationProgressStage.IncrementalChanges, "增量扫描", 0, changedCount, "", false);
            for (int i = 0; i < changedCount; i++)
            {
                ThrowIfCanceled(cancelRequested);
                int index = (int)((long)i * sourceItems.Count / changedCount);
                if (!changedIndexes.Add(index)) continue;
                changed.Add(CreateRenamedItem(sourceItems[index], i + 1));
                if (ShouldReport(i + 1, changedCount))
                    Report(progress, SimulationProgressStage.IncrementalChanges, "增量扫描", i + 1, changedCount, sourceItems[index].SourcePath, false);
            }
            changedCount = changed.Count;

            ArchiveEngine engine = NewEngine();
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch recognition = Stopwatch.StartNew();
            Report(progress, SimulationProgressStage.RecognizingAndPlanning, "增量扫描", 0, changed.Count, "", true);
            Action<ScanProgressInfo> engineProgress = delegate(ScanProgressInfo info)
            {
                if (info == null) return;
                Report(progress, SimulationProgressStage.RecognizingAndPlanning, "增量扫描",
                    info.Current, info.Total, info.CurrentPath, info.Indeterminate);
            };
            List<PlanItem> changedPlan = engine.ResolvePlanVirtual(
                changed,
                @"Z:\GuiGuiSimulation\Archive",
                maxAuthors,
                groupTemplate,
                recognizedGroupTemplates,
                authorFolderTemplate,
                recognizedAuthorFolderTemplates,
                CloneEnvironment(environment),
                engineProgress,
                cancelRequested);
            recognition.Stop();

            Dictionary<int, PlanItem> changedByOrdinal = new Dictionary<int, PlanItem>();
            for (int i = 0; i < changedPlan.Count; i++)
            {
                int ordinal = ParseOrdinal(changedPlan[i].FileName);
                if (ordinal >= 0) changedByOrdinal[ordinal] = changedPlan[i];
            }

            List<PlanItem> merged = new List<PlanItem>(sourceItems.Count);
            int cacheHits = 0;
            Report(progress, SimulationProgressStage.CacheLookup, "增量扫描", 0, sourceItems.Count, "", false);
            for (int i = 0; i < sourceItems.Count; i++)
            {
                ThrowIfCanceled(cancelRequested);
                if (changedIndexes.Contains(i))
                {
                    PlanItem changedItem;
                    if (changedByOrdinal.TryGetValue(i + 1, out changedItem))
                        merged.Add(ClonePlanItem(changedItem));
                    continue;
                }
                PlanItem cached;
                if (cache.TryGetValue(CacheKey(sourceItems[i]), out cached))
                {
                    merged.Add(ClonePlanItem(cached));
                    cacheHits++;
                }
                if (ShouldReport(i + 1, sourceItems.Count))
                    Report(progress, SimulationProgressStage.CacheLookup, "增量扫描", i + 1, sourceItems.Count, sourceItems[i].SourcePath, false);
            }

            Report(progress, SimulationProgressStage.ConflictAnalysis, "增量扫描", 0, merged.Count, "", true);
            ApplyBatchTargetConflictMarks(merged);
            long organizeMs;
            int moved, skipped;
            SimulateOrganize(merged, environment, out moved, out skipped, out organizeMs, cancelRequested, progress, "增量扫描");
            total.Stop();

            SimulationRoundResult result = BuildRound("增量扫描", merged, environment);
            result.SourceFiles = sourceItems.Count;
            result.CacheHits = cacheHits;
            result.RecalculatedFiles = changedCount;
            result.IndexChangedFiles = changedCount;
            result.SimulatedMoves = moved;
            result.SimulatedSkips = skipped;
            result.RecognitionPlanMs = recognition.ElapsedMilliseconds;
            result.OrganizeMs = organizeMs;
            result.TotalMs = total.ElapsedMilliseconds;
            result.Plan = ClonePlan(merged);
            return result;
        }

        private ArchiveEngine NewEngine()
        {
            ArchiveEngine engine = new ArchiveEngine(_runEntityStore ?? _entityStore, _tagCleaningStore);
            engine.RecognitionMode = _recognitionMode;
            // Deliberately do not attach FileIndex/Parsed/Recognition/Destination
            // production caches. The benchmark owns its isolated in-memory state.
            return engine;
        }

        private static List<SourceRecord> BuildSources(
            IList<SimulationAuthorSeed> authors,
            int sourceFileCount,
            SimulationNameMode mode,
            Random random,
            Func<bool> cancelRequested,
            Action<SimulationProgressInfo> progress)
        {
            List<SourceRecord> result = new List<SourceRecord>();
            int total = Math.Max(0, sourceFileCount);
            if (authors == null || authors.Count == 0 || total <= 0) return result;
            Report(progress, SimulationProgressStage.GeneratingSources, "", 0, total, "", false);
            for (int i = 0; i < total; i++)
            {
                ThrowIfCanceled(cancelRequested);
                int ordinal = i + 1;
                // Cycle through the deterministic unique-author sample. This
                // models a real library where one author commonly owns multiple
                // files instead of forcing one new author for every source file.
                SimulationAuthorSeed author = authors[i % authors.Count];
                string name = ChooseName(author, mode, random);
                string fileName = "[" + name + "] Simulation " + ordinal.ToString("D5") + " [DL版].zip";
                PlanItem item = new PlanItem();
                item.FileName = fileName;
                item.SourcePath = @"Z:\GuiGuiSimulation\Source\" + fileName;
                item.LastWriteTime = new DateTime(2026, 1, 1).AddSeconds(ordinal);
                item.FileSize = 1024L * 1024L + ordinal;
                result.Add(new SourceRecord { Author = author, Item = item });
                if (ShouldReport(ordinal, total))
                    Report(progress, SimulationProgressStage.GeneratingSources, "", ordinal, total, item.SourcePath, false);
            }
            return result;
        }

        private static string ChooseName(SimulationAuthorSeed author, SimulationNameMode mode, Random random)
        {
            if (author == null) return "Unknown";
            if (mode == SimulationNameMode.Roman && !String.IsNullOrWhiteSpace(author.RomanName))
                return author.RomanName;
            if (mode == SimulationNameMode.Alias && !String.IsNullOrWhiteSpace(author.Alias))
                return author.Alias;
            if (mode == SimulationNameMode.Mixed)
            {
                List<string> names = new List<string>();
                AddUnique(names, author.CanonicalName);
                AddUnique(names, author.RomanName);
                AddUnique(names, author.Alias);
                if (names.Count > 0) return names[random.Next(names.Count)];
            }
            return !String.IsNullOrWhiteSpace(author.CanonicalName)
                ? author.CanonicalName
                : !String.IsNullOrWhiteSpace(author.RomanName)
                    ? author.RomanName
                    : author.Alias;
        }

        private static SimulationArchiveEnvironment BuildEnvironment(
            IList<SourceRecord> sources,
            SimulationBenchmarkOptions options,
            int maxAuthors,
            string groupTemplate,
            string authorFolderTemplate,
            Func<bool> cancelRequested,
            Action<SimulationProgressInfo> progress)
        {
            SimulationArchiveEnvironment environment = new SimulationArchiveEnvironment();
            List<SourceRecord> uniqueSources = (sources ?? new List<SourceRecord>())
                .Where(delegate(SourceRecord x) { return x != null && x.Author != null; })
                .GroupBy(delegate(SourceRecord x) { return x.Author.Id; })
                .Select(delegate(IGrouping<int, SourceRecord> g) { return g.First(); })
                .ToList();
            int existingAuthorCount = (int)Math.Round(uniqueSources.Count * Math.Max(0, Math.Min(100, options.ExistingAuthorPercent)) / 100.0);
            existingAuthorCount = Math.Max(0, Math.Min(uniqueSources.Count, existingAuthorCount));

            Dictionary<int, string> authorPathById = new Dictionary<int, string>();
            int targetFileGoal = Math.Max(0, Math.Min(100000, options.ExistingTargetFileCount));
            int environmentTotal = existingAuthorCount + targetFileGoal;
            Report(progress, SimulationProgressStage.BuildingTargetEnvironment, "", 0, environmentTotal, "", environmentTotal <= 0);
            for (int i = 0; i < existingAuthorCount; i++)
            {
                ThrowIfCanceled(cancelRequested);
                SimulationAuthorSeed seed = uniqueSources[i].Author;
                int groupNumber = (i / maxAuthors) + 1;
                string groupName = GroupNaming.Render(groupTemplate, groupNumber);
                string groupPath = Path.Combine(@"Z:\GuiGuiSimulation\Archive", groupName);
                environment.Groups[groupNumber] = groupPath;
                string folderName = AuthorFolderNaming.Render(authorFolderTemplate, seed.CanonicalName);
                string authorPath = Path.Combine(groupPath, folderName);
                AuthorFolder folder = new AuthorFolder();
                folder.GroupNumber = groupNumber;
                folder.AuthorName = seed.CanonicalName;
                folder.AuthorPath = authorPath;
                folder.PreferredIdentity = AuthorRules.GetPreferredAuthorIdentity(seed.CanonicalName);
                ArchiveEngine.IndexAuthorFolderName(folder, seed.CanonicalName);
                environment.ExistingAuthors.Add(folder);
                authorPathById[seed.Id] = authorPath;
                if (ShouldReport(i + 1, environmentTotal))
                    Report(progress, SimulationProgressStage.BuildingTargetEnvironment, "", i + 1, environmentTotal, authorPath, false);
            }

            int requestedTargetFiles = targetFileGoal;
            int created = 0;
            if (options.UseSameNameTargetFiles)
            {
                foreach (SourceRecord source in sources)
                {
                    ThrowIfCanceled(cancelRequested);
                    if (created >= requestedTargetFiles) break;
                    string authorPath;
                    if (!authorPathById.TryGetValue(source.Author.Id, out authorPath)) continue;
                    string targetPath = Path.Combine(authorPath, source.Item.FileName);
                    environment.ExistingTargetFiles.Add(targetPath);
                    created++;
                    int current = existingAuthorCount + created;
                    if (ShouldReport(current, environmentTotal))
                        Report(progress, SimulationProgressStage.BuildingTargetEnvironment, "", current, environmentTotal, targetPath, false);
                }
            }

            string fallbackRoot = environment.Groups.Count > 0
                ? environment.Groups.Values.First()
                : @"Z:\GuiGuiSimulation\Archive";
            while (created < requestedTargetFiles)
            {
                ThrowIfCanceled(cancelRequested);
                string targetPath = Path.Combine(fallbackRoot, "Existing Simulation " + (created + 1).ToString("D5") + ".zip");
                environment.ExistingTargetFiles.Add(targetPath);
                created++;
                int current = existingAuthorCount + created;
                if (ShouldReport(current, environmentTotal))
                    Report(progress, SimulationProgressStage.BuildingTargetEnvironment, "", current, environmentTotal, targetPath, false);
            }
            return environment;
        }

        private static SimulationArchiveEnvironment CloneEnvironment(SimulationArchiveEnvironment source)
        {
            SimulationArchiveEnvironment clone = new SimulationArchiveEnvironment();
            if (source == null) return clone;
            clone.ExistingAuthors = new List<AuthorFolder>(source.ExistingAuthors ?? new List<AuthorFolder>());
            clone.Groups = source.Groups != null
                ? new SortedDictionary<int, string>(source.Groups)
                : new SortedDictionary<int, string>();
            clone.ExistingTargetFiles = source.ExistingTargetFiles != null
                ? new HashSet<string>(source.ExistingTargetFiles, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return clone;
        }

        private static void SimulateOrganize(
            IList<PlanItem> plan,
            SimulationArchiveEnvironment environment,
            out int moved,
            out int skipped,
            out long elapsedMs,
            Func<bool> cancelRequested,
            Action<SimulationProgressInfo> progress,
            string roundName)
        {
            HashSet<string> virtualFiles = environment != null && environment.ExistingTargetFiles != null
                ? new HashSet<string>(environment.ExistingTargetFiles, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            moved = 0;
            skipped = 0;
            Stopwatch sw = Stopwatch.StartNew();
            IList<PlanItem> items = plan ?? new List<PlanItem>();
            int current = 0;
            Report(progress, SimulationProgressStage.SimulatedOrganize, roundName, 0, items.Count, "", items.Count <= 0);
            foreach (PlanItem item in items)
            {
                ThrowIfCanceled(cancelRequested);
                current++;
                if (ShouldReport(current, items.Count))
                    Report(progress, SimulationProgressStage.SimulatedOrganize, roundName, current, items.Count, item != null ? item.TargetPath : "", false);
                if (item == null || !item.CanMove || String.IsNullOrWhiteSpace(item.TargetPath))
                {
                    skipped++;
                    continue;
                }
                if (!virtualFiles.Add(item.TargetPath))
                {
                    skipped++;
                    continue;
                }
                moved++;
            }
            // Items skipped via continue still need a final progress update.
            Report(progress, SimulationProgressStage.SimulatedOrganize, roundName, items.Count, items.Count, "", false);
            sw.Stop();
            elapsedMs = sw.ElapsedMilliseconds;
        }

        private static SimulationRoundResult BuildRound(
            string name,
            IList<PlanItem> plan,
            SimulationArchiveEnvironment environment)
        {
            SimulationRoundResult result = new SimulationRoundResult();
            result.Name = name ?? "";
            HashSet<string> existingAuthorPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (environment != null && environment.ExistingAuthors != null)
            {
                foreach (AuthorFolder folder in environment.ExistingAuthors)
                    if (folder != null && !String.IsNullOrWhiteSpace(folder.AuthorPath))
                        existingAuthorPaths.Add(folder.AuthorPath);
            }

            foreach (PlanItem item in plan ?? new List<PlanItem>())
            {
                if (item == null) continue;

                bool ambiguous = item.StatusCode == PlanStatusCode.Ambiguous ||
                    item.StatusCode == PlanStatusCode.CandidateConfirmation;
                bool unrecognized = item.StatusCode == PlanStatusCode.Unrecognized;
                if (ambiguous) result.Ambiguous++;
                else if (unrecognized) result.Unrecognized++;
                else if (!String.IsNullOrWhiteSpace(item.MatchedAs))
                {
                    if (existingAuthorPaths.Contains(item.TargetDir ?? "")) result.Matched++;
                    else result.NewAuthors++;
                }

                if (item.StatusCode == PlanStatusCode.TargetExists) result.TargetConflicts++;
                if (item.StatusCode == PlanStatusCode.BatchTargetConflict) result.BatchConflicts++;
            }
            return result;
        }

        private static void ApplyBatchTargetConflictMarks(IList<PlanItem> plan)
        {
            ExecutionSafety.ApplyBatchTargetConflictMarks(plan);
        }

        private static Dictionary<string, PlanItem> BuildPlanCache(IEnumerable<PlanItem> plan)
        {
            Dictionary<string, PlanItem> cache = new Dictionary<string, PlanItem>(StringComparer.OrdinalIgnoreCase);
            foreach (PlanItem item in plan ?? new List<PlanItem>())
                if (item != null) cache[CacheKey(item)] = ClonePlanItem(item);
            return cache;
        }

        private static string CacheKey(PlanItem item)
        {
            if (item == null) return "";
            return (item.SourcePath ?? "") + "|" + (item.FileName ?? "");
        }

        private static PlanItem CreateRenamedItem(PlanItem source, int mutation)
        {
            PlanItem result = new PlanItem();
            string baseName = Path.GetFileNameWithoutExtension(source.FileName ?? "");
            string extension = Path.GetExtension(source.FileName ?? "");
            result.FileName = baseName + " [Revision " + mutation.ToString("D3") + "]" + extension;
            result.SourcePath = @"Z:\GuiGuiSimulation\Source\" + result.FileName;
            result.LastWriteTime = source.LastWriteTime.AddMinutes(1);
            result.FileSize = source.FileSize + mutation;
            return result;
        }

        private static int ParseOrdinal(string fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName)) return -1;
            int marker = fileName.IndexOf("Simulation ", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return -1;
            marker += "Simulation ".Length;
            int end = marker;
            while (end < fileName.Length && Char.IsDigit(fileName[end])) end++;
            int ordinal;
            if (!Int32.TryParse(fileName.Substring(marker, end - marker), out ordinal)) return -1;
            return ordinal;
        }

        private static List<PlanItem> ClonePlan(IEnumerable<PlanItem> plan)
        {
            return (plan ?? new List<PlanItem>()).Select(ClonePlanItem).ToList();
        }

        private static PlanItem ClonePlanItem(PlanItem source)
        {
            if (source == null) return new PlanItem();
            PlanItem p = new PlanItem();
            p.FileId = source.FileId;
            p.FileName = source.FileName ?? "";
            p.SourcePath = source.SourcePath ?? "";
            p.Author = source.Author ?? "";
            p.MatchedAs = source.MatchedAs ?? "";
            p.MatchWhy = source.MatchWhy ?? "";
            p.TargetDir = source.TargetDir ?? "";
            p.TargetPath = source.TargetPath ?? "";
            p.Status = source.Status ?? "";
            p.CanMove = source.CanMove;
            p.ManualTargetDir = source.ManualTargetDir ?? "";
            p.ManualTargetName = source.ManualTargetName ?? "";
            p.ManualTargetAuthor = source.ManualTargetAuthor ?? "";
            p.CandidatePaths = new List<string>(source.CandidatePaths ?? new List<string>());
            p.CandidateNames = new List<string>(source.CandidateNames ?? new List<string>());
            p.CandidateIsPlanned = new List<bool>(source.CandidateIsPlanned ?? new List<bool>());
            p.LastWriteTime = source.LastWriteTime;
            p.FileSize = source.FileSize;
            p.RecognitionScore = source.RecognitionScore;
            p.RecognitionRunnerUpScore = source.RecognitionRunnerUpScore;
            p.StatusCode = source.StatusCode;
            p.EvidenceKind = source.EvidenceKind;
            p.StatusArgument = source.StatusArgument ?? "";
            p.PlanConflictKind = source.PlanConflictKind ?? "";
            p.ConflictTargetPath = source.ConflictTargetPath ?? "";
            p.ConflictSourcePaths = new List<string>(source.ConflictSourcePaths ?? new List<string>());
            p.CanMoveBeforePlanConflict = source.CanMoveBeforePlanConflict;
            p.StatusBeforePlanConflict = source.StatusBeforePlanConflict ?? "";
            p.StatusCodeBeforePlanConflict = source.StatusCodeBeforePlanConflict;
            return p;
        }

        private static void AddUnique(List<string> items, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            foreach (string item in items)
                if (String.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return;
            items.Add(value);
        }

        private static bool ShouldReport(int current, int total)
        {
            return current <= 1 || current == total || (current % 64) == 0;
        }

        private static void Report(
            Action<SimulationProgressInfo> progress,
            SimulationProgressStage stage,
            string roundName,
            int current,
            int total,
            string currentItem,
            bool indeterminate)
        {
            if (progress == null) return;
            SimulationProgressInfo info = new SimulationProgressInfo();
            info.Stage = stage;
            info.RoundName = roundName ?? "";
            info.Current = Math.Max(0, current);
            info.Total = Math.Max(0, total);
            info.CurrentItem = currentItem ?? "";
            info.Indeterminate = indeterminate || total <= 0;
            progress(info);
        }

        private static void ThrowIfCanceled(Func<bool> cancelRequested)
        {
            if (cancelRequested != null && cancelRequested())
                throw new OperationCanceledException();
        }
    }
}
