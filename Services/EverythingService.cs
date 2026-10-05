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
        private const UInt32 SortDateModifiedDescending = 14;
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
                    EnsureSdkAvailable();
                    ThrowIfCanceled(cancelRequested);

                    List<ScanExcludedItem> excluded = new List<ScanExcludedItem>();
                    int discovered = 0;
                    List<FileInfo> files =
                        QueryEverything(
                            source,
                            recursive,
                            scanLimit,
                            blockedPaths,
                            normalizedExtensions,
                            excluded,
                            out discovered,
                            progress,
                            cancelRequested);

                    ThrowIfCanceled(cancelRequested);

                    // If Everything did return candidates but every one was
                    // excluded by global rules/manual exclusions, this is a
                    // legitimate empty result and must not trigger a filesystem
                    // fallback that would repeat the work.
                    if (files.Count > 0 || discovered > 0 || excluded.Count > 0)
                    {
                        r.Files = files;
                        r.ExcludedItems = excluded;
                        r.DiscoveredCount = discovered;
                        r.Backend = "Everything SDK";
                        r.Detail = files.Count > 0
                            ? "Everything 官方 SDK 高速模式"
                            : "Everything 官方 SDK 高速模式（结果 0）";
                        return r;
                    }

                    List<ScanExcludedItem> checkExcluded = new List<ScanExcludedItem>();
                    int checkDiscovered = 0;
                    List<FileInfo> check =
                        SearchFileSystem(
                            source,
                            recursive,
                            scanLimit,
                            blockedPaths,
                            normalizedExtensions,
                            checkExcluded,
                            out checkDiscovered,
                            progress,
                            cancelRequested);

                    ThrowIfCanceled(cancelRequested);

                    if (check.Count == 0 && checkExcluded.Count == 0)
                    {
                        r.Files = files;
                        r.ExcludedItems = excluded;
                        r.DiscoveredCount = discovered;
                        r.Backend = "Everything SDK";
                        r.Detail = "Everything 官方 SDK 高速模式（结果 0）";
                        return r;
                    }

                    r.Files = check;
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
                    int discovered = 0;
                    r.Files =
                        SearchFileSystem(
                            source,
                            recursive,
                            scanLimit,
                            blockedPaths,
                            normalizedExtensions,
                            excluded,
                            out discovered,
                            progress,
                            cancelRequested);
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
            int fsDiscovered = 0;
            r.Files =
                SearchFileSystem(
                    source,
                    recursive,
                    scanLimit,
                    blockedPaths,
                    normalizedExtensions,
                    fsExcluded,
                    out fsDiscovered,
                    progress,
                    cancelRequested);
            r.ExcludedItems = fsExcluded;
            r.DiscoveredCount = fsDiscovered;

            ThrowIfCanceled(cancelRequested);

            r.Backend = "FileSystem";
            r.Detail = "Everything 未运行，使用普通文件系统扫描";

            return r;
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
                using (WebClient wc = new WebClient())
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

        private List<FileInfo> QueryEverything(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            List<ScanExcludedItem> excludedItems,
            out int discoveredCount,
            Action<ScanProgressInfo> progress,
            Func<bool> cancelRequested)
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
            Native.SetSort(SortDateModifiedDescending);
            Native.SetRequestFlags(RequestFullPathAndFileName);
            Native.SetSearch(
                "\"" + sourceFull.Replace("\"", "") + "\" " + extQuery);

            if (!Native.Query(true))
            {
                throw new InvalidOperationException(
                    "Everything SDK 查询失败，错误代码：" +
                    Native.GetLastError().ToString());
            }

            UInt32 count = Native.GetNumResults();
            List<FileInfo> result = new List<FileInfo>();
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

                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;
                if (!FileTypeRules.ContainsExtension(normalizedExtensions, path))
                    continue;

                FileInfo f;
                try { f = new FileInfo(path); }
                catch { continue; }

                string parent =
                    Path.GetFullPath(f.DirectoryName ?? "")
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                bool belongs =
                    recursive
                        ? String.Equals(parent, sourceFull, StringComparison.OrdinalIgnoreCase) ||
                          parent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        : String.Equals(parent, sourceFull, StringComparison.OrdinalIgnoreCase);

                if (!belongs) continue;
                discoveredCount++;

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

            // Everything_SetSort(SortDateModifiedDescending) already returns
            // newest -> oldest. Re-sorting here forced a LastWriteTime metadata
            // read for every candidate and was especially expensive in filtered
            // Early Stop scans. Filtering preserves the native order.
            if (scanLimit > 0 && result.Count > scanLimit)
                result = result.Take(scanLimit).ToList();

            ReportSearchProgress(progress, result.Count, result.Count, source, false);
            return result;
        }

        private List<FileInfo> SearchFileSystem(
            string source,
            bool recursive,
            int scanLimit,
            HashSet<string> blockedPaths,
            IEnumerable<string> extensions,
            List<ScanExcludedItem> excludedItems,
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

                    ScanExclusionMatch exclusion;
                    if (_scanExclusionStore != null &&
                        _scanExclusionStore.TryMatchFilePath(path, source, out exclusion))
                    {
                        AddExcludedItem(excludedItems, path, false, exclusion);
                        continue;
                    }

                    if (IsBlocked(path, blockedPaths))
                        continue;

                    if (!FileTypeRules.ContainsExtension(normalizedExtensions, path))
                        continue;

                    discoveredCount++;
                    try { result.Add(new FileInfo(path)); }
                    catch { }
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
                        continue;
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
            [DllImport("Everything32.dll")] internal static extern void Everything_Reset();
        }
    }
}
