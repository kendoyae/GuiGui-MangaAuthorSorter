using System;
using System.Collections.Generic;

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
        IFileSystemIndexConfigurationVersionProvider
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

        private IFileSystemIndexProvider Select()
        {
            if (Mode == FileSystemIndexProviderMode.Windows)
                return _native;
            if (Mode == FileSystemIndexProviderMode.Everything)
                return _everything.IsAvailable ? _everything : _native;
            return _everything.IsAvailable ? _everything : _native;
        }
    }
}
