using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace MangaAuthorSorter
{
    public sealed class FileTypeProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> Extensions { get; set; }

        public FileTypeProfile()
        {
            Id = "";
            Name = "";
            Extensions =
                new List<string>();
        }

        public override string ToString()
        {
            return Name ?? "";
        }
    }

    public sealed class FileTypeProfileConfig
    {
        public string CurrentProfileId { get; set; }
        public List<FileTypeProfile> Profiles { get; set; }

        public FileTypeProfileConfig()
        {
            CurrentProfileId =
                FileTypeRules.DefaultArchiveProfileId;

            Profiles =
                new List<FileTypeProfile>();
        }
    }

    internal sealed class FileTypeProfileStore
    {
        private readonly string _path;

        public FileTypeProfileStore(
            string path)
        {
            _path = path;
        }

        public FileTypeProfileConfig LoadOrCreate(
            UserSettingsData legacySettings)
        {
            if (File.Exists(_path))
            {
                try
                {
                    FileTypeProfileConfig loaded =
                        Deserialize(
                            File.ReadAllText(
                                _path,
                                Encoding.UTF8));

                    loaded =
                        NormalizeConfig(
                            loaded);

                    if (loaded.Profiles.Count > 0)
                    {
                        Save(loaded);
                        return loaded;
                    }
                }
                catch
                {
                    // 配置文件损坏时自动退回默认配置。
                }
            }

            FileTypeProfileConfig config =
                CreateDefaultConfig();

            ApplyLegacySettings(
                config,
                legacySettings);

            Save(config);
            return config;
        }

        public FileTypeProfileConfig Load()
        {
            if (!File.Exists(_path))
            {
                FileTypeProfileConfig created =
                    CreateDefaultConfig();

                Save(created);
                return created;
            }

            try
            {
                FileTypeProfileConfig loaded =
                    Deserialize(
                        File.ReadAllText(
                            _path,
                            Encoding.UTF8));

                loaded =
                    NormalizeConfig(
                        loaded);

                if (loaded.Profiles.Count == 0)
                {
                    loaded =
                        CreateDefaultConfig();
                }

                return loaded;
            }
            catch
            {
                return CreateDefaultConfig();
            }
        }

        public void Save(
            FileTypeProfileConfig config)
        {
            FileTypeProfileConfig normalized =
                NormalizeConfig(
                    config);

            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            serializer.MaxJsonLength =
                Int32.MaxValue;

            string json =
                serializer.Serialize(
                    normalized);

            File.WriteAllText(
                _path,
                json,
                new UTF8Encoding(true));
        }

        public FileTypeProfileConfig CloneConfig(
            FileTypeProfileConfig source)
        {
            FileTypeProfileConfig clone =
                new FileTypeProfileConfig();

            if (source == null)
                return clone;

            clone.CurrentProfileId =
                source.CurrentProfileId ?? "";

            clone.Profiles =
                new List<FileTypeProfile>();

            foreach (FileTypeProfile profile
                     in source.Profiles ??
                        new List<FileTypeProfile>())
            {
                clone.Profiles.Add(
                    CloneProfile(
                        profile));
            }

            return NormalizeConfig(
                clone);
        }

        public FileTypeProfile FindProfile(
            FileTypeProfileConfig config,
            string id)
        {
            if (config == null ||
                config.Profiles == null)
            {
                return null;
            }

            return config.Profiles.FirstOrDefault(
                delegate(FileTypeProfile p)
                {
                    return String.Equals(
                        p.Id,
                        id,
                        StringComparison.OrdinalIgnoreCase);
                });
        }

        public FileTypeProfile GetCurrentProfile(
            FileTypeProfileConfig config)
        {
            if (config == null)
                return null;

            FileTypeProfile current =
                FindProfile(
                    config,
                    config.CurrentProfileId);

            if (current != null)
                return current;

            return config.Profiles != null
                ? config.Profiles.FirstOrDefault()
                : null;
        }

        public string CreateCustomProfileId(
            FileTypeProfileConfig config)
        {
            while (true)
            {
                string id =
                    "custom_" +
                    Guid.NewGuid()
                        .ToString("N")
                        .Substring(0, 10);

                if (FindProfile(
                        config,
                        id) == null)
                {
                    return id;
                }
            }
        }

        public FileTypeProfileConfig RestoreSystemPresets(
            FileTypeProfileConfig config)
        {
            FileTypeProfileConfig working =
                CloneConfig(
                    config);

            FileTypeProfile archive =
                FindProfile(
                    working,
                    FileTypeRules.DefaultArchiveProfileId);

            if (archive == null)
            {
                archive =
                    CreateArchiveProfile();

                working.Profiles.Insert(
                    0,
                    archive);
            }
            else
            {
                archive.Name =
                    "Archive files";

                archive.Extensions =
                    FileTypeRules.GetCompressionDefaults();
            }

            FileTypeProfile video =
                FindProfile(
                    working,
                    FileTypeRules.DefaultVideoProfileId);

            if (video == null)
            {
                video =
                    CreateVideoProfile();

                int index =
                    Math.Min(
                        1,
                        working.Profiles.Count);

                working.Profiles.Insert(
                    index,
                    video);
            }
            else
            {
                video.Name =
                    "Video files";

                video.Extensions =
                    FileTypeRules.GetVideoDefaults();
            }

            return NormalizeConfig(
                working);
        }

        private static FileTypeProfileConfig
            CreateDefaultConfig()
        {
            FileTypeProfileConfig config =
                new FileTypeProfileConfig();

            config.CurrentProfileId =
                FileTypeRules.DefaultArchiveProfileId;

            config.Profiles =
                new List<FileTypeProfile>
                {
                    CreateArchiveProfile(),
                    CreateVideoProfile()
                };

            return config;
        }

        private static FileTypeProfile
            CreateArchiveProfile()
        {
            FileTypeProfile profile =
                new FileTypeProfile();

            profile.Id =
                FileTypeRules.DefaultArchiveProfileId;
            profile.Name =
                "Archive files";
            profile.Extensions =
                FileTypeRules.GetCompressionDefaults();

            return profile;
        }

        private static FileTypeProfile
            CreateVideoProfile()
        {
            FileTypeProfile profile =
                new FileTypeProfile();

            profile.Id =
                FileTypeRules.DefaultVideoProfileId;
            profile.Name =
                "Video files";
            profile.Extensions =
                FileTypeRules.GetVideoDefaults();

            return profile;
        }

        private static FileTypeProfile
            CloneProfile(
                FileTypeProfile source)
        {
            FileTypeProfile clone =
                new FileTypeProfile();

            if (source == null)
                return clone;

            clone.Id =
                source.Id ?? "";

            clone.Name =
                source.Name ?? "";

            clone.Extensions =
                FileTypeRules.NormalizeExtensions(
                    source.Extensions);

            return clone;
        }

        private static FileTypeProfileConfig
            NormalizeConfig(
                FileTypeProfileConfig config)
        {
            if (config == null)
            {
                config =
                    new FileTypeProfileConfig();
            }

            if (config.Profiles == null)
            {
                config.Profiles =
                    new List<FileTypeProfile>();
            }

            List<FileTypeProfile> normalized =
                new List<FileTypeProfile>();

            HashSet<string> ids =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (FileTypeProfile raw
                     in config.Profiles)
            {
                if (raw == null)
                    continue;

                string id =
                    (raw.Id ?? "")
                        .Trim();

                if (id.Length == 0 ||
                    ids.Contains(id))
                {
                    id =
                        "custom_" +
                        Guid.NewGuid()
                            .ToString("N")
                            .Substring(0, 10);
                }

                ids.Add(id);

                string name =
                    (raw.Name ?? "")
                        .Trim();

                if (name.Length == 0)
                {
                    name =
                        "Unnamed profile";
                }

                FileTypeProfile item =
                    new FileTypeProfile();

                item.Id = id;
                item.Name = name;
                item.Extensions =
                    FileTypeRules.NormalizeExtensions(
                        raw.Extensions);

                normalized.Add(item);
            }

            config.Profiles =
                normalized;

            if (config.Profiles.Count == 0)
            {
                config.Profiles.Add(
                    CreateArchiveProfile());

                config.Profiles.Add(
                    CreateVideoProfile());
            }

            bool currentExists =
                config.Profiles.Any(
                    delegate(FileTypeProfile p)
                    {
                        return String.Equals(
                            p.Id,
                            config.CurrentProfileId,
                            StringComparison.OrdinalIgnoreCase);
                    });

            if (!currentExists)
            {
                config.CurrentProfileId =
                    config.Profiles[0].Id;
            }

            return config;
        }

        private static FileTypeProfileConfig
            Deserialize(
                string json)
        {
            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            serializer.MaxJsonLength =
                Int32.MaxValue;

            FileTypeProfileConfig config =
                serializer.Deserialize<FileTypeProfileConfig>(
                    json ?? "");

            return config;
        }

        private static void ApplyLegacySettings(
            FileTypeProfileConfig config,
            UserSettingsData legacy)
        {
            if (config == null ||
                legacy == null ||
                !legacy.HasLegacyFileTypeConfig)
            {
                return;
            }

            FileTypeProfile archive =
                config.Profiles.FirstOrDefault(
                    delegate(FileTypeProfile p)
                    {
                        return String.Equals(
                            p.Id,
                            FileTypeRules.DefaultArchiveProfileId,
                            StringComparison.OrdinalIgnoreCase);
                    });

            FileTypeProfile video =
                config.Profiles.FirstOrDefault(
                    delegate(FileTypeProfile p)
                    {
                        return String.Equals(
                            p.Id,
                            FileTypeRules.DefaultVideoProfileId,
                            StringComparison.OrdinalIgnoreCase);
                    });

            List<string> compression =
                FileTypeRules.NormalizeExtensions(
                    legacy.LegacyCompressionExtensions);

            List<string> videoExtensions =
                FileTypeRules.NormalizeExtensions(
                    legacy.LegacyVideoExtensions);

            if (archive != null &&
                compression.Count > 0)
            {
                archive.Extensions =
                    compression;
            }

            if (video != null &&
                videoExtensions.Count > 0)
            {
                video.Extensions =
                    videoExtensions;
            }

            config.CurrentProfileId =
                String.Equals(
                    legacy.LegacyFileTypeMode,
                    "Video",
                    StringComparison.OrdinalIgnoreCase)
                    ? FileTypeRules.DefaultVideoProfileId
                    : FileTypeRules.DefaultArchiveProfileId;
        }
    }
}
