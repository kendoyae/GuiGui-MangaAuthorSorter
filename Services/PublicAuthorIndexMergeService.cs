using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    /// <summary>
    /// Rebuilds a local public-index view from the official index plus user-downloaded
    /// reference evidence. The original official snapshot is never edited; a staged
    /// database is atomically activated at the NEXT startup before SQLite readers open.
    /// This makes replacement/removal of CSV files idempotent and reversible.
    /// AuthorEntities.json is neither opened nor changed here.
    /// </summary>
    internal static class PublicAuthorIndexMergeService
    {
        private sealed class MergeManifest
        {
            public string OfficialSha { get; set; }
            public string LastMergedSha { get; set; }
            public string ExpectedActiveSha { get; set; }
            public string PendingSha { get; set; }
            public string PendingOfficialSha { get; set; }
        }

        private static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string Active = System.IO.Path.Combine(Root, AppFiles.AuthorIndexDatabase);
        private static readonly string Official = Active + ".official-base";
        private static readonly string Pending = Active + ".pending";
        private static readonly string OfficialNext = Official + ".next";
        private static readonly string State = Active + ".merge-state.json";
        private static readonly object Gate = new object();
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private static MergeManifest ReadManifest()
        {
            try { return File.Exists(State) ? Json.Deserialize<MergeManifest>(File.ReadAllText(State)) : null; }
            catch { return null; }
        }
        private static void SaveManifest(MergeManifest value)
        {
            string temporary = State + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(State)) File.Replace(temporary, State, null);
            else File.Move(temporary, State);
        }
        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string QuoteSql(string value) { return "'" + value.Replace("'", "''") + "'"; }

        public static bool HasActiveIndex { get { return File.Exists(Active); } }
        public static bool HasPendingIndex { get { return File.Exists(Pending); } }
        public static bool HasBuilderSchemaIndex
        {
            get
            {
                if (!File.Exists(Active)) return false;
                try { VerifyIndex(Active); return true; } catch { return false; }
            }
        }
        /// <summary>Explicit user-requested fresh builder v4 base; backs up an old schema without importing it.</summary>
        public static void PrepareFreshBase()
        {
            lock (Gate)
            {
                if (File.Exists(Pending) || File.Exists(OfficialNext))
                    throw new InvalidOperationException("存在尚未生效的更新，请先重启归归后再建立新基线。");
                string before = File.Exists(Active) ? Sha256(Active) : "";
                if (File.Exists(Active))
                {
                    string backup = Active + ".before-v4-build.bak";
                    if (File.Exists(backup)) throw new InvalidOperationException("旧公共库备份已存在，请先手动移走并核实备份后重试："+backup);
                    File.Copy(Active,backup,false);
                }
                if (File.Exists(Official)) File.Delete(Official);
                CreateEmptyOfficial();
                SaveManifest(new MergeManifest {OfficialSha=Sha256(Official),LastMergedSha=before,ExpectedActiveSha=before});
            }
        }

        private static void VerifyIndex(string file)
        {
            if (!File.Exists(file) || new FileInfo(file).Length < 8192)
                throw new InvalidDataException("Public author database file is empty or missing.");
            using (var db = new NativeSqlite(file))
            {
                if (db.Scalar("PRAGMA quick_check;") != "ok")
                    throw new InvalidDataException("Public index SQLite integrity check failed.");
                if (db.Scalar("PRAGMA user_version;") != "4")
                    throw new InvalidDataException("当前 GuiGuiAuthorIndex.db 不是构建器 Schema v4。请先备份，下载或导入 Schema v4 官方库；高级构建也可选择「新建空库」从零构建。");
                foreach (string table in new[] { "DataSource", "EntityData", "EntityAliasData", "ProviderIdentityData", "ArtistGroup", "ArtistGroupSource" })
                    if (db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=" + QuoteSql(table)) != "1")
                        throw new InvalidDataException("Public index missing table: " + table);
                foreach (string view in new[] { "Entity", "EntityAlias", "ProviderIdentity" })
                    if (db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='view' AND name=" + QuoteSql(view)) != "1")
                        throw new InvalidDataException("Public index missing view: " + view);
                if (db.Scalar("SELECT COUNT(*) FROM Entity WHERE EntityType NOT IN ('Artist','Group');") != "0")
                    throw new InvalidDataException("Public index includes invalid entity types.");
                if (db.Scalar("PRAGMA foreign_key_check;") != "")
                    throw new InvalidDataException("Public index has broken foreign keys.");
            }
        }

        private static void CreateEmptyOfficial()
        {
            string temporary = Official + ".creating";
            if (File.Exists(temporary)) File.Delete(temporary);
            try
            {
                using (var db = new NativeSqlite(temporary, true))
                    db.Execute("PRAGMA foreign_keys=ON;" + PublicIndexSchema.CreateSql);
                VerifyIndex(temporary);
                File.Move(temporary, Official);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void PromoteOfficial(MergeManifest m)
        {
            if (m == null || String.IsNullOrEmpty(m.PendingOfficialSha)) return;
            if (!File.Exists(OfficialNext) || !String.Equals(Sha256(OfficialNext), m.PendingOfficialSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pending official baseline is missing or changed.");
            VerifyIndex(OfficialNext);
            if (File.Exists(Official)) File.Replace(OfficialNext, Official, Official + ".previous.bak");
            else File.Move(OfficialNext, Official);
            m.OfficialSha = m.PendingOfficialSha;
            m.PendingOfficialSha = "";
            SaveManifest(m);
        }

        /// <summary>Called before MainForm opens any SQLite readers.</summary>
        public static bool ActivatePending()
        {
            lock (Gate)
            {
                MergeManifest m = ReadManifest();
                if (!File.Exists(Pending))
                {
                    // Recover a process interruption between index activation and official-base promotion.
                    if (m != null && !String.IsNullOrEmpty(m.PendingOfficialSha) &&
                        File.Exists(Active) && String.Equals(Sha256(Active), m.LastMergedSha, StringComparison.OrdinalIgnoreCase))
                        PromoteOfficial(m);
                    return true;
                }
                string current = File.Exists(Active) ? Sha256(Active) : "";
                if (m == null ||
                    !String.Equals(Sha256(Pending), m.PendingSha, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(current, m.ExpectedActiveSha ?? "", StringComparison.OrdinalIgnoreCase))
                    return false; // Stale staged output must never overwrite a newer installation.
                if (!String.IsNullOrEmpty(m.PendingOfficialSha) &&
                    (!File.Exists(OfficialNext) || !String.Equals(Sha256(OfficialNext), m.PendingOfficialSha, StringComparison.OrdinalIgnoreCase)))
                    return false;
                VerifyIndex(Pending);
                if (File.Exists(Active)) File.Replace(Pending, Active, Active + ".pre-merge.bak");
                else File.Move(Pending, Active);
                m.LastMergedSha = m.PendingSha;
                m.ExpectedActiveSha = m.PendingSha;
                m.PendingSha = "";
                SaveManifest(m);
                PromoteOfficial(m);
                return true;
            }
        }

        /// <summary>
        /// Stage a complete official index for activation at the next startup.
        /// This ordinary update path deliberately does not open, create, or replay the
        /// optional reference-evidence database used by advanced local builds.
        /// </summary>
        public static void StageOfficialIndex(string databasePath)
        {
            lock (Gate)
            {
                if (String.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
                    throw new FileNotFoundException("Official public index is missing.", databasePath);

                VerifyIndex(databasePath);
                string currentSha = File.Exists(Active) ? Sha256(Active) : "";
                string desiredSha = Sha256(databasePath);
                string pendingBuilding = Pending + ".building";
                string officialBuilding = OfficialNext + ".building";

                try
                {
                    File.Copy(databasePath, pendingBuilding, true);
                    File.Copy(databasePath, officialBuilding, true);
                    VerifyIndex(pendingBuilding);
                    VerifyIndex(officialBuilding);
                    if (!String.Equals(Sha256(pendingBuilding), desiredSha, StringComparison.OrdinalIgnoreCase) ||
                        !String.Equals(Sha256(officialBuilding), desiredSha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Official index copy checksum mismatch.");

                    if (File.Exists(Pending)) File.Delete(Pending);
                    if (File.Exists(OfficialNext)) File.Delete(OfficialNext);
                    File.Move(pendingBuilding, Pending);
                    File.Move(officialBuilding, OfficialNext);

                    MergeManifest manifest = ReadManifest() ?? new MergeManifest();
                    manifest.ExpectedActiveSha = currentSha;
                    manifest.PendingSha = desiredSha;
                    manifest.PendingOfficialSha = desiredSha;
                    SaveManifest(manifest);
                }
                finally
                {
                    if (File.Exists(pendingBuilding)) File.Delete(pendingBuilding);
                    if (File.Exists(officialBuilding)) File.Delete(officialBuilding);
                }
            }
        }

        /// <summary>
        /// Regenerate the indexed public view from a known official base + ALL committed work evidence.
        /// Passing replacementOfficial stages an official download without overwriting active SQLite readers.
        /// No official base and no active index means an empty Schema v4 baseline is created.
        /// </summary>
        public static void BuildPending(string evidenceDatabasePath, string replacementOfficial = null,
            IProgress<AdvancedSourceProgress> progress = null, CancellationToken token = default(CancellationToken),
            AdvancedOperationControl control = null)
        {
            lock (Gate)
            {
                Checkpoint(token, control);
                Report(progress, "正在检查公共数据库和已导入证据…");
                if (!File.Exists(evidenceDatabasePath))
                    throw new FileNotFoundException("Missing imported reference evidence.", evidenceDatabasePath);
                string currentSha = File.Exists(Active) ? Sha256(Active) : "";
                MergeManifest m = ReadManifest();
                bool knownActive = m != null &&
                    (String.Equals(currentSha, m.LastMergedSha ?? "", StringComparison.OrdinalIgnoreCase) ||
                     String.Equals(currentSha, m.ExpectedActiveSha ?? "", StringComparison.OrdinalIgnoreCase));
                if (File.Exists(Active) && (!File.Exists(Official) || !knownActive))
                {
                    // An external official DB replacement is a new baseline, not local merge evidence.
                    File.Copy(Active, Official, true);
                    m = new MergeManifest { LastMergedSha = "", OfficialSha = currentSha };
                }
                if (!File.Exists(Official) && !File.Exists(Active))
                {
                    CreateEmptyOfficial();
                    m = new MergeManifest { OfficialSha = Sha256(Official), LastMergedSha = "" };
                }
                if (m == null) m = new MergeManifest { OfficialSha = Sha256(Official) };
                if (!String.IsNullOrEmpty(m.OfficialSha) && !String.Equals(Sha256(Official), m.OfficialSha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Public official baseline has changed unexpectedly.");
                string baseline = Official;
                if (!String.IsNullOrEmpty(replacementOfficial))
                {
                    string desiredSha = Sha256(replacementOfficial);
                    if (File.Exists(OfficialNext)) File.Delete(OfficialNext);
                    File.Copy(replacementOfficial, OfficialNext);
                    VerifyIndex(OfficialNext);
                    if (!String.Equals(Sha256(OfficialNext), desiredSha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Official index copy checksum mismatch.");
                    m.PendingOfficialSha = desiredSha;
                    baseline = OfficialNext;
                }
                else if (!String.IsNullOrEmpty(m.PendingOfficialSha))
                {
                    if (!File.Exists(OfficialNext) || !String.Equals(Sha256(OfficialNext), m.PendingOfficialSha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Incomplete pending official baseline; retry official download.");
                    baseline = OfficialNext;
                }
                string tmp = Pending + ".building";
                try
                {
                    Report(progress, "正在复制公共库基线…");
                    Checkpoint(token, control);
                    File.Copy(baseline, tmp, true);
                    Checkpoint(token, control);
                    using (var db = new NativeSqlite(tmp))
                    {
                        db.Execute("PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
                        db.Execute("ATTACH DATABASE " + QuoteSql(System.IO.Path.GetFullPath(evidenceDatabasePath)) + " AS refdata;");
                        db.Execute("BEGIN IMMEDIATE TRANSACTION;");
                        Report(progress, "正在合并公共库实体与来源证据…");
                        db.SetProgressHandler(token, control, progress);
                        try
                        {
                            db.Execute(MergeSql);
                            db.ClearProgressHandler();
                            Checkpoint(token, control);
                            db.Execute("COMMIT;");
                        }
                        catch
                        {
                            db.ClearProgressHandler();
                            try { db.Execute("ROLLBACK;"); } catch { }
                            throw;
                        }
                        db.Execute("DETACH DATABASE refdata;");
                    }
                    Checkpoint(token, control);
                    Report(progress, "正在校验合并后的公共数据库…");
                    VerifyIndex(tmp);
                    if (File.Exists(Pending)) File.Delete(Pending);
                    File.Move(tmp, Pending);
                    Report(progress, "正在登记待激活的公共库版本…");
                    m.ExpectedActiveSha = currentSha;
                    m.PendingSha = Sha256(Pending);
                    SaveManifest(m);
                    Report(progress, "公共库合并完成，将在重启归归后生效。");
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }
        }

        private static void Checkpoint(CancellationToken token, AdvancedOperationControl control)
        {
            if (control != null) control.Checkpoint(token);
            else token.ThrowIfCancellationRequested();
        }
        private static void Report(IProgress<AdvancedSourceProgress> progress, string message)
        {
            if (progress != null) progress.Report(new AdvancedSourceProgress { Message = message, Current = 0, Total = 0 });
        }

        private const string MergeSql = @"
            -- Remote config may add additional sources of the existing CSV format.
            -- Unknown sources remain unverified, source-scoped archive identities.
            INSERT OR IGNORE INTO DataSource(SourceKey,DisplayName)
            SELECT DISTINCT SourceId,SourceId FROM refdata.SourceFile WHERE State='imported';

            CREATE TEMP TABLE Incoming (
                SourceId TEXT NOT NULL, Kind TEXT NOT NULL, Ns TEXT NOT NULL,
                Provider TEXT NOT NULL, ExternalKey TEXT NOT NULL,
                Name TEXT NOT NULL, Normal TEXT NOT NULL,
                PrettyName TEXT NOT NULL, PrettyNormal TEXT NOT NULL,
                WorkCount INTEGER NOT NULL, EntityId INTEGER, Conflict INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(SourceId,Kind,Normal)
            );
            INSERT INTO Incoming(SourceId,Kind,Ns,Provider,ExternalKey,Name,Normal,PrettyName,PrettyNormal,WorkCount)
            SELECT e.SourceId,e.EntityType,
                   CASE e.EntityType WHEN 'Artist' THEN 'artist' ELSE 'group' END,
                   CASE WHEN e.SourceId='nh-metadata-archive' THEN 'nHentai' WHEN e.SourceId IN ('eh-current','eh-tag-aggregate') THEN 'E-Hentai' ELSE 'Archive:' || e.SourceId END,
                   CASE WHEN e.SourceId='nh-metadata-archive' THEN 'nhentai:' WHEN e.SourceId IN ('eh-current','eh-tag-aggregate') THEN 'ehentai:' ELSE 'archive:' || e.SourceId || ':' END ||
                       CASE e.EntityType WHEN 'Artist' THEN 'artist' ELSE 'group' END || ':' || e.NormalizedName,
                   MIN(e.Name),e.NormalizedName,
                   MIN(COALESCE(NULLIF(e.PrettyName,''),e.Name)),
                   MIN(COALESCE(NULLIF(e.NormalizedPretty,''),e.NormalizedName)),
                   COUNT(DISTINCT e.GalleryId)
            FROM refdata.EntityEvidence e
            JOIN refdata.SourceFile f ON f.SourceId=e.SourceId AND f.Path=e.SourceFile
            WHERE f.State='imported' AND e.EntityType IN ('Artist','Group')
                AND LENGTH(e.NormalizedName) BETWEEN 1 AND 300
                -- If a gallery appears in several monthly files, the newest active
                -- monthly file wins. Never add evidence from stale duplicate rows.
                AND (e.SourceId='eh-tag-aggregate' OR e.SourceFile=(SELECT MAX(w.SourceFile) FROM refdata.WorkEvidence w
                    JOIN refdata.SourceFile sf ON sf.SourceId=w.SourceId AND sf.Path=w.SourceFile
                    WHERE w.SourceId=e.SourceId AND w.GalleryId=e.GalleryId AND sf.State='imported'))
            GROUP BY e.SourceId,e.EntityType,e.NormalizedName;

            -- Large tag-aggregate imports can contain 100,000+ distinct identities.
            -- The PK starts with SourceId and cannot support Kind/Normal or EntityId probes;
            -- without these indexes, correlated lookups become quadratic in tag count.
            CREATE INDEX IX_Incoming_ProviderMatch ON Incoming(Provider,Ns,Normal,SourceId);
            CREATE INDEX IX_Incoming_NameMatch ON Incoming(Kind,Normal,EntityId);
            CREATE INDEX IX_Incoming_EntityMatch ON Incoming(EntityId,Provider,Kind);

            -- Prefer a provider identity. A unique exact cross-provider match is
            -- allowed, but duplicate provider names never silently collapse.
            UPDATE Incoming SET EntityId=(
                SELECT p.EntityId FROM ProviderIdentityData p
                WHERE p.Provider=Incoming.Provider AND p.Namespace=Incoming.Ns
                  AND p.NormalizedTag=Incoming.Normal LIMIT 1
            );
            UPDATE Incoming SET Conflict=1
            WHERE EntityId IS NULL AND (
                SELECT COUNT(DISTINCT p.EntityId) FROM ProviderIdentityData p
                WHERE p.Namespace=Incoming.Ns AND p.NormalizedTag=Incoming.Normal
            ) > 1;
            -- Exact display name across DIFFERENT providers is NOT identity proof.
            -- Preserve independent entities and emit conflicts for later review.
            UPDATE Incoming SET EntityId=(
                SELECT e.Id FROM EntityData e
                WHERE e.ExternalId=Incoming.ExternalKey AND e.EntityType=Incoming.Kind LIMIT 1
            ) WHERE EntityId IS NULL AND Conflict=0;

            CREATE TABLE IF NOT EXISTS PublicMergeConflict(
                SourceId TEXT NOT NULL,EntityType TEXT NOT NULL,NormalizedName TEXT NOT NULL,
                DisplayName TEXT NOT NULL,Reason TEXT NOT NULL,EvidenceCount INTEGER NOT NULL,
                PRIMARY KEY(SourceId,EntityType,NormalizedName)
            ) WITHOUT ROWID;
            DELETE FROM PublicMergeConflict;
            INSERT INTO PublicMergeConflict(SourceId,EntityType,NormalizedName,DisplayName,Reason,EvidenceCount)
            SELECT SourceId,Kind,Normal,Name,'Multiple provider identities',WorkCount
            FROM Incoming WHERE Conflict=1;

            INSERT INTO EntityData(CanonicalName,RomanName,NormalizedCanonical,
                EHArtistTag,NHArtistTag,NHGroupTag,SourceId,ExternalId,EntityType,VerificationSource,EHNamespace,EHTag,Verified,UpdatedUtc)
            SELECT PrettyName,PrettyName,PrettyNormal,
                   CASE WHEN Kind='Artist' AND Provider='E-Hentai' THEN Name ELSE '' END,
                   CASE WHEN Kind='Artist' AND Provider='nHentai' THEN Name ELSE '' END,
                   CASE WHEN Kind='Group' AND Provider='nHentai' THEN Name ELSE '' END,
                   (SELECT Id FROM DataSource WHERE SourceKey=Incoming.SourceId),
                   ExternalKey,Kind,CASE WHEN Provider='E-Hentai' THEN 'OfflineDatabase' ELSE 'Unverified' END,
                   CASE WHEN Provider='E-Hentai' THEN Ns ELSE '' END,
                   CASE WHEN Provider='E-Hentai' THEN Name ELSE '' END,
                   0,strftime('%Y-%m-%dT%H:%M:%fZ','now')
            FROM Incoming WHERE EntityId IS NULL AND Conflict=0
               AND NOT EXISTS (SELECT 1 FROM Incoming competing
                 WHERE competing.Provider=Incoming.Provider AND competing.Ns=Incoming.Ns
                 AND competing.Normal=Incoming.Normal AND competing.SourceId<Incoming.SourceId
                 AND competing.Conflict=0 AND competing.EntityId IS NULL)
            ORDER BY SourceId,Kind,Normal;
            UPDATE Incoming SET EntityId=(
                SELECT e.Id FROM EntityData e
                WHERE e.ExternalId=Incoming.ExternalKey AND e.EntityType=Incoming.Kind LIMIT 1
            ) WHERE EntityId IS NULL AND Conflict=0;

            -- Don't double count mirrored works already summarized in the official baseline.
            -- A matching name observed in multiple sources is corroboration,
            -- NOT proof of identity. Different assigned IDs need manual review.
            CREATE TEMP TABLE AmbiguousIncomingName AS
            SELECT Kind,Normal FROM Incoming WHERE EntityId IS NOT NULL
            GROUP BY Kind,Normal HAVING COUNT(DISTINCT EntityId)>1;
            CREATE UNIQUE INDEX IX_AmbiguousIncomingName ON AmbiguousIncomingName(Kind,Normal);
            INSERT INTO PublicMergeConflict(SourceId,EntityType,NormalizedName,DisplayName,Reason,EvidenceCount)
            SELECT i.SourceId,i.Kind,i.Normal,i.Name,'Cross-source identity ambiguity',i.WorkCount
            FROM Incoming i INNER JOIN AmbiguousIncomingName d
                ON d.Kind=i.Kind AND d.Normal=i.Normal
            WHERE i.EntityId IS NOT NULL
            ON CONFLICT(SourceId,EntityType,NormalizedName) DO UPDATE SET
                Reason=excluded.Reason,EvidenceCount=excluded.EvidenceCount;

            INSERT INTO ProviderIdentityData(EntityId,Provider,Namespace,Tag,NormalizedTag,ExternalId,SourceId,EvidenceCount)
            SELECT EntityId,Provider,Ns,Name,Normal,ExternalKey,
                   (SELECT Id FROM DataSource WHERE SourceKey=Incoming.SourceId),WorkCount
            FROM Incoming WHERE EntityId IS NOT NULL AND Conflict=0
            ON CONFLICT(Provider,Namespace,NormalizedTag) DO UPDATE SET
              EvidenceCount=MAX(ProviderIdentityData.EvidenceCount,excluded.EvidenceCount);

            INSERT OR IGNORE INTO EntityAliasData(EntityId,Alias,NormalizedAlias,AliasType,SourceId)
            SELECT EntityId,Name,Normal,'provider_tag',
                   (SELECT Id FROM DataSource WHERE SourceKey=Incoming.SourceId)
            FROM Incoming WHERE EntityId IS NOT NULL AND Conflict=0;
            INSERT OR IGNORE INTO EntityAliasData(EntityId,Alias,NormalizedAlias,AliasType,SourceId)
            SELECT EntityId,PrettyName,PrettyNormal,'roman',
                   (SELECT Id FROM DataSource WHERE SourceKey=Incoming.SourceId)
            FROM Incoming WHERE EntityId IS NOT NULL AND Conflict=0;

            -- Only nHentai metadata can supply NH fields; an external CSV
            -- must never be promoted to an E-Hentai provider identity.
            UPDATE EntityData SET
                NHArtistTag=CASE WHEN EntityType='Artist' AND NHArtistTag=''
                    THEN COALESCE((SELECT Name FROM Incoming i WHERE i.EntityId=EntityData.Id
                        AND i.Provider='nHentai' AND i.Kind='Artist' LIMIT 1),'')
                    ELSE NHArtistTag END,
                NHGroupTag=CASE WHEN EntityType='Group' AND NHGroupTag=''
                    THEN COALESCE((SELECT Name FROM Incoming i WHERE i.EntityId=EntityData.Id
                        AND i.Provider='nHentai' AND i.Kind='Group' LIMIT 1),'')
                    ELSE NHGroupTag END
            WHERE Id IN (SELECT EntityId FROM Incoming
                         WHERE EntityId IS NOT NULL AND Conflict=0 AND Provider='nHentai');

            CREATE TEMP TABLE Pairs(SourceId TEXT NOT NULL,ArtistId INTEGER NOT NULL,
                GroupId INTEGER NOT NULL,WorkCount INTEGER NOT NULL,
                PRIMARY KEY(SourceId,ArtistId,GroupId));
            INSERT INTO Pairs
            SELECT a.SourceId,ia.EntityId,ig.EntityId,
                COUNT(DISTINCT a.GalleryId)
            FROM refdata.EntityEvidence a
            JOIN refdata.EntityEvidence g ON a.SourceId=g.SourceId AND a.SourceFile=g.SourceFile
                AND a.GalleryId=g.GalleryId AND a.EntityType='Artist' AND g.EntityType='Group'
            JOIN refdata.SourceFile f ON f.SourceId=a.SourceId AND f.Path=a.SourceFile
            JOIN Incoming ia ON ia.SourceId=a.SourceId AND ia.Kind='Artist' AND ia.Normal=a.NormalizedName
            JOIN Incoming ig ON ig.SourceId=g.SourceId AND ig.Kind='Group' AND ig.Normal=g.NormalizedName
            WHERE f.State='imported' AND a.SourceId<>'eh-tag-aggregate'
                AND a.SourceFile=(SELECT MAX(w.SourceFile) FROM refdata.WorkEvidence w
                    JOIN refdata.SourceFile sf ON sf.SourceId=w.SourceId AND sf.Path=w.SourceFile
                    WHERE w.SourceId=a.SourceId AND w.GalleryId=a.GalleryId AND sf.State='imported')
                AND ia.Conflict=0 AND ig.Conflict=0 AND ia.EntityId IS NOT NULL AND ig.EntityId IS NOT NULL
                AND ia.EntityId<>ig.EntityId
            GROUP BY a.SourceId,ia.EntityId,ig.EntityId;

            INSERT INTO ArtistGroup(ArtistId,GroupId,EvidenceCount)
            SELECT ArtistId,GroupId,MAX(WorkCount) FROM Pairs
            GROUP BY ArtistId,GroupId
            ON CONFLICT(ArtistId,GroupId) DO UPDATE SET
                EvidenceCount=MAX(ArtistGroup.EvidenceCount,excluded.EvidenceCount);
            INSERT INTO ArtistGroupSource(ArtistId,GroupId,SourceId,EvidenceCount)
            SELECT ArtistId,GroupId,SourceId,WorkCount FROM Pairs WHERE 1
            ON CONFLICT(ArtistId,GroupId,SourceId) DO UPDATE SET
                EvidenceCount=MAX(ArtistGroupSource.EvidenceCount,excluded.EvidenceCount);
        ";

        private sealed class NativeSqlite : IDisposable
        {
            private IntPtr _db;
            private SqliteProgressCallback _callback;
            private CancellationToken _token;
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate int SqliteProgressCallback(IntPtr context);
            private const int OK = 0, ROW = 100, DONE = 101;
            public NativeSqlite(string file, bool create = false)
            {
                if (Native.sqlite3_open_v2(Encoding.UTF8.GetBytes(file + "\0"), out _db, create ? 0x00000006 : 0x00000002, IntPtr.Zero) != OK || _db == IntPtr.Zero)
                    throw new IOException("Cannot open staged public index for update.");
            }
            public void Dispose() { if (_db != IntPtr.Zero) { ClearProgressHandler(); Native.sqlite3_close(_db); _db = IntPtr.Zero; } }
            public void SetProgressHandler(CancellationToken token, AdvancedOperationControl control, IProgress<AdvancedSourceProgress> progress)
            {
                _token = token;
                Stopwatch watch = Stopwatch.StartNew();
                long lastReported = 0;
                _callback = delegate(IntPtr ignored)
                {
                    if (token.IsCancellationRequested) return 1;
                    try { if (control != null) control.Checkpoint(token); }
                    catch (OperationCanceledException) { return 1; }
                    if (progress != null && watch.ElapsedMilliseconds - lastReported >= 1500)
                    {
                        lastReported = watch.ElapsedMilliseconds;
                        Report(progress, "正在合并公共库（已运行 " + watch.Elapsed.TotalSeconds.ToString("0") + " 秒）…");
                    }
                    return token.IsCancellationRequested ? 1 : 0;
                };
                Native.sqlite3_progress_handler(_db, 20000, _callback, IntPtr.Zero);
            }
            public void ClearProgressHandler()
            {
                if (_db != IntPtr.Zero) Native.sqlite3_progress_handler(_db, 0, null, IntPtr.Zero);
                _callback = null;
                _token = CancellationToken.None;
            }
            public void Execute(string sql)
            {
                IntPtr error;
                if (Native.sqlite3_exec(_db, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, out error) != OK)
                {
                    string detail = error == IntPtr.Zero ? "SQLite error" : Marshal.PtrToStringAnsi(error);
                    if (error != IntPtr.Zero) Native.sqlite3_free(error);
                    if (_token.IsCancellationRequested) throw new OperationCanceledException(_token);
                    throw new InvalidDataException("Public index merge failed: " + detail);
                }
            }
            public string Scalar(string sql)
            {
                IntPtr statement;
                if (Native.sqlite3_prepare_v2(_db, Encoding.UTF8.GetBytes(sql + "\0"), -1, out statement, IntPtr.Zero) != OK)
                    throw new InvalidDataException("Invalid SQLite validation query.");
                try
                {
                    if (Native.sqlite3_step(statement) != ROW) return "";
                    IntPtr text = Native.sqlite3_column_text(statement, 0);
                    if (text == IntPtr.Zero) return "";
                    int length = Native.sqlite3_column_bytes(statement, 0);
                    byte[] bytes = new byte[length]; Marshal.Copy(text, bytes, 0, length);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally { Native.sqlite3_finalize(statement); }
            }
            private static class Native
            {
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_open_v2(byte[] file,out IntPtr db,int flags,IntPtr vfs);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_close(IntPtr db);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_exec(IntPtr db,byte[] sql,IntPtr callback,IntPtr arg,out IntPtr error);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void sqlite3_progress_handler(IntPtr db,int n,SqliteProgressCallback callback,IntPtr context);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void sqlite3_free(IntPtr error);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int n,out IntPtr stmt,IntPtr tail);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_step(IntPtr stmt);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_finalize(IntPtr stmt);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_text(IntPtr stmt,int index);
                [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_column_bytes(IntPtr stmt,int index);
            }
        }
    }
}
