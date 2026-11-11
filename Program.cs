using System;
using System.Threading;
using System.Security.AccessControl;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    public static class Program
    {
        private const string InstanceName = @"Local\HpuPowerWindows.Widget.Instance";
        private const string WakeName = @"Local\HpuPowerWindows.Widget.Wake";

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--check-storage")
            {
                Environment.ExitCode = AppData.CheckStorage();
                return;
            }
            if (args.Length == 1 && args[0] == "--check-tray")
            {
                using (var tray = new TrayIcon(System.Drawing.SystemIcons.Application, null))
                {
                    tray.Register();
                    Environment.ExitCode = tray.Available ? 0 : 2;
                }
                return;
            }
            DiagnosticLog.Start();
            try
            {
                using (var instance = OpenInstanceMutex())
                {
                    bool acquired;
                    try { acquired = instance.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }

                    if (!acquired)
                    {
                        DiagnosticLog.Write("application.instance.already_running");
                        SignalExistingInstance();
                        return;
                    }

                    try
                    {
                        using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName))
                            RunWidget(wake);
                    }
                    finally { instance.ReleaseMutex(); }
                }
                DiagnosticLog.Write("application.loop.ended");
            }
            catch (Exception error)
            {
                DiagnosticLog.RecordException("application.fatal", error);
                Environment.ExitCode = 1;
                DiagnosticLog.ShowError("程序遇到异常并已停止。请保留诊断日志以便排查。");
            }
        }

        private static Mutex OpenInstanceMutex()
        {
            try { return Mutex.OpenExisting(InstanceName, MutexRights.Synchronize | MutexRights.Modify); }
            catch (WaitHandleCannotBeOpenedException) { return new Mutex(false, InstanceName); }
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (var wake = EventWaitHandle.OpenExisting(WakeName, EventWaitHandleRights.Modify))
                    wake.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException error)
            {
                DiagnosticLog.RecordException("application.instance.wake_denied", error);
            }
        }

        private static void RunWidget(EventWaitHandle wake)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(true);
            using (WidgetForm widget = new WidgetForm())
            {
                RegisteredWaitHandle registration = null;
                widget.Shown += delegate
                {
                    registration = ThreadPool.RegisterWaitForSingleObject(wake, delegate
                    {
                        try { widget.BeginInvoke((MethodInvoker)widget.RestoreFromLaunch); }
                        catch (InvalidOperationException) { } // The widget is closing.
                    }, null, Timeout.Infinite, false);
                };
                try { Application.Run(widget); }
                finally { if (registration != null) registration.Unregister(null); }
            }
        }
    }
}
