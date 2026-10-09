using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.13.10.0")]
[assembly: AssemblyFileVersion("1.13.10.0")]
[assembly: AssemblyInformationalVersion("1.13.10")]
[assembly: AssemblyTitle("GuiGui")]
[assembly: AssemblyProduct("GuiGui")]
[assembly: AssemblyDescription("归归 / GuiGui 漫画作者识别与归档工具")]

namespace MangaAuthorSorter
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                try { if (!PublicAuthorIndexMergeService.ActivatePending())
                    System.Diagnostics.Trace.WriteLine("Stale pending author-index update left unchanged; run reference revalidation again."); }
                catch (Exception updateError)
                {
                    // Keep the previously valid public database; a failed update must not stop the app.
                    System.Diagnostics.Trace.WriteLine("Pending author-index update: " + updateError);
                }
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                try { UiMessageBox.Show(ex.ToString(), "程序启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                catch { }
            }
        }
    }
}
