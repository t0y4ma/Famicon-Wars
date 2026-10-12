using System;

namespace FamiconWars.Core
{
    /// <summary>
    /// The time limit for one phase, chosen before a match on a slider: 20 seconds to 5 minutes on a
    /// logarithmic scale (finer steps at the short end), and past the last step, no limit.
    /// When the time runs out the phase ends (as if 「手番終了」 was pressed).
    /// </summary>
    public static class TurnTime
    {
        public const int MinSeconds = 20, MaxSeconds = 300, DefaultSeconds = 60;
        /// <summary>No time limit.</summary>
        public const int Unlimited = 0;
        /// <summary>Slider positions 0..Steps-1 run from MinSeconds to MaxSeconds; position Steps means no limit.</summary>
        public const int Steps = 25;

        /// <summary>Seconds for a slider position (0 = no limit at the far right).</summary>
        public static int FromStep(int step)
        {
            if (step >= Steps) return Unlimited;
            step = Math.Max(0, step);
            double t = step / (double)(Steps - 1);
            return Nice(MinSeconds * Math.Pow(MaxSeconds / (double)MinSeconds, t));
        }

        /// <summary>The slider position nearest to a number of seconds (Unlimited = the far right).</summary>
        public static int ToStep(int seconds)
        {
            if (seconds <= Unlimited) return Steps;
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < Steps; i++)
            {
                int d = Math.Abs(FromStep(i) - seconds);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Rounded to steps a person would pick: 1 s under 30 s, 5 s under 2 min, 10 s above.</summary>
        static int Nice(double s)
        {
            int unit = s < 30 ? 1 : s < 120 ? 5 : 10;
            int v = (int)Math.Round(s / unit) * unit;
            return Math.Max(MinSeconds, Math.Min(MaxSeconds, v));
        }

        /// <summary>A clamped, valid setting (0 = no limit).</summary>
        public static int Clamp(int seconds) => seconds <= Unlimited ? Unlimited : Math.Max(MinSeconds, Math.Min(MaxSeconds, seconds));

        /// <summary>「1分」「45秒」「2分30秒」「無制限」.</summary>
        public static string Label(int seconds)
        {
            if (seconds <= Unlimited) return "無制限";
            int m = seconds / 60, sec = seconds % 60;
            if (m == 0) return sec + "秒";
            return sec == 0 ? m + "分" : m + "分" + sec + "秒";
        }
    }
}
