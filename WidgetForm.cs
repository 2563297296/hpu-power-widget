using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace HpuPowerWindows
{
    public sealed partial class WidgetForm : Form
    {
        private enum WidgetState { Empty, Loading, Fresh, Cached, Error }

        private readonly WidgetConfig config;

        private RoomOption room;
        private UsageSnapshot snapshot;
        private WidgetState state = WidgetState.Empty;
        private string lastError = "";
        private readonly Timer refreshTimer;
        private readonly Timer clockTimer;
        private readonly Timer positionSaveTimer = new Timer { Interval = 300 };
        private bool positionTracking;
        private readonly IconButton refreshIcon;
        private readonly IconButton settingsIcon;
        private readonly TrayIcon tray;
        private TaskbarWidget taskbarWidget;
        private ContextMenuStrip taskbarMenu;
        private SettingsForm activeSettings;
        private readonly System.Threading.CancellationTokenSource shutdown = new System.Threading.CancellationTokenSource();
        private Task<UsageSnapshot> activeQuery;
        private readonly Icon appIcon;
        private bool trayDisposed;
        private readonly ToolTip toolTip = new ToolTip();
        private bool refreshing;
        private bool settingsOpen;
        private string requestedRoomId;
        private bool refreshAfterCurrent;
        private bool exiting;
        private bool dragging;
        private Point dragStartScreen;
        private Point dragStartForm;
        private bool dragMoved;

        private int CardWidth { get { return WidgetConfig.NormalizeCardWidth(config.cardWidth); } }
        private int CardHeight { get { return WidgetConfig.NormalizeCardHeight(config.cardHeight); } }
        private readonly float widgetScale;

        public WidgetForm() : this(GetDesktopScale()) { }

        internal WidgetForm(float desktopScale)
        {
            widgetScale = desktopScale;
            config = AppData.LoadConfig();

            room = RoomDirectory.Find(config.roomId);
            if (room != null) snapshot = AppData.LoadSnapshot(room.id);
            state = snapshot == null ? WidgetState.Empty : WidgetState.Cached;

            AutoScaleMode = AutoScaleMode.None;
            Text = "HPU 用电";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(ScaleWidget(CardWidth), ScaleWidget(CardHeight));
            BackColor = Color.FromArgb(12, 34, 62);
            ForeColor = Color.White;
            ShowInTaskbar = false;
            TopMost = config.topMost;
            DoubleBuffered = true;
            Opacity = Math.Max(0.70, Math.Min(1.0, config.opacity / 100.0));
            ContextMenuStrip = BuildContextMenu();

            refreshIcon = MakeIconButton(CardWidth - 110, 8, 46, IconKind.Refresh);
            toolTip.SetToolTip(refreshIcon, "立即刷新");
            settingsIcon = MakeIconButton(CardWidth - 58, 8, 46, IconKind.Settings);
            toolTip.SetToolTip(settingsIcon, "设置");
            Controls.Add(refreshIcon);
            Controls.Add(settingsIcon);
            appearanceTimer.Tick += delegate { AnimateAppearance(); };
            VisibleChanged += delegate { UpdateAppearanceTimer(); };
            Disposed += delegate { appearanceTimer.Dispose(); };
            MouseEnter += delegate { hovered = true; Invalidate(); };
            MouseLeave += delegate { hovered = false; Invalidate(); };
            ApplyAppearance();
            refreshIcon.Click += delegate { RefreshAsync(); };
            settingsIcon.Click += delegate { OpenSettings(); };

            MouseDown += BeginDrag;
            MouseMove += MoveDrag;
            MouseUp += EndDrag;
            MouseCaptureChanged += delegate { if (dragging && !Capture) FinishDrag(); };
            LocationChanged += delegate
            {
                if (!positionTracking || dragging || !Visible || config.taskbarMode || exiting) return;
                positionSaveTimer.Stop();
                positionSaveTimer.Start();
            };
            positionSaveTimer.Tick += delegate { SaveCardPosition(); };
            Disposed += delegate { positionSaveTimer.Dispose(); };
            MouseDoubleClick += delegate { OpenSettings(); };

            refreshTimer = new Timer();
            refreshTimer.Interval = Math.Max(1, config.refreshMinutes) * 60000;
            refreshTimer.Tick += delegate { RefreshAsync(); };

            clockTimer = new Timer();
            clockTimer.Interval = 30000;
            clockTimer.Tick += delegate { Invalidate(); };

            appIcon = Icon.ExtractAssociatedIcon(typeof(WidgetForm).Assembly.Location);
            if (appIcon != null) Icon = appIcon;
            tray = new TrayIcon(appIcon ?? SystemIcons.Application, BuildContextMenu());
            tray.Activated += ShowWidget;
            tray.AvailabilityChanged += UpdateTaskbarFallback;
            Disposed += delegate
            {
                refreshTimer.Dispose();
                clockTimer.Dispose();
                DisposeTray();
                if (taskbarWidget != null) taskbarWidget.Dispose();
                if (taskbarMenu != null) taskbarMenu.Dispose();
                shutdown.Dispose();
                toolTip.Dispose();
            };

            FormClosing += OnFormClosing;
            Shown += delegate
            {
                tray.Register();
                RestorePosition();
                positionTracking = true;
                refreshTimer.Start();
                clockTimer.Start();
                UpdateRoomHeader();
                Invalidate();
                ApplyDisplayMode();
                if (room == null) BeginInvoke((MethodInvoker)OpenSettings);
                else RefreshAsync();
            };
        }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new WidgetContextMenu();
            menu.Font = new Font("Microsoft YaHei UI", 10f);
            menu.ShowImageMargin = false;
            menu.Items.Add("立即刷新", null, delegate { RefreshAsync(); });
            menu.Items.Add("设置", null, delegate { OpenSettings(); });
            foreach (ToolStripItem item in menu.Items)
            {
                item.AutoSize = false;
                item.Height = 34;
                item.Padding = new Padding(10, 4, 10, 4);
            }
            ToolStripMenuItem top = new ToolStripMenuItem("窗口置顶");
            top.Checked = TopMost;
            top.Click += delegate
            {
                TopMost = !TopMost;
                top.Checked = TopMost;
                config.topMost = TopMost;
                SaveSettings();
            };
            menu.Items.Add(top);
            ToolStripMenuItem appearanceMenu = new ToolStripMenuItem("外观");
            ToolStripMenuItem[] cardStyles = AddAppearanceItems(appearanceMenu, AppearanceNames, false);
            ToolStripMenuItem[] taskbarStyles = AddAppearanceItems(appearanceMenu, TaskbarWidget.AppearanceNames, true);
            menu.Opening += delegate
            {
                top.Checked = TopMost;
                UpdateAppearanceItems(cardStyles, !config.taskbarMode, AppearanceIndex);
                int taskbarIndex = TaskbarWidget.NormalizeAppearance(config.taskbarAppearance);
                UpdateAppearanceItems(taskbarStyles, config.taskbarMode, taskbarIndex);
            };
            UpdateAppearanceItems(cardStyles, !config.taskbarMode, AppearanceIndex);
            UpdateAppearanceItems(taskbarStyles, config.taskbarMode,
                TaskbarWidget.NormalizeAppearance(config.taskbarAppearance));
            menu.Items.Add(appearanceMenu);
            ToolStripMenuItem display = new ToolStripMenuItem("显示模式");
            var card = new ToolStripMenuItem("桌面卡片", null, delegate { SelectDisplayMode(false, config.taskbarRight); });
            var left = new ToolStripMenuItem("任务栏左侧", null, delegate { SelectDisplayMode(true, false); });
            var right = new ToolStripMenuItem("任务栏右侧", null, delegate { SelectDisplayMode(true, true); });
            display.DropDownItems.AddRange(new ToolStripItem[] { card, left, right });
            display.DropDownOpening += delegate
            {
                card.Checked = !config.taskbarMode;
                left.Checked = config.taskbarMode && !config.taskbarRight;
                right.Checked = config.taskbarMode && config.taskbarRight;
            };
            menu.Items.Add(display);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开诊断日志", null, delegate { DiagnosticLog.OpenFolder(); });
            menu.Items.Add("退出", null, delegate { RequestExit(); });
            return menu;
        }

        private ToolStripMenuItem[] AddAppearanceItems(ToolStripMenuItem menu, string[] names, bool forTaskbar)
        {
            var items = new ToolStripMenuItem[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(names[i]);
                item.Click += delegate
                {
                    if (exiting || config.taskbarMode != forTaskbar) return;
                    if (forTaskbar)
                    {
                        config.taskbarAppearance = index;
                        if (taskbarWidget != null) taskbarWidget.SetAppearance(index);
                    }
                    else
                    {
                        config.appearance = index;
                        ApplyAppearance();
                    }
                    SaveSettings();
                };
                menu.DropDownItems.Add(item);
                items[i] = item;
            }
            return items;
        }

        private static void UpdateAppearanceItems(ToolStripMenuItem[] items, bool available, int selected)
        {
            for (int i = 0; i < items.Length; i++)
            {
                items[i].Available = available;
                items[i].Checked = i == selected;
            }
        }

        private IconButton MakeIconButton(int x, int y, int size, IconKind kind)
        {
            IconButton button = new IconButton();
            button.Kind = kind;
            button.SetBounds(ScaleWidget(x), ScaleWidget(y), ScaleWidget(size), ScaleWidget(size));
            button.AccessibleName = kind == IconKind.Refresh ? "刷新" : "设置";
            return button;
        }


        private async void RefreshAsync()
        {
            if (exiting) return;
            if (refreshing)
            {
                refreshAfterCurrent = room != null && room.id != requestedRoomId;
                return;
            }
            if (room == null)
            {
                state = WidgetState.Empty;
                lastError = "请先选择寝室";
                Invalidate();
                return;
            }

            RoomOption requestedRoom = room;
            refreshing = true;
            requestedRoomId = requestedRoom.id;
            refreshStarted = animationClock.ElapsedMilliseconds;
            refreshIcon.Busy = true;
            if (snapshot == null) state = WidgetState.Loading;
            lastError = "";
            SyncTaskbarReading();
            toolTip.SetToolTip(this, "");
            Invalidate();

            try
            {
                activeQuery = SchoolPowerClient.QueryAsync(requestedRoom, shutdown.Token);
                UsageSnapshot data = await activeQuery;
                if (exiting || IsDisposed) return;
                if (room == null || room.id != requestedRoom.id)
                {
                    refreshAfterCurrent = true;
                    return;
                }
                data.roomId = requestedRoom.id;
                transitionFrom = snapshot;
                transitionStarted = animationClock.ElapsedMilliseconds;
                snapshot = data;
                state = WidgetState.Fresh;
                AppData.QueueSnapshot(data);
                CheckLowBalanceAlert(data);
            }
            catch (Exception error)
            {
                if (!exiting && !IsDisposed && room != null && room.id == requestedRoom.id)
                {
                    lastError = CleanError(error.Message);
                    state = WidgetState.Error;
                    toolTip.SetToolTip(this, lastError);
                }
            }
            finally
            {
                activeQuery = null;
                refreshing = false;
                requestedRoomId = null;
                if (!IsDisposed && !exiting)
                {
                    refreshIcon.Busy = false;
                    SyncTaskbarReading();
                    Invalidate();
                }
                if (!exiting && refreshAfterCurrent)
                {
                    refreshAfterCurrent = false;
                    RefreshAsync();
                }
            }
        }

        private void CheckLowBalanceAlert(UsageSnapshot data)
        {
            if (LowBalanceReminder.Update(config, data, message => tray.ShowBalloonTip(
                7000, room.DisplayText + " · 低电提醒", message, ToolTipIcon.Warning)))
                SaveSettings();
        }

        private string CleanError(string message)
        {
            string value = String.IsNullOrWhiteSpace(message) ? "未知错误" : message.Trim();
            Match http = Regex.Match(value, @"HTTP\s*(\d{3})", RegexOptions.IgnoreCase);
            if (http.Success) return "HTTP " + http.Groups[1].Value;
            if (value.IndexOf("响应超时", StringComparison.OrdinalIgnoreCase) >= 0) return "响应超时";
            if (value.IndexOf("Node.js", StringComparison.OrdinalIgnoreCase) >= 0) return "缺少 Node.js";
            value = value.Replace("\r", " ").Replace("\n", " ");
            if (value.Length > 120) value = value.Substring(0, 120) + "…";
            return value;
        }

        private string ShortStatus()
        {
            if (refreshing) return "刷新中";
            if (state == WidgetState.Error) return "刷新失败";
            if (snapshot == null) return "等待数据";
            return state == WidgetState.Cached ? "缓存" : "";
        }

        private string UpdateStatus()
        {
            string status = ShortStatus();
            if (snapshot == null || snapshot.fetchedAt == default(DateTime)) return status;

            DateTime time = snapshot.fetchedAt.ToLocalTime();
            string stamp = "更新于 " + time.ToString(time.Date == DateTime.Today ? "HH:mm" : "MM-dd HH:mm");
            return String.IsNullOrEmpty(status) ? stamp : status + " · " + stamp;
        }

        private bool saveWarningShown;

        private async void SaveSettings()
        {
            await PersistSettingsAsync(false);
        }

        private async Task PersistSettingsAsync(bool explicitSave)
        {
            string failure = await AppData.QueueConfig(config);
            if (failure == null)
            {
                saveWarningShown = false;
                return;
            }
            if (exiting || IsDisposed) return;
            if (explicitSave)
            {
                MessageBox.Show(this, "本次更改仅在当前运行期间生效。无法保存设置：" +
                    Environment.NewLine + Environment.NewLine + failure, "设置未保存",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (!saveWarningShown)
            {
                tray.ShowBalloonTip(5000, "设置无法保存",
                    "可在设置中点击保存查看无法写入的路径。", ToolTipIcon.Warning);
            }
            saveWarningShown = true;
        }

        private async void OpenSettings()
        {
            if (settingsOpen || exiting) return;
            settingsOpen = true;
            DiagnosticLog.Write("settings.request; taskbar=" + config.taskbarMode + "; hostVisible=" + Visible);
            try
            {
                UpdateAppearanceTimer();
                await ShowSettingsAsync();
            }
            catch (Exception error)
            {
                DiagnosticLog.RecordException("settings.failed", error);
                if (!exiting && !IsDisposed)
                    DiagnosticLog.ShowError("设置窗口打开或保存时发生异常。详细信息已写入诊断日志。");
            }
            finally
            {
                settingsOpen = false;
                if (!IsDisposed && !exiting)
                {
                    UpdateAppearanceTimer();
                    if (config.taskbarMode && taskbarWidget != null) taskbarWidget.RefreshPlacement();
                }
                DiagnosticLog.Write("settings.finished");
            }
        }

        private async Task ShowSettingsAsync()
        {
            DiagnosticLog.Write("settings.construct.begin");
            using (SettingsForm form = new SettingsForm(room, config))
            {
                // The taskbar overlay can hide or move independently while the settings window is open.
                // Keep the dialog attached to the persistent application window.
                if (!Visible) form.StartPosition = FormStartPosition.CenterScreen;
                DialogResult result;
                activeSettings = form;
                try
                {
                    DiagnosticLog.Write("settings.dialog.show");
                    result = form.ShowDialog(this);
                    DiagnosticLog.Write("settings.dialog.result=" + result);
                }
                finally { activeSettings = null; }
                if (exiting || IsDisposed || result != DialogResult.OK) return;
                bool startWithWindows = form.SelectedStartWithWindows;
                bool changedRoom = room == null || form.SelectedRoom == null || room.id != form.SelectedRoom.id;
                room = form.SelectedRoom;
                config.roomId = room == null ? "" : room.id;
                config.refreshMinutes = form.SelectedRefreshMinutes;
                bool thresholdChanged = config.lowBalanceThreshold != form.SelectedLowBalanceThreshold;
                config.lowBalanceThreshold = form.SelectedLowBalanceThreshold;
                config.topMost = form.SelectedTopMost;
                config.opacity = form.SelectedOpacity;
                config.appearance = form.SelectedAppearance;
                config.cardWidth = form.SelectedCardWidth;
                config.cardHeight = form.SelectedCardHeight;
                config.taskbarMode = form.SelectedTaskbarMode;
                config.taskbarRight = form.SelectedTaskbarRight;
                config.taskbarAppearance = form.SelectedTaskbarAppearance;
                ApplyCardSize();
                ApplyAppearance();
                TopMost = config.topMost;
                Opacity = Math.Max(0.70, Math.Min(1.0, config.opacity / 100.0));
                refreshTimer.Interval = Math.Max(1, config.refreshMinutes) * 60000;
                Task persist = PersistSettingsAsync(true);
                if (changedRoom)
                {
                    transitionFrom = null;
                    snapshot = null;
                    state = snapshot == null ? WidgetState.Empty : WidgetState.Cached;
                    lastError = "";
                }
                UpdateRoomHeader();
                ApplyDisplayMode();
                Invalidate();
                await persist;
                if (exiting || IsDisposed) return;
                if (changedRoom && room != null)
                {
                    string id = room.id;
                    UsageSnapshot cached = await Task.Run(() => AppData.LoadSnapshot(id));
                    if (exiting || IsDisposed || room == null || room.id != id) return;
                    if (snapshot == null && cached != null)
                    {
                        snapshot = cached;
                        if (state != WidgetState.Error) state = WidgetState.Cached;
                        SyncTaskbarReading();
                        Invalidate();
                    }
                }
                if (room != null && (changedRoom || snapshot == null || (thresholdChanged && config.lowBalanceThreshold > 0))) RefreshAsync();
                await UpdateStartupAsync(startWithWindows);
            }
        }

        private async Task UpdateStartupAsync(bool enabled)
        {
            try { await Task.Run(() => StartupShortcut.SetEnabled(enabled)); }
            catch (Exception error)
            {
                if (!IsDisposed && !exiting)
                    MessageBox.Show(this, "无法更新开机自启动设置。" + Environment.NewLine + error.Message,
                        "HPU 用电", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateRoomHeader()
        {
            Text = room == null ? "HPU 用电" : room.DisplayText;
            Invalidate();
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragMoved = false;
            dragStartScreen = Cursor.Position;
            dragStartForm = Location;
            Capture = true;
        }

        private void MoveDrag(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point current = Cursor.Position;
            int dx = current.X - dragStartScreen.X;
            int dy = current.Y - dragStartScreen.Y;
            if (!dragMoved)
            {
                Size threshold = SystemInformation.DragSize;
                if (Math.Abs(dx) < Math.Max(4, threshold.Width / 2)
                    && Math.Abs(dy) < Math.Max(4, threshold.Height / 2)) return;
                dragMoved = true;
            }
            Left = dragStartForm.X + dx;
            Top = dragStartForm.Y + dy;
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            if (dragging && e.Button == MouseButtons.Left) FinishDrag();
        }

        private void FinishDrag()
        {
            dragging = false;
            Capture = false;
            if (!dragMoved) return;
            SnapToScreen();
            SaveCardPosition();
        }

        private void SaveCardPosition()
        {
            positionSaveTimer.Stop();
            if (config.taskbarMode || exiting || (config.x == Left && config.y == Top)) return;
            config.x = Left;
            config.y = Top;
            SaveSettings();
        }

        private void SnapToScreen()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int margin = 20;
            if (Math.Abs(Left - area.Left) < margin) Left = area.Left + 8;
            if (Math.Abs(area.Right - (Left + Width)) < margin) Left = area.Right - Width - 8;
            if (Math.Abs(Top - area.Top) < margin) Top = area.Top + 8;
            if (Math.Abs(area.Bottom - (Top + Height)) < margin) Top = area.Bottom - Height - 8;
        }

        private void ApplyCardSize()
        {
            Size newSize = new Size(ScaleWidget(CardWidth), ScaleWidget(CardHeight));
            if (ClientSize == newSize) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            ClientSize = newSize;
            refreshIcon.Left = ScaleWidget(CardWidth - 110);
            settingsIcon.Left = ScaleWidget(CardWidth - 58);
            KeepCardInView(area);
            config.x = Left;
            config.y = Top;
            Invalidate(true);
        }

        private void KeepCardInView(Rectangle area)
        {
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
        }

        private void RestorePosition()
        {
            Point saved = new Point(config.x, config.y);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            bool restored = false;
            if (saved != new Point(-1, -1))
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (!screen.WorkingArea.Contains(saved)) continue;
                    area = screen.WorkingArea;
                    Location = saved;
                    restored = true;
                    break;
                }
            }
            if (!restored)
            {
                Left = area.Right - Width - 24;
                Top = area.Bottom - Height - 24;
            }
            KeepCardInView(area);
        }

        internal void RestoreFromLaunch()
        {
            if (IsDisposed || exiting) return;
            DiagnosticLog.Write("application.instance.restore");
            if (activeSettings != null)
            {
                activeSettings.Activate();
                return;
            }
            if (settingsOpen) return;
            ApplyDisplayMode();
            if (!config.taskbarMode) Activate();
        }

        private void ShowWidget()
        {
            if (settingsOpen || exiting) return;
            ApplyDisplayMode();
            if (config.taskbarMode) OpenSettings();
            else Activate();
        }

        private void SelectDisplayMode(bool taskbar, bool right)
        {
            if (exiting || settingsOpen) return;
            positionSaveTimer.Stop();
            StoreWindowPosition();
            config.taskbarMode = taskbar;
            config.taskbarRight = right;
            ApplyDisplayMode();
            SaveSettings();
        }

        private void UpdateTaskbarFallback()
        {
            bool showTaskbarButton = !tray.Available && !config.taskbarMode;
            if (ShowInTaskbar != showTaskbarButton) ShowInTaskbar = showTaskbarButton;
        }

        private void ApplyDisplayMode()
        {
            DiagnosticLog.Write("display.apply; taskbar=" + config.taskbarMode + "; right=" + config.taskbarRight + "; scale=" + widgetScale);
            UpdateTaskbarFallback();
            if (!config.taskbarMode)
            {
                if (taskbarWidget != null) taskbarWidget.StopFollowing();
                WindowState = FormWindowState.Normal;
                if (!Visible) Show();
                return;
            }

            if (taskbarWidget == null)
            {
                taskbarWidget = new TaskbarWidget(widgetScale);
                taskbarMenu = BuildContextMenu();
                taskbarWidget.ContextMenuStrip = taskbarMenu;
                taskbarWidget.SettingsRequested += delegate
                {
                    if (!exiting && !IsDisposed && IsHandleCreated)
                    {
                        DiagnosticLog.Write("taskbar.settings.queued");
                        BeginInvoke((MethodInvoker)OpenSettings);
                    }
                };
            }
            SyncTaskbarReading();
            taskbarWidget.StartFollowing(config.taskbarRight, config.taskbarAppearance);
            Hide();
        }

        private void SyncTaskbarReading()
        {
            if (taskbarWidget == null) return;
            string text = room == null ? "点击选择寝室" : "HPU 用电 · " + UpdateStatus();
            string tooltip = room == null ? "点击选择房间" : room.DisplayText + " · " + UpdateStatus();
            if (!refreshing && state == WidgetState.Error && !String.IsNullOrEmpty(lastError))
                tooltip += Environment.NewLine + lastError;
            if (snapshot == null)
            {
                taskbarWidget.UpdateReading(null, null, null, text, tooltip, 0);
                return;
            }

            string days = snapshot.daysLeft >= 0 && !Double.IsInfinity(snapshot.daysLeft)
                ? FormatDays(snapshot.daysLeft) + " 天" : "—";
            int severity = Math.Max(BalanceLevel(snapshot.lightingBalance), BalanceLevel(snapshot.airBalance));
            taskbarWidget.UpdateReading(days, TaskbarBalance(snapshot.lightingBalance),
                TaskbarBalance(snapshot.airBalance), text, tooltip, severity);
        }

        private static string TaskbarBalance(double value)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? "—" : FormatNumber(value, "0.##") + "度";
        }

        private void RequestExit()
        {
            if (exiting || IsDisposed) return;
            DiagnosticLog.Write("application.exit.requested");
            // Let Explorer and WinForms finish dispatching the menu click before closing its windows.
            BeginInvoke((MethodInvoker)ExitApplication);
        }

        private void BeginShutdown()
        {
            exiting = true;
            UpdateAppearanceTimer();
            refreshTimer.Stop();
            clockTimer.Stop();
            positionSaveTimer.Stop();
            if (activeSettings != null) activeSettings.DialogResult = DialogResult.Cancel;
            if (taskbarWidget != null) taskbarWidget.StopFollowing();
            shutdown.Cancel();
        }

        private async void ExitApplication()
        {
            if (exiting || IsDisposed) return;
            BeginShutdown();
            StoreWindowPosition();
            Task<UsageSnapshot> query = activeQuery;
            try
            {
                Task<string> save = AppData.QueueConfig(config);
                if (query != null)
                {
                    try { await query; }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { System.Diagnostics.Trace.TraceWarning(error.Message); }
                }
                await save;
            }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning(error.Message); }
            finally
            {
                if (!IsDisposed) Close();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (!tray.Available)
                {
                    WindowState = FormWindowState.Minimized;
                    return;
                }
                Hide();
                tray.ShowBalloonTip(1200, "HPU 用电", "小组件已隐藏到系统托盘", ToolTipIcon.Info);
                return;
            }
            if (!exiting)
            {
                BeginShutdown();
                StoreWindowPosition();
                AppData.QueueConfig(config).GetAwaiter().GetResult();
            }
            DisposeTray();
        }

        private void DisposeTray()
        {
            if (trayDisposed) return;
            trayDisposed = true;
            tray.Dispose();
            if (appIcon != null) appIcon.Dispose();
        }

        private void StoreWindowPosition()
        {
            config.x = Left;
            config.y = Top;
            config.topMost = TopMost;
        }

        private static float GetDesktopScale()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                return screen.DpiX / 96f;
        }

        private int ScaleWidget(int value) { return (int)Math.Round(value * widgetScale); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (widgetScale <= 0 || ClientSize.Width < 2 || ClientSize.Height < 2) return;
            using (GraphicsPath path = RoundedPath(ClientRectangle, ScaleWidget(18)))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        private static string FormatDays(double days)
        {
            if (days < 10) return FormatNumber(days, "0.0");
            return FormatNumber(days, "0");
        }

        private static string FormatNumber(double value, string format)
        {
            return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static Font MakeFont(string family, float size, FontStyle style)
        {
            try { return new Font(family, size * 96f / 72f, style, GraphicsUnit.Pixel); }
            catch { return new Font(FontFamily.GenericSansSerif, size * 96f / 72f, style, GraphicsUnit.Pixel); }
        }

        private static SizeF Measure(Graphics g, string text, Font font)
        {
            return g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        }

        private static void DrawText(Graphics g, string text, Font font, Color color,
                                     RectangleF bounds, StringAlignment alignment)
        {
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (SolidBrush brush = new SolidBrush(color))
            {
                format.Alignment = alignment;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags |= StringFormatFlags.NoWrap;
                format.FormatFlags |= StringFormatFlags.NoClip;
                g.DrawString(text, font, brush, bounds, format);
            }
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }


    }
}
