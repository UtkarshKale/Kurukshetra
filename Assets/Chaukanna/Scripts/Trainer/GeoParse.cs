using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Chaukanna.Trainer
{
    // Reads a typed or pasted position: "34.5735, 76.0974", "34.5735 76.0974", or degrees-minutes-seconds such as
    // 34°34'24.79"N 76°05'50.63"E (as Google Earth shows it). S and W make a value negative.
    public static class GeoParse
    {
        static readonly Regex Decimal = new Regex(@"^\s*(-?\d+(?:\.\d+)?)\s*[,;\s]\s*(-?\d+(?:\.\d+)?)\s*$");
        static readonly Regex Dms = new Regex(@"(\d+(?:\.\d+)?)\s*(?:°|º|d|\s)\s*(?:(\d+(?:\.\d+)?)\s*(?:'|′|’|m|\s)\s*)?(?:(\d+(?:\.\d+)?)\s*(?:""|″|”|''|s)?\s*)?([NSEW])", RegexOptions.IgnoreCase);

        public static bool TryParseLatLon(string text, out double latitude, out double longitude)
        {
            latitude = longitude = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var d = Decimal.Match(text);
            if (d.Success)
            {
                latitude = double.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                longitude = double.Parse(d.Groups[2].Value, CultureInfo.InvariantCulture);
                return Valid(latitude, longitude);
            }
            var parts = Dms.Matches(text);
            if (parts.Count != 2) return false;
            bool haveLat = false, haveLon = false;
            foreach (Match m in parts)
            {
                double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                    + (m.Groups[2].Success ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 60.0 : 0.0)
                    + (m.Groups[3].Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 3600.0 : 0.0);
                char h = char.ToUpperInvariant(m.Groups[4].Value[0]);
                if (h == 'S' || h == 'W') v = -v;
                if (h == 'N' || h == 'S') { latitude = v; haveLat = true; }
                else { longitude = v; haveLon = true; }
            }
            return haveLat && haveLon && Valid(latitude, longitude);
        }

        static bool Valid(double lat, double lon) => Math.Abs(lat) <= 90.0 && Math.Abs(lon) <= 180.0;
    }
}