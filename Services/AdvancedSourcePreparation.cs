using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace MangaAuthorSorter
{
    internal static class AdvancedSourcePreparation
    {
        public static string Prepare(string file, IProgress<AdvancedSourceProgress> progress, CancellationToken token, AdvancedOperationControl control = null)
        {
            string suffix = Path.GetExtension(file).ToLowerInvariant();
            if (suffix==".db" || suffix==".sqlite" || suffix==".sqlite3") return file;
            string target=Path.Combine(Path.GetTempPath(),"guigui-source-"+Guid.NewGuid().ToString("N")+".db");
            try
            {
                if(suffix==".gz")
                {
                    using(FileStream input=File.OpenRead(file))
                    using(GZipStream unzip=new GZipStream(input,CompressionMode.Decompress))
                    using(FileStream output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536))
                    {
                        byte[] buffer=new byte[65536];int count;long copied=0;
                        while((count=unzip.Read(buffer,0,buffer.Length))>0)
                        {
                            if(control!=null)control.Checkpoint(token); else token.ThrowIfCancellationRequested();copied+=count;
                            if(copied>16L*1024*1024*1024)throw new InvalidDataException("解压结果超过 16GB 安全上限。");
                            output.Write(buffer,0,count);
                            if(progress!=null)progress.Report(new AdvancedSourceProgress{Message="解压 GZIP："+copied/1024/1024+" MB",Current=0,Total=0});
                        }
                    }
                }
                else if(suffix==".zstd" || suffix==".zst")
                {
                    // Optional external zstd.exe, deliberately not bundled into the normal GuiGui EXE.
                    string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Modules","zstd.exe");
                    if(!File.Exists(exe)) exe="zstd.exe";
                    using(Process process=new Process())
                    {
                        process.StartInfo=new ProcessStartInfo {FileName=exe,
                            Arguments="-d -f -o \""+target+"\" \""+file+"\"",
                            UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
                        try {process.Start();} catch(Exception ex) {throw new InvalidOperationException(
                            "E-Hentai Current 是 ZSTD 压缩文件。请将可信的 zstd.exe 放到 Modules\\zstd.exe，或先自行解压为 .db，再选择文件。 "+ex.Message);}
                        using(token.Register(delegate {try{process.Kill();}catch{}}))
                        {
                            string error=process.StandardError.ReadToEnd();
                            process.WaitForExit();token.ThrowIfCancellationRequested();
                            if(process.ExitCode!=0) throw new InvalidDataException("ZSTD 解压失败："+(error.Length>400?error.Substring(0,400):error));
                        }
                    }
                }
                else throw new NotSupportedException("不支持的压缩格式："+suffix);
                using(FileStream reader=File.OpenRead(target))
                {
                    byte[] header=new byte[16];if(reader.Read(header,0,16)!=16||
                        System.Text.Encoding.ASCII.GetString(header)!="SQLite format 3\0")
                        throw new InvalidDataException("解压后的数据不是 SQLite 数据库。");
                }
                return target;
            }
            catch{if(File.Exists(target))try{File.Delete(target);}catch{} throw;}
        }
    }
}
