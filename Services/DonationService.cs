using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    internal sealed class DonationMethodConfig
    {
        public bool Enabled { get; set; }
        public string QrCodeUrl { get; set; }
        public string Url { get; set; }
    }

    internal sealed class DonationConfig
    {
        public int Version { get; set; }
        public DonationMethodConfig WeChat { get; set; }
        public DonationMethodConfig Alipay { get; set; }
        public DonationMethodConfig Afdian { get; set; }
        public DonationMethodConfig KoFi { get; set; }
    }

    internal sealed class DonationContent
    {
        public byte[] WeChatImage;
        public byte[] AlipayImage;
        public bool WeChatEnabled = true;
        public bool AlipayEnabled = true;
        public bool AfdianEnabled = true;
        public bool KoFiEnabled = true;
        public string AfdianUrl = DonationService.DefaultAfdianUrl;
        public string KoFiUrl = DonationService.DefaultKoFiUrl;
    }

    internal sealed class DonationService
    {
        public const string ConfigUrl = "https://kendoyae.github.io/GuiGui-MangaAuthorSorter/donation.json";
        public const string DefaultAfdianUrl = "https://afdian.com/a/kendo";
        public const string DefaultKoFiUrl = "https://ko-fi.com/kendoyae";

        private const int MaxConfigBytes = 64 * 1024;
        private const int MaxImageBytes = 5 * 1024 * 1024;
        private readonly string _cacheDir;

        public DonationService(string appDir)
        {
            _cacheDir = Path.Combine(appDir ?? AppDomain.CurrentDomain.BaseDirectory, "DonationCache");
        }

        public Task<DonationContent> LoadAsync()
        {
            return Task.Factory.StartNew<DonationContent>(Load);
        }

        private DonationContent Load()
        {
            DonationContent content = new DonationContent();
            DonationConfig config = TryLoadRemoteConfig();
            if (config != null)
                TryWriteCache("DonationConfig.json", Serialize(config));
            else
                config = TryLoadCachedConfig();

            if (config == null || config.Version < 1)
                return content;

            ApplyMethod(config.WeChat, "wechat.png", true, content);
            ApplyMethod(config.Alipay, "alipay.png", false, content);
            ApplyLink(config.Afdian, DefaultAfdianUrl, "afdian.com", delegate(bool enabled, string url)
            {
                content.AfdianEnabled = enabled;
                content.AfdianUrl = url;
            });
            ApplyLink(config.KoFi, DefaultKoFiUrl, "ko-fi.com", delegate(bool enabled, string url)
            {
                content.KoFiEnabled = enabled;
                content.KoFiUrl = url;
            });
            return content;
        }

        private void ApplyMethod(DonationMethodConfig method, string cacheName, bool wechat, DonationContent content)
        {
            if (method == null) return;
            if (wechat) content.WeChatEnabled = method.Enabled;
            else content.AlipayEnabled = method.Enabled;
            if (!method.Enabled) return;

            byte[] image = null;
            if (IsTrustedPagesUrl(method.QrCodeUrl))
            {
                image = TryDownload(method.QrCodeUrl, MaxImageBytes);
                if (IsSupportedImage(image))
                    TryWriteCache(cacheName, image);
                else
                    image = null;
            }

            if (image == null)
            {
                byte[] cached = TryReadCache(cacheName, MaxImageBytes);
                if (IsSupportedImage(cached)) image = cached;
            }

            if (wechat) content.WeChatImage = image;
            else content.AlipayImage = image;
        }

        private static void ApplyLink(
            DonationMethodConfig method,
            string fallbackUrl,
            string trustedHost,
            Action<bool, string> assign)
        {
            if (method == null)
            {
                assign(true, fallbackUrl);
                return;
            }

            string url = IsTrustedExternalUrl(method.Url, trustedHost) ? method.Url : fallbackUrl;
            assign(method.Enabled, url);
        }

        private DonationConfig TryLoadRemoteConfig()
        {
            byte[] bytes = TryDownload(ConfigUrl, MaxConfigBytes);
            return TryDeserialize(bytes);
        }

        private DonationConfig TryLoadCachedConfig()
        {
            return TryDeserialize(TryReadCache("DonationConfig.json", MaxConfigBytes));
        }

        private static DonationConfig TryDeserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                string json = System.Text.Encoding.UTF8.GetString(bytes);
                return new JavaScriptSerializer().Deserialize<DonationConfig>(json);
            }
            catch { return null; }
        }

        private static byte[] Serialize(DonationConfig config)
        {
            return System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(config));
        }

        private static byte[] TryDownload(string url, int maxBytes)
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.UserAgent = "GuiGui-MangaAuthorSorter/" + AppVersion.UserAgentVersion;
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > maxBytes)
                        return null;
                    using (Stream stream = response.GetResponseStream())
                    using (MemoryStream output = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        int total = 0;
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            total += read;
                            if (total > maxBytes) return null;
                            output.Write(buffer, 0, read);
                        }
                        return output.ToArray();
                    }
                }
            }
            catch { return null; }
        }

        private byte[] TryReadCache(string name, int maxBytes)
        {
            try
            {
                string path = Path.Combine(_cacheDir, name);
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > maxBytes) return null;
                return File.ReadAllBytes(path);
            }
            catch { return null; }
        }

        private void TryWriteCache(string name, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            string temporary = null;
            try
            {
                Directory.CreateDirectory(_cacheDir);
                string path = Path.Combine(_cacheDir, name);
                temporary = path + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                File.Copy(temporary, path, true);
            }
            catch { }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch { }
                }
            }
        }

        private static bool IsTrustedPagesUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            return String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(uri.Host, "kendoyae.github.io", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/GuiGui-MangaAuthorSorter/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTrustedExternalUrl(string value, string trustedHost)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            return String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                (String.Equals(uri.Host, trustedHost, StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(uri.Host, "www." + trustedHost, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSupportedImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12) return false;
            bool png = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            bool jpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[bytes.Length - 2] == 0xFF && bytes[bytes.Length - 1] == 0xD9;
            return png || jpeg;
        }
    }
}
