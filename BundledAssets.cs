using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace HpuPowerWindows
{
    internal static class BundledAssets
    {
        private static readonly Assembly AppAssembly = typeof(BundledAssets).Assembly;
        private static readonly object RuntimeGate = new object();

        private static ZipArchive Open()
        {
            Stream stream = AppAssembly.GetManifestResourceStream("HpuPowerWindows.Runtime.zip");
            if (stream == null) throw new InvalidDataException("程序内置资源缺失。");
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }

        private static ZipArchiveEntry Entry(ZipArchive archive, string name)
        {
            ZipArchiveEntry entry = archive.GetEntry(name);
            if (entry == null) throw new FileNotFoundException("程序内置资源缺失。", name);
            return entry;
        }

        public static string ReadText(string name)
        {
            using (ZipArchive archive = Open())
            using (StreamReader reader = new StreamReader(Entry(archive, name).Open()))
                return reader.ReadToEnd();
        }

        public static string PrepareQueryRuntime()
        {
            lock (RuntimeGate)
            {
                string root = AppData.StorageDirectory;
                if (root == null) throw new IOException("没有可写入的目录，无法准备查询环境。");

                string directory = Path.Combine(root, "runtime", AppAssembly.ManifestModule.ModuleVersionId.ToString("N"));
                try
                {
                    ExtractRuntime(directory);
                    return directory;
                }
                catch (IOException error) { throw RuntimeError(directory, error); }
                catch (UnauthorizedAccessException error) { throw RuntimeError(directory, error); }
                catch (System.Security.SecurityException error) { throw RuntimeError(directory, error); }
            }
        }

        private static IOException RuntimeError(string directory, Exception error)
        {
            DiagnosticLog.RecordException("query.runtime.directory_unavailable", error);
            return new IOException("无法在以下目录准备查询环境：" + directory, error);
        }

        private static void ExtractRuntime(string directory)
        {
            AppData.EnsureDirectory(directory);
            using (ZipArchive archive = Open())
            {
                Extract(archive, directory, "node.exe");
                Extract(archive, directory, "fetch-usage.js");
                Extract(archive, directory, "NODE-LICENSE.txt");
            }
        }

        private static void Extract(ZipArchive archive, string directory, string name)
        {
            ZipArchiveEntry entry = Entry(archive, name);
            string target = Path.Combine(directory, name);
            if (IsComplete(target, entry.Length)) return;
            string temporary = Path.Combine(directory, Path.GetRandomFileName());
            try
            {
                using (Stream input = entry.Open())
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                    input.CopyTo(output);
                try
                {
                    if (File.Exists(target)) File.Replace(temporary, target, null);
                    else File.Move(temporary, target);
                }
                catch (IOException)
                {
                    if (!IsComplete(target, entry.Length)) throw;
                }
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool IsComplete(string path, long length)
        {
            FileInfo info = new FileInfo(path);
            return info.Exists && info.Length == length;
        }
    }
}
