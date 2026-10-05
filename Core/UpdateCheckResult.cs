using System;

namespace MangaAuthorSorter
{
    internal enum UpdateCheckStatus
    {
        UpdateAvailable,
        UpToDate,
        DevelopmentVersion,
        NoRelease,
        NetworkError,
        Timeout,
        InvalidVersion,
        InvalidResponse
    }

    internal sealed class UpdateCheckResult
    {
        public Version CurrentVersion;
        public Version LatestVersion;
        public UpdateCheckStatus Status;
        public string ReleaseName = "";
        public string ReleaseNotes = "";
        public string ReleaseUrl = "";
        public DateTime? PublishedAt;

        public bool HasUpdate
        {
            get { return Status == UpdateCheckStatus.UpdateAvailable; }
        }
    }
}
