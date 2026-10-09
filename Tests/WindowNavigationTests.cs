using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace MangaAuthorSorter.Tests {
    internal static class WindowNavigationTests {
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr handle);
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Check(bool value, string text) { if (!value) throw new Exception(text); }
        private static T Field<T>(object form, string name) { return (T)form.GetType().GetField(name, Private).GetValue(form); }
        private static void Call(object form, string method, params object[] args) { form.GetType().GetMethod(method, Private).Invoke(form, args); }
        [STAThread] public static int Main() {
            try {
                using (MainForm home = new MainForm()) {
                    home.StartPosition = FormStartPosition.Manual; home.Location = new System.Drawing.Point(-20000, -20000); var handle = home.Handle;
                    using (Timer rescue = new Timer { Interval = 500 }) {
                        bool modalBlocked = false;
                        rescue.Tick += delegate { var panel = Field<OnlineAuthorSettingsForm>(home, "_onlineAuthorSettingsForm"); if (panel != null && panel.Modal) { modalBlocked = true; panel.Close(); } };
                        rescue.Start(); Call(home, "ShowOnlineAuthorSettingsDialog", home); rescue.Stop();
                        Check(!modalBlocked, "Opening online settings blocked the home window's call stack.");
                    }
                    OnlineAuthorSettingsForm online = Field<OnlineAuthorSettingsForm>(home, "_onlineAuthorSettingsForm");
                    online.Location = home.Location;
                    Check(online.Visible && !online.Modal && online.Owner == home && IsWindowEnabled(home.Handle), "Online settings must be modeless and owned by home.");
                    Call(online, "OpenEntityLibrary");
                    AuthorEntityLibraryForm library = Field<AuthorEntityLibraryForm>(home, "_authorEntityLibraryForm"); library.Location = home.Location;
                    Check(library.Visible && library.Owner == home && !library.Modal && IsWindowEnabled(home.Handle), "Entity library navigation disabled home or used the settings owner.");
                    Check(Field<TabControl>(library, "_tabs").TabCount == 3, "Entity library must expose only three main sections; advanced views belong to contextual dialogs.");
                    Call(home, "ShowOnlineAuthorSettingsDialog", library);
                    Check(Object.ReferenceEquals(online, Field<OnlineAuthorSettingsForm>(home, "_onlineAuthorSettingsForm")), "Opening settings twice must reuse its existing instance.");
                    library.Hide(); online.Hide(); Application.DoEvents();
                    Check(IsWindowEnabled(home.Handle), "Returning to home must not leave a hidden modal blocker.");
                    Call(home, "ShowAuthorEntityLibrary");
                    Check(Object.ReferenceEquals(library, Field<AuthorEntityLibraryForm>(home, "_authorEntityLibraryForm")) && library.Visible, "Hidden entity library must reopen without duplication.");
                    Call(home, "ShowOnlineAuthorSettingsDialog", home);
                    bool previous = Field<bool>(home, "_onlineAuthorLookupEnabled"); Field<CheckBox>(online, "_enabled").Checked = !previous;
                    Call(online, "SaveValues");
                    Check(Field<OnlineAuthorSettingsForm>(home, "_onlineAuthorSettingsForm") == null && Field<bool>(home, "_onlineAuthorLookupEnabled") == !previous, "Saving modeless settings must apply values and release its singleton.");
                    var settings = new UserSettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppFiles.UserSettings)).Load();
                    Check(settings.OnlineAuthorLookupEnabled == !previous, "Saved online settings must persist.");
                    Call(home, "ShowOnlineAuthorSettingsDialog", home);
                    online = Field<OnlineAuthorSettingsForm>(home, "_onlineAuthorSettingsForm"); online.Location = home.Location;
                    Field<CheckBox>(online, "_enabled").Checked = previous; ((Button)online.CancelButton).PerformClick();
                    Check(online.IsDisposed && Field<bool>(home, "_onlineAuthorLookupEnabled") == !previous, "Cancel must close modeless settings without applying edits.");
                    Call(home, "ShowOnlineAuthorSettingsDialog", home);
                    online = Field<OnlineAuthorSettingsForm>(home, "_onlineAuthorSettingsForm"); online.Location = home.Location;
                    home.Close(); Application.DoEvents();
                    Check(online.IsDisposed && library.IsDisposed, "Closing home must release its owned management windows.");
                }
                Console.WriteLine("[PASS] Home remains enabled across online settings/entity library navigation; singleton reuse, hide/reopen, save/persistence, cancel and owner shutdown passed."); return 0;
            } catch (Exception error) { Console.Error.WriteLine("[FAIL] " + error); return 1; }
        }
    }
}