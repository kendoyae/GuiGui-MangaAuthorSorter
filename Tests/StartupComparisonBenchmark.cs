using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Reflection deliberately loads the supplied binary. This compares the actual
// pre-iteration executable with the new build, including preexisting changes.
internal static class StartupComparisonBenchmark
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Get(object value, string name) { return value.GetType().GetField(name, Fields).GetValue(value); }
    private static object Call(object value, string name, params object[] args) { return value.GetType().GetMethod(name, Fields).Invoke(value, args); }
    private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
    private static Assembly appAssembly;
    private static IList History() { return (IList)appAssembly.GetType("MangaAuthorSorter.ScanPerformanceDiagnostics").GetMethod("Snapshot", BindingFlags.Public | BindingFlags.Static).Invoke(null, null); }
    private static void Wait(Func<bool> done)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!done())
        {
            Application.DoEvents(); Thread.Sleep(5);
            if (timer.ElapsedMilliseconds > 60000) throw new Exception("Benchmark timeout");
        }
        Application.DoEvents();
    }
    private static Form Open()
    {
        Form form = (Form)Activator.CreateInstance(appAssembly.GetType("MangaAuthorSorter.MainForm"), true);
        form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000);
        form.Show(); return form;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string app = AppDomain.CurrentDomain.BaseDirectory;
            appAssembly = Assembly.LoadFrom(Path.Combine(app, "BenchmarkApp.exe"));
            string source = Path.Combine(app, "Source"), target = Path.Combine(app, "Target");
            Directory.CreateDirectory(source); Directory.CreateDirectory(Path.Combine(target, "Author"));
            for (int i = 0; i < 8490; i++) File.WriteAllText(Path.Combine(source, "[Author] Book" + i.ToString("D5") + ".zip"), "test");
            File.WriteAllText(Path.Combine(app, "UserSettings.ini"),
                "Source=" + Encode(source) + "\r\nAuthorRoot=" + Encode(target) + "\r\nScanLimit=" + Encode("0") +
                "\r\nPerformanceDiagnosticsEnabled=" + Encode("True") + "\r\nScanWarmupEnabled=" + Encode("True") +
                "\r\nEverythingEnabled=" + Encode("True") + "\r\nLastUpdateCheckUtc=" + Encode(DateTime.UtcNow.ToString("o")));
            Form cold = Open(); Call(cold, "ScanPreview", false);
            Wait(() => !(bool)Get(cold, "_isScanning") && History().Count == 1);
            cold.Close();
            Stopwatch startup = Stopwatch.StartNew();
            Form restored = Open(); long observed = -1;
            Wait(delegate {
                if (observed < 0 && (bool)Get(restored, "_restoredPlanNeedsValidation")) observed = startup.ElapsedMilliseconds;
                return !(bool)Get(restored, "_isScanning") && History().Count >= 2;
            });
            object report = History()[History().Count - 1];
            Console.WriteLine("[BENCH] " + args[0] + ": restore=" + Get(restored, "_startupRestoreMs") +
                " ms; first observed interactive list=" + observed + " ms; validation=" + Get(report, "TotalResponseMs") +
                " ms; plan read=" + Get(report, "PlanCacheReadMs") + " ms; index sync=" + Get(report, "IndexReconcileMs") +
                " ms; reused=" + Get(report, "PlanCacheHits") + "; recalculated=" + Get(report, "RecalculatedFiles") +
                "; working set=" + (Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024) + " MiB.");
            restored.Close(); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("[FAIL] " + ex); return 1; }
    }
}
