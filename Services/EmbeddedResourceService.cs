using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;

namespace MangaAuthorSorter
{
    internal static class EmbeddedResourceService
    {
        public const string Readme = "MangaAuthorSorter.Readme.txt";
        public const string LicenseNotices = "MangaAuthorSorter.LicenseNotices.txt";
        public const string Changelog = "MangaAuthorSorter.Changelog.md";
        public const string SupportWeChat = "MangaAuthorSorter.SupportWeChat.png";
        public const string SupportAlipay = "MangaAuthorSorter.SupportAlipay.png";

        public static string ReadText(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null) return "";
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }

        public static Image ReadImage(string resourceName)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (Image source = Image.FromStream(stream))
                        return new Bitmap(source);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
