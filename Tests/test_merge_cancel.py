"""Portable simulation of SQLite progress_handler interruption and transaction rollback."""
from pathlib import Path
import sqlite3,re,tempfile
root=Path(__file__).resolve().parents[1]/'Services'
schema=re.search(r'CreateSql = @"(.*?)";', (root/'PublicIndexSchema.cs').read_text(), re.S).group(1)
merge=re.search(r'private const string MergeSql = @"(.*?)";', (root/'PublicAuthorIndexMergeService.cs').read_text(), re.S).group(1)
with tempfile.TemporaryDirectory() as d:
    db=Path(d)/'p.db';ev=Path(d)/'e.db'
    c=sqlite3.connect(db);c.executescript(schema);c.close()
    e=sqlite3.connect(ev)
    e.executescript('''CREATE TABLE SourceFile(SourceId TEXT,Path TEXT,Sha TEXT,State TEXT,UpdatedUtc TEXT,PRIMARY KEY(SourceId,Path));
    CREATE TABLE EntityEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT,EntityType TEXT,Name TEXT,NormalizedName TEXT,PrettyName TEXT,NormalizedPretty TEXT,Status TEXT,EntityKey TEXT,PRIMARY KEY(SourceId,SourceFile,GalleryId,EntityType,NormalizedName));
    CREATE INDEX IX_EntityEvidence_Name ON EntityEvidence(EntityType,NormalizedName);
    CREATE TABLE WorkEvidence(SourceId TEXT,SourceFile TEXT,GalleryId TEXT,PRIMARY KEY(SourceId,GalleryId,SourceFile));
    INSERT INTO SourceFile VALUES('eh-tag-aggregate','tag-aggregate','x','imported','now');''')
    e.executemany('INSERT INTO EntityEvidence VALUES(?,?,?,?,?,?,?,?,?,?)',
      (('eh-tag-aggregate','tag-aggregate','tag:artist:a'+str(i),'Artist','a'+str(i),'a'+str(i),'a'+str(i),'a'+str(i),'candidate','') for i in range(25000)))
    e.commit();e.close()
    c=sqlite3.connect(db);c.execute('ATTACH DATABASE ? AS refdata',(str(ev),))
    ticks=[0]
    def cancel():
        ticks[0]+=1
        return 1 if ticks[0]>10 else 0
    c.set_progress_handler(cancel,2000)
    try:
        c.executescript('BEGIN IMMEDIATE;'+merge+'COMMIT;')
        raise AssertionError('Expected progress_handler interruption')
    except sqlite3.OperationalError as err:
        assert 'interrupted' in str(err),err
        c.set_progress_handler(None,0)
        if c.in_transaction: c.rollback()
    assert c.execute('SELECT COUNT(*) FROM EntityData').fetchone()[0]==0
    assert c.execute('PRAGMA quick_check').fetchone()[0]=='ok'
    c.close()
    print('PASS: SQLite progress handler interrupts large merge and rolls back unfinished writes')
