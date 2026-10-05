using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    public sealed class HistoryDatabase
    {
        public int Version { get; set; }
        public List<HistorySession> Sessions { get; set; }

        public HistoryDatabase()
        {
            Version = 1;
            Sessions = new List<HistorySession>();
        }
    }

    public sealed class HistorySession
    {
        public string Id { get; set; }
        public string ExecutedAt { get; set; }
        public string SourceRoot { get; set; }
        public string TargetRoot { get; set; }
        public string ScanRange { get; set; }
        public string FileTypeProfileId { get; set; }
        public string FileTypeProfileName { get; set; }
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public List<HistoryItem> Items { get; set; }

        public HistorySession()
        {
            Id = "";
            ExecutedAt = "";
            SourceRoot = "";
            TargetRoot = "";
            ScanRange = "";
            FileTypeProfileId = "";
            FileTypeProfileName = "";
            Items = new List<HistoryItem>();
        }
    }

    public sealed class HistoryItem
    {
        public string FileName { get; set; }
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public string Author { get; set; }
        public string MatchedAs { get; set; }
        public string RecognitionCode { get; set; }
        public string MatchWhy { get; set; }
        public string ResultCode { get; set; }
        public string ResultMessage { get; set; }

        public HistoryItem()
        {
            FileName = "";
            SourcePath = "";
            TargetPath = "";
            Author = "";
            MatchedAs = "";
            RecognitionCode = "";
            MatchWhy = "";
            ResultCode = "";
            ResultMessage = "";
        }
    }

    internal sealed class HistoryStore
    {
        private readonly string _path;
        private readonly object _sync = new object();

        public HistoryStore(string path)
        {
            _path = path;
        }

        public string PathName { get { return _path; } }

        public List<HistorySession> LoadSessions()
        {
            lock (_sync)
            {
                HistoryDatabase db = LoadDatabase();
                return db.Sessions
                    .Where(delegate(HistorySession s) { return s != null; })
                    .OrderByDescending(delegate(HistorySession s) { return s.ExecutedAt ?? ""; })
                    .ToList();
            }
        }

        public void AddSession(HistorySession session)
        {
            if (session == null)
                throw new ArgumentNullException("session");

            lock (_sync)
            {
                HistoryDatabase db = LoadDatabase();
                if (db.Sessions == null)
                    db.Sessions = new List<HistorySession>();

                db.Sessions.Add(session);
                SaveDatabase(db);
            }
        }

        public bool DeleteSession(string id)
        {
            if (String.IsNullOrWhiteSpace(id))
                return false;

            lock (_sync)
            {
                HistoryDatabase db = LoadDatabase();
                int before = db.Sessions.Count;
                db.Sessions = db.Sessions
                    .Where(delegate(HistorySession s)
                    {
                        return !String.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                if (db.Sessions.Count == before)
                    return false;

                SaveDatabase(db);
                return true;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                SaveDatabase(new HistoryDatabase());
            }
        }

        public string ExportSessionCsv(HistorySession session, string path)
        {
            if (session == null)
                throw new ArgumentNullException("session");
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Export path is empty.", "path");

            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("ExecutedAt,SourceRoot,TargetRoot,ScanRange,FileTypeProfile,Total,Success,Skipped,Failed");
                writer.WriteLine(
                    Csv(session.ExecutedAt) + "," +
                    Csv(session.SourceRoot) + "," +
                    Csv(session.TargetRoot) + "," +
                    Csv(session.ScanRange) + "," +
                    Csv(session.FileTypeProfileName) + "," +
                    session.TotalCount.ToString() + "," +
                    session.SuccessCount.ToString() + "," +
                    session.SkippedCount.ToString() + "," +
                    session.FailedCount.ToString());

                writer.WriteLine();
                writer.WriteLine("FileName,SourcePath,TargetPath,Author,MatchedAs,RecognitionCode,MatchWhy,ResultCode,ResultMessage");

                foreach (HistoryItem item in session.Items ?? new List<HistoryItem>())
                {
                    writer.WriteLine(
                        Csv(item.FileName) + "," +
                        Csv(item.SourcePath) + "," +
                        Csv(item.TargetPath) + "," +
                        Csv(item.Author) + "," +
                        Csv(item.MatchedAs) + "," +
                        Csv(item.RecognitionCode) + "," +
                        Csv(item.MatchWhy) + "," +
                        Csv(item.ResultCode) + "," +
                        Csv(item.ResultMessage));
                }
            }

            return path;
        }

        private HistoryDatabase LoadDatabase()
        {
            if (!File.Exists(_path))
                return new HistoryDatabase();

            string json = File.ReadAllText(_path, Encoding.UTF8);
            if (String.IsNullOrWhiteSpace(json))
                return new HistoryDatabase();

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = Int32.MaxValue;
                HistoryDatabase db = serializer.Deserialize<HistoryDatabase>(json);
                if (db == null)
                    db = new HistoryDatabase();
                if (db.Sessions == null)
                    db.Sessions = new List<HistorySession>();
                return db;
            }
            catch
            {
                try
                {
                    string backup = _path + ".corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";
                    File.Copy(_path, backup, true);
                }
                catch
                {
                    // Keep going with an empty database. A best-effort backup was attempted.
                }

                return new HistoryDatabase();
            }
        }

        private void SaveDatabase(HistoryDatabase db)
        {
            string dir = Path.GetDirectoryName(_path);
            if (!String.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            string json = serializer.Serialize(db ?? new HistoryDatabase());

            string temp = _path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(true));

            try
            {
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temp, _path, null);
                    }
                    catch
                    {
                        File.Copy(temp, _path, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch { }
                }
            }
        }

        private static string Csv(string value)
        {
            string s = value ?? "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
