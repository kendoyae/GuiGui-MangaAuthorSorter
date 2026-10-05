using System;
using System.Reflection;

namespace MangaAuthorSorter
{
    internal static class AppVersion
    {
        public static Version Current
        {
            get
            {
                Version value = Assembly.GetExecutingAssembly().GetName().Version;
                return value == null
                    ? new Version(0, 0, 0)
                    : new Version(value.Major, value.Minor, Math.Max(0, value.Build));
            }
        }

        public static string Display
        {
            get
            {
                Version value = Current;
                return "V" + value.Major + "." + value.Minor + "." + value.Build;
            }
        }

        public static string UserAgentVersion
        {
            get { return Display.Substring(1); }
        }
    }
}
