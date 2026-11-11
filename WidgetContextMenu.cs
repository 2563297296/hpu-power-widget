using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    internal sealed class WidgetContextMenu : ContextMenuStrip
    {
        private readonly Timer outsideClick = new Timer { Interval = 15 };
        private int previousButtons;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        public WidgetContextMenu()
        {
            AutoClose = true;
            outsideClick.Tick += delegate { CheckOutsideClick(MouseButtonsDown(), Cursor.Position); };
        }

        private void CheckOutsideClick(int buttons, Point point)
        {
            bool pressed = (buttons & ~previousButtons) != 0;
            previousButtons = buttons;
            if (pressed && !ContainsPoint(this, point))
                Close(ToolStripDropDownCloseReason.AppClicked);
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);
            previousButtons = MouseButtonsDown();
            outsideClick.Start();
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            outsideClick.Stop();
            base.OnClosed(e);
        }

        internal static bool ContainsPoint(ToolStripDropDown menu, Point point)
        {
            if (!menu.Visible) return false;
            if (menu.Bounds.Contains(point)) return true;
            foreach (ToolStripItem item in menu.Items)
            {
                var branch = item as ToolStripDropDownItem;
                if (branch != null && branch.HasDropDownItems && ContainsPoint(branch.DropDown, point)) return true;
            }
            return false;
        }

        private static int MouseButtonsDown()
        {
            return ((GetAsyncKeyState(1) & 0x8000) != 0 ? 1 : 0)
                | ((GetAsyncKeyState(2) & 0x8000) != 0 ? 2 : 0)
                | ((GetAsyncKeyState(4) & 0x8000) != 0 ? 4 : 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                outsideClick.Stop();
                base.Dispose(disposing);
                outsideClick.Dispose();
                return;
            }
            base.Dispose(disposing);
        }
    }
}
