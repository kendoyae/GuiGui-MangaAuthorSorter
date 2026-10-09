using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MangaAuthorSorter.Tests
{
    internal static class StartupUiExperimentTests
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Field<T>(MainForm form, string name) { return (T)typeof(MainForm).GetField(name, Private).GetValue(form); }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Wait(Func<bool> condition)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!condition())
            {
                Application.DoEvents(); Thread.Sleep(3);
                if (timeout.ElapsedMilliseconds > 120000) throw new Exception("Startup experiment timed out");
            }
            Application.DoEvents();
        }
        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            int exit = 1;
            using (ApplicationContext context = new ApplicationContext())
            using (System.Windows.Forms.Timer kickoff = new System.Windows.Forms.Timer { Interval = 1 })
            {
                kickoff.Tick += delegate
                {
                    kickoff.Stop();
                    try { Run(args); exit = 0; }
                    catch (Exception ex) { Console.Error.WriteLine("[FAIL] " + ex); }
                    finally { context.ExitThread(); }
                };
                kickoff.Start(); Application.Run(context);
            }
            return exit;
        }
        private static void Run(string[] args)
        {
            string app = AppDomain.CurrentDomain.BaseDirectory;
            Require(app.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(app.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("GuiGui-StartupUi-"),
                "Use the isolated runner; no user library or settings may be modified.");
            string mode = args[0];
            if (mode == "Seed")
            {
                string source = Path.Combine(app, "Source"), target = Path.Combine(app, "Target");
                Directory.CreateDirectory(source); Directory.CreateDirectory(target);
                string group = Path.Combine(target, GroupNaming.Render(GroupNaming.DefaultTemplate, 1));
                Directory.CreateDirectory(group);
                AuthorEntityDatabase entities = new AuthorEntityDatabase();
                for (int i = 0; i < 1169; i++)
                {
                    string author = "Author" + i.ToString("D4");
                    Directory.CreateDirectory(Path.Combine(group, author));
                    entities.Authors.Add(new AuthorEntityRecord { Id = i + 1, CanonicalName = author });
                }
                File.WriteAllText(Path.Combine(app, AppFiles.AuthorEntities), new JavaScriptSerializer().Serialize(entities));
                for (int i = 0; i < 8490; i++) File.WriteAllText(Path.Combine(source,
                    "[Author" + (i % 1169).ToString("D4") + "] Book" + i.ToString("D5") + ".zip"), "fixture");
                UserSettingsStore settings = new UserSettingsStore(Path.Combine(app, AppFiles.UserSettings));
                settings.UpdateSettings(source, target, GroupNaming.DefaultTemplate, 20, AuthorFolderNaming.DefaultTemplate);
                settings.UpdateScanSettings(0, false, 0);
                settings.UpdatePerformanceSettings(true, true, true);
                settings.UpdateLastUpdateCheckUtc(DateTime.UtcNow);
                MainForm seed = new MainForm(); seed.Show();
                typeof(MainForm).GetMethod("ScanPreview", Private).Invoke(seed, new object[] { false });
                Wait(() => !Field<bool>(seed, "_isScanning") && ScanPerformanceDiagnostics.Snapshot().Count > 0);
                Require(ScanPerformanceDiagnostics.Snapshot().Last().ResultCount == 8490, "Incomplete seed scan");
                seed.Close(); Console.WriteLine("[PASS] Isolated 8490-file / 1169-author seed cache prepared.");
                return;
            }
            MainForm.StartupValidationTiming timing = mode == "A" ? MainForm.StartupValidationTiming.Immediate :
                mode == "B" ? MainForm.StartupValidationTiming.AfterResponsive : MainForm.StartupValidationTiming.DisabledForExperiment;
            Require(mode == "A" || mode == "B" || mode == "C", "Unknown experiment mode");
            MainForm form = new MainForm(timing);
            int historyBefore = ScanPerformanceDiagnostics.Snapshot().Count;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(Screen.PrimaryScreen.WorkingArea.Left + 10, Screen.PrimaryScreen.WorkingArea.Top + 10);
            form.Show();
            StartupUiTrace trace = Field<StartupUiTrace>(form, "_startupTrace");
            Wait(() => trace.Milliseconds("Responsive") >= 0);
            Require(Field<List<PlanItem>>(form, "_plan").Count == 8490, "Cache restoration lost rows");
            Require(Field<DataGridView>(form, "_grid").Enabled, "Restored grid is not interactive");
            ScanPerformanceEntry validation = null;
            if (mode != "C")
            {
                Wait(() => !Field<bool>(form, "_isScanning") && ScanPerformanceDiagnostics.Snapshot().Count > historyBefore);
                validation = ScanPerformanceDiagnostics.Snapshot().Last();
                Require(validation.PlanCacheHits == 8490 && validation.ActualRecognitions == 0 && validation.PlanCacheWriteMs == 0,
                    "Experiment changed cache reuse or recognition behavior");
                if (mode == "B") Require(trace.Milliseconds("ValidationDispatch") >= trace.Milliseconds("Responsive"),
                    "B started validation before the painted UI responded");
            }
            else
            {
                Require(trace.Milliseconds("ValidationDispatch") < 0 && ScanPerformanceDiagnostics.Snapshot().Count == historyBefore,
                    "C unexpectedly performed background validation");
                Require(Field<bool>(form, "_restoredPlanNeedsValidation") && !Field<Button>(form, "_btnExecute").Enabled,
                    "Unvalidated C cache must remain read-only");
            }
            Dictionary<string, long> points = new Dictionary<string, long>();
            foreach (string field in trace.Export().Split(';'))
            {
                string[] pair = field.Split(':'); points[pair[0]] = Int64.Parse(pair[1]);
                string key = "Performance.StartupTrace." + pair[0];
                Require(Field<LanguageManager>(form, "_language").Get(key) != key, "Missing startup trace localization: " + key);
            }
            var report = new { Mode = mode, Trace = points, Initialization = Field<long[]>(form, "_startupInitializationStages"),
                WindowShownMs = Field<long>(form, "_startupWindowShownMs"), CacheAppliedMs = Field<long>(form, "_startupInteractiveMs"),
                ResponsiveMs = trace.Milliseconds("Responsive"), ValidationMs = validation == null ? -1 : validation.TotalResponseMs,
                CompletenessMs = validation == null ? -1 : validation.DiscoveryCheckMs,
                EnumerationMs = validation == null ? -1 : validation.DiscoveryEnumerateMs,
                Queries = validation == null ? 0 : validation.EverythingQueryCount,
                Reused = validation == null ? -1 : validation.PlanCacheHits, Recognized = validation == null ? -1 : validation.ActualRecognitions };
            File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(report));
            Console.WriteLine("[BENCH] " + mode + ": shown=" + report.WindowShownMs + "; applied=" + report.CacheAppliedMs +
                "; painted/message-responsive=" + report.ResponsiveMs + "; validation=" + report.ValidationMs + " ms.");
            form.Close();
        }
    }
}
