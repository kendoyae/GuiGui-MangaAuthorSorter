using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace MangaAuthorSorter
{
    internal static class ExecutionSafety
    {
        public static void ApplyBatchTargetConflictMarks(IList<PlanItem> plan)
        {
            if (plan == null) return;
            IEnumerable<IGrouping<string, PlanItem>> groups = plan
                .Where(delegate(PlanItem p)
                {
                    return p != null && p.CanMove &&
                        !String.IsNullOrWhiteSpace(p.TargetPath) &&
                        !String.Equals(p.SourcePath, p.TargetPath, StringComparison.OrdinalIgnoreCase);
                })
                .GroupBy(delegate(PlanItem p) { return p.TargetPath; }, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, PlanItem> group in groups)
            {
                List<PlanItem> conflicts = group
                    .GroupBy(delegate(PlanItem p) { return p.SourcePath; }, StringComparer.OrdinalIgnoreCase)
                    .Select(delegate(IGrouping<string, PlanItem> g) { return g.First(); })
                    .ToList();
                if (conflicts.Count <= 1) continue;
                List<string> sources = conflicts.Select(delegate(PlanItem p) { return p.SourcePath; }).ToList();
                foreach (PlanItem item in conflicts)
                {
                    item.CanMoveBeforePlanConflict = item.CanMove;
                    item.StatusBeforePlanConflict = item.Status ?? "";
                    item.StatusCodeBeforePlanConflict = item.StatusCode;
                    item.CanMove = false;
                    item.Status = "批次内目标重名冲突";
                    item.StatusCode = PlanStatusCode.BatchTargetConflict;
                    item.PlanConflictKind = "batch-target";
                    item.ConflictTargetPath = group.Key;
                    item.ConflictSourcePaths = new List<string>(sources);
                }
            }
        }

        public static string GetVolumeRoot(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return "";

            try
            {
                string full = Path.GetFullPath(path.Trim());
                string root = Path.GetPathRoot(full) ?? "";
                return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }

        public static bool IsSameVolume(string sourcePath, string targetPath)
        {
            string sourceRoot = GetVolumeRoot(sourcePath);
            string targetRoot = GetVolumeRoot(targetPath);

            return sourceRoot.Length > 0 &&
                   targetRoot.Length > 0 &&
                   String.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetAvailableSpace(string path, out long freeBytes, out string error)
        {
            freeBytes = 0;
            error = "";

            string root = GetVolumeRoot(path);
            if (root.Length == 0)
            {
                error = "Unable to determine the target volume.";
                return false;
            }

            try
            {
                DriveInfo drive = new DriveInfo(root + Path.DirectorySeparatorChar);
                if (!drive.IsReady)
                {
                    error = "The target volume is not ready.";
                    return false;
                }

                freeBytes = drive.AvailableFreeSpace;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryProbeWritable(string directory, out string error)
        {
            error = "";

            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                error = "The target directory does not exist.";
                return false;
            }

            string probe = Path.Combine(
                directory,
                ".manga_author_sorter_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (FileStream stream = new FileStream(
                    probe,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough))
                {
                    stream.WriteByte(0);
                    stream.Flush();
                }

                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(probe))
                        File.Delete(probe);
                }
                catch
                {
                }

                error = ex.Message;
                return false;
            }
        }

        public static void SafeMoveFile(
            string sourcePath,
            string targetPath,
            long reserveBytes,
            long remainingCrossVolumeBytes)
        {
            if (IsSameVolume(sourcePath, targetPath))
            {
                File.Move(sourcePath, targetPath);
                return;
            }

            long freeBytes;
            string spaceError;
            if (!TryGetAvailableSpace(targetPath, out freeBytes, out spaceError))
                throw new IOException(spaceError);

            long required = Math.Max(0L, remainingCrossVolumeBytes) + Math.Max(0L, reserveBytes);
            if (freeBytes < required)
            {
                throw new IOException(
                    "Insufficient free space on the target volume. Required: " +
                    required.ToString() + ", available: " + freeBytes.ToString() + ".");
            }

            string tempPath = targetPath + ".moving";
            if (File.Exists(tempPath) || Directory.Exists(tempPath))
                throw new IOException("Temporary target already exists: " + tempPath);

            if (File.Exists(targetPath) || Directory.Exists(targetPath))
                throw new IOException("Target already exists: " + targetPath);

            bool targetFinalized = false;

            try
            {
                File.Copy(sourcePath, tempPath, false);

                long sourceLength = new FileInfo(sourcePath).Length;
                long tempLength = new FileInfo(tempPath).Length;
                if (sourceLength != tempLength)
                {
                    throw new IOException(
                        "Copied file length verification failed. Source: " +
                        sourceLength.ToString() + ", target: " + tempLength.ToString() + ".");
                }

                File.Move(tempPath, targetPath);
                targetFinalized = true;

                try
                {
                    File.Delete(sourcePath);
                }
                catch
                {
                    // Preserve the original file as the authoritative copy if
                    // source deletion fails after the target was finalized.
                    try
                    {
                        if (File.Exists(targetPath))
                            File.Delete(targetPath);
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                if (!targetFinalized)
                {
                    try
                    {
                        if (File.Exists(targetPath) && File.Exists(sourcePath))
                            File.Delete(targetPath);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }
    }
}
