using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.11.24.0")]
[assembly: AssemblyFileVersion("1.11.24.0")]
[assembly: AssemblyInformationalVersion("1.11.24")]

namespace MangaAuthorSorter
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                try { UiMessageBox.Show(ex.ToString(), "程序启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                catch { }
            }
        }
    }
}
