using System;
using System.IO;
using System.Security.Cryptography;

namespace MangaAuthorSorter.Tests
{
    internal static class OfficialUpdateNoEvidenceTests
    {
        private static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length != 1 || !File.Exists(args[0]))
                    throw new InvalidOperationException("Pass one valid Schema v4 public index.");

                string root = AppDomain.CurrentDomain.BaseDirectory;
                string active = Path.Combine(root, AppFiles.AuthorIndexDatabase);
                string evidence = Path.Combine(root, "GuiGuiReferenceEvidence.db");
                if (File.Exists(evidence)) throw new InvalidOperationException("Test directory is not isolated.");

                string expected = Hash(args[0]);
                PublicAuthorIndexMergeService.StageOfficialIndex(args[0]);
                if (File.Exists(evidence)) throw new InvalidOperationException("Ordinary staging created an evidence database.");
                if (!PublicAuthorIndexMergeService.ActivatePending()) throw new InvalidOperationException("Staged index was rejected.");
                if (!File.Exists(active) || !String.Equals(Hash(active), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Activated index differs from the downloaded index.");
                if (File.Exists(evidence)) throw new InvalidOperationException("Activation created an evidence database.");

                Console.WriteLine("[PASS] Complete public-index update stages and activates without GuiGuiReferenceEvidence.db.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("[FAIL] " + error);
                return 1;
            }
        }
    }
}
