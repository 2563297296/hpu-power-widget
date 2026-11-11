using System;
using System.Collections.Generic;
using System.Globalization;

namespace HpuPowerWindows
{
    internal static class LowBalanceReminder
    {
        private const int Lighting = 1;
        private const int AirConditioning = 2;

        // Returns true if the notification state changed and should be saved.
        internal static bool Update(WidgetConfig config, UsageSnapshot data, Func<string, bool> notify)
        {
            if (data == null || String.IsNullOrEmpty(data.roomId) || config.lowBalanceThreshold <= 0)
                return false;

            var roomAlerts = config.lowBalanceAlerts ?? (config.lowBalanceAlerts = new Dictionary<string, int>());
            int previous;
            roomAlerts.TryGetValue(data.roomId, out previous);
            int threshold = config.lowBalanceThreshold;
            int current = LowState(data.lightingBalance, threshold, previous, Lighting)
                        | LowState(data.airBalance, threshold, previous, AirConditioning);
            int newAlerts = current & ~previous;

            // A failed delivery must not consume the reminder for the next successful refresh.
            int next = current & previous;
            if (newAlerts != 0 && notify(BuildMessage(data, newAlerts, threshold)))
                next = current;
            if (next == previous) return false;
            if (next == 0) roomAlerts.Remove(data.roomId);
            else roomAlerts[data.roomId] = next;
            return true;
        }

        private static int LowState(double balance, int threshold, int previous, int meter)
        {
            if (Double.IsNaN(balance) || Double.IsInfinity(balance)) return previous & meter;
            return balance < threshold ? meter : 0;
        }

        private static string BuildMessage(UsageSnapshot data, int meters, int threshold)
        {
            var balances = new List<string>(2);
            if ((meters & Lighting) != 0) balances.Add(DescribeBalance("照明", data.lightingBalance));
            if ((meters & AirConditioning) != 0) balances.Add(DescribeBalance("空调", data.airBalance));
            return String.Join("，", balances) + "。低于 " + threshold + " 度，请及时充值。";
        }

        private static string DescribeBalance(string meter, double balance)
        {
            return meter + "余额 " + balance.ToString("0.##", CultureInfo.CurrentCulture) + " 度";
        }
    }
}
