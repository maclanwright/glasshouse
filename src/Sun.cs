using System;

namespace Glasshouse
{
    // Sunrise and sunset for a place and day (NOAA's simplified formula, within a minute or two), to show when
    // the apps will switch between light and dark.
    public static class Sun
    {
        public static (DateTime rise, DateTime set)? Times(double lat, double lon, DateTime day)
        {
            double Rad(double d) => d * Math.PI / 180;
            double Deg(double r) => r * 180 / Math.PI;
            int n = day.DayOfYear;
            double g = 2 * Math.PI / 365 * (n - 1 + 0.5);
            double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
            double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
            double cosH = Math.Cos(Rad(90.833)) / (Math.Cos(Rad(lat)) * Math.Cos(decl)) - Math.Tan(Rad(lat)) * Math.Tan(decl);
            if (cosH < -1 || cosH > 1) return null;   // midnight sun or polar night
            double ha = Deg(Math.Acos(cosH));
            double riseUtc = 720 - 4 * (lon + ha) - eqTime, setUtc = 720 - 4 * (lon - ha) - eqTime;
            var midnightUtc = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Utc);
            return (midnightUtc.AddMinutes(riseUtc).ToLocalTime(), midnightUtc.AddMinutes(setUtc).ToLocalTime());
        }

        public static string Clock(DateTime t) => t.ToString("h:mm tt").ToLowerInvariant();
    }
}
