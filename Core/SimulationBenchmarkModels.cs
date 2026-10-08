using System;
using System.Collections.Generic;

namespace MangaAuthorSorter
{
    internal enum SimulationNameMode
    {
        Canonical,
        Roman,
        Alias,
        Mixed
    }

    internal enum SimulationRunMode
    {
        FirstScan,
        CachedRescan,
        IncrementalScan,
        Complete
    }

    internal enum SimulationAuthorIndexCacheMode
    {
        Cold,
        Warm
    }


    internal enum SimulationProgressStage
    {
        Initializing,
        CheckingDatabase,
        LoadingAuthors,
        PreparingAuthorIndex,
        GeneratingSources,
        BuildingTargetEnvironment,
        PreparingBaseline,
        RecognizingAndPlanning,
        CacheLookup,
        IncrementalChanges,
        ConflictAnalysis,
        SimulatedOrganize,
        Finalizing,
        Completed,
        Canceling,
        Canceled,
        Failed
    }

    internal sealed class SimulationProgressInfo
    {
        public SimulationProgressStage Stage;
        public string RoundName = "";
        public int Current;
        public int Total;
        public string CurrentItem = "";
        public bool Indeterminate;
    }

    internal sealed class SimulationAuthorSeed
    {
        public int Id;
        public string CanonicalName = "";
        public string RomanName = "";
        public string Alias = "";
    }

    internal sealed class SimulationBenchmarkOptions
    {
        public int SourceFileCount = 1500;
        public int UniqueAuthorCount = 300;
        public int ExistingAuthorPercent = 80;
        public int ExistingTargetFileCount = 150;
        public bool UseSameNameTargetFiles = true;
        public decimal IncrementalChangePercent = 1M;
        public int Seed = 20261008;
        public SimulationNameMode NameMode = SimulationNameMode.Mixed;
        public SimulationRunMode RunMode = SimulationRunMode.Complete;
        public SimulationAuthorIndexCacheMode AuthorIndexCacheMode = SimulationAuthorIndexCacheMode.Warm;
    }

    internal sealed class SimulationArchiveEnvironment
    {
        public List<AuthorFolder> ExistingAuthors = new List<AuthorFolder>();
        public SortedDictionary<int, string> Groups = new SortedDictionary<int, string>();
        public HashSet<string> ExistingTargetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class SimulationRoundResult
    {
        public string Name = "";
        public int SourceFiles;
        public int CacheHits;
        public int RecalculatedFiles;
        public int IndexChangedFiles;
        public int Matched;
        public int NewAuthors;
        public int Ambiguous;
        public int Unrecognized;
        public int TargetConflicts;
        public int BatchConflicts;
        public int SimulatedMoves;
        public int SimulatedSkips;
        public long RecognitionPlanMs;
        public long OrganizeMs;
        public long TotalMs;
        public List<PlanItem> Plan = new List<PlanItem>();
    }

    internal sealed class SimulationBenchmarkReport
    {
        public DateTime Time = DateTime.Now;
        public string DatabasePath = "";
        public long AvailableArtists;
        public int RequestedFiles;
        public int GeneratedFiles;
        public int UniqueAuthors;
        public double AverageFilesPerAuthor;
        public int ExistingAuthorFolders;
        public int ExistingTargetFiles;
        public int Seed;
        public SimulationAuthorIndexCacheMode AuthorIndexCacheMode;
        public long AuthorIndexBuildMs;
        public int AuthorIndexArtists;
        public int AuthorIndexLookupKeys;
        public int AuthorIndexIdentityNames;
        public int AuthorIndexRelations;
        public long DataPreparationMs;
        public List<SimulationRoundResult> Rounds = new List<SimulationRoundResult>();
    }
}
