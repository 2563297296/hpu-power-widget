using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading.Tasks;

namespace HpuPowerWindows
{
    public static class AppData
    {
        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        }

        private static readonly object WriteGate = new object();
        private static Task pendingWrite = Task.FromResult(true);

        private static Task<string> QueueWrite(string name, string value)
        {
            lock (WriteGate)
            {
                Task<string> write = pendingWrite.ContinueWith(
                    previous => TryWriteData(name, value),
                    System.Threading.CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
                pendingWrite = write;
                return write;
            }
        }
        private const string DataFolderName = "HPU-Power-Widget";

        private static readonly string[] WriteDirectories = new[]
        {
            DataFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify)),
            DataFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify)),
            DataFolder(AppDirectory)
        }.Where(path => path != null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private static readonly Lazy<string> ActiveDirectory = new Lazy<string>(FindWritableDirectory);

        private static string DataFolder(string root)
        {
            return String.IsNullOrEmpty(root) || !Path.IsPathRooted(root)
                ? null : Path.Combine(root, DataFolderName);
        }

        internal static string StorageDirectory { get { return ActiveDirectory.Value; } }

        private static string FindWritableDirectory()
        {
            foreach (string directory in WriteDirectories)
            {
                string probe = Path.Combine(directory, "check-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    WriteData(probe, "first");
                    WriteData(probe, "second");
                    return directory;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
                finally { DeleteProbe(probe); }
            }
            return null;
        }

        private static void DeleteProbe(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateDirectory(string path, IntPtr security);

        internal static void EnsureDirectory(string directory)
        {
            if (Directory.Exists(directory)) return;
            try
            {
                Directory.CreateDirectory(directory);
                return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string parent = Path.GetDirectoryName(directory);
            if (!String.IsNullOrEmpty(parent)) EnsureDirectory(parent);
            if (CreateDirectory(directory, IntPtr.Zero)) return;
            int error = Marshal.GetLastWin32Error();
            if (Directory.Exists(directory)) return;
            if (error == 5) throw new UnauthorizedAccessException("Windows 拒绝创建数据目录。");
            throw new IOException("无法创建数据目录。", new Win32Exception(error));
        }

        internal static int CheckStorage()
        {
            string directory = StorageDirectory;
            if (directory == null)
            {
                Console.Error.WriteLine(String.Join(Environment.NewLine, WriteDirectories));
                return 2;
            }

            Console.WriteLine(directory);
            string name = "check-" + Guid.NewGuid().ToString("N") + ".json";
            string path = Path.Combine(directory, name);
            try
            {
                string first = QueueWrite(name, "{\"refreshMinutes\":13}").GetAwaiter().GetResult();
                if (first != null) throw new IOException(first);
                if (ReadData<WidgetConfig>(name).refreshMinutes != 13) return 3;
                string second = QueueWrite(name, "{\"refreshMinutes\":17}").GetAwaiter().GetResult();
                if (second != null) throw new IOException(second);
                if (ReadData<WidgetConfig>(name).refreshMinutes != 17) return 3;
                Console.WriteLine(BundledAssets.PrepareQueryRuntime());
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 4;
            }
            finally { DeleteProbe(path); }
        }

        private static IEnumerable<string> DataPaths(string name)
        {
            string roaming = DataFolder(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            return WriteDirectories.Concat(new[] { roaming }).Where(path => path != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(directory => Path.Combine(directory, name))
                .OrderByDescending(LastWriteTime);
        }

        private static DateTime LastWriteTime(string path)
        {
            try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
            catch (IOException) { return DateTime.MinValue; }
            catch (UnauthorizedAccessException) { return DateTime.MinValue; }
        }

        private static T ReadData<T>(string name) where T : class
        {
            foreach (string path in DataPaths(name))
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    T value = CreateSerializer().Deserialize<T>(File.ReadAllText(path));
                    if (value != null) return value;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            return null;
        }

        private static string TryWriteData(string name, string value)
        {
            string directory = StorageDirectory;
            if (directory == null)
                return "Windows 拒绝写入以下数据目录，或磁盘空间不足：" +
                    Environment.NewLine + String.Join(Environment.NewLine, WriteDirectories);
            string path = Path.Combine(directory, name);
            try
            {
                WriteData(path, value);
                if (Path.GetFileName(path) == "config.json") DiagnosticLog.Write("config.saved");
                return null;
            }
            catch (IOException error) { return ReportWriteFailure(path, error); }
            catch (UnauthorizedAccessException error) { return ReportWriteFailure(path, error); }
            catch (System.Security.SecurityException error) { return ReportWriteFailure(path, error); }
        }

        private static string ReportWriteFailure(string path, Exception error)
        {
            DiagnosticLog.RecordException("data.write.failed; file=" + Path.GetFileName(path), error);
            return path + Environment.NewLine + error.Message;
        }

        private static void WriteData(string path, string value)
        {
            string directory = Path.GetDirectoryName(path);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            EnsureDirectory(directory);
            try
            {
                File.WriteAllText(temporary, value);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }

        public static string AppDirectory
        {
            get { return Path.GetDirectoryName(typeof(AppData).Assembly.Location); }
        }

        public static WidgetConfig LoadConfig()
        {
            return ReadData<WidgetConfig>("config.json") ?? new WidgetConfig();
        }

        public static Task<string> QueueConfig(WidgetConfig config)
        {
            return QueueWrite("config.json", CreateSerializer().Serialize(config));
        }

        public static List<RoomOption> LoadCatalog()
        {
            string catalog = BundledAssets.ReadText("room_catalog.json");
            CatalogRoot root = CreateSerializer().Deserialize<CatalogRoot>(catalog);
            if (root == null || root.entries == null || root.entries.Count == 0)
                throw new InvalidDataException("寝室目录为空");
            return root.entries;
        }

        public static UsageSnapshot LoadSnapshot(string roomId)
        {
            UsageSnapshot snapshot = ReadData<UsageSnapshot>(SnapshotName(roomId));
            if (snapshot == null || snapshot.fetchedAt == default(DateTime)) return null;
            snapshot.roomId = roomId ?? "";
            return snapshot;
        }

        public static void QueueSnapshot(UsageSnapshot snapshot)
        {
            if (snapshot == null || String.IsNullOrEmpty(snapshot.roomId)) return;
            QueueWrite(SnapshotName(snapshot.roomId), CreateSerializer().Serialize(snapshot));
        }

        private static string SnapshotName(string roomId)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(roomId ?? ""));
                string key = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return "snapshot-" + key + ".json";
            }
        }
    }
}
