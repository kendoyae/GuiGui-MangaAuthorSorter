using System;
using System.IO;

namespace MangaAuthorSorter
{
    internal static class AppFiles
    {
        public const string AuthorAliases = "AuthorAliases.txt";
        public const string ExclusionList = "ExclusionList.txt";
        public const string UserSettings = "UserSettings.ini";
        public const string FileTypeProfiles = "FileTypeProfiles.json";
        public const string History = "History.json";
        public const string GridLayouts = "GridLayouts.ini";
        public const string AuthorEntities = "AuthorEntities.json";
        public const string TagCleaningRules = "TagCleaningRules.json";
        public const string ScanExclusionRules = "ScanExclusionRules.json";
        public const string ScanPerformanceLog = "ScanPerformance.log";

        public static string ResolveDataFile(
            string appDir,
            string currentName,
            params string[] legacyNames)
        {
            string currentPath = Path.Combine(appDir, currentName);
            if (File.Exists(currentPath))
                return currentPath;

            if (legacyNames != null)
            {
                foreach (string legacyName in legacyNames)
                {
                    if (String.IsNullOrWhiteSpace(legacyName))
                        continue;

                    string legacyPath = Path.Combine(appDir, legacyName);
                    if (!File.Exists(legacyPath))
                        continue;

                    try
                    {
                        File.Move(legacyPath, currentPath);
                        return currentPath;
                    }
                    catch
                    {
                        try
                        {
                            File.Copy(legacyPath, currentPath, false);
                            return currentPath;
                        }
                        catch
                        {
                            // If the directory is not writable, keep using the
                            // legacy file rather than silently losing user data.
                            return legacyPath;
                        }
                    }
                }
            }

            return currentPath;
        }

    }
}
