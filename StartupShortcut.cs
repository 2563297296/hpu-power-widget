using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HpuPowerWindows
{
    internal static class StartupShortcut
    {
        private static string ShortcutPath()
        {
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (String.IsNullOrEmpty(directory)) throw new IOException("Windows 启动文件夹不可用。");
            return Path.Combine(directory, "HPU 用电.lnk");
        }

        public static bool IsEnabled()
        {
            try { return File.Exists(ShortcutPath()); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static void SetEnabled(bool enabled)
        {
            UpdateShortcut(ShortcutPath(), typeof(StartupShortcut).Assembly.Location, enabled);
        }

        internal static void UpdateShortcut(string path, string executable, bool enabled)
        {
            if (!enabled)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            // Stage outside the Startup folder until the link is complete.
            string parent = Path.GetDirectoryName(directory) ?? directory;
            string temporary = Path.Combine(parent, "hpu-" + Guid.NewGuid().ToString("N") + ".lnk");
            object shell = null;
            object shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(type);
                dynamic link = ((dynamic)shell).CreateShortcut(temporary);
                shortcut = link;
                link.TargetPath = executable;
                link.WorkingDirectory = Path.GetDirectoryName(executable);
                link.Description = "HPU 宿舍用电小组件";
                link.Save();
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
