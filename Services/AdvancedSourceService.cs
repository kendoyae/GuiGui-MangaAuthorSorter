using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class AdvancedSourceDefinition
    {
        public string Id, Name, Kind, FileName, Repository, AssetName, ReleaseTag, Url, Description;
        public AdvancedSourceDefinition(string id, string name, string kind, string file, string repository, string tag, string asset, string detail)
        {
            Id=id; Name=name; Kind=kind; FileName=file; Repository=repository; ReleaseTag=tag; AssetName=asset;
            Url=kind=="csv" ? "https://github.com/"+repository :
                "https://github.com/"+repository+"/releases/"+(tag=="latest"?"latest/download/":"download/"+tag+"/")+asset;
            Description=detail;
        }
    }
    internal sealed class AdvancedRemoteMetadata { public string Details; public long Size; public DateTime UpdatedUtc; }
    internal sealed class AdvancedSourceProgress { public string Message; public long Current; public long Total; }

    internal static class AdvancedSourceService
    {
        public static readonly AdvancedSourceDefinition[] Sources =
        {
            new AdvancedSourceDefinition("eh-current", "E-Hentai Current", "sqlite-zstd", "e-hentai.db.zstd",
                "URenko/e-hentai-db", "nightly", "e-hentai.db.zstd",
                "E-Hentai 画廊 SQLite（ZSTD）；完整作品标签、作者及社团关系。过滤 .zstd 时需要外部 zstd.exe，亦可选择已解压 .db。"),
            new AdvancedSourceDefinition("nh-metadata-archive", "nh-metadata-archive", "csv", "nh-metadata-archive.csv",
                "van-geaux/nh-metadata-archive", "main", "", "按月份 CSV 增量更新；Artist/Group、标题和画廊 ID。"),
            new AdvancedSourceDefinition("eh-tag-aggregate", "E-Hentai Tag Aggregate", "sqlite-gzip", "aggregated.sqlite.gz",
                "EhTagTranslation/EhTagDb", "latest", "aggregated.sqlite.gz",
                "聚合标签来源：artist/group 命名空间和标签出现次数。只证实标签存在，不把聚合样例当成作品关系。")
        };
        public static Task<AdvancedSourceDefinition[]> GetConfiguredSourcesAsync()
        {
            return Task.Run(delegate
            {
                // Only URLs of known parser types are remotely configurable. Config cannot introduce code.
                AdvancedSourceDefinition[] result = Sources.Select(x => new AdvancedSourceDefinition(x.Id,x.Name,x.Kind,x.FileName,x.Repository,x.ReleaseTag,x.AssetName,x.Description)).ToArray();
                ReferenceSourcesConfig config=AuthorReferenceLibraryService.Current.GetSourcesConfiguration(CancellationToken.None);
                if (config.advancedSources == null) return result;
                foreach(AdvancedBuilderSourceConfig item in config.advancedSources)
                {
                    if (!item.enabled) continue;
                    foreach(AdvancedSourceDefinition source in result)
                    {
                        if (source.Id != item.id) continue;
                        source.Repository=item.repository;source.ReleaseTag=item.releaseTag;
                        source.AssetName=item.assetName;source.FileName=item.assetName;
                        source.Url="https://github.com/"+item.repository+"/releases/"+
                            (item.releaseTag=="latest"?"latest/download/":"download/"+item.releaseTag+"/")+item.assetName;
                    }
                }
                return result;
            });
        }
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private static HttpWebRequest Request(string url)
        {
            Uri uri=new Uri(url);
            if(uri.Scheme!=Uri.UriSchemeHttps) throw new InvalidDataException("HTTPS required");
            HttpWebRequest req=(HttpWebRequest)WebRequest.Create(uri);
            req.UserAgent="GuiGui/"+AppVersion.UserAgentVersion;
            req.Timeout=30000;req.ReadWriteTimeout=45000;
            try {ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;} catch{}
            return req;
        }
        private static bool Trusted(string url)
        {
            Uri uri;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps) return false;
            string host=uri.Host.ToLowerInvariant();
            return host=="github.com"||host=="raw.githubusercontent.com"||host=="release-assets.githubusercontent.com"||
                host=="objects.githubusercontent.com"||host=="api.github.com";
        }
        private static Dictionary<string,object> LoadJson(string url, CancellationToken token)
        {
            HttpWebRequest req=Request(url);req.Accept="application/vnd.github+json";
            using(token.Register(delegate {try{req.Abort();}catch{}}))
            using(HttpWebResponse res=(HttpWebResponse)req.GetResponse())
            using(StreamReader reader=new StreamReader(res.GetResponseStream(),Encoding.UTF8))
            {
                if(!Trusted(res.ResponseUri.AbsoluteUri)) throw new InvalidDataException("Untrusted GitHub redirect");
                string content=reader.ReadToEnd();
                if(content.Length>2*1024*1024)throw new InvalidDataException("GitHub manifest too large");
                return Json.DeserializeObject(content) as Dictionary<string,object>;
            }
        }
        public static Task<AdvancedRemoteMetadata> CheckAsync(AdvancedSourceDefinition source, CancellationToken token)
        {
            return Task.Run(delegate
            {
                if(source.Kind=="csv")
                {
                    var info=AuthorReferenceLibraryService.Current.GetRemoteInfoAsync(true).GetAwaiter().GetResult();
                    return new AdvancedRemoteMetadata{Size=info.Size,UpdatedUtc=info.UpdatedUtc,
                        Details=info.ChangedFiles+" 文件 / "+info.TotalFiles+" 总计 / 待处理 "+info.Size+" 字节"};
                }
                string api="https://api.github.com/repos/"+source.Repository+"/releases/"+
                    (source.ReleaseTag=="latest" ? "latest" : "tags/"+Uri.EscapeDataString(source.ReleaseTag));
                Dictionary<string,object> payload=LoadJson(api,token);
                if(payload==null)throw new InvalidDataException("Invalid release metadata");
                object assets; if(!payload.TryGetValue("assets",out assets))throw new InvalidDataException("No GitHub release assets");
                foreach(object raw in (object[])assets)
                {
                    var item=raw as Dictionary<string,object>;
                    if(item==null||Convert.ToString(item["name"])!=source.AssetName)continue;
                    long size=Convert.ToInt64(item["size"]);
                    DateTime utc; DateTime.TryParse(Convert.ToString(payload["published_at"]),out utc);
                    return new AdvancedRemoteMetadata {Size=size,UpdatedUtc=utc,
                        Details=source.AssetName+" · "+(size/1024.0/1024.0).ToString("0.0")+" MB · " + Convert.ToString(payload["tag_name"])};
                }
                throw new InvalidDataException("Asset not found: "+source.AssetName);
            },token);
        }
        public static Task<string> DownloadCsvAsync(AuthorReferenceRemoteInfo info, string directory,
            IProgress<AdvancedSourceProgress> progress, CancellationToken token, AdvancedOperationControl control = null)
        {
            return Task.Run(delegate
            {
                if (info==null) throw new ArgumentNullException("info");
                string output=Path.Combine(directory,"nh-metadata-archive");Directory.CreateDirectory(output);
                var active=new HashSet<string>(info.All.Where(x=>x.Source=="nh-metadata-archive").Select(x=>Path.GetFileName(x.Path)),StringComparer.OrdinalIgnoreCase);
                string obsolete=Path.Combine(output,"obsolete");
                foreach(string old in Directory.GetFiles(output,"*.csv",SearchOption.TopDirectoryOnly))
                {
                    if(active.Contains(Path.GetFileName(old)))continue;
                    Directory.CreateDirectory(obsolete);
                    string moved=Path.Combine(obsolete,Path.GetFileName(old));
                    if(File.Exists(moved))File.Delete(moved);
                    File.Move(old,moved);
                    if(File.Exists(old+".sha"))
                    {
                        if(File.Exists(moved+".sha"))File.Delete(moved+".sha");
                        File.Move(old+".sha",moved+".sha");
                    }
                }
                long done=0;
                foreach(ReferenceSourceFile item in info.Changed)
                {
                    token.ThrowIfCancellationRequested();
                    if(item.Source!="nh-metadata-archive")continue;
                    // Every file is validated against the GitHub blob before it is retained.
                    string target=Path.Combine(output,Path.GetFileName(item.Path));
                    string partial=target+".part";
                    HttpWebRequest request=Request(item.Url);
                    try
                    {
                        using(token.Register(delegate{try{request.Abort();}catch{}}))
                        using(HttpWebResponse reply=(HttpWebResponse)request.GetResponse())
                        using(Stream input=reply.GetResponseStream())
                        using(FileStream file=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None))
                        {
                            if(!Trusted(reply.ResponseUri.AbsoluteUri))throw new InvalidDataException("Untrusted CSV redirect");
                            byte[] chunk=new byte[65536];int n;long length=0;
                            while((n=input.Read(chunk,0,chunk.Length))>0)
                            {
                                if(control!=null)control.Checkpoint(token); else token.ThrowIfCancellationRequested();length+=n;
                                if(length>item.Size)throw new InvalidDataException("CSV size exceeds GitHub manifest");
                                file.Write(chunk,0,n);
                                if(progress!=null)progress.Report(new AdvancedSourceProgress{Message="下载 "+item.Path,Current=done+length,Total=info.Size});
                            }
                            if(length!=item.Size)throw new InvalidDataException("Incomplete CSV download");
                        }
                        byte[] content=File.ReadAllBytes(partial);
                        byte[] prefix=Encoding.ASCII.GetBytes("blob "+content.Length+"\0");
                        string sha;
                        using(System.Security.Cryptography.SHA1 digest=System.Security.Cryptography.SHA1.Create())
                        {
                            digest.TransformBlock(prefix,0,prefix.Length,prefix,0);
                            digest.TransformFinalBlock(content,0,content.Length);
                            sha=BitConverter.ToString(digest.Hash).Replace("-","").ToLowerInvariant();
                        }
                        if(!sha.Equals(item.Sha,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("GitHub CSV SHA mismatch");
                        if(File.Exists(target))File.Delete(target);
                        File.Move(partial,target);
                        File.WriteAllText(target+".sha",item.Sha,Encoding.ASCII);
                        done+=item.Size;
                    }
                    finally {if(File.Exists(partial))File.Delete(partial);}
                }
                return output;
            },token);
        }
        public static Task<string> DownloadAsync(AdvancedSourceDefinition source,string directory,IProgress<AdvancedSourceProgress> progress,CancellationToken token,AdvancedOperationControl control = null)
        {
            return Task.Run(delegate
            {
                if(source.Kind=="csv")throw new NotSupportedException("CSV files use the incremental GitHub downloader");
                Directory.CreateDirectory(directory);
                string destination=Path.Combine(directory,source.FileName);
                string partial=destination+".part";
                if(!Trusted(source.Url))throw new InvalidDataException("Untrusted source URL");
                HttpWebRequest req=Request(source.Url);
                try
                {
                    using(token.Register(delegate {try{req.Abort();}catch{}}))
                    using(HttpWebResponse res=(HttpWebResponse)req.GetResponse())
                    using(Stream incoming=res.GetResponseStream())
                    using(FileStream file=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,65536))
                    {
                        if(!Trusted(res.ResponseUri.AbsoluteUri))throw new InvalidDataException("Untrusted download redirect");
                        const long maximum=4L*1024*1024*1024;
                        if(res.ContentLength>maximum)throw new InvalidDataException("Data source exceeds 4 GB safety limit");
                        long copied=0;byte[] buffer=new byte[131072];int count;
                        while((count=incoming.Read(buffer,0,buffer.Length))>0)
                        {
                            if(control!=null)control.Checkpoint(token); else token.ThrowIfCancellationRequested();copied+=count;
                            if(copied>maximum)throw new InvalidDataException("Data source exceeds safety limit");
                            file.Write(buffer,0,count);
                            if(progress!=null)progress.Report(new AdvancedSourceProgress{Message="下载 "+source.Name+"："+(copied/1024/1024)+" MB",Current=copied,Total=res.ContentLength});
                        }
                        file.Flush(true);
                        if(res.ContentLength>=0&&copied!=res.ContentLength)throw new InvalidDataException("Incomplete download");
                    }
                    if(File.Exists(destination))File.Delete(destination);
                    File.Move(partial,destination);
                    return destination;
                }
                catch { if(File.Exists(partial))File.Delete(partial); throw; }
            },token);
        }
    }
}
