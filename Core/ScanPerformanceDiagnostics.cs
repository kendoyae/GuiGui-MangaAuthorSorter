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
        public long WarmupWaitMs, SnapshotPrepareMs, FileDiscoveryMs;
        public long AuthorMatchMs, UiApplyMs, TotalResponseMs;
        public int CandidateCount, ResultCount;
    }

    internal sealed class ScanWarmupStatusEntry
    {
        public DateTime Time;
        public ScanWarmupState State;
        public string Provider = "";
        public int CandidateCount;
        public long FileDiscoveryMs, TargetIndexMs, SnapshotPrepareMs;
    }

    internal static class ScanPerformanceDiagnostics
    {
        public const string FormatVersion = "GL3";
        private static readonly object Gate = new object();
        private static readonly List<ScanPerformanceEntry> Entries = new List<ScanPerformanceEntry>();
        private static bool _enabled;
        private static bool _warmupEnabled = true;
        private static bool _everythingEnabled = true;
        private static string _logPath = "";
        private static ScanWarmupStatusEntry _lastWarmupStatus;

        public static event Action<ScanPerformanceEntry> EntryAdded;
        public static event Action<ScanWarmupStatusEntry> WarmupStatusChanged;
        public static event Action SettingsChanged;
        public static bool WarmupEnabled
        {
            get { lock (Gate) return _warmupEnabled; }
            set { bool changed; lock (Gate) { changed = _warmupEnabled != value; _warmupEnabled = value; }
                if (changed && SettingsChanged != null) SettingsChanged(); }
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
                _logPath = logPath ?? ""; _enabled = enabled; _warmupEnabled = warmupEnabled;
                _everythingEnabled = everythingEnabled;
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
        public static void Clear() { lock (Gate) Entries.Clear(); }

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
                PositivePair(text, "WW", e.WarmupWaitMs); PositivePair(text, "SP", e.SnapshotPrepareMs); PositivePair(text, "FD", e.FileDiscoveryMs);
                PositivePair(text, "AM", e.AuthorMatchMs); PositivePair(text, "UI", e.UiApplyMs);
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
                    if (line == "#GL3") { gl2 = true; continue; }
                    if (line.StartsWith("#GL", StringComparison.Ordinal)) { gl2 = false; continue; }
                    if (!gl2) continue;
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.Length == 0) continue;
                    string[] v = line.Split('|');
                    DateTime time; if (!DateTime.TryParseExact(v[0], "yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) continue;
                    Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int i = 1; i < v.Length; i++) { int split = v[i].IndexOf('='); if (split > 0) fields[v[i].Substring(0, split)] = v[i].Substring(split + 1); }
                    ScanPerformanceEntry entry = new ScanPerformanceEntry { Time = time, WarmupEnabled = GetBool(fields, "H") || GetBool(fields, "RB"), EverythingEnabled = Get(fields, "P") == "ESDK",
                        Provider = Get(fields, "P") == "ESDK" ? "Everything SDK" : "FileSystem", WarmupHit = GetBool(fields, "H"), ReadyBeforeRequest = GetBool(fields, "RB"), SnapshotHit = GetBool(fields, "SH"),
                        WarmupWaitMs = GetLong(fields, "WW"), SnapshotPrepareMs = GetLong(fields, "SP"), FileDiscoveryMs = GetLong(fields, "FD"),
                        AuthorMatchMs = GetLong(fields, "AM"), UiApplyMs = GetLong(fields, "UI"), TotalResponseMs = GetLong(fields, "TR"),
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
