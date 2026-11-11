using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    internal sealed class TrayIcon : NativeWindow, IDisposable
    {
        private const int CallbackMessage = 0x8001;
        private const int Add = 0, Modify = 1, Delete = 2, SetFocus = 3;
        private const int MessageFlag = 1, IconFlag = 2, TipFlag = 4, InfoFlag = 16;
        private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        private readonly Icon icon;
        private readonly ContextMenuStrip menu;
        private bool attempted;

        public bool Available { get; private set; }
        public event Action Activated;
        public event Action AvailabilityChanged;

        public TrayIcon(Icon icon, ContextMenuStrip menu)
        {
            this.icon = icon;
            this.menu = menu;
            CreateHandle(new CreateParams());
            if (menu != null) menu.Closed += MenuClosed;
        }

        public void Register()
        {
            if (Available || Handle == IntPtr.Zero) return;
            Data data = CreateData();
            data.Flags = MessageFlag | IconFlag | TipFlag;
            data.Callback = CallbackMessage;
            data.Icon = icon.Handle;
            data.Tip = "HPU 宿舍用电";
            bool added = Shell_NotifyIcon(Add, ref data);
            int error = Marshal.GetLastWin32Error();
            if (!attempted || added)
                DiagnosticLog.Write(added ? "tray.registered" : "tray.registration_failed; win32=" + error);
            attempted = true;
            if (!added) return;
            Available = true;
            if (AvailabilityChanged != null) AvailabilityChanged();
        }

        public bool ShowBalloonTip(int timeout, string title, string text, ToolTipIcon kind)
        {
            if (!Available) return false;
            Data data = CreateData();
            data.Flags = InfoFlag;
            data.Title = title.Length <= 63 ? title : title.Substring(0, 63);
            data.Info = text.Length <= 255 ? text : text.Substring(0, 255);
            data.Timeout = timeout;
            data.InfoFlags = (int)kind;
            return Shell_NotifyIcon(Modify, ref data);
        }

        private Data CreateData()
        {
            return new Data { Size = Marshal.SizeOf(typeof(Data)), Window = Handle, Id = 1 };
        }

        protected override void WndProc(ref Message message)
        {
            if (TaskbarCreated != 0 && message.Msg == TaskbarCreated)
            {
                bool wasAvailable = Available;
                Available = false;
                attempted = false;
                Register();
                if (wasAvailable && !Available && AvailabilityChanged != null) AvailabilityChanged();
            }
            else if (message.Msg == CallbackMessage)
            {
                int notification = message.LParam.ToInt32();
                if (notification == 0x202 && Activated != null) Activated();
                if (notification == 0x205 && menu != null)
                {
                    SetForegroundWindow(Handle);
                    menu.Show(Cursor.Position);
                }
                return;
            }
            base.WndProc(ref message);
        }

        private void MenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            Data data = CreateData();
            Shell_NotifyIcon(SetFocus, ref data);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            if (menu != null) menu.Closed -= MenuClosed;
            if (Available)
            {
                Data data = CreateData();
                Shell_NotifyIcon(Delete, ref data);
            }
            Available = false;
            DestroyHandle();
            if (menu != null) menu.Dispose();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Data
        {
            public int Size;
            public IntPtr Window;
            public int Id, Flags, Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public int State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public int Timeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
            public int InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int action, ref Data data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
    }
}
