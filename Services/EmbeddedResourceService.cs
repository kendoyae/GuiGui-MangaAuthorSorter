using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Collections.Generic;

namespace MangaAuthorSorter
{
    internal static class EmbeddedResourceService
    {
        public const string LicenseNotices = "MangaAuthorSorter.LicenseNotices.txt";
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

        public static string ReadLocalizedDocument(string documentName, string languageCode)
        {
            List<string> languages = new List<string>();
            AddLanguageCandidate(languages, languageCode);
            AddLanguageCandidate(languages, "en-US");
            AddLanguageCandidate(languages, "zh-CN");

            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resourceNames = assembly.GetManifestResourceNames();
            foreach (string language in languages)
            {
                string expected = "MangaAuthorSorter.Docs." + documentName + "." + language + ".md";
                foreach (string resourceName in resourceNames)
                {
                    if (String.Equals(resourceName, expected, StringComparison.OrdinalIgnoreCase))
                        return ReadText(resourceName);
                }
            }

            return "";
        }

        private static void AddLanguageCandidate(List<string> languages, string languageCode)
        {
            if (String.IsNullOrWhiteSpace(languageCode)) return;
            foreach (string existing in languages)
            {
                if (String.Equals(existing, languageCode, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            languages.Add(languageCode.Trim());
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
