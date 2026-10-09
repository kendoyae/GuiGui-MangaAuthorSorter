using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MangaAuthorSorter.Tests
{
    internal static class StartupRecoveryTests
    {
        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static string modalFailure;
        private static string ControlText(Control control)
        {
            return control.Text + " " + String.Join(" ", control.Controls.Cast<Control>().Select(ControlText));
        }
        private static T Field<T>(object form, string name) { return (T)form.GetType().GetField(name, Private | BindingFlags.Public).GetValue(form); }
        private static void Call(object form, string name, params object[] args) { form.GetType().GetMethod(name, Private).Invoke(form, args); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void PumpUntil(Func<bool> done, string message)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!done())
            {
                Application.DoEvents(); Thread.Sleep(5);
                if (modalFailure != null) throw new Exception("Unexpected test dialog: " + modalFailure);
                if (timer.ElapsedMilliseconds > 180000) throw new Exception("Timed out: " + message);
            }
            Application.DoEvents();
        }
        private static MainForm Open()
        {
            Console.WriteLine("[STEP] Constructing main window");
            MainForm form = new MainForm();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-20000, -20000);
            Console.WriteLine("[STEP] Showing main window");
            form.Show(); Console.WriteLine("[STEP] Main window shown"); return form;
        }
        private static ScanPerformanceEntry Scan(MainForm form)
        {
            int before = ScanPerformanceDiagnostics.Snapshot().Count;
            Console.WriteLine("[STEP] Starting scan; scanning=" + Field<bool>(form, "_isScanning"));
            Call(form, "ScanPreview", false);
            PumpUntil(() => !Field<bool>(form, "_isScanning") &&
                ScanPerformanceDiagnostics.Snapshot().Count > before, "scan completion");
            Check(ScanPerformanceDiagnostics.Snapshot().Count == before + 1, "Scan did not complete with a performance record. Status=" + Field<Label>(form, "_lblStatus").Text);
            Console.WriteLine("[STEP] Scan completed");
            return ScanPerformanceDiagnostics.Snapshot().Last();
        }

        private static void VerifyExecutionProbe(MainForm form, string path)
        {
            PlanItem item = Field<List<PlanItem>>(form, "_plan").Single(x => x.SourcePath == path);
            File.Delete(path);
            MethodInfo evaluate = typeof(MainForm).GetMethod("EvaluateExecutionSafety", Private);
            object display = evaluate.Invoke(form, new object[] { new List<PlanItem> { item }, false });
            object execution = evaluate.Invoke(form, new object[] { new List<PlanItem> { item }, true });
            Check(Field<int>(display, "MissingSources") == 0 && Field<int>(execution, "MissingSources") == 1,
                "Execution must recheck actual source files even when the display uses indexed metadata.");
            Console.WriteLine("[PASS] Execution probes actual disk state and blocks a disappeared cached source.");
        }

        private static void VerifyMutationDuringValidation(MainForm form, string source)
        {
            string path = Path.Combine(source, "[Author] DuringValidation.zip");
            using (System.Windows.Forms.Timer mutation = new System.Windows.Forms.Timer { Interval = 10 })
            {
                mutation.Tick += delegate { mutation.Stop(); File.WriteAllText(path, "changed during validation"); };
                mutation.Start(); ScanPerformanceEntry result = Scan(form);
                Check(result.ResultCount == 8491 && Field<List<PlanItem>>(form, "_plan").Any(x => x.SourcePath == path),
                    "A superseded validation result overwrote the newer filesystem state.");
            }
            File.Delete(path); Thread.Sleep(100); Scan(form);
            Console.WriteLine("[PASS] A file mutation during validation is reconciled before the latest list is published.");
        }
        [STAThread]
        public static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            int result = 1;
            using (ApplicationContext context = new ApplicationContext())
            using (System.Windows.Forms.Timer kickoff = new System.Windows.Forms.Timer { Interval = 1 })
            {
                kickoff.Tick += delegate { kickoff.Stop(); result = Run(); context.ExitThread(); };
                kickoff.Start(); Application.Run(context);
            }
            return result;
        }

        private static int Run()
        {
            try
            {
                System.Windows.Forms.Timer modalGuard = new System.Windows.Forms.Timer { Interval = 200 };
                modalGuard.Tick += delegate {
                    foreach (Form dialog in Application.OpenForms.Cast<Form>().Where(x => x.Modal).ToArray())
                    { modalFailure = ControlText(dialog); dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                };
                modalGuard.Start();
                string app = AppDomain.CurrentDomain.BaseDirectory;
                Check(app.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(app.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("GuiGui-StartupRecovery-"),
                    "Run this test through its isolated runner to protect existing application data.");
                string source = Path.Combine(app, "Source"), target = Path.Combine(app, "Target");
                Directory.CreateDirectory(source); Directory.CreateDirectory(target);
                Directory.CreateDirectory(Path.Combine(target, "Author"));
                for (int i = 0; i < 8490; i++) File.WriteAllText(Path.Combine(source, "[Author] Book" + i.ToString("D5") + ".zip"), "test");
                UserSettingsStore store = new UserSettingsStore(Path.Combine(app, AppFiles.UserSettings));
                store.UpdateSettings(source, target, GroupNaming.DefaultTemplate, 20, AuthorFolderNaming.DefaultTemplate);
                store.UpdateScanSettings(0, false, 0);
                bool useEverything = Environment.GetEnvironmentVariable("GUIGUI_TEST_EVERYTHING") == "1";
                store.UpdatePerformanceSettings(true, false, useEverything);
                store.UpdateLastUpdateCheckUtc(DateTime.UtcNow);
                Directory.CreateDirectory(Path.Combine(app, "Cache"));
                string legacy = Path.Combine(app, "Cache", "ScanWarmupSnapshot.json");
                File.WriteAllText(legacy, "legacy fixture retained");
                ScanPerformanceEntry initial;
                { MainForm form = Open();
                    Check(Field<List<PlanItem>>(form, "_plan").Count == 0, "First run unexpectedly restored results.");
                    initial = Scan(form);
                    Check(initial.ResultCount == 8490 && initial.ActualRecognitions == 8490, "First scan must recognize all fixture files.");
                    form.Close();
                }
                ScanPerformanceEntry restarted;
                bool browsedDuringValidation = false;
                { MainForm form = Open();
                    PumpUntil(delegate {
                        if (Field<bool>(form, "_restoredPlanNeedsValidation"))
                        {
                            Check(Field<DataGridView>(form, "_grid").Enabled && Field<Control>(form, "_filterBar").Enabled,
                                "Restored list or filters are disabled during validation.");
                            Field<TextBox>(form, "_txtListSearch").Text = "Book000";
                            browsedDuringValidation = true;
                        }
                        return Field<bool>(form, "_hasCompletedScanSession") && !Field<bool>(form, "_isScanning");
                    }, "automatic restoration and validation");
                    restarted = ScanPerformanceDiagnostics.Snapshot().Last();
                    Check(restarted.ResultCount == 8490 && restarted.PlanCacheHits == 8490 && restarted.ActualRecognitions == 0,
                        "Restart must reuse every valid recognition result.");
                    Check(restarted.EverythingQueryCount <= 1, "Startup issued duplicate Everything queries.");
                    Check(browsedDuringValidation, "Did not observe an interactive restored list before validation completed.");
                    Field<TextBox>(form, "_txtListSearch").Text = "";
                    File.WriteAllText(Path.Combine(source, "[Author] Added.zip"), "added"); Thread.Sleep(100);
                    ScanPerformanceEntry added = Scan(form);
                    Check(added.ResultCount == 8491 && added.IndexAdded == 1 && added.ActualRecognitions == 1, "Adding one file recalculated unrelated records.");
                    string modified = Path.Combine(source, "[Author] Book00000.zip");
                    File.AppendAllText(modified, "metadata changed"); Thread.Sleep(100);
                    ScanPerformanceEntry changed = Scan(form);
                    Check(changed.IndexModified == 1 && changed.ActualRecognitions == 0 &&
                        Field<List<PlanItem>>(form, "_plan").Single(x => x.SourcePath == modified).FileSize == new FileInfo(modified).Length,
                        "Metadata-only change should preserve recognition and update size.");
                    File.Delete(Path.Combine(source, "[Author] Added.zip")); Thread.Sleep(100);
                    ScanPerformanceEntry deleted = Scan(form);
                    Check(deleted.ResultCount == 8490 && deleted.IndexRemoved == 1 && deleted.ActualRecognitions == 0, "Deleting one file produced wrong incremental results.");
                    VerifyMutationDuringValidation(form, source);
                    VerifyExecutionProbe(form, modified);
                    form.Close();
                }
                { MainForm form = Open();
                    PumpUntil(() => Field<bool>(form, "_isScanning"), "startup validation beginning");
                    form.Close();
                    PumpUntil(() => form.IsDisposed, "deferred close after cancellation");
                    Console.WriteLine("[PASS] Closing during startup validation cancels the worker and safely releases the database.");
                }
                string offline = Path.Combine(app, "SourceOffline");
                Check(Path.GetFullPath(source).StartsWith(app, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFullPath(offline).StartsWith(app, StringComparison.OrdinalIgnoreCase), "Test move escaped its isolated directory.");
                Directory.Move(source, offline);
                { MainForm form = Open();
                    PumpUntil(() => Field<bool>(form, "_restoredPlanNeedsValidation"), "restore unavailable source");
                    Check(!Field<bool>(form, "_isScanning") && !Field<Button>(form, "_btnExecute").Enabled,
                        "Unavailable source must preserve a read-only list without scanning or executing.");
                    form.Close();
                }
                Directory.Move(offline, source);
                Console.WriteLine("[PASS] Unavailable source restores the cached list and blocks disk operations.");
                Check(File.ReadAllText(legacy) == "legacy fixture retained", "Upgrade deleted the legacy snapshot.");
                Console.WriteLine("[PASS] Real WinForms restart: restored list is interactive before validation; 8490 cached plans reused; add/modify/delete isolated; legacy snapshot preserved; Everything enabled=" + useEverything + ".");
                Console.WriteLine("[BENCH] Provider=" + restarted.Provider + ": first scan=" + initial.TotalResponseMs +
                    " ms; startup restore=" + restarted.StartupRestoreMs + " ms; first interactive list=" + restarted.FirstInteractiveMs +
                    " ms; window shown=" + restarted.StartupWindowShownMs + " ms; completeness=" + restarted.DiscoveryCheckMs + " ms; background validation=" + restarted.BackgroundValidationMs + " ms; Everything queries=" + restarted.EverythingQueryCount + ".");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("[FAIL] " + ex); return 1; }
        }
    }
}
