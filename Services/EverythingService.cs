using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace MangaAuthorSorter
{
    internal sealed class EverythingService
    {
        private const UInt32 RequestFullPathAndFileName = 0x00000004;
        private const UInt32 RequestSize = 0x00000010;
        private const UInt32 RequestDateCreated = 0x00000020;
        private const UInt32 RequestDateModified = 0x00000040;
        private const UInt32 SortNameAscending = 1;
        private const UInt32 AllResults = 0xffffffff;
        private readonly string _appDir;
        private readonly ScanExclusionRuleStore _scanExclusionStore;
        private bool _downloadAttempted;

        public EverythingService(string appDir)
            : this(appDir, null)
        {
        }

        public EverythingService(string appDir, ScanExclusionRuleStore scanExclusionStore)
        {
            _appDir = appDir;
            _scanExclusionStore = scanExclusionStore;
        }

        public string IndexConfigurationVersion
        {
            get
            {
                if (_scanExclusionStore == null) return "ScanExclusion=none";
                try
                {
                    string path = _scanExclusionStore.Path;
                    long ticks = File.Exists(path)
                        ? File.GetLastWriteTimeUtc(path).Ticks : 0;
                    return "ScanExclusion=" + ticks.ToString();
                }
                catch { return "ScanExclusion=unknown"; }
            }
        }

        public bool IsEverythingRunning()
        {
            try
            {
                return Process.GetProcessesByName("Everything").Length > 0 ||
                       Process.GetProcessesByName("Everything64").Length > 0;
            }
            catch { return false; }
        }

        public SearchResult SearchArchives(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths)
        {
            return SearchFiles(
                source,
                recursive,
                scanLimit,
                blockedPaths,
                FileTypeRules.GetCompressionDefaults());
        }

        public SearchResult SearchFiles(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions)
        {
            return SearchFiles(
                source,
                recursive,
                scanLimit,
                blockedPaths,
                extensions,
                null,
                null);
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
            ThrowIfCanceled(cancelRequested);

            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    "原始位置不存在：" +
                    source);
            }

            List<string> normalizedExtensions =
                FileTypeRules.NormalizeExtensions(
                    extensions);

            if (normalizedExtensions.Count == 0)
            {
                throw new InvalidOperationException(
                    "没有设置任何需要扫描的文件扩展名。");
            }

            SearchResult r = new SearchResult();

            ReportSearchProgress(
                progress,
                0,
                0,
                source,
                true);

            if (ScanPerformanceDiagnostics.EverythingEnabled && IsEverythingRunning())
            {
                try
                {
                    ThrowIfCanceled(cancelRequested);
                    Stopwatch sdkTimer = Stopwatch.StartNew();
                    try { EnsureSdkAvailable(); }
                    finally { r.SdkPrepareMs = sdkTimer.ElapsedMilliseconds; }
                    ThrowIfCanceled(cancelRequested);

                    List<ScanExcludedItem> excluded = new List<ScanExcludedItem>();
                    List<FileInfo> indexFiles = new List<FileInfo>();
                    List<SourceIndexFileSnapshot> indexEntries = new List<SourceIndexFileSnapshot>();
                    int discovered = 0;
                    r.EverythingQueryCount++;
                    List<FileInfo> files =
                        QueryEverything(
                            source,
                            recursive,
                            scanLimit,
                            blockedPaths,
                            normalizedExtensions,
                            excluded,
                            indexFiles,
                            indexEntries,
                            out discovered,
                            progress,
                            cancelRequested,
                            r);

                    ThrowIfCanceled(cancelRequested);

                    // If Everything did return candidates but every one was
                    // excluded by global rules/manual exclusions, this is a
                    // legitimate empty result and must not trigger a filesystem
                    // fallback that would repeat the work.
                    if (files.Count > 0 || discovered > 0 || excluded.Count > 0)
                    {
                        r.Files = files;
                        r.IndexFiles = indexFiles;
                        r.IndexEntries = indexEntries;
                        r.ExcludedItems = excluded;
                        r.DiscoveredCount = discovered;
                        r.Backend = "Everything SDK";
                        r.Detail = files.Count > 0
                            ? "Everything 官方 SDK 高速模式"
                            : "Everything 官方 SDK 高速模式（结果 0）";
                        return r;
                    }

                    // A successful empty SDK response is trustworthy only if
                    // the requested directory itself is present in Everything's
                    // index. Otherwise e.g. an excluded/unindexed drive would
                    // silently yield a false empty result. Probe the indexed
                    // directory by exact full path without walking its contents.
                    if (IsEverythingDirectoryIndexed(source))
                    {
                        r.Files = files;
                        r.IndexFiles = indexFiles;
                        r.IndexEntries = indexEntries;
                        r.ExcludedItems = excluded;
                        r.DiscoveredCount = discovered;
                        r.Backend = "Everything SDK";
                        r.Detail = "Everything 官方 SDK 高速模式（已确认空结果）";
                        return r;
                    }

                    List<ScanExcludedItem> checkExcluded = new List<ScanExcludedItem>();
                    List<FileInfo> checkIndexFiles = new List<FileInfo>();
                    List<SourceIndexFileSnapshot> checkIndexEntries = new List<SourceIndexFileSnapshot>();
                    int checkDiscovered = 0;
                    List<FileInfo> check =
                        SearchFileSystem(
                            source,
                            recursive,
                            scanLimit,
                            blockedPaths,
                            normalizedExtensions,
                            checkExcluded,
                            checkIndexFiles,
                            checkIndexEntries,
                            out checkDiscovered,
                            progress,
                            cancelRequested);

                    ThrowIfCanceled(cancelRequested);

                    if (check.Count == 0 && checkExcluded.Count == 0)
                    {
                        r.Files = files;
                        r.IndexFiles = indexFiles;
                        r.IndexEntries = indexEntries;
                        r.ExcludedItems = excluded;
                        r.DiscoveredCount = discovered;
                        r.Backend = "Everything SDK";
                        r.Detail = "Everything 官方 SDK 高速模式（结果 0）";
                        return r;
                    }

                    r.Files = check;
                    r.IndexFiles = checkIndexFiles;
                    r.IndexEntries = checkIndexEntries;
                    r.ExcludedItems = checkExcluded;
                    r.DiscoveredCount = checkDiscovered;
                    r.Backend = "FileSystem";
                    r.Detail = "Everything SDK 返回 0 个结果，但本地目录存在符合当前文件类型设置的文件；本次已自动回退普通模式";
                    return r;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ThrowIfCanceled(cancelRequested);

                    List<ScanExcludedItem> excluded = new List<ScanExcludedItem>();
                    List<FileInfo> indexFiles = new List<FileInfo>();
                    List<SourceIndexFileSnapshot> indexEntries = new List<SourceIndexFileSnapshot>();
                    int discovered = 0;
                    r.Files =
                        SearchFileSystem(
                            source,
                            recursive,
                            scanLimit,
                            blockedPaths,
                            normalizedExtensions,
                            excluded,
                            indexFiles,
                            indexEntries,
                            out discovered,
                            progress,
                            cancelRequested);
                    r.IndexFiles = indexFiles;
                    r.IndexEntries = indexEntries;
                    r.ExcludedItems = excluded;
                    r.DiscoveredCount = discovered;

                    ThrowIfCanceled(cancelRequested);

                    r.Backend = "FileSystem";
                    r.Detail =
                        "Everything 已运行，但 SDK 查询失败；已自动回退：" +
                        ex.Message;
                    return r;
                }
            }

            List<ScanExcludedItem> fsExcluded = new List<ScanExcludedItem>();
            List<FileInfo> fsIndexFiles = new List<FileInfo>();
            List<SourceIndexFileSnapshot> fsIndexEntries = new List<SourceIndexFileSnapshot>();
            int fsDiscovered = 0;
            r.Files =
                SearchFileSystem(
                    source,
                    recursive,
                    scanLimit,
                    blockedPaths,
                    normalizedExtensions,
                    fsExcluded,
                    fsIndexFiles,
                    fsIndexEntries,
                    out fsDiscovered,
                    progress,
                    cancelRequested);
            r.IndexFiles = fsIndexFiles;
            r.IndexEntries = fsIndexEntries;
            r.ExcludedItems = fsExcluded;
            r.DiscoveredCount = fsDiscovered;

            ThrowIfCanceled(cancelRequested);

            r.Backend = "FileSystem";
            r.Detail = "Everything 未运行，使用普通文件系统扫描";

            return r;
        }

        internal SearchResult SearchFilesNative(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            ThrowIfCanceled(cancelRequested);
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("原始位置不存在：" + source);

            List<string> normalizedExtensions =
                FileTypeRules.NormalizeExtensions(extensions);
            if (normalizedExtensions.Count == 0)
                throw new InvalidOperationException("没有设置任何需要扫描的文件扩展名。");

            List<ScanExcludedItem> excluded = new List<ScanExcludedItem>();
            List<FileInfo> indexFiles = new List<FileInfo>();
            List<SourceIndexFileSnapshot> indexEntries = new List<SourceIndexFileSnapshot>();
            int discovered;
            SearchResult result = new SearchResult();
            result.Files = SearchFileSystem(
                source, recursive, scanLimit, blockedPaths,
                normalizedExtensions, excluded, indexFiles, indexEntries, out discovered,
                progress, cancelRequested);
            result.IndexFiles = indexFiles;
            result.IndexEntries = indexEntries;
            result.ExcludedItems = excluded;
            result.DiscoveredCount = discovered;
            result.Backend = "FileSystem";
            result.Detail = "使用 Windows 文件系统扫描";
            return result;
        }

        private string GetSdkDllPath()
        {
            return Path.Combine(_appDir, IntPtr.Size == 8 ? "Everything64.dll" : "Everything32.dll");
        }

        private void EnsureSdkAvailable()
        {
            string dll = GetSdkDllPath();
            if (File.Exists(dll)) return;
            if (_downloadAttempted) throw new FileNotFoundException("未找到 Everything 官方 SDK DLL。", dll);
            _downloadAttempted = true;
            DownloadOfficialSdk();
            if (!File.Exists(dll)) throw new FileNotFoundException("Everything 官方 SDK 下载后仍未找到对应 DLL。", dll);
        }

        private void DownloadOfficialSdk()
        {
            string temp = Path.Combine(Path.GetTempPath(), "MangaAuthorSorter_EverythingSDK_" + Guid.NewGuid().ToString("N"));
            string zip = Path.Combine(temp, "Everything-SDK.zip");
            string extract = Path.Combine(temp, "sdk");
            Directory.CreateDirectory(temp);
            try
            {
                try { ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12; } catch { }
                using (WebClient wc = new SdkDownloadClient())
                {
                    wc.Headers.Add("User-Agent", "MangaAuthorSorter-CSharp/1.0");
                    wc.DownloadFile("https://www.voidtools.com/Everything-SDK.zip", zip);
                }
                ZipFile.ExtractToDirectory(zip, extract);
                string wanted = IntPtr.Size == 8 ? "Everything64.dll" : "Everything32.dll";
                string found = Directory.GetFiles(extract, wanted, SearchOption.AllDirectories).FirstOrDefault();
                if (found == null) throw new FileNotFoundException("官方 SDK 压缩包中未找到 " + wanted);
                File.Copy(found, GetSdkDllPath(), true);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        private sealed class SdkDownloadClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 5000;
                HttpWebRequest http = request as HttpWebRequest;
                if (http != null) http.ReadWriteTimeout = 5000;
                return request;
            }
        }

        internal static string BuildEverythingSourceQuery(
            string sourceFull, bool recursive, string extQuery)
        {
            string quoted = "\"" + (sourceFull ?? "").Replace("\"", "") + "\"";
            return (recursive ? quoted : "parent:" + quoted) + " " + (extQuery ?? "");
        }

        // Return true only when the same absolute directory can be found in
        // Everything's directory index. This is a small index probe, not a
        // recursive filesystem walk. Unindexed/offline paths still use the
        // original filesystem safety fallback.
        private static bool IsEverythingDirectoryIndexed(string source)
        {
            try
            {
                string full = Path.GetFullPath(source).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (String.IsNullOrWhiteSpace(full)) return false;
                Native.Reset();
                Native.SetMatchPath(true);
                Native.SetMatchCase(false);
                Native.SetMatchWholeWord(false);
                Native.SetRegex(false);
                Native.SetOffset(0);
                Native.SetMax(32);
                Native.SetRequestFlags(RequestFullPathAndFileName);
                Native.SetSearch("folder:\"" + full.Replace("\"", "") + "\"");
                if (!Native.Query(true)) return false;
                UInt32 count = Native.GetNumResults();
                StringBuilder buffer = new StringBuilder(32768);
                for (UInt32 i = 0; i < count; i++)
                {
                    buffer.Length = 0;
                    if (Native.GetResultFullPathName(i, buffer, (UInt32)buffer.Capacity) == 0)
                        continue;
                    if (String.Equals(
                        buffer.ToString().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        full, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // Uncertain index coverage must not hide actual files.
            }
            return false;
        }

        private List<FileInfo> QueryEverything(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            List<ScanExcludedItem> excludedItems,
            List<FileInfo> indexFiles,
            List<SourceIndexFileSnapshot> indexEntries,
            out int discoveredCount,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested,
            SearchResult diagnostics)
        {
            discoveredCount = 0;
            List<string> normalizedExtensions =
                FileTypeRules.NormalizeExtensions(extensions);

            string extQuery =
                FileTypeRules.BuildEverythingExtQuery(normalizedExtensions);

            if (String.IsNullOrWhiteSpace(extQuery))
                return new List<FileInfo>();

            string sourceFull =
                Path.GetFullPath(source)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            Native.Reset();
            Native.SetMatchPath(true);
            Native.SetMatchCase(false);
            Native.SetMatchWholeWord(false);
            Native.SetRegex(false);
            Native.SetOffset(0);
            Native.SetMax(AllResults);
            // Name sorting is free in Everything. Date sorting can cause cold
            // metadata work when its fast-sort index is unavailable. We already
            // request modification timestamps, so sort those snapshots locally.
            Native.SetSort(SortNameAscending);
            Native.SetRequestFlags(
                RequestFullPathAndFileName |
                RequestSize |
                RequestDateCreated |
                RequestDateModified);
            // Everything 1.4+ supports parent:"absolute path" for immediate
            // children. Avoid querying an entire subtree only to throw away
            // every descendant when recursive scanning is disabled.
            Native.SetSearch(BuildEverythingSourceQuery(sourceFull, recursive, extQuery));

            Stopwatch queryTimer = Stopwatch.StartNew();
            bool querySucceeded = Native.Query(true);
            diagnostics.EverythingWaitMs = queryTimer.ElapsedMilliseconds;
            if (!querySucceeded)
            {
                throw new InvalidOperationException(
                    "Everything SDK 查询失败，错误代码：" +
                    Native.GetLastError().ToString());
            }

            UInt32 count = Native.GetNumResults();
            Stopwatch readTimer = Stopwatch.StartNew();
            List<FileInfo> result = new List<FileInfo>();
            Dictionary<string, DateTime> modifiedTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            string prefix = sourceFull + Path.DirectorySeparatorChar;

            // Result filtering is intentionally indeterminate because global
            // exclusion rules can remove items. The determinate planning stage
            // therefore counts only files that actually enter recognition.
            // Reuse one long-path buffer instead of allocating ~32 KB for
            // every Everything result.
            StringBuilder buffer = new StringBuilder(32768);
            for (UInt32 i = 0; i < count; i++)
            {
                ThrowIfCanceled(cancelRequested);

                buffer.Length = 0;
                UInt32 copied = Native.GetResultFullPathName(i, buffer, (UInt32)buffer.Capacity);
                if (copied == 0) continue;

                string path = buffer.ToString();
                if ((i % 32) == 0 || i + 1 == count)
                {
                    ReportSearchProgress(progress, result.Count, 0, path, true);
                }

                // Everything already owns a live NTFS index. Do not touch the
                // physical file here merely to verify a result that Everything
                // has just returned; that would turn an index query back into a
                // per-file filesystem scan.
                if (String.IsNullOrWhiteSpace(path))
                    continue;
                if (!FileTypeRules.ContainsExtension(normalizedExtensions, path))
                    continue;

                string parent;
                try
                {
                    parent = Path.GetFullPath(Path.GetDirectoryName(path) ?? "")
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch { continue; }

                bool belongs =
                    recursive
                        ? String.Equals(parent, sourceFull, StringComparison.OrdinalIgnoreCase) ||
                          parent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        : String.Equals(parent, sourceFull, StringComparison.OrdinalIgnoreCase);

                if (!belongs) continue;
                discoveredCount++;

                FileInfo f;
                try { f = new FileInfo(path); }
                catch { continue; }
                if (indexFiles != null) indexFiles.Add(f);
                if (indexEntries != null)
                {
                    long size;
                    NativeFileTime created;
                    NativeFileTime modified;
                    bool hasSize = Native.GetResultSize(i, out size);
                    bool hasCreated = Native.GetResultDateCreated(i, out created);
                    bool hasModified = Native.GetResultDateModified(i, out modified);
                    modifiedTimes[path] = hasModified ? ToDateTimeUtc(modified) : DateTime.MinValue;
                    indexEntries.Add(new SourceIndexFileSnapshot
                    {
                        FullPath = path,
                        DirectoryPath = parent,
                        FileName = Path.GetFileName(path) ?? "",
                        Extension = Path.GetExtension(path) ?? "",
                        FileSize = hasSize ? size : -1,
                        CreationTimeUtc = hasCreated
                            ? ToDateTimeUtc(created) : DateTime.MinValue,
                        LastWriteTimeUtc = hasModified
                            ? ToDateTimeUtc(modified) : DateTime.MinValue
                    });
                }

                ScanExclusionMatch exclusion;
                if (_scanExclusionStore != null &&
                    _scanExclusionStore.TryMatchFilePath(path, sourceFull, out exclusion))
                {
                    AddExcludedItem(excludedItems, path, false, exclusion);
                    continue;
                }

                if (IsBlocked(path, blockedPaths))
                    continue;

                result.Add(f);
            }

            result = OrderByIndexedModificationTime(result, modifiedTimes);
            if (scanLimit > 0 && result.Count > scanLimit)
                result = result.Take(scanLimit).ToList();

            diagnostics.EverythingReadMs = readTimer.ElapsedMilliseconds;

            ReportSearchProgress(progress, result.Count, result.Count, source, false);
            return result;
        }

        internal static List<FileInfo> OrderByIndexedModificationTime(IEnumerable<FileInfo> files,
            IDictionary<string, DateTime> modifiedTimes)
        {
            // FileInfo.LastWriteTime would perform a physical per-file read.
            // Missing SDK timestamps sort last; equal timestamps stay stable.
            return files.OrderByDescending(delegate(FileInfo file)
            {
                DateTime time;
                return modifiedTimes.TryGetValue(file.FullName, out time) ? time : DateTime.MinValue;
            }).ToList();
        }

        private List<FileInfo> SearchFileSystem(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            List<ScanExcludedItem> excludedItems,
            List<FileInfo> indexFiles,
            List<SourceIndexFileSnapshot> indexEntries,
            out int discoveredCount,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
        {
            discoveredCount = 0;
            List<string> normalizedExtensions =
                FileTypeRules.NormalizeExtensions(extensions);

            List<FileInfo> result = new List<FileInfo>();
            Stack<string> pending = new Stack<string>();
            pending.Push(source);
            int inspected = 0;

            ReportSearchProgress(progress, 0, 0, source, true);

            while (pending.Count > 0)
            {
                ThrowIfCanceled(cancelRequested);
                string folder = pending.Pop();

                string[] files;
                try { files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly); }
                catch { files = new string[0]; }

                foreach (string path in files)
                {
                    ThrowIfCanceled(cancelRequested);
                    inspected++;

                    if ((inspected % 64) == 0)
                        ReportSearchProgress(progress, result.Count, 0, path, true);

                    if (!FileTypeRules.ContainsExtension(normalizedExtensions, path))
                        continue;

                    FileInfo indexed;
                    try
                    {
                        indexed = new FileInfo(path);
                        // The filesystem provider cannot avoid metadata IO, but it
                        // reads it once and passes the snapshot forward so the DB
                        // reconciliation does not read the same file again.
                        long size = indexed.Length;
                        DateTime modifiedUtc = indexed.LastWriteTimeUtc;
                        DateTime createdUtc = indexed.CreationTimeUtc;
                        if (indexFiles != null) indexFiles.Add(indexed);
                        if (indexEntries != null)
                        {
                            indexEntries.Add(new SourceIndexFileSnapshot
                            {
                                FullPath = indexed.FullName,
                                DirectoryPath = indexed.DirectoryName ?? "",
                                FileName = indexed.Name ?? "",
                                Extension = indexed.Extension ?? "",
                                FileSize = size,
                                LastWriteTimeUtc = modifiedUtc,
                                CreationTimeUtc = createdUtc
                            });
                        }
                    }
                    catch { continue; }

                    discoveredCount++;
                    ScanExclusionMatch exclusion;
                    if (_scanExclusionStore != null &&
                        _scanExclusionStore.TryMatchFilePath(path, source, out exclusion))
                    {
                        AddExcludedItem(excludedItems, path, false, exclusion);
                        continue;
                    }

                    if (IsBlocked(path, blockedPaths))
                        continue;

                    result.Add(indexed);
                }

                if (!recursive) continue;

                string[] folders;
                try { folders = Directory.GetDirectories(folder, "*", SearchOption.TopDirectoryOnly); }
                catch { folders = new string[0]; }

                foreach (string child in folders)
                {
                    ThrowIfCanceled(cancelRequested);
                    ScanExclusionMatch folderExclusion;
                    if (_scanExclusionStore != null &&
                        _scanExclusionStore.TryMatchFolderPath(child, source, out folderExclusion))
                    {
                        AddExcludedItem(excludedItems, child, true, folderExclusion);
                        // Exclusion is a query/view rule, not a FileIndex rule.
                        // Keep walking the subtree so the durable canonical index
                        // remains complete and can be reprojected instantly if
                        // the exclusion rule later changes. Descendant files are
                        // filtered below before they enter result.Files.
                    }
                    pending.Push(child);
                }
            }

            ThrowIfCanceled(cancelRequested);

            result =
                result.OrderByDescending(delegate(FileInfo f) { return f.LastWriteTime; }).ToList();

            if (scanLimit > 0 && result.Count > scanLimit)
                result = result.Take(scanLimit).ToList();

            ReportSearchProgress(progress, result.Count, result.Count, source, false);
            return result;
        }

        private static void AddExcludedItem(
            List<ScanExcludedItem> items,
            string path,
            bool isDirectory,
            ScanExclusionMatch match)
        {
            if (items == null || match == null) return;

            bool matchedFolder =
                String.Equals(
                    ScanExclusionScope.Normalize(match.Scope),
                    ScanExclusionScope.FolderName,
                    StringComparison.OrdinalIgnoreCase) &&
                !String.IsNullOrWhiteSpace(match.MatchedPath);

            string effectivePath =
                matchedFolder ? match.MatchedPath : (path ?? "");
            bool effectiveDirectory = isDirectory || matchedFolder;

            // Everything returns files rather than folders. If a folder rule
            // excludes a subtree, many result files can point back to the same
            // excluded folder. Keep one folder record instead of one row per
            // descendant so the exclusion count remains meaningful and cheap.
            if (effectiveDirectory)
            {
                foreach (ScanExcludedItem existing in items)
                {
                    if (existing == null) continue;
                    if (String.Equals(existing.Path, effectivePath, StringComparison.OrdinalIgnoreCase) &&
                        String.Equals(existing.RuleName, match.RuleName ?? "", StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            ScanExcludedItem item = new ScanExcludedItem();
            item.Path = effectivePath;
            try
            {
                item.DisplayName = effectiveDirectory
                    ? new DirectoryInfo(effectivePath).Name
                    : Path.GetFileName(effectivePath);
            }
            catch
            {
                item.DisplayName = effectivePath;
            }
            item.RuleName = match.RuleName ?? "";
            item.Scope = match.Scope ?? (effectiveDirectory ? ScanExclusionScope.FolderName : ScanExclusionScope.FileName);
            item.IsDirectory = effectiveDirectory;
            items.Add(item);
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

        private static void ReportSearchProgress(
            Action<ScanProgressInfo> progress,
            int current,
            int total,
            string path,
            bool indeterminate)
        {
            if (progress == null)
                return;

            ScanProgressInfo info =
                new ScanProgressInfo();
            info.Stage = ScanProgressStage.Searching;
            info.Current = current;
            info.Total = total;
            info.CurrentPath = path ?? "";
            info.Indeterminate = indeterminate;
            progress(info);
        }

        private static bool IsBlocked(
            string path,
            HashSet<string> blockedPaths)
        {
            if (blockedPaths == null ||
                blockedPaths.Count == 0)
            {
                return false;
            }

            string normalized =
                BlockListStore.NormalizePath(path);

            return blockedPaths.Contains(
                normalized);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            public UInt32 LowDateTime;
            public UInt32 HighDateTime;
        }

        private static DateTime ToDateTimeUtc(NativeFileTime value)
        {
            UInt64 raw = ((UInt64)value.HighDateTime << 32) | value.LowDateTime;
            if (raw == 0 || raw > Int64.MaxValue) return DateTime.MinValue;
            try { return DateTime.FromFileTimeUtc((Int64)raw); }
            catch { return DateTime.MinValue; }
        }

        private static class Native
        {
            public static void Reset() { if (IntPtr.Size == 8) Native64.Everything_Reset(); else Native32.Everything_Reset(); }
            public static void SetSearch(string s) { if (IntPtr.Size == 8) Native64.Everything_SetSearchW(s); else Native32.Everything_SetSearchW(s); }
            public static void SetMatchPath(bool v) { if (IntPtr.Size == 8) Native64.Everything_SetMatchPath(v); else Native32.Everything_SetMatchPath(v); }
            public static void SetMatchCase(bool v) { if (IntPtr.Size == 8) Native64.Everything_SetMatchCase(v); else Native32.Everything_SetMatchCase(v); }
            public static void SetMatchWholeWord(bool v) { if (IntPtr.Size == 8) Native64.Everything_SetMatchWholeWord(v); else Native32.Everything_SetMatchWholeWord(v); }
            public static void SetRegex(bool v) { if (IntPtr.Size == 8) Native64.Everything_SetRegex(v); else Native32.Everything_SetRegex(v); }
            public static void SetMax(UInt32 v) { if (IntPtr.Size == 8) Native64.Everything_SetMax(v); else Native32.Everything_SetMax(v); }
            public static void SetOffset(UInt32 v) { if (IntPtr.Size == 8) Native64.Everything_SetOffset(v); else Native32.Everything_SetOffset(v); }
            public static void SetSort(UInt32 v) { if (IntPtr.Size == 8) Native64.Everything_SetSort(v); else Native32.Everything_SetSort(v); }
            public static void SetRequestFlags(UInt32 v) { if (IntPtr.Size == 8) Native64.Everything_SetRequestFlags(v); else Native32.Everything_SetRequestFlags(v); }
            public static bool Query(bool wait) { return IntPtr.Size == 8 ? Native64.Everything_QueryW(wait) : Native32.Everything_QueryW(wait); }
            public static UInt32 GetNumResults() { return IntPtr.Size == 8 ? Native64.Everything_GetNumResults() : Native32.Everything_GetNumResults(); }
            public static UInt32 GetLastError() { return IntPtr.Size == 8 ? Native64.Everything_GetLastError() : Native32.Everything_GetLastError(); }
            public static UInt32 GetResultFullPathName(UInt32 i, StringBuilder b, UInt32 c)
            { return IntPtr.Size == 8 ? Native64.Everything_GetResultFullPathNameW(i, b, c) : Native32.Everything_GetResultFullPathNameW(i, b, c); }
            public static bool GetResultSize(UInt32 i, out long size)
            { return IntPtr.Size == 8 ? Native64.Everything_GetResultSize(i, out size) : Native32.Everything_GetResultSize(i, out size); }
            public static bool GetResultDateCreated(UInt32 i, out NativeFileTime time)
            { return IntPtr.Size == 8 ? Native64.Everything_GetResultDateCreated(i, out time) : Native32.Everything_GetResultDateCreated(i, out time); }
            public static bool GetResultDateModified(UInt32 i, out NativeFileTime time)
            { return IntPtr.Size == 8 ? Native64.Everything_GetResultDateModified(i, out time) : Native32.Everything_GetResultDateModified(i, out time); }
        }

        private static class Native64
        {
            [DllImport("Everything64.dll", CharSet = CharSet.Unicode)] internal static extern void Everything_SetSearchW(string search);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetMatchCase([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetMatchWholeWord([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetRegex([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetMax(UInt32 value);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetOffset(UInt32 value);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetSort(UInt32 value);
            [DllImport("Everything64.dll")] internal static extern void Everything_SetRequestFlags(UInt32 value);
            [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);
            [DllImport("Everything64.dll")] internal static extern UInt32 Everything_GetNumResults();
            [DllImport("Everything64.dll")] internal static extern UInt32 Everything_GetLastError();
            [DllImport("Everything64.dll", CharSet = CharSet.Unicode)] internal static extern UInt32 Everything_GetResultFullPathNameW(UInt32 index, StringBuilder buffer, UInt32 bufferSize);
            [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_GetResultSize(UInt32 index, out long size);
            [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_GetResultDateCreated(UInt32 index, out NativeFileTime time);
            [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_GetResultDateModified(UInt32 index, out NativeFileTime time);
            [DllImport("Everything64.dll")] internal static extern void Everything_Reset();
        }

        private static class Native32
        {
            [DllImport("Everything32.dll", CharSet = CharSet.Unicode)] internal static extern void Everything_SetSearchW(string search);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetMatchCase([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetMatchWholeWord([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetRegex([MarshalAs(UnmanagedType.Bool)] bool value);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetMax(UInt32 value);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetOffset(UInt32 value);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetSort(UInt32 value);
            [DllImport("Everything32.dll")] internal static extern void Everything_SetRequestFlags(UInt32 value);
            [DllImport("Everything32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);
            [DllImport("Everything32.dll")] internal static extern UInt32 Everything_GetNumResults();
            [DllImport("Everything32.dll")] internal static extern UInt32 Everything_GetLastError();
            [DllImport("Everything32.dll", CharSet = CharSet.Unicode)] internal static extern UInt32 Everything_GetResultFullPathNameW(UInt32 index, StringBuilder buffer, UInt32 bufferSize);
            [DllImport("Everything32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_GetResultSize(UInt32 index, out long size);
            [DllImport("Everything32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_GetResultDateCreated(UInt32 index, out NativeFileTime time);
            [DllImport("Everything32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Everything_GetResultDateModified(UInt32 index, out NativeFileTime time);
            [DllImport("Everything32.dll")] internal static extern void Everything_Reset();
        }
    }
}
