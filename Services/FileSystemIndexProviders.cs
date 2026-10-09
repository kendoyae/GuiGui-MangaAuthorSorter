using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MangaAuthorSorter
{
    internal enum FileSystemIndexProviderMode
    {
        Auto,
        Everything,
        Windows
    }

    /// <summary>
    /// The only file-discovery contract used by scan orchestration.  Provider
    /// implementations discover filesystem state; parsing and recognition stay
    /// in the business layer.
    /// </summary>
    internal interface IFileSystemIndexProvider
    {
        string ProviderId { get; }
        bool IsAvailable { get; }

        SearchResult SearchFiles(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested);
    }

    internal interface IFileSystemSnapshotVersionProvider
    {
        long SnapshotRevision { get; }
    }

    internal interface IFileSystemScopedSnapshotVersionProvider
    {
        long GetSnapshotRevision(string source, bool recursive, HashSet<string> blockedPaths, IEnumerable<string> extensions);
    }

    internal interface IFileSystemIndexRefreshProvider
    {
        SearchResult SearchFilesAfterChange(string source, bool recursive, int scanLimit,
            HashSet<string> blockedPaths, IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress, Func<bool> cancelRequested);
        SearchResult ValidateDiscoveryScope(SearchResult result, string source, bool recursive,
            HashSet<string> blockedPaths, IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress, Func<bool> cancelRequested);
    }

    /// <summary>
    /// Version of provider-side filtering/index semantics that are not explicit
    /// SearchFiles arguments (for example the scan-exclusion rule file).
    /// PersistentSourceIndexProvider includes this in its canonical snapshot key
    /// so a settings change reprojects/reconciles instead of serving stale rows.
    /// </summary>
    internal interface IFileSystemIndexConfigurationVersionProvider
    {
        string IndexConfigurationVersion { get; }
    }

    internal interface IFileSystemIndexMutationSink
    {
        void RegisterExpectedMutation(params string[] paths);
        void CancelExpectedMutation(params string[] paths);
        void ApplySuccessfulMove(string sourcePath, string targetPath);
        void ApplySuccessfulRename(string oldPath, string newPath);
    }

    internal interface IScanSessionProvider
    {
        ScanSessionSnapshot CurrentScanSession { get; }
        SearchResult StartScanSession(
            string sourceRoot,
            SearchResult completeIndex,
            int scanLimit);
    }

    internal sealed class EverythingProvider : IFileSystemIndexProvider,
        IFileSystemIndexConfigurationVersionProvider
    {
        private readonly EverythingService _service;

        public EverythingProvider(EverythingService service)
        {
            if (service == null) throw new ArgumentNullException("service");
            _service = service;
        }

        public string ProviderId { get { return "Everything"; } }
        public string IndexConfigurationVersion
        {
            get { return _service.IndexConfigurationVersion; }
        }

        public bool IsAvailable
        {
            get
            {
                return ScanPerformanceDiagnostics.EverythingEnabled &&
                    _service.IsEverythingRunning();
            }
        }

        public SearchResult SearchFiles(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            return _service.SearchFiles(
                source, recursive, scanLimit, blockedPaths, extensions,
                progress, cancelRequested);
        }
    }

    internal sealed class NativeFileSystemProvider : IFileSystemIndexProvider,
        IFileSystemIndexConfigurationVersionProvider
    {
        private readonly EverythingService _service;

        public NativeFileSystemProvider(EverythingService service)
        {
            if (service == null) throw new ArgumentNullException("service");
            _service = service;
        }

        public string ProviderId { get { return "Windows"; } }
        public string IndexConfigurationVersion
        {
            get { return _service.IndexConfigurationVersion; }
        }
        public bool IsAvailable { get { return true; } }

        public SearchResult SearchFiles(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            return _service.SearchFilesNative(
                source, recursive, scanLimit, blockedPaths, extensions,
                progress, cancelRequested);
        }
    }

    /// <summary>
    /// Selects a provider without leaking that decision into scan, cache, or UI
    /// business logic. Auto always has a Windows fallback.
    /// </summary>
    internal sealed class FileSystemIndexProviderRouter : IFileSystemIndexProvider,
        IFileSystemIndexConfigurationVersionProvider, IFileSystemIndexRefreshProvider
    {
        private readonly IFileSystemIndexProvider _everything;
        private readonly IFileSystemIndexProvider _native;

        public FileSystemIndexProviderRouter(
            IFileSystemIndexProvider everything,
            IFileSystemIndexProvider nativeProvider)
        {
            if (everything == null) throw new ArgumentNullException("everything");
            if (nativeProvider == null) throw new ArgumentNullException("nativeProvider");
            _everything = everything;
            _native = nativeProvider;
        }

        public FileSystemIndexProviderMode Mode { get; set; }

        public string ProviderId
        {
            get { return Select().ProviderId; }
        }

        public bool IsAvailable { get { return Select().IsAvailable; } }
        public string IndexConfigurationVersion
        {
            get
            {
                IFileSystemIndexConfigurationVersionProvider versioned =
                    Select() as IFileSystemIndexConfigurationVersionProvider;
                return versioned != null ? versioned.IndexConfigurationVersion : "";
            }
        }

        public SearchResult SearchFiles(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            IFileSystemIndexProvider selected = Select();
            // EverythingService already performs a guarded SDK query and an
            // automatic native fallback. Keeping that recovery at the provider
            // boundary also preserves the original diagnostic detail.
            return selected.SearchFiles(
                source, recursive, scanLimit, blockedPaths, extensions,
                progress, cancelRequested);
        }

        public SearchResult SearchFilesAfterChange(string source, bool recursive, int scanLimit,
            HashSet<string> blockedPaths, IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress, Func<bool> cancelRequested)
        {
            // A watcher already proved the live filesystem changed. Everything
            // may not have indexed that event yet; validate this round natively.
            return _native.SearchFiles(source, recursive, scanLimit, blockedPaths, extensions, progress, cancelRequested);
        }

        public SearchResult ValidateDiscoveryScope(SearchResult result, string source, bool recursive,
            HashSet<string> blockedPaths, IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress, Func<bool> cancelRequested)
        {
            if (result == null || result.Backend != "Everything SDK") return result;
            System.Diagnostics.Stopwatch checkTimer = System.Diagnostics.Stopwatch.StartNew();
            bool complete = false;
            try
            {
                // Names only: do not reread size/time for every indexed file.
                // A newly created directory can exist before Everything finishes
                // indexing it, even without any event in this process's watcher.
                HashSet<string> remaining = new HashSet<string>(
                    (result.IndexEntries ?? new List<SourceIndexFileSnapshot>()).Select(x => x.FullPath),
                    StringComparer.OrdinalIgnoreCase);
                HashSet<string> allowed = new HashSet<string>(FileTypeRules.NormalizeExtensions(extensions), StringComparer.OrdinalIgnoreCase);
                result.DiscoverySetMs = checkTimer.ElapsedMilliseconds;
                long enumerateTicks = 0, compareTicks = 0;
                complete = true;
                using (IEnumerator<string> paths = ScopeFileNames.Enumerate(source, recursive, allowed, cancelRequested).GetEnumerator())
                {
                    while (true)
                    {
                        long start = System.Diagnostics.Stopwatch.GetTimestamp();
                        bool hasNext = paths.MoveNext();
                        enumerateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                        if (!hasNext) break;
                        start = System.Diagnostics.Stopwatch.GetTimestamp();
                        bool found = remaining.Remove(paths.Current);
                        compareTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                        if (!found) { complete = false; break; }
                    }
                }
                result.DiscoveryEnumerateMs = enumerateTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;
                result.DiscoveryCompareMs = compareTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;
                complete = complete && remaining.Count == 0;
            }
            // An unreadable subtree is not evidence that SDK-only entries were
            // deleted. Preserve the indexed result instead of pruning its rows.
            catch (IOException) { return result; }
            catch (UnauthorizedAccessException) { return result; }
            finally { result.DiscoveryCheckMs = checkTimer.ElapsedMilliseconds; }
            if (complete) return result;
            int queries = result.EverythingQueryCount;
            SearchResult validated = SearchFilesAfterChange(source, recursive, 0, blockedPaths, extensions, progress, cancelRequested);
            validated.EverythingQueryCount += queries;
            validated.SdkPrepareMs = result.SdkPrepareMs;
            validated.EverythingWaitMs = result.EverythingWaitMs;
            validated.EverythingReadMs = result.EverythingReadMs;
            validated.DiscoveryCheckMs = result.DiscoveryCheckMs;
            validated.DiscoverySetMs = result.DiscoverySetMs;
            validated.DiscoveryEnumerateMs = result.DiscoveryEnumerateMs;
            validated.DiscoveryCompareMs = result.DiscoveryCompareMs;
            return validated;
        }

        private IFileSystemIndexProvider Select()
        {
            if (Mode == FileSystemIndexProviderMode.Windows)
                return _native;
            if (Mode == FileSystemIndexProviderMode.Everything)
                return _everything.IsAvailable ? _everything : _native;
            return _everything.IsAvailable ? _everything : _native;
        }
    }

    // Keep Framework enumeration: the large-fetch native variant regressed on
    // the user's physical library despite being faster on a small local fixture.
    internal static class ScopeFileNames
    {
        internal static IEnumerable<string> Enumerate(string source, bool recursive,
            HashSet<string> allowedExtensions, Func<bool> canceled)
        {
            foreach (string path in Directory.EnumerateFiles(source, "*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                if (canceled != null && canceled()) throw new OperationCanceledException();
                if (allowedExtensions.Contains(Path.GetExtension(path).TrimStart('.'))) yield return path;
            }
        }
    }
}
