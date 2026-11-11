using System;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HpuPowerWindows
{
    public sealed class SettingsForm : Form
    {
        private readonly ComboBox campus;
        private readonly ComboBox building;
        private readonly ComboBox floor;
        private readonly ListBox roomList;
        private readonly NumericUpDown refreshMinutes;
        private readonly CheckBox topMost;
        private readonly NumericUpDown opacity;
        private readonly Label detail;
        private readonly ComboBox displayMode;
        private readonly ComboBox taskbarSide;
        private readonly ComboBox taskbarAppearance;
        private readonly CheckBox startWithWindows;
        private readonly NumericUpDown lowBalanceThreshold;
        private readonly NumericUpDown cardWidth;
        private readonly NumericUpDown cardHeight;
        private readonly RoomOption currentRoom;
        private int loadVersion;
        private bool resetting;


        private readonly ComboBox appearance;
        public int SelectedCardWidth { get { return (int)cardWidth.Value; } }
        public int SelectedCardHeight { get { return (int)cardHeight.Value; } }
        public bool SelectedTaskbarMode { get { return displayMode.SelectedIndex == 1; } }
        public int SelectedTaskbarAppearance { get { return taskbarAppearance.SelectedIndex; } }
        public bool SelectedTaskbarRight { get { return taskbarSide.SelectedIndex == 1; } }
        public int SelectedLowBalanceThreshold { get { return (int)lowBalanceThreshold.Value; } }
        public bool SelectedStartWithWindows { get { return startWithWindows.Checked; } }
        public int SelectedAppearance { get { return appearance.SelectedIndex; } }
        public RoomOption SelectedRoom { get; private set; }
        public int SelectedRefreshMinutes { get { return (int)refreshMinutes.Value; } }
        public bool SelectedTopMost { get { return topMost.Checked; } }
        public int SelectedOpacity { get { return (int)opacity.Value; } }

        public SettingsForm(RoomOption current, WidgetConfig config)
        {
            currentRoom = current;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            Text = current == null ? "HPU 用电 · 首次设置" : "HPU 用电 · 小组件设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 846);
            BackColor = Color.FromArgb(244, 248, 253);
            Font = new Font("Microsoft YaHei UI", 9f);

            Label title = new Label();
            title.Name = "roomSelectionTitle";
            title.UseCompatibleTextRendering = false;
            title.AutoSize = true;
            title.Text = "选择寝室";
            title.Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(15, 43, 78);
            title.Location = new Point(20, 12);
            Controls.Add(title);

            campus = CreateChoice("校区", 20, 112);
            building = CreateChoice("楼栋", 142, 174);
            floor = CreateChoice("楼层", 326, 174);
            campus.Items.AddRange(new object[]
            {
                new CatalogChoice { name = "选择校区" },
                new CatalogChoice { id = "north", name = "北校区" },
                new CatalogChoice { id = "south", name = "南校区" }
            });
            campus.SelectedIndex = 0;
            SetChoices(building, "选择楼栋", new CatalogChoice[0]);
            SetChoices(floor, "选择楼层", new CatalogChoice[0]);

            roomList = new ListBox();
            roomList.SetBounds(20, 106, 480, 400);
            roomList.IntegralHeight = false;
            roomList.Enabled = false;
            roomList.AccessibleName = "房间";
            Controls.Add(roomList);


            detail = new Label();
            detail.SetBounds(20, 514, 480, 24);
            detail.ForeColor = Color.FromArgb(74, 98, 128);
            Controls.Add(detail);

            Label intervalLabel = new Label();
            intervalLabel.Text = "刷新（分钟）";
            intervalLabel.SetBounds(20, 550, 140, 26);
            Controls.Add(intervalLabel);

            refreshMinutes = new NumericUpDown();
            refreshMinutes.Minimum = 1;
            refreshMinutes.Maximum = 1440;
            refreshMinutes.Value = Math.Max(1, Math.Min(1440, config.refreshMinutes));
            refreshMinutes.SetBounds(165, 547, 90, 28);
            Controls.Add(refreshMinutes);

            topMost = new CheckBox();
            topMost.Text = "窗口置顶";
            topMost.Checked = config.topMost;
            topMost.SetBounds(290, 586, 120, 28);
            Controls.Add(topMost);

            Label opacityLabel = new Label();
            opacityLabel.Text = "透明度（%）";
            opacityLabel.SetBounds(20, 586, 140, 26);
            Controls.Add(opacityLabel);

            opacity = new NumericUpDown();
            opacity.Minimum = 70;
            opacity.Maximum = 100;
            opacity.Increment = 5;
            opacity.Value = Math.Max(70, Math.Min(100, config.opacity));
            opacity.SetBounds(165, 583, 90, 28);
            Controls.Add(opacity);

            Label appearanceLabel = new Label();
            appearanceLabel.Text = "外观";
            appearanceLabel.SetBounds(270, 550, 86, 26);
            Controls.Add(appearanceLabel);

            appearance = new ComboBox();
            appearance.DropDownStyle = ComboBoxStyle.DropDownList;
            appearance.Items.AddRange(WidgetForm.AppearanceNames);
            appearance.SelectedIndex = Math.Max(0, Math.Min(appearance.Items.Count - 1, config.appearance));
            appearance.SetBounds(360, 547, 140, 28);
            Controls.Add(appearance);

            Label sizeLabel = new Label();
            sizeLabel.Text = "尺寸（宽 × 高）";
            sizeLabel.SetBounds(20, 624, 140, 26);
            Controls.Add(sizeLabel);

            cardWidth = CreateDimension("卡片宽度", 165, WidgetConfig.MinCardWidth,
                WidgetConfig.MaxCardWidth, WidgetConfig.NormalizeCardWidth(config.cardWidth));
            cardHeight = CreateDimension("卡片高度", 255, WidgetConfig.MinCardHeight,
                WidgetConfig.MaxCardHeight, WidgetConfig.NormalizeCardHeight(config.cardHeight));

            Button resetSize = new Button();
            resetSize.Text = "恢复默认";
            resetSize.SetBounds(345, 618, 123, 32);
            resetSize.Click += delegate
            {
                cardWidth.Value = WidgetConfig.DefaultCardWidth;
                cardHeight.Value = WidgetConfig.DefaultCardHeight;
            };
            Controls.Add(resetSize);

            Label modeLabel = new Label();
            modeLabel.Text = "显示模式";
            modeLabel.SetBounds(20, 662, 120, 26);
            Controls.Add(modeLabel);

            displayMode = CreateChoice("显示模式", 165, 155);
            displayMode.Top = 659;
            displayMode.Items.AddRange(new object[] { "模式 1 · 桌面卡片", "模式 2 · 任务栏" });
            displayMode.SelectedIndex = config.taskbarMode ? 1 : 0;

            taskbarSide = CreateChoice("任务栏位置", 330, 138);
            taskbarSide.Top = 659;
            taskbarSide.Items.AddRange(new object[] { "任务栏左侧", "任务栏右侧" });
            taskbarSide.SelectedIndex = config.taskbarRight ? 1 : 0;
            taskbarSide.Enabled = SelectedTaskbarMode;
            Label taskbarAppearanceLabel = new Label();
            taskbarAppearanceLabel.Text = "任务栏外观";
            taskbarAppearanceLabel.SetBounds(20, 697, 120, 26);
            Controls.Add(taskbarAppearanceLabel);
            taskbarAppearance = CreateChoice("任务栏外观", 165, 155);
            taskbarAppearance.Top = 695;
            taskbarAppearance.Items.AddRange(TaskbarWidget.AppearanceNames);
            taskbarAppearance.SelectedIndex = TaskbarWidget.NormalizeAppearance(config.taskbarAppearance);
            taskbarAppearance.Enabled = SelectedTaskbarMode;
            displayMode.SelectedIndexChanged += delegate
            {
                taskbarSide.Enabled = SelectedTaskbarMode;
                taskbarAppearance.Enabled = SelectedTaskbarMode;
            };

            Label reminderLabel = new Label();
            reminderLabel.Text = "低电提醒（度）";
            reminderLabel.SetBounds(20, 740, 140, 26);
            Controls.Add(reminderLabel);

            lowBalanceThreshold = CreateDimension("低电提醒阈值", 165, 0, WidgetConfig.MaxLowBalanceThreshold,
                Math.Max(0, Math.Min(WidgetConfig.MaxLowBalanceThreshold, config.lowBalanceThreshold)));
            lowBalanceThreshold.Top = 737;
            Label reminderHint = new Label();
            reminderHint.Text = "任一余额低于此值时提醒，0 为关闭";
            reminderHint.SetBounds(260, 740, 240, 26);
            Controls.Add(reminderHint);

            startWithWindows = new CheckBox();
            startWithWindows.Text = "开机自启动";
            startWithWindows.Checked = StartupShortcut.IsEnabled();
            startWithWindows.SetBounds(20, 783, 160, 32);
            Controls.Add(startWithWindows);

            Button save = new Button();
            save.Text = "保存";
            save.SetBounds(310, 782, 90, 36);
            save.BackColor = Color.FromArgb(45, 108, 223);
            save.ForeColor = Color.White;
            save.FlatStyle = FlatStyle.Flat;
            save.FlatAppearance.BorderSize = 0;
            save.Click += delegate { AcceptSettings(); };
            Controls.Add(save);

            Button cancel = new Button();
            cancel.Text = current == null ? "稍后设置" : "取消";
            cancel.SetBounds(410, 782, 90, 36);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
            SelectedRoom = current;

            campus.SelectedIndexChanged += async delegate { await LoadLevel(0); };
            building.SelectedIndexChanged += async delegate { await LoadLevel(1); };
            floor.SelectedIndexChanged += async delegate { await LoadLevel(2); };
            roomList.SelectedIndexChanged += delegate
            {
                RoomOption room = roomList.SelectedItem as RoomOption;
                if (room != null) detail.Text = room.DisplayText + " · " + room.DetailText;
            };
            roomList.DoubleClick += delegate { if (roomList.SelectedItem != null) AcceptSettings(); };
            ShowCurrentRoom();
        }

        protected override void OnLoad(EventArgs e)
        {
            DiagnosticLog.Write("settings.load.begin");
            base.OnLoad(e);
            Rectangle workArea = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
            Size frame = Size - ClientSize;
            Size content = ClientSize;
            int maxWidth = Math.Max(1, workArea.Width - frame.Width - 16);
            int maxHeight = Math.Max(1, workArea.Height - frame.Height - 16);
            if (content.Width > maxWidth || content.Height > maxHeight)
            {
                int width = Math.Min(maxWidth, content.Width + SystemInformation.VerticalScrollBarWidth);
                ClientSize = new Size(width, Math.Min(maxHeight, content.Height));
                AutoScrollMinSize = content;
            }
            DiagnosticLog.Write("settings.load.ready; size=" + ClientSize.Width + "x" + ClientSize.Height);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (currentRoom == null) campus.Select();
            DiagnosticLog.Write("settings.visible");
        }

        private NumericUpDown CreateDimension(string name, int left, int min, int max, int value)
        {
            NumericUpDown input = new NumericUpDown();
            input.AccessibleName = name;
            input.Minimum = min;
            input.Maximum = max;
            input.Value = value;
            input.SetBounds(left, 622, 75, 28);
            Controls.Add(input);
            return input;
        }

        private ComboBox CreateChoice(string name, int left, int width)
        {
            var combo = new ComboBox();
            combo.AccessibleName = name;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.SetBounds(left, 66, width, 30);
            Controls.Add(combo);
            return combo;
        }

        private void ShowCurrentRoom()
        {
            detail.Text = currentRoom == null ? "请依次选择校区、楼栋、楼层和寝室，再点击保存。" : "当前房间：" + currentRoom.DisplayText;
        }

        private static string ChoiceId(ComboBox combo)
        {
            CatalogChoice choice = combo.SelectedItem as CatalogChoice;
            return choice == null ? null : choice.id;
        }

        private static void SetChoices(ComboBox combo, string hint, CatalogChoice[] choices)
        {
            combo.Items.Clear();
            combo.Items.Add(new CatalogChoice { name = hint });
            combo.Items.AddRange(choices);
            combo.SelectedIndex = 0;
            combo.Enabled = choices.Length > 0;
        }

        private async Task LoadLevel(int level)
        {
            if (resetting) return;
            int version = ++loadVersion;
            resetting = true;
            try
            {
                if (level == 0) SetChoices(building, "选择楼栋", new CatalogChoice[0]);
                if (level <= 1) SetChoices(floor, "选择楼层", new CatalogChoice[0]);
                roomList.Items.Clear();
                roomList.Enabled = false;
            }
            finally { resetting = false; }

            string campusId = ChoiceId(campus);
            string buildingId = ChoiceId(building);
            string floorId = ChoiceId(floor);
            if (campusId == null) { ShowCurrentRoom(); return; }
            if (level == 1 && buildingId == null) { detail.Text = "请选择楼栋"; return; }
            if (level == 2 && floorId == null) { detail.Text = "请选择楼层"; return; }

            detail.Text = "正在加载…";
            try
            {
                if (level == 2)
                {
                    RoomOption[] rooms = await Task.Run(() => RoomDirectory.Rooms(campusId, buildingId, floorId));
                    if (IsDisposed || version != loadVersion) return;
                    roomList.Items.AddRange(rooms);
                    roomList.Enabled = rooms.Length > 0;
                    detail.Text = rooms.Length == 0 ? "该楼层没有房间" : "请选择房间";
                    return;
                }

                CatalogChoice[] choices = await Task.Run(() => level == 0
                    ? RoomDirectory.Buildings(campusId) : RoomDirectory.Floors(campusId, buildingId));
                if (IsDisposed || version != loadVersion) return;
                string hint = level == 0 ? "选择楼栋" : "选择楼层";
                resetting = true;
                try { SetChoices(level == 0 ? building : floor, hint, choices); }
                finally { resetting = false; }
                detail.Text = choices.Length == 0 ? "当前没有可选项目" : "请" + hint;
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException ||
                    error is ArgumentException || error is InvalidOperationException)) throw;
                if (!IsDisposed && version == loadVersion)
                    detail.Text = "无法读取房间目录，请检查程序是否已完整解压。";
            }
        }

        private void AcceptSettings()
        {
            RoomOption selected = ChoiceId(campus) == null ? currentRoom : roomList.SelectedItem as RoomOption;
            if (selected == null)
            {
                MessageBox.Show(this, "请依次选择校区、楼栋、楼层和房间。",
                    "HPU 用电", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SelectedRoom = selected;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
