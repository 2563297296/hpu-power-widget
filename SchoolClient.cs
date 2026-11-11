using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Threading;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HpuPowerWindows
{
    public static class SchoolPowerClient
    {
        public static Task<UsageSnapshot> QueryAsync(RoomOption room)
        {
            return QueryAsync(room, CancellationToken.None);
        }

        internal static Task<UsageSnapshot> QueryAsync(RoomOption room, CancellationToken cancellation)
        {
            return Task.Run(() => QueryBlocking(room, cancellation), cancellation);
        }

        private static UsageSnapshot QueryBlocking(RoomOption room, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string runtime = BundledAssets.PrepareQueryRuntime();
            cancellation.ThrowIfCancellationRequested();
            string node = Path.Combine(runtime, "node.exe");
            string script = Path.Combine(runtime, "fetch-usage.js");
            var json = new JavaScriptSerializer();
            string roomJson = json.Serialize(room);
            string argument = Convert.ToBase64String(Encoding.UTF8.GetBytes(roomJson));

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = node;
            info.Arguments = Quote(script) + " " + Quote(argument);
            info.WorkingDirectory = runtime;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.Start();
                string output;
                string stderr;
                using (cancellation.Register(() => StopProcess(process)))
                {
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(45000))
                    {
                        StopProcess(process);
                        cancellation.ThrowIfCancellationRequested();
                        throw new TimeoutException("学校查询响应超时");
                    }
                    cancellation.ThrowIfCancellationRequested();
                    output = outputTask.GetAwaiter().GetResult();
                    stderr = errorTask.GetAwaiter().GetResult();
                }
                if (String.IsNullOrWhiteSpace(output))
                    throw new InvalidOperationException(String.IsNullOrWhiteSpace(stderr)
                        ? "Node 查询进程没有返回结果" : stderr.Trim());
                NodeResult result = json.Deserialize<NodeResult>(output);
                if (result == null) throw new InvalidOperationException("查询结果格式无效");
                if (!String.IsNullOrEmpty(result.error)) throw new InvalidOperationException(result.error);

                UsageSnapshot snapshot = new UsageSnapshot();
                snapshot.roomId = room == null ? "" : room.id;
                snapshot.lightingBalance = result.lightingBalance;
                snapshot.airBalance = result.airBalance;
                snapshot.lightingAvg7 = result.lightingAvg7;
                snapshot.airAvg7 = result.airAvg7;
                snapshot.avgDaily7 = result.avgDaily7;
                snapshot.lightingDaysLeft = result.lightingDaysLeft;
                snapshot.airDaysLeft = result.airDaysLeft;
                snapshot.daysLeft = result.daysLeft;
                snapshot.lightingDepleteDate = result.lightingDepleteDate ?? "";
                snapshot.airDepleteDate = result.airDepleteDate ?? "";
                snapshot.latestDate = result.latestDate ?? "";
                snapshot.latestKwh = result.latestKwh;
                snapshot.latestCost = result.latestCost;
                snapshot.sevenDayKwh = result.sevenDayKwh;
                snapshot.sevenDayCost = result.sevenDayCost;
                snapshot.snapshotAt = result.snapshotAt ?? "";
                snapshot.sourceStatus = String.IsNullOrEmpty(result.sourceStatus) ? "fresh" : result.sourceStatus;
                snapshot.fetchedAt = DateTime.Now;
                return snapshot;
            }
        }

        private static void StopProcess(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception error) { Trace.TraceWarning(error.Message); }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private sealed class NodeResult
        {
            public string error { get; set; }
            public double lightingBalance { get; set; }
            public double airBalance { get; set; }
            public double lightingAvg7 { get; set; }
            public double airAvg7 { get; set; }
            public double avgDaily7 { get; set; }
            public double lightingDaysLeft { get; set; }
            public double airDaysLeft { get; set; }
            public double daysLeft { get; set; }
            public string lightingDepleteDate { get; set; }
            public string airDepleteDate { get; set; }
            public string latestDate { get; set; }
            public double latestKwh { get; set; }
            public double latestCost { get; set; }
            public double sevenDayKwh { get; set; }
            public double sevenDayCost { get; set; }
            public string snapshotAt { get; set; }
            public string sourceStatus { get; set; }
        }
    }
}
