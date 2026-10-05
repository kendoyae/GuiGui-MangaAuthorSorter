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
        public long WarmupWaitMs, SnapshotPrepareMs, FileDiscoveryMs, TargetIndexMs;
        public long CandidatePrepareMs, AuthorMatchMs, SortMs, RowBuildMs, AddRowsMs;
        public long LayoutMs, FinalizeMs, UiApplyMs, TotalResponseMs;
        public int CandidateCount, ResultCount;
    }

    internal static class ScanPerformanceDiagnostics
    {
        public const string FormatVersion = "GL1";
        public const string FieldSchema = "t,h,rs,ht,sh,pw,sp,fd,ti,cp,am,so,rm,ba,lr,uf,ui,tr,c,r";
        private static readonly object Gate = new object();
        private static readonly List<ScanPerformanceEntry> Entries = new List<ScanPerformanceEntry>();
        private static bool _enabled;
        private static bool _warmupEnabled = true;
        private static bool _everythingEnabled = true;
        private static string _logPath = "";
        private static string _lastContext = "";
        private static string _lastDate = "";

        public static event Action<ScanPerformanceEntry> EntryAdded;
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
                _everythingEnabled = everythingEnabled; _lastContext = ""; _lastDate = "";
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
        public static void Clear() { lock (Gate) Entries.Clear(); }

        private static void AppendCompactLog(ScanPerformanceEntry e)
        {
            if (String.IsNullOrWhiteSpace(_logPath)) return;
            try
            {
                StringBuilder text = new StringBuilder();
                bool header = !File.Exists(_logPath) || new FileInfo(_logPath).Length == 0 || !ContainsFormatHeader();
                if (header)
                {
                    if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 0) text.AppendLine();
                    text.AppendLine("#" + FormatVersion);
                    text.AppendLine("#DATE=" + e.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    text.AppendLine("#F=" + FieldSchema);
                    text.AppendLine("#UNIT=ms");
                    _lastContext = "";
                    _lastDate = e.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                string currentDate = e.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!header && !String.Equals(currentDate, _lastDate, StringComparison.Ordinal))
                {
                    text.AppendLine("@DATE=" + currentDate);
                    _lastDate = currentDate;
                }
                string context = "W=" + B(e.WarmupEnabled) + "|E=" + B(e.EverythingEnabled) + "|P=" + ProviderCode(e.Provider);
                if (!String.Equals(context, _lastContext, StringComparison.Ordinal))
                {
                    text.AppendLine((header ? "#CTX|" : "@CTX|") + context);
                    _lastContext = context;
                }
                text.AppendLine(String.Join("|", new string[] { e.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    B(e.WarmupHit), B(e.ReadyBeforeRequest), e.ReadyBeforeRequest ? "2" : (e.WarmupHit ? "1" : "0"),
                    B(e.SnapshotHit), N(e.WarmupWaitMs), N(e.SnapshotPrepareMs), N(e.FileDiscoveryMs), N(e.TargetIndexMs),
                    N(e.CandidatePrepareMs), N(e.AuthorMatchMs), N(e.SortMs), N(e.RowBuildMs), N(e.AddRowsMs),
                    N(e.LayoutMs), N(e.FinalizeMs), N(e.UiApplyMs), N(e.TotalResponseMs),
                    e.CandidateCount.ToString(CultureInfo.InvariantCulture), e.ResultCount.ToString(CultureInfo.InvariantCulture) }));
                File.AppendAllText(_logPath, text.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
        private static string B(bool value) { return value ? "1" : "0"; }
        private static string N(long value) { return value.ToString(CultureInfo.InvariantCulture); }
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
                bool gl1 = false, warmup = true, everything = true;
                string provider = "FileSystem";
                DateTime date = DateTime.Today;
                foreach (string raw in File.ReadAllLines(_logPath, Encoding.UTF8))
                {
                    string line = (raw ?? "").Trim();
                    if (line == "#GL1") { gl1 = true; continue; }
                    if (line.StartsWith("#GL", StringComparison.Ordinal) && line != "#GL1") { gl1 = false; continue; }
                    if (!gl1) continue;
                    if (line.StartsWith("#DATE=", StringComparison.Ordinal) || line.StartsWith("@DATE=", StringComparison.Ordinal))
                    {
                        DateTime parsedDate;
                        if (DateTime.TryParseExact(line.Substring(6), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out parsedDate)) { date = parsedDate.Date; _lastDate = line.Substring(6); }
                        continue;
                    }
                    if (line.StartsWith("#CTX|", StringComparison.Ordinal) || line.StartsWith("@CTX|", StringComparison.Ordinal))
                    {
                        string[] parts = line.Substring(5).Split('|');
                        foreach (string part in parts)
                        {
                            if (part == "W=0") warmup = false; else if (part == "W=1") warmup = true;
                            else if (part == "E=0") everything = false; else if (part == "E=1") everything = true;
                            else if (part == "P=ESDK") provider = "Everything SDK"; else if (part == "P=SYS") provider = "FileSystem";
                        }
                        continue;
                    }
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.Length == 0) continue;
                    string[] v = line.Split('|');
                    if (v.Length != 20) continue;
                    DateTime time; if (!DateTime.TryParseExact(v[0], "HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) continue;
                    long[] n = new long[15]; bool valid = true;
                    for (int i = 0; i < n.Length; i++) if (!Int64.TryParse(v[i + 5], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) { valid = false; break; }
                    if (!valid) continue;
                    Entries.Add(new ScanPerformanceEntry { Time = date.Add(time.TimeOfDay), WarmupEnabled = warmup, EverythingEnabled = everything,
                        Provider = provider, WarmupHit = v[1] == "1", ReadyBeforeRequest = v[2] == "1", SnapshotHit = v[4] == "1",
                        WarmupWaitMs = n[0], SnapshotPrepareMs = n[1], FileDiscoveryMs = n[2], TargetIndexMs = n[3], CandidatePrepareMs = n[4],
                        AuthorMatchMs = n[5], SortMs = n[6], RowBuildMs = n[7], AddRowsMs = n[8], LayoutMs = n[9], FinalizeMs = n[10],
                        UiApplyMs = n[11], TotalResponseMs = n[12], CandidateCount = (int)n[13], ResultCount = (int)n[14] });
                    if (Entries.Count > 1000) Entries.RemoveAt(0);
                }
            }
            catch { Entries.Clear(); }
        }
    }
}
