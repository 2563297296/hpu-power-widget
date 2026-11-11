using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    internal static class DiagnosticLog
    {
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly int ProcessId = GetProcessId();
        private static string directory;
        private const string FileName = "debug.log";

        private static int GetProcessId()
        {
            using (Process process = Process.GetCurrentProcess()) return process.Id;
        }

        public static void Write(string stage)
        {
            lock (Gate)
            {
                string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)
                    + " [pid " + ProcessId + ", thread " + Thread.CurrentThread.ManagedThreadId + "] "
                    + stage + Environment.NewLine;
                if (directory != null && TryAppend(directory, line)) return;
                directory = null;
                foreach (string candidate in CandidateDirectories())
                {
                    if (!TryAppend(candidate, line)) continue;
                    directory = candidate;
                    return;
                }
            }
        }

        private static string[] CandidateDirectories()
        {
            string dataDirectory = AppData.StorageDirectory;
            return dataDirectory == null ? new string[0] : new[] { Path.Combine(dataDirectory, "logs") };
        }

        private static bool TryAppend(string folder, string line)
        {
            try
            {
                if (!Path.IsPathRooted(folder)) return false;
                AppData.EnsureDirectory(folder);
                string path = Path.Combine(folder, FileName);
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= 512 * 1024)
                {
                    string previous = Path.Combine(folder, "debug.previous.log");
                    if (File.Exists(previous)) File.Replace(path, previous, null);
                    else File.Move(path, previous);
                }
                File.AppendAllText(path, line, Utf8);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
        }

        public static void RecordException(string stage, Exception error)
        {
            var report = new StringBuilder(stage);
            for (Exception current = error; current != null; current = current.InnerException)
            {
                report.AppendLine();
                report.Append(current.GetType().FullName);
                report.Append(" (0x").Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append(')');
                if (!String.IsNullOrEmpty(current.StackTrace)) report.AppendLine().Append(current.StackTrace);
            }
            Write(report.ToString());
        }

        public static void Start()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                RecordException("runtime.unhandled; terminating=" + e.IsTerminating, e.ExceptionObject as Exception);
            };
            AppDomain.CurrentDomain.ProcessExit += delegate { Write("process.exit"); };
            Write("process.start; version=" + Assembly.GetExecutingAssembly().GetName().Version
                + "; windows=" + Environment.OSVersion.Version + "; x64=" + Environment.Is64BitProcess);
        }

        public static void ShowError(string message)
        {
            string folder;
            lock (Gate) folder = directory;
            if (folder != null) message += Environment.NewLine + Environment.NewLine + Path.Combine(folder, FileName);
            MessageBox.Show(message, "HPU 用电 · 诊断", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void OpenFolder()
        {
            Write("log.open");
            string folder;
            lock (Gate) folder = directory;
            if (folder == null)
            {
                ShowError("无法写入诊断日志，请检查数据目录的访问权限和可用空间。");
                return;
            }
            try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
            catch (Exception error)
            {
                RecordException("log.open.failed", error);
                ShowError("无法打开日志文件夹。可以手动打开以下路径。");
            }
        }
    }
}
