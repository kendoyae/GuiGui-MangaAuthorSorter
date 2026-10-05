using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MangaAuthorSorter
{
    internal sealed class BlockListStore
    {
        private readonly string _path;
        private readonly Func<string, string> _translate;

        public BlockListStore(string path, Func<string, string> translate)
        {
            _path = path;
            _translate = translate;
        }

        public string PathName
        {
            get { return _path; }
        }

        public void EnsureExists()
        {
            if (File.Exists(_path))
                return;

            Save(new List<string>());
        }

        public List<string> Load()
        {
            List<string> result = new List<string>();

            if (!File.Exists(_path))
                return result;

            HashSet<string> seen =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string raw in
                     File.ReadAllLines(
                         _path,
                         Encoding.UTF8))
            {
                string line =
                    (raw ?? "").Trim();

                if (line.Length == 0 ||
                    line.StartsWith("#"))
                {
                    continue;
                }

                string normalized =
                    NormalizePath(line);

                if (normalized.Length > 0 &&
                    seen.Add(normalized))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        public HashSet<string> LoadSet()
        {
            return new HashSet<string>(
                Load(),
                StringComparer.OrdinalIgnoreCase);
        }

        public int AddPaths(
            IEnumerable<string> paths)
        {
            List<string> items = Load();

            HashSet<string> seen =
                new HashSet<string>(
                    items,
                    StringComparer.OrdinalIgnoreCase);

            int added = 0;

            foreach (string raw in paths)
            {
                string path =
                    NormalizePath(raw);

                if (path.Length == 0)
                    continue;

                if (seen.Add(path))
                {
                    items.Add(path);
                    added++;
                }
            }

            Save(items);
            return added;
        }

        public int RemovePaths(
            IEnumerable<string> paths)
        {
            HashSet<string> remove =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string raw in paths)
            {
                string path =
                    NormalizePath(raw);

                if (path.Length > 0)
                    remove.Add(path);
            }

            List<string> oldItems = Load();

            List<string> newItems =
                new List<string>();

            foreach (string item in oldItems)
            {
                if (!remove.Contains(item))
                    newItems.Add(item);
            }

            int removed =
                oldItems.Count - newItems.Count;

            Save(newItems);
            return removed;
        }

        public bool IsBlocked(
            string path)
        {
            string normalized =
                NormalizePath(path);

            if (normalized.Length == 0)
                return false;

            return LoadSet().Contains(
                normalized);
        }

        private void Save(
            List<string> items)
        {
            List<string> lines = GetHeaderLines();
            if (lines.Count == 0)
                lines.AddRange(CreateLocalizedHeader());

            foreach (string item in items)
            {
                if (!String.IsNullOrWhiteSpace(item))
                    lines.Add(item);
            }

            File.WriteAllLines(
                _path,
                lines.ToArray(),
                new UTF8Encoding(true));
        }

        private List<string> GetHeaderLines()
        {
            List<string> header = new List<string>();
            if (!File.Exists(_path)) return header;

            try
            {
                foreach (string raw in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    string line = raw ?? "";
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    {
                        header.Add(line);
                        continue;
                    }
                    break;
                }
            }
            catch
            {
                header.Clear();
            }

            while (header.Count > 0 && String.IsNullOrWhiteSpace(header[header.Count - 1]))
                header.RemoveAt(header.Count - 1);
            if (header.Count > 0) header.Add("");
            return header;
        }

        private List<string> CreateLocalizedHeader()
        {
            List<string> lines = new List<string>();
            lines.Add("# " + T("DataFile.ExclusionList.Title"));
            lines.Add("# " + T("DataFile.ExclusionList.PathRule"));
            lines.Add("# " + T("DataFile.ExclusionList.Description"));
            lines.Add("# " + T("DataFile.ExclusionList.ManageHint"));
            lines.Add("");
            return lines;
        }

        private string T(string key)
        {
            if (_translate == null) return key;
            string value = _translate(key);
            return String.IsNullOrWhiteSpace(value) ? key : value;
        }

        public static string NormalizePath(
            string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return "";

            string value =
                path.Trim().Trim('"');

            try
            {
                return Path.GetFullPath(value);
            }
            catch
            {
                return value;
            }
        }
    }
}
