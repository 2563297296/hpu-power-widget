using System;
using System.Collections.Generic;

namespace HpuPowerWindows
{
    public sealed class Meter
    {
        public string building { get; set; }
        public string floor { get; set; }
        public string room { get; set; }
    }

    public sealed class RoomOption
    {
        public string id { get; set; }
        public string campus { get; set; }
        public string name { get; set; }
        public string building { get; set; }
        public string floor { get; set; }
        public Meter lighting { get; set; }
        public Meter air { get; set; }

        public string CampusLabel { get { return campus == "south" ? "南校区" : "北校区"; } }
        public string DisplayText { get { return name + " · " + CampusLabel; } }
        public string DetailText { get { return CampusLabel + " · " + building + " · " + floor; } }
        public override string ToString() { return DisplayText + "  " + building + " " + floor; }
    }

    public sealed class CatalogRoot
    {
        public List<RoomOption> entries { get; set; }
    }

    public sealed class WidgetConfig
    {
        public int appearance { get; set; }
        public bool taskbarMode { get; set; }
        public bool taskbarRight { get; set; }
        public int taskbarAppearance { get; set; }
        public const int DefaultCardWidth = 410;
        public const int DefaultCardHeight = 218;
        public const int MinCardWidth = 370;
        public const int MaxCardWidth = 720;
        public const int MinCardHeight = 210;
        public const int MaxCardHeight = 380;
        public int cardWidth { get; set; }
        public int cardHeight { get; set; }

        public static int NormalizeCardWidth(int width)
        {
            return width == 0 ? DefaultCardWidth : Math.Max(MinCardWidth, Math.Min(MaxCardWidth, width));
        }

        public static int NormalizeCardHeight(int height)
        {
            return height == 0 ? DefaultCardHeight : Math.Max(MinCardHeight, Math.Min(MaxCardHeight, height));
        }
        public string roomId { get; set; }
        public int refreshMinutes { get; set; }
        public bool topMost { get; set; }
        public int opacity { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public const int MaxLowBalanceThreshold = 1000;
        public int lowBalanceThreshold { get; set; }
        public Dictionary<string, int> lowBalanceAlerts { get; set; }

        public WidgetConfig()
        {
            taskbarRight = true;
            taskbarAppearance = 2;
            cardWidth = DefaultCardWidth;
            cardHeight = DefaultCardHeight;
            roomId = "";
            refreshMinutes = 10;
            topMost = false;
            opacity = 100;
            x = -1;
            y = -1;
            lowBalanceThreshold = 10;
            lowBalanceAlerts = new Dictionary<string, int>();
        }
    }

    public sealed class UsageRow
    {
        public string date { get; set; }
        public double kwh { get; set; }
        public double cost { get; set; }
    }

    public sealed class MeterData
    {
        public double balance { get; set; }
        public List<UsageRow> rows { get; set; }
        public MeterData() { rows = new List<UsageRow>(); }
    }

    public sealed class UsageSnapshot
    {
        public string roomId { get; set; }
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
        public DateTime fetchedAt { get; set; }
    }
}
