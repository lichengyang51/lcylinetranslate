using System.Reflection;

namespace MultiChatManager2
{
    public static class VersionManager
    {
        public static Version CurrentVersion
        {
            get
            {
                return Assembly
                           .GetExecutingAssembly()
                           .GetName()
                           .Version
                       ?? new Version(1, 0, 0, 0);
            }
        }

        public static string CurrentVersionText
        {
            get
            {
                return $"{CurrentVersion.Major}." +
                       $"{CurrentVersion.Minor}." +
                       $"{CurrentVersion.Build}";
            }
        }
    }
}