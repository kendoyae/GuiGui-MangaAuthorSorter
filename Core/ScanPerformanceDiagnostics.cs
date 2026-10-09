using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MangaAuthorSorter
{
    internal sealed class ScanPerformanceEntry
    {
        public DateTime Time;
        public bool WarmupEnabled, EverythingEnabled, WarmupHit, ReadyBeforeRequest, SnapshotHit;
        public string Provider = "";
        public string StartupInitializationStages = "";
        public string StartupUiTrace = "";
        public long WarmupWaitMs, SnapshotPrepareMs, FileDiscoveryMs;
        public long ProviderQueryMs, IndexReconcileMs;
        public long PlanCacheReadMs, PlanCacheWriteMs, FinalizeMs;
        public long AuthorMatchMs, UiApplyMs, TotalResponseMs;
        public long IdentitySourceMs, TargetDirectoryMs, InitialIndexMs, IncrementalIndexMs;
        public long PrepareRecognitionMs, PlanningLoopMs, OnlineLookupMs;
        public int InitialIndexBuilds, IncrementalIndexAdds, UniqueAuthors, NewAuthorFolders;
        public int CandidateCount, ResultCount;
        public long ParsedCacheHits, ParsedCacheMisses;
        public long RecognitionCacheHits, RecognitionCacheMisses;
        public long DestinationCacheHits, DestinationCacheMisses;
        public int IndexCacheHits, IndexAdded, IndexRemoved, IndexModified, IndexMoved, IndexRenamed;
        public int PlanCacheHits, RecalculatedFiles;
        public long StartupRestoreMs = -1, BackgroundValidationMs = -1;
        public long DiscoverySetMs = -1, DiscoveryEnumerateMs = -1, DiscoveryCompareMs = -1;
        public long SdkPrepareMs = -1, EverythingWaitMs = -1, EverythingReadMs = -1, DiscoveryCheckMs = -1;
        public int EverythingQueryCount = -1;
        public long FirstInteractiveMs = -1, StartupWindowShownMs = -1;
        public int ActualRecognitions = -1;
    }

    // All points and intervals use the form's one monotonic clock. Intervals
    // may nest; consumers must not sum them with their parent intervals.
    internal sealed class StartupUiTrace
    {
        private readonly System.Diagnostics.Stopwatch _clock;
        private readonly Dictionary<string, long> _values = new Dictionary<string, long>();
        public StartupUiTrace(System.Diagnostics.Stopwatch clock) { _clock = clock; }
        public long Now { get { return _clock.ElapsedTicks; } }
        public void Point(string key) { if (!_values.ContainsKey(key)) _values[key] = Now; }
        public void Interval(string key, long start)
        { long previous; _values.TryGetValue(key, out previous); _values[key] = previous + Now - start; }
        public void Duration(string key, long ticks) { _values[key] = ticks; }
        public void PointAt(string key, long ticks) { if (!_values.ContainsKey(key)) _values[key] = ticks; }
        public long Milliseconds(string key)
        { long value; return _values.TryGetValue(key, out value) ? value * 1000 / System.Diagnostics.Stopwatch.Frequency : -1; }
        public string Export()
        {
            List<string> values = new List<string>();
            foreach (string key in _values.Keys)
                values.Add(key + ":" + Milliseconds(key).ToString(CultureInfo.InvariantCulture));
            return String.Join(";", values);
        }
    }

    internal sealed class ScanWarmupStatusEntry
    {
        public DateTime Time;
        public ScanWarmupState State;
        public string Provider = "";
        public string Stage = "";
        public string Error = "";
        public int CandidateCount = -1;
        public long FileDiscoveryMs = -1, TargetIndexMs = -1, SnapshotPrepareMs = -1;
        public long ParsedCacheHits = -1, ParsedCacheMisses = -1;
        public long RecognitionCacheHits = -1, RecognitionCacheMisses = -1;
        public long DestinationCacheHits = -1, DestinationCacheMisses = -1;
    }

    internal static class ScanPerformanceDiagnostics
    {
        public const string FormatVersion = "GL4";
        private static readonly object Gate = new object();
        private static readonly List<ScanPerformanceEntry> Entries = new List<ScanPerformanceEntry>();
        private static bool _enabled;
        private static bool _everythingEnabled = true;
        private static string _logPath = "";
        private static ScanWarmupStatusEntry _lastWarmupStatus;

        public static event Action<ScanPerformanceEntry> EntryAdded;
        public static event Action<ScanWarmupStatusEntry> WarmupStatusChanged;
        public static event Action SettingsChanged;
        public static bool WarmupEnabled
        {
            get { return true; }
            set { /* Legacy setting retained for source compatibility. Preparation is automatic. */ }
        }
        public static bool EverythingEnabled
        {
            get { lock (Gate) return _everythingEnabled; }
            set { bool changed; lock (Gate) { changed = _everythingEnabled != value; _everythingEnabled = value; }
                if (changed && SettingsChanged != null) SettingsChanged(); }
        }
        public static bool Enabled
        {
            get { lock (Gate) return _enabled; }
            set { bool changed; lock (Gate) { changed = _enabled != value; _enabled = value; }
            }
        }

        public static void Initialize(string logPath, bool enabled, bool warmupEnabled, bool everythingEnabled)
        {
            lock (Gate)
            {
                _logPath = logPath ?? ""; _enabled = enabled;
                _everythingEnabled = everythingEnabled;
                _lastWarmupStatus = null;
                Entries.Clear();
                LoadRecentEntries();
            }
        }

        public static void Record(ScanPerformanceEntry entry)
        {
            if (entry == null || !Enabled) return;
            if (entry.Time == DateTime.MinValue) entry.Time = DateTime.Now;
            lock (Gate)
            {
                Entries.Add(entry);
                if (Entries.Count > 1000) Entries.RemoveRange(0, Entries.Count - 1000);
                AppendCompactLog(entry);
            }
            Action<ScanPerformanceEntry> handler = EntryAdded;
            if (handler != null) handler(entry);
        }

        public static List<ScanPerformanceEntry> Snapshot()
        { lock (Gate) return new List<ScanPerformanceEntry>(Entries); }
        public static ScanWarmupStatusEntry LastWarmupStatus()
        { lock (Gate) return _lastWarmupStatus; }
        public static void PublishWarmupStatus(ScanWarmupStatusEntry status)
        {
            if (status == null) return;
            if (status.Time == DateTime.MinValue) status.Time = DateTime.Now;
            lock (Gate) _lastWarmupStatus = status;
            Action<ScanWarmupStatusEntry> handler = WarmupStatusChanged;
            if (handler != null) handler(status);
        }
        // Clear must persist across restarts. Only drop the in-memory entries after
        // successfully resetting the log; otherwise retain them for the user.
        public static bool TryClear(out string error)
        {
            lock (Gate)
            {
                try
                {
                    if (!String.IsNullOrWhiteSpace(_logPath))
                        File.WriteAllText(_logPath,
                            "#" + FormatVersion + Environment.NewLine +
                            "#UNIT=ms; zero-duration fields omitted" + Environment.NewLine,
                            new UTF8Encoding(false));
                    Entries.Clear();
                    error = "";
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        private static void AppendCompactLog(ScanPerformanceEntry e)
        {
            if (String.IsNullOrWhiteSpace(_logPath)) return;
            try
            {
                StringBuilder text = new StringBuilder();
                if (!File.Exists(_logPath) || new FileInfo(_logPath).Length == 0 || !ContainsFormatHeader())
                { if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 0) text.AppendLine(); text.AppendLine("#" + FormatVersion); text.AppendLine("#UNIT=ms; zero-duration fields omitted"); }
                text.Append(e.Time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
                Pair(text, "P", ProviderCode(e.Provider)); Pair(text, "H", B(e.WarmupHit)); Pair(text, "RB", B(e.ReadyBeforeRequest)); Pair(text, "SH", B(e.SnapshotHit));
                Pair(text, "E", B(e.EverythingEnabled));
                PositivePair(text, "WW", e.WarmupWaitMs); PositivePair(text, "SP", e.SnapshotPrepareMs); PositivePair(text, "FD", e.FileDiscoveryMs);
                PositivePair(text, "FQ", e.ProviderQueryMs); PositivePair(text, "FS", e.IndexReconcileMs);
                PositivePair(text, "PCR", e.PlanCacheReadMs); PositivePair(text, "PCW", e.PlanCacheWriteMs);
                PositivePair(text, "FIN", e.FinalizeMs);
                PositivePair(text, "AM", e.AuthorMatchMs); PositivePair(text, "UI", e.UiApplyMs);
                PositivePair(text, "IS", e.IdentitySourceMs); PositivePair(text, "TD", e.TargetDirectoryMs);
                PositivePair(text, "IB", e.InitialIndexMs); PositivePair(text, "II", e.IncrementalIndexMs);
                PositivePair(text, "PR", e.PrepareRecognitionMs); PositivePair(text, "PL", e.PlanningLoopMs);
                PositivePair(text, "OL", e.OnlineLookupMs);
                Pair(text, "IBC", e.InitialIndexBuilds.ToString(CultureInfo.InvariantCulture));
                Pair(text, "IAC", e.IncrementalIndexAdds.ToString(CultureInfo.InvariantCulture));
                Pair(text, "UA", e.UniqueAuthors.ToString(CultureInfo.InvariantCulture));
                Pair(text, "NA", e.NewAuthorFolders.ToString(CultureInfo.InvariantCulture));
                Pair(text, "PH", e.ParsedCacheHits.ToString(CultureInfo.InvariantCulture)); Pair(text, "PM", e.ParsedCacheMisses.ToString(CultureInfo.InvariantCulture));
                Pair(text, "RH", e.RecognitionCacheHits.ToString(CultureInfo.InvariantCulture)); Pair(text, "RM", e.RecognitionCacheMisses.ToString(CultureInfo.InvariantCulture));
                Pair(text, "DH", e.DestinationCacheHits.ToString(CultureInfo.InvariantCulture)); Pair(text, "DM", e.DestinationCacheMisses.ToString(CultureInfo.InvariantCulture));
                Pair(text, "IC", e.IndexCacheHits.ToString(CultureInfo.InvariantCulture)); Pair(text, "IA", e.IndexAdded.ToString(CultureInfo.InvariantCulture));
                Pair(text, "ID", e.IndexRemoved.ToString(CultureInfo.InvariantCulture)); Pair(text, "IM", e.IndexModified.ToString(CultureInfo.InvariantCulture));
                Pair(text, "IV", e.IndexMoved.ToString(CultureInfo.InvariantCulture)); Pair(text, "IR", e.IndexRenamed.ToString(CultureInfo.InvariantCulture));
                Pair(text, "PC", e.PlanCacheHits.ToString(CultureInfo.InvariantCulture)); Pair(text, "RC", e.RecalculatedFiles.ToString(CultureInfo.InvariantCulture));
                if (e.StartupRestoreMs >= 0) Pair(text, "SR", e.StartupRestoreMs.ToString(CultureInfo.InvariantCulture));
                if (e.BackgroundValidationMs >= 0) Pair(text, "BV", e.BackgroundValidationMs.ToString(CultureInfo.InvariantCulture));
                if (e.EverythingQueryCount >= 0) Pair(text, "EQ", e.EverythingQueryCount.ToString(CultureInfo.InvariantCulture));
                if (e.FirstInteractiveMs >= 0) Pair(text, "FI", e.FirstInteractiveMs.ToString(CultureInfo.InvariantCulture));
                if (e.StartupWindowShownMs >= 0) Pair(text, "WS", e.StartupWindowShownMs.ToString(CultureInfo.InvariantCulture));
                if (!String.IsNullOrEmpty(e.StartupInitializationStages)) Pair(text, "SI", e.StartupInitializationStages);
                if (!String.IsNullOrEmpty(e.StartupUiTrace)) Pair(text, "SU", e.StartupUiTrace);
                if (e.ActualRecognitions >= 0) Pair(text, "AR", e.ActualRecognitions.ToString(CultureInfo.InvariantCulture));
                if (e.SdkPrepareMs >= 0) Pair(text, "SP", e.SdkPrepareMs.ToString(CultureInfo.InvariantCulture));
                if (e.EverythingWaitMs >= 0) Pair(text, "EW", e.EverythingWaitMs.ToString(CultureInfo.InvariantCulture));
                if (e.EverythingReadMs >= 0) Pair(text, "ER", e.EverythingReadMs.ToString(CultureInfo.InvariantCulture));
                if (e.DiscoveryCheckMs >= 0) Pair(text, "DC", e.DiscoveryCheckMs.ToString(CultureInfo.InvariantCulture));
                if (e.DiscoverySetMs >= 0) Pair(text, "DS", e.DiscoverySetMs.ToString(CultureInfo.InvariantCulture));
                if (e.DiscoveryEnumerateMs >= 0) Pair(text, "DN", e.DiscoveryEnumerateMs.ToString(CultureInfo.InvariantCulture));
                if (e.DiscoveryCompareMs >= 0) Pair(text, "DCM", e.DiscoveryCompareMs.ToString(CultureInfo.InvariantCulture));
                PositivePair(text, "TR", e.TotalResponseMs); Pair(text, "C", e.CandidateCount.ToString(CultureInfo.InvariantCulture)); Pair(text, "R", e.ResultCount.ToString(CultureInfo.InvariantCulture)); text.AppendLine();
                File.AppendAllText(_logPath, text.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
        private static string B(bool value) { return value ? "1" : "0"; }
        private static void Pair(StringBuilder text, string key, string value) { text.Append('|').Append(key).Append('=').Append(value); }
        private static void PositivePair(StringBuilder text, string key, long value) { if (value > 0) Pair(text, key, value.ToString(CultureInfo.InvariantCulture)); }
        private static string ProviderCode(string provider)
        { return String.Equals(provider, "Everything SDK", StringComparison.OrdinalIgnoreCase) ? "ESDK" : "SYS"; }

        private static bool ContainsFormatHeader()
        {
            foreach (string line in File.ReadLines(_logPath, Encoding.UTF8))
                if (String.Equals((line ?? "").Trim(), "#" + FormatVersion, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void LoadRecentEntries()
        {
            if (String.IsNullOrWhiteSpace(_logPath) || !File.Exists(_logPath)) return;
            try
            {
                bool gl2 = false;
                foreach (string raw in File.ReadAllLines(_logPath, Encoding.UTF8))
                {
                    string line = (raw ?? "").Trim();
                    if (line == "#GL3" || line == "#GL4") { gl2 = true; continue; }
                    if (line.StartsWith("#GL", StringComparison.Ordinal)) { gl2 = false; continue; }
                    if (!gl2) continue;
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.Length == 0) continue;
                    string[] v = line.Split('|');
                    DateTime time; if (!DateTime.TryParseExact(v[0], "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) continue;
                    Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int i = 1; i < v.Length; i++) { int split = v[i].IndexOf('='); if (split > 0) fields[v[i].Substring(0, split)] = v[i].Substring(split + 1); }
                    ScanPerformanceEntry entry = new ScanPerformanceEntry { Time = time, WarmupEnabled = GetBool(fields, "H") || GetBool(fields, "RB"), EverythingEnabled = fields.ContainsKey("E") ? GetBool(fields, "E") : Get(fields, "P") == "ESDK",
                        Provider = Get(fields, "P") == "ESDK" ? "Everything SDK" : "FileSystem", WarmupHit = GetBool(fields, "H"), ReadyBeforeRequest = GetBool(fields, "RB"), SnapshotHit = GetBool(fields, "SH"),
                        WarmupWaitMs = GetLong(fields, "WW"), SnapshotPrepareMs = GetLong(fields, "SP"), FileDiscoveryMs = GetLong(fields, "FD"),
                        ProviderQueryMs = GetLong(fields, "FQ"), IndexReconcileMs = GetLong(fields, "FS"),
                        PlanCacheReadMs = GetLong(fields, "PCR"), PlanCacheWriteMs = GetLong(fields, "PCW"),
                        FinalizeMs = GetLong(fields, "FIN"),
                        AuthorMatchMs = GetLong(fields, "AM"), UiApplyMs = GetLong(fields, "UI"), TotalResponseMs = GetLong(fields, "TR"),
                        IdentitySourceMs = GetLong(fields, "IS"), TargetDirectoryMs = GetLong(fields, "TD"),
                        InitialIndexMs = GetLong(fields, "IB"), IncrementalIndexMs = GetLong(fields, "II"),
                        PrepareRecognitionMs = GetLong(fields, "PR"), PlanningLoopMs = GetLong(fields, "PL"),
                        OnlineLookupMs = GetLong(fields, "OL"), InitialIndexBuilds = (int)GetLong(fields, "IBC"),
                        IncrementalIndexAdds = (int)GetLong(fields, "IAC"), UniqueAuthors = (int)GetLong(fields, "UA"),
                        NewAuthorFolders = (int)GetLong(fields, "NA"),
                        ParsedCacheHits = GetLong(fields, "PH"), ParsedCacheMisses = GetLong(fields, "PM"),
                        RecognitionCacheHits = GetLong(fields, "RH"), RecognitionCacheMisses = GetLong(fields, "RM"),
                        DestinationCacheHits = GetLong(fields, "DH"), DestinationCacheMisses = GetLong(fields, "DM"),
                        IndexCacheHits = (int)GetLong(fields, "IC"), IndexAdded = (int)GetLong(fields, "IA"), IndexRemoved = (int)GetLong(fields, "ID"),
                        IndexModified = (int)GetLong(fields, "IM"), IndexMoved = (int)GetLong(fields, "IV"), IndexRenamed = (int)GetLong(fields, "IR"),
                        PlanCacheHits = (int)GetLong(fields, "PC"), RecalculatedFiles = (int)GetLong(fields, "RC"),
                        StartupRestoreMs = fields.ContainsKey("SR") ? GetLong(fields, "SR") : -1,
                        BackgroundValidationMs = fields.ContainsKey("BV") ? GetLong(fields, "BV") : -1,
                        EverythingQueryCount = fields.ContainsKey("EQ") ? (int)GetLong(fields, "EQ") : -1,
                        FirstInteractiveMs = fields.ContainsKey("FI") ? GetLong(fields, "FI") : -1,
                        StartupWindowShownMs = fields.ContainsKey("WS") ? GetLong(fields, "WS") : -1,
                        StartupInitializationStages = Get(fields, "SI"),
                        StartupUiTrace = Get(fields, "SU"),
                        ActualRecognitions = fields.ContainsKey("AR") ? (int)GetLong(fields, "AR") : -1,
                        SdkPrepareMs = fields.ContainsKey("SP") ? GetLong(fields, "SP") : -1,
                        EverythingWaitMs = fields.ContainsKey("EW") ? GetLong(fields, "EW") : -1,
                        EverythingReadMs = fields.ContainsKey("ER") ? GetLong(fields, "ER") : -1,
                        DiscoveryCheckMs = fields.ContainsKey("DC") ? GetLong(fields, "DC") : -1,
                        DiscoverySetMs = fields.ContainsKey("DS") ? GetLong(fields, "DS") : -1,
                        DiscoveryEnumerateMs = fields.ContainsKey("DN") ? GetLong(fields, "DN") : -1,
                        DiscoveryCompareMs = fields.ContainsKey("DCM") ? GetLong(fields, "DCM") : -1,
                        CandidateCount = (int)GetLong(fields, "C"), ResultCount = (int)GetLong(fields, "R") };
                    Entries.Add(entry);
                    if (Entries.Count > 1000) Entries.RemoveAt(0);
                }
            }
            catch { Entries.Clear(); }
        }
        private static string Get(Dictionary<string, string> fields, string key) { string value; return fields.TryGetValue(key, out value) ? value : ""; }
        private static bool GetBool(Dictionary<string, string> fields, string key) { return Get(fields, key) == "1"; }
        private static long GetLong(Dictionary<string, string> fields, string key) { long value; return Int64.TryParse(Get(fields, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0; }
    }
}
