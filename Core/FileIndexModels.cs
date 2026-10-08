using System;
using System.Collections.Generic;

namespace MangaAuthorSorter
{
    internal interface ICacheLayerDiagnostics
    {
        long HitCount { get; }
        long MissCount { get; }
    }

    internal sealed class CacheDiagnosticsSnapshot
    {
        public long ParsedHits;
        public long ParsedMisses;
        public long RecognitionHits;
        public long RecognitionMisses;
        public long DestinationHits;
        public long DestinationMisses;
    }
    internal enum PersistentCacheState
    {
        Unknown,
        Active,
        Clean,
        NeedsValidation,
        Rebuilding
    }

    internal sealed class FileIndexVersions
    {
        public int SchemaVersion = 8;
        public string ParserVersion = "2";
        public string AliasVersion = "";
        public string EntityVersion = "";
        public string RecognitionRuleVersion = "1";
        public string TagCleaningVersion = "";
        public string ExclusionRuleVersion = "";
        public string FileTypeProfileVersion = "";
        public string LocalDatabaseVersion = "";
    }

    internal sealed class SourceIndexFileSnapshot
    {
        public string FullPath = "";
        public string DirectoryPath = "";
        public string FileName = "";
        public string Extension = "";
        public long FileSize = -1;
        public DateTime LastWriteTimeUtc = DateTime.MinValue;
        public DateTime CreationTimeUtc = DateTime.MinValue;
    }

    internal sealed class SourceFileIndexEntry
    {
        public long FileId = 0;
        public long RootId;
        public string FullPath = "";
        public string RelativePath = "";
        public string DirectoryPath = "";
        public string FileName = "";
        public string Extension = "";
        public long FileSize = -1;
        public DateTime LastWriteTimeUtc;
        public DateTime CreationTimeUtc;
    }

    internal sealed class ScanSessionSnapshot
    {
        public long SessionId;
        public long RootId;
        public Dictionary<string, long> FileIdsByPath =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public HashSet<long> FileIds = new HashSet<long>();
    }

    internal sealed class ParsedMetadataCacheEntry
    {
        public long FileId = 0;
        public string FullPath = "";
        public string AuthorCandidate = "";
        public string GroupCandidate = "";
        public string Title = "";
        public string EventName = "";
        public string DetectedTags = "";
        public string NormalizedFileName = "";
        public string ParserVersion = "";
        public string TagCleaningVersion = "";
        public string InputFingerprint = "";
    }

    internal interface IParsedMetadataCache
    {
        bool TryGetAuthorCandidates(string fullPath, string fileName, out List<string> candidates);
        void StoreAuthorCandidates(string fullPath, string fileName, IEnumerable<string> candidates);
        void Invalidate(string fullPath);
        void Flush();
    }

    internal sealed class RecognitionCacheEntry
    {
        public long FileId = 0;
        public string FullPath = "";
        public int EntityId = 0;
        public string MatchedAuthor = "";
        public string MatchedGroup = "";
        public int Confidence = 0;
        public string RecognitionStatus = "";
        public string RecognitionSource = "";
        public string RecognitionReason = "";
        public string AliasVersion = "";
        public string EntityVersion = "";
        public string RecognitionRuleVersion = "";
        public string ExclusionRuleVersion = "";
        public string LocalDatabaseVersion = "";
        public string InputFingerprint = "";
        public string EvidenceKind = "";
        public int RunnerUpConfidence = 0;
        public string UpdatedUtc = "";
    }

    internal interface IRecognitionFactCache
    {
        bool TryGet(string fullPath, string fileName, out RecognitionCacheEntry entry);
        void Store(PlanItem item);
        void Invalidate(string fullPath);
        void Flush();
    }

    internal sealed class DestinationFolderIndexEntry
    {
        public long FolderId = 0;
        public long RootId = 0;
        public long ParentFolderId = 0;
        public string FullPath = "";
        public string RelativePath = "";
        public string FolderName = "";
        public string NormalizedFolderName = "";
        public int GroupNumber = 0;
        public string LogicalAuthor = "";
        public bool IsGroup;
    }

    internal interface IDestinationIndexCache
    {
        bool TryLoad(
            string root,
            string cacheKey,
            out List<DestinationFolderIndexEntry> folders);
        void Store(
            string root,
            string cacheKey,
            IEnumerable<KeyValuePair<int, string>> groups,
            IEnumerable<AuthorFolder> authors);
        void Invalidate(string root);
    }

    internal sealed class DestinationFileIndexEntry
    {
        public long FileId = 0;
        public long RootId = 0;
        public long FolderId = 0;
        public string FullPath = "";
        public string FileName = "";
        public string NormalizedFileName = "";
        public string Extension = "";
        public long FileSize = -1;
        public DateTime LastWriteTimeUtc = DateTime.MinValue;
    }

    internal enum FileIndexPathChangeKind
    {
        Moved,
        Renamed
    }

    internal sealed class FileIndexPathChange
    {
        public FileIndexPathChangeKind Kind;
        public long FileId;
        public string OldPath = "";
        public string NewPath = "";
    }

    internal sealed class FileIndexDelta
    {
        public int CacheHits;
        public int Added;
        public int Removed;
        public int Modified;
        public int Moved;
        public int Renamed;
        public List<string> ChangedPaths = new List<string>();
        public List<string> PlanInvalidatedPaths = new List<string>();
        public List<string> RecognitionInvalidatedPaths = new List<string>();
        public List<FileIndexPathChange> PathChanges = new List<FileIndexPathChange>();
    }

    internal sealed class FileIndexRootStatus
    {
        public long RootId = 0;
        public string RootPath = "";
        public string Provider = "";
        public DateTime LastSyncTimeUtc = DateTime.MinValue;
        public PersistentCacheState State = PersistentCacheState.Unknown;
        public int FileCount = 0;
        public int FolderCount = 0;
    }
}
