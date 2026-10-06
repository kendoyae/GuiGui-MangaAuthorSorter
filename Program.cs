using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyVersion("1.12.0.0")]
[assembly: AssemblyFileVersion("1.12.0.0")]
[assembly: AssemblyInformationalVersion("1.12.0")]
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
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                try { UiMessageBox.Show(ex.ToString(), "程序启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                catch { }
            }
        }
    }
}
