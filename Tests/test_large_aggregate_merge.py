"""Portable large-aggregate SQLite merge stress test (102,582 tag identities)."""
from pathlib import Path
import sqlite3,re,tempfile,time
root=Path(__file__).resolve().parents[1]/'Services'
schema=re.search(r'CreateSql = @"(.*?)";', (root/'PublicIndexSchema.cs').read_text('utf-8'), re.S).group(1)
merge=re.search(r'private const string MergeSql = @"(.*?)";', (root/'PublicAuthorIndexMergeService.cs').read_text('utf-8'), re.S).group(1)
with tempfile.TemporaryDirectory() as temp:
    src=Path(temp)/'evidence.db'; base=Path(temp)/'public.db'
    b=sqlite3.connect(base);b.executescript(schema);b.commit();b.close()
    e=sqlite3.connect(src)
    e.executescript('''CREATE TABLE SourceFile(SourceId TEXT,Path TEXT,Sha TEXT,State TEXT,UpdatedUtc TEXT, PRIMARY KEY(SourceId,Path));
      CREATE TABLE EntityEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT,EntityType TEXT,Name TEXT,NormalizedName TEXT,Status TEXT,EntityKey TEXT,PrettyName TEXT,NormalizedPretty TEXT, PRIMARY KEY(SourceId,SourceFile,GalleryId,EntityType,NormalizedName));
      CREATE TABLE WorkEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT, PRIMARY KEY(SourceId,GalleryId,SourceFile));
      CREATE INDEX IX_EntityEvidence_Name ON EntityEvidence(EntityType,NormalizedName);
      INSERT INTO SourceFile VALUES('eh-tag-aggregate','tag-aggregate','abc','imported','now');''')
    records=(( 'eh-tag-aggregate','tag-aggregate','tag:'+kind+':'+name,kind,name,name,'candidate','',name,name)
            for kind,number in [('Artist',80000),('Group',22582)] for n in range(number) for name in (kind.lower()+str(n),))
    e.executemany('INSERT INTO EntityEvidence VALUES(?,?,?,?,?,?,?,?,?,?)',records)
    e.commit();e.close()
    for run in (1,2):
        c=sqlite3.connect(base)
        c.execute('ATTACH DATABASE ? AS refdata',(str(src),))
        start=time.monotonic()
        c.executescript('BEGIN IMMEDIATE;'+merge+'COMMIT;')
        elapsed=round(time.monotonic()-start,2)
        stats={t:c.execute('SELECT COUNT(*) FROM '+t).fetchone()[0] for t in ('EntityData','ProviderIdentityData','ArtistGroup','PublicMergeConflict')}
        assert stats=={'EntityData':102582,'ProviderIdentityData':102582,'ArtistGroup':0,'PublicMergeConflict':0},stats
        print('PASS: large aggregate round',run,'elapsed seconds',elapsed,'entities',stats['EntityData'],flush=True)
        c.close()
