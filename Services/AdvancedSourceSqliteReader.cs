using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MangaAuthorSorter
{
    internal sealed class AdvancedGalleryRow
    {
        public string Id = "", Artists = "", Groups = "", JapaneseTitle = "", EnglishTitle = "";
        public bool IsTagAggregate;
    }
    /// <summary>
    /// Native Windows SQLite streaming reader. Reads only creator namespaces; never loads
    /// the complete large E-Hentai database in memory and requires no .NET 8 packages.
    /// </summary>
    internal sealed class AdvancedSourceSqliteReader : IDisposable
    {
        private const int OK=0, ROW=100, DONE=101;
        private IntPtr _db;
        private static byte[] U(string text) {return Encoding.UTF8.GetBytes(text+"\0");}
        private static string Q(string identifier) {return "\""+identifier.Replace("\"","\"\"")+"\"";}
        public AdvancedSourceSqliteReader(string path)
        {
            if (Native.sqlite3_open_v2(U(path),out _db,1,IntPtr.Zero)!=OK||_db==IntPtr.Zero)
                throw new InvalidDataException("无法打开 SQLite 来源文件。请先解压为 .db。");
        }
        public void Dispose(){if(_db!=IntPtr.Zero){Native.sqlite3_close(_db);_db=IntPtr.Zero;}}
        private string Text(IntPtr stmt,int column)
        {
            IntPtr text=Native.sqlite3_column_text(stmt,column);if(text==IntPtr.Zero)return "";
            int size=Native.sqlite3_column_bytes(stmt,column);byte[] data=new byte[size];
            Marshal.Copy(text,data,0,size);return Encoding.UTF8.GetString(data);
        }
        private IEnumerable<string[]> Query(string sql, int columns, CancellationToken token)
        {
            IntPtr stmt;
            if(Native.sqlite3_prepare_v2(_db,U(sql),-1,out stmt,IntPtr.Zero)!=OK||stmt==IntPtr.Zero)
                throw new InvalidDataException("无法读取 E-Hentai 数据结构："+Error());
            try
            {
                int result;
                while((result=Native.sqlite3_step(stmt))==ROW)
                {
                    token.ThrowIfCancellationRequested();
                    string[] row=new string[columns];for(int i=0;i<columns;i++)row[i]=Text(stmt,i);
                    yield return row;
                }
                if(result!=DONE)throw new InvalidDataException("读取 SQLite 数据异常："+Error());
            }
            finally{Native.sqlite3_finalize(stmt);}
        }
        private string Error(){return Marshal.PtrToStringAnsi(Native.sqlite3_errmsg(_db))??"SQLite error";}
        private HashSet<string> Columns(string table, CancellationToken token)
        {
            return new HashSet<string>(Query("PRAGMA table_info("+Q(table)+")",6,token).Select(r=>r[1]),StringComparer.OrdinalIgnoreCase);
        }
        private static string Find(HashSet<string> cols, params string[] keys)
        {foreach(string key in keys)if(cols.Contains(key))return key;return null;}
        private static string NameFromTag(string raw, string prefix)
        {return raw.Substring(prefix.Length).Trim();}
        private static void AddTag(string raw, HashSet<string> artists, HashSet<string> groups)
        {
            if(raw.StartsWith("artist:",StringComparison.OrdinalIgnoreCase))artists.Add(NameFromTag(raw,"artist:"));
            else if(raw.StartsWith("group:",StringComparison.OrdinalIgnoreCase))groups.Add(NameFromTag(raw,"group:"));
            else if(raw.StartsWith("circle:",StringComparison.OrdinalIgnoreCase))groups.Add(NameFromTag(raw,"circle:"));
        }
        public IEnumerable<AdvancedGalleryRow> Read(string kind, CancellationToken token)
        {
            var tables=new HashSet<string>(Query("SELECT name FROM sqlite_master WHERE type='table'",1,token).Select(r=>r[0]),StringComparer.OrdinalIgnoreCase);
            if(kind=="eh-tag-aggregate")
            {
                if(!tables.Contains("tag_aggregate"))throw new InvalidDataException("来源缺少 tag_aggregate 表；请检查是否选择正确的聚合库。");
                foreach(var r in Query("SELECT namespace,tag FROM tag_aggregate WHERE lower(namespace) IN ('artist','group','circle')",2,token))
                {
                    string ns=r[0].ToLowerInvariant();string name=r[1].Trim();if(name.Length==0)continue;
                    var row=new AdvancedGalleryRow {Id="tag:"+ns+":"+name, IsTagAggregate=true};
                    if(ns=="artist")row.Artists=name;else row.Groups=name;
                    yield return row;
                }
                yield break;
            }
            // Find the packed tag + gallery association found in E-Hentai dumps.
            string tagTable=null,tagId=null,tagValue=null,linkTable=null,linkTag=null,linkGallery=null,tagNamespace=null;
            foreach(string candidate in tables)
            {
                HashSet<string> c=Columns(candidate,token);
                string id=Find(c,"tag_id","tagid","id");string val=Find(c,"name","tag","value");
                if(id==null||val==null)continue;
                foreach(string linker in tables)
                {
                    if(String.Equals(linker,candidate,StringComparison.OrdinalIgnoreCase))continue;
                    HashSet<string> lc=Columns(linker,token);
                    string tid=Find(lc,"tag_id","tagid","tid");string gid=Find(lc,"gallery_id","galleryid","gid");
                    if(tid==null||gid==null)continue;
                    tagTable=candidate;tagId=id;tagValue=val;tagNamespace=Find(c,"namespace","type","category");
                    linkTable=linker;linkTag=tid;linkGallery=gid;break;
                }
                if(tagTable!=null)break;
            }
            if(tagTable==null)throw new InvalidDataException("无法识别作品→标签关联结构。当前来源须为 E-Hentai 完整 SQLite，请不要把聚合库当作品库。");
            HashSet<string> gCols=tables.Contains("gallery")?Columns("gallery",token):new HashSet<string>();
            string gidColumn=Find(gCols,"gid","gallery_id","id");
            string title=Find(gCols,"title","title_english");
            string japanese=Find(gCols,"title_jpn","title_japanese","japanese_title");
            bool join=gidColumn!=null&&(title!=null||japanese!=null);
            string englishExpr=join && title!=null?"COALESCE(g."+Q(title)+",'')":"''";
            string jpExpr=join && japanese!=null?"COALESCE(g."+Q(japanese)+",'')":"''";
            string nameExpr=tagNamespace==null ? "t."+Q(tagValue) : "(t."+Q(tagNamespace)+" || ':' || t."+Q(tagValue)+")";
            string sql="SELECT CAST(l."+Q(linkGallery)+" AS TEXT),"+nameExpr+","+englishExpr+","+jpExpr+
                " FROM "+Q(linkTable)+" l JOIN "+Q(tagTable)+" t ON l."+Q(linkTag)+"=t."+Q(tagId)+
                (join?" LEFT JOIN gallery g ON g."+Q(gidColumn)+"=l."+Q(linkGallery):"")+
                (join && gCols.Contains("expunged")?" WHERE COALESCE(g.expunged,0)=0":"")+
                " ORDER BY l."+Q(linkGallery);
            string current=null;HashSet<string> artists=new HashSet<string>(StringComparer.OrdinalIgnoreCase),groups=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string en="",jp="";
            foreach(string[] r in Query(sql,4,token))
            {
                if(current!=null && r[0]!=current)
                {
                    if(artists.Count>0||groups.Count>0)yield return new AdvancedGalleryRow{Id=current,Artists=String.Join(",",artists),Groups=String.Join(",",groups),EnglishTitle=en,JapaneseTitle=jp};
                    artists.Clear();groups.Clear();
                }
                current=r[0];en=r[2];jp=r[3];AddTag(r[1],artists,groups);
            }
            if(current!=null &&(artists.Count>0||groups.Count>0))yield return new AdvancedGalleryRow{Id=current,Artists=String.Join(",",artists),Groups=String.Join(",",groups),EnglishTitle=en,JapaneseTitle=jp};
        }
        private static class Native
        {
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern int sqlite3_open_v2(byte[] file,out IntPtr db,int flags,IntPtr vfs);
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern int sqlite3_close(IntPtr db);
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr statement,IntPtr tail);
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern int sqlite3_step(IntPtr statement);
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern int sqlite3_finalize(IntPtr statement);
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern int sqlite3_column_bytes(IntPtr statement,int column);
            [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]public static extern IntPtr sqlite3_errmsg(IntPtr db);
        }
    }
}
