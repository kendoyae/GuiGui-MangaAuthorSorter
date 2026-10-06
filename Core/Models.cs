using System;
using System.Collections.Generic;

namespace MangaAuthorSorter
{
    internal sealed class PlanItem
    {
        public string FileName = "";
        public string SourcePath = "";
        public string Author = "";
        public string MatchedAs = "";
        public string MatchWhy = "";
        public string TargetDir = "";
        public string TargetPath = "";
        public string Status = "";
        public bool CanMove;
        public string ManualTargetDir = "";
        public string ManualTargetName = "";
        public string ManualTargetAuthor = "";
        public List<string> CandidatePaths = new List<string>();
        public List<string> CandidateNames = new List<string>();
        public List<bool> CandidateIsPlanned = new List<bool>();
        public DateTime LastWriteTime;
        public long FileSize = -1;

        // Populated after the archive plan is built. A batch conflict means
        // multiple source files resolve to the same final target path.
        public string PlanConflictKind = "";
        public string ConflictTargetPath = "";
        public List<string> ConflictSourcePaths = new List<string>();
        public bool CanMoveBeforePlanConflict;
        public string StatusBeforePlanConflict = "";

        // V1.11.6 display-only row for a global scan exclusion.
        // These items are never stored in the executable archive plan.
        public bool IsExcludedPreview;
        public string ExclusionRuleName = "";
        public string ExclusionScope = "";
        public bool ExclusionIsDirectory;
    }

    internal sealed class AliasGroup
    {
        public string Canonical = "";
        public List<string> Names = new List<string>();
        public HashSet<string> Norms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class AuthorFolder
    {
        public int GroupNumber;
        public string AuthorName = "";
        public string AuthorPath = "";
        public string PreferredIdentity = "";
        public HashSet<string> Norms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CautiousNorms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Role-aware identity indexes used by V1.10.26 structured matching.
        // DirectNorms stores the whole folder identity; Society/Creator norms
        // store explicit parts such as "circle (author)"; SegmentNorms stores
        // conservative whitespace-separated Japanese/CJK name segments.
        public HashSet<string> DirectNorms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SocietyNorms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CreatorNorms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SegmentNorms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal enum AuthorMatchType { Matched, NotFound, Ambiguous, Choice }

    internal sealed class AuthorMatchResult
    {
        public AuthorMatchType Type;
        public AuthorFolder Folder;
        public string Why = "";
        public List<AuthorFolder> Candidates = new List<AuthorFolder>();
        public string Society = "";
        public string Creator = "";
    }

    internal sealed class CompositeParts
    {
        public string Society = "";
        public string Creator = "";
    }

    internal sealed class StructuredAuthorParts
    {
        public string Society = "";
        public List<string> Creators = new List<string>();
    }

    internal sealed class ScanExcludedItem
    {
        public string Path = "";
        public string DisplayName = "";
        public string RuleName = "";
        public string Scope = "";
        public bool IsDirectory;
    }

    internal sealed class SearchResult
    {
        public List<System.IO.FileInfo> Files = new List<System.IO.FileInfo>();
        public List<ScanExcludedItem> ExcludedItems = new List<ScanExcludedItem>();
        public int DiscoveredCount;
        public string Backend = "FileSystem";
        public string Detail = "";
    }
    internal enum ScanProgressStage
    {
        Searching,
        Planning,
        OnlineResolving,
        Rebuilding,
        Filtering
    }

    internal sealed class ScanProgressInfo
    {
        public ScanProgressStage Stage;
        public int Current;
        public int Total;
        public string CurrentPath = "";
        public bool Indeterminate;
        public int Checked;
    }

}
