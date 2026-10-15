using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // A problem with an elevation file that the user can act on.
    public class DemException : Exception
    {
        public DemException(string message) : base(message) { }
    }

    // Local metric coordinates around a centre point on the WGS84 ellipsoid.
    // Metres per degree are taken from the ellipsoid's radii of curvature at the centre latitude, so a degree of longitude
    // shrinks correctly towards the poles. Good to well under 0.1 % across the few kilometres a terrain covers.
    public static class GeoMath
    {
        const double A = 6378137.0;                 // WGS84 semi-major axis, metres
        const double E2 = 0.00669437999014;         // first eccentricity squared

        public static double MetresPerDegreeLatitude(double latitude)
        {
            double s = Math.Sin(latitude * Math.PI / 180.0);
            double m = A * (1.0 - E2) / Math.Pow(1.0 - E2 * s * s, 1.5);   // meridional radius
            return m * Math.PI / 180.0;
        }

        public static double MetresPerDegreeLongitude(double latitude)
        {
            double phi = latitude * Math.PI / 180.0;
            double s = Math.Sin(phi);
            double n = A / Math.Sqrt(1.0 - E2 * s * s);                    // prime-vertical radius
            return n * Math.Cos(phi) * Math.PI / 180.0;
        }

        // East/north metres from the centre to latitude/longitude.
        public static void LocalToGeo(double centreLat, double centreLon, double east, double north, out double lat, out double lon)
        {
            lat = centreLat + north / MetresPerDegreeLatitude(centreLat);
            lon = centreLon + east / MetresPerDegreeLongitude(centreLat);
        }
    }

    // Layer A: source elevation in metres on a regular latitude/longitude grid (EPSG:4326), row 0 = northern edge.
    // Held apart from anything generated from it and never modified after loading.
    public class ElevationGrid
    {
        public int width, height;
        public float[] metres;                      // row-major, north to south, west to east
        public double west, south, east, north;     // outer edges of the cell centres' extent, degrees
        public bool pixelIsArea;                    // true: samples are cell centres inside the bounds; false: samples sit on the bounds
        public int noDataCount;
        public float minElevation, maxElevation, meanElevation;
        public string sourceName = "", format = "", notes = "";

        public double CentreLatitude => (south + north) * 0.5;
        public double CentreLongitude => (west + east) * 0.5;
        public double CellDegreesX => pixelIsArea ? (east - west) / width : (east - west) / Math.Max(1, width - 1);
        public double CellDegreesY => pixelIsArea ? (north - south) / height : (north - south) / Math.Max(1, height - 1);
        public double CellMetresX => CellDegreesX * GeoMath.MetresPerDegreeLongitude(CentreLatitude);
        public double CellMetresY => CellDegreesY * GeoMath.MetresPerDegreeLatitude(CentreLatitude);
        public double WidthMetres => (east - west) * GeoMath.MetresPerDegreeLongitude(CentreLatitude);
        public double HeightMetres => (north - south) * GeoMath.MetresPerDegreeLatitude(CentreLatitude);

        // Bilinear sample. Positions outside the data are clamped to the edge and counted by the caller via Contains().
        public float Sample(double lat, double lon)
        {
            double fx, fy;
            if (pixelIsArea)
            {
                fx = (lon - west) / (east - west) * width - 0.5;
                fy = (north - lat) / (north - south) * height - 0.5;
            }
            else
            {
                fx = (lon - west) / (east - west) * (width - 1);
                fy = (north - lat) / (north - south) * (height - 1);
            }
            if (fx < 0.0) fx = 0.0; else if (fx > width - 1) fx = width - 1;
            if (fy < 0.0) fy = 0.0; else if (fy > height - 1) fy = height - 1;
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
            double tx = fx - x0, ty = fy - y0;
            double top = metres[y0 * width + x0] * (1.0 - tx) + metres[y0 * width + x1] * tx;
            double bottom = metres[y1 * width + x0] * (1.0 - tx) + metres[y1 * width + x1] * tx;
            return (float)(top * (1.0 - ty) + bottom * ty);
        }

        public bool Contains(double lat, double lon)
        {
            return lat >= south && lat <= north && lon >= west && lon <= east;
        }

        // Replaces missing samples with the average of their valid neighbours, growing inwards until none are left,
        // so gaps become smooth patches instead of spikes or pits. Then records min, max and mean.
        public void FillNoDataAndMeasure(bool[] missing)
        {
            noDataCount = 0;
            if (missing != null)
            {
                foreach (bool m in missing) if (m) noDataCount++;
                if (noDataCount == metres.Length) throw new DemException("Every sample in this file is NoData; there is no elevation to use.");
                int remaining = noDataCount, guard = width + height;
                var next = new bool[missing.Length];
                while (remaining > 0 && guard-- > 0)
                {
                    Array.Copy(missing, next, missing.Length);
                    for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        if (!missing[i]) continue;
                        double sum = 0.0;
                        int n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int j = ny * width + nx;
                            if (missing[j]) continue;
                            sum += metres[j];
                            n++;
                        }
                        if (n == 0) continue;
                        metres[i] = (float)(sum / n);
                        next[i] = false;
                        remaining--;
                    }
                    Array.Copy(next, missing, missing.Length);
                }
            }

            double total = 0.0;
            minElevation = float.MaxValue;
            maxElevation = float.MinValue;
            foreach (float v in metres)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) throw new DemException("The file contains values that are not numbers.");
                if (v < minElevation) minElevation = v;
                if (v > maxElevation) maxElevation = v;
                total += v;
            }
            meanElevation = (float)(total / metres.Length);
            // Earth's surface runs from about -430 m to 8849 m. Anything far outside is a wrong format, byte order or unit.
            if (minElevation < -1000f || maxElevation > 9500f)
                throw new DemException($"Elevations run from {minElevation:F0} m to {maxElevation:F0} m, which is not possible on Earth. Check the byte order, signed setting, scale or NoData value.");
        }

        public string Describe()
        {
            return $"{sourceName} ({format})\n{width} x {height} samples, about {CellMetresX:F1} m x {CellMetresY:F1} m per sample\n"
                + $"Bounds: {south:F5} to {north:F5} N, {west:F5} to {east:F5} E ({WidthMetres / 1000.0:F2} km x {HeightMetres / 1000.0:F2} km)\n"
                + $"Elevation {minElevation:F1} m to {maxElevation:F1} m, mean {meanElevation:F1} m\n"
                + (noDataCount > 0 ? $"{noDataCount} NoData samples filled from their neighbours\n" : "No missing samples\n")
                + notes;
        }
    }

    // Readers for the elevation formats that can be read exactly without a geospatial library.
    public static class DemReader
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ElevationGrid Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new DemException("No elevation file was chosen.");
            path = ElevationDownloader.Resolve(path);
            if (!File.Exists(path)) throw new DemException("Elevation file not found: " + path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            ElevationGrid grid;
            switch (ext)
            {
                case ".hgt": grid = LoadHgt(path); break;
                case ".asc": grid = LoadAsciiGrid(path); break;
                case ".raw":
                case ".r16": grid = LoadRaw16(path); break;
                case ".tif":
                case ".tiff":
                    throw new DemException("GeoTIFF is not read directly (no geospatial library is installed). Convert it first:\n"
                        + "  gdal_translate -of AAIGrid input.tif output.asc\nthen pick the .asc file. See the terrain README.");
                default:
                    throw new DemException($"Unsupported elevation format '{ext}'. Supported: .hgt (SRTM), .asc (Esri ASCII grid), .raw / .r16 with a .json sidecar.");
            }
            grid.sourceName = Path.GetFileName(path);
            return grid;
        }

        // SRTM HGT: one-degree tile, 16-bit signed big-endian, north row first, samples on the degree lines.
        // The south-west corner is in the file name, for example N28E077.hgt.
        static ElevationGrid LoadHgt(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            if (name.Length < 7 || (name[0] != 'N' && name[0] != 'S') || (name[3] != 'E' && name[3] != 'W')
                || !int.TryParse(name.Substring(1, 2), out int lat) || !int.TryParse(name.Substring(4, 3), out int lon))
                throw new DemException("An .hgt file must be named after its south-west corner, for example N28E077.hgt. This name gives no position: " + Path.GetFileName(path));
            if (name[0] == 'S') lat = -lat;
            if (name[3] == 'W') lon = -lon;

            byte[] bytes = File.ReadAllBytes(path);
            int side = (int)Math.Round(Math.Sqrt(bytes.Length / 2.0));
            if (side * side * 2 != bytes.Length || (side != 3601 && side != 1201))
                throw new DemException($"This .hgt file is {bytes.Length} bytes, which is neither a 1-arc-second (3601 x 3601) nor a 3-arc-second (1201 x 1201) tile. It may be truncated or still compressed.");

            var grid = new ElevationGrid { width = side, height = side, metres = new float[side * side], west = lon, east = lon + 1, south = lat, north = lat + 1, pixelIsArea = false, format = side == 3601 ? "SRTM HGT, 1 arc-second" : "SRTM HGT, 3 arc-second" };
            var missing = new bool[side * side];
            for (int i = 0; i < side * side; i++)
            {
                short v = (short)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
                if (v == short.MinValue) { missing[i] = true; continue; }
                grid.metres[i] = v;
            }
            grid.notes = "CRS: WGS84 geographic (EPSG:4326). Heights: metres above the EGM96 geoid.";
            grid.FillNoDataAndMeasure(missing);
            return grid;
        }

        // Esri ASCII grid (GDAL "AAIGrid"): text header, then rows from north to south. Expects a geographic grid in degrees.
        static ElevationGrid LoadAsciiGrid(string path)
        {
            using (var reader = new StreamReader(path))
            {
                int cols = 0, rows = 0;
                double xll = double.NaN, yll = double.NaN, cell = double.NaN, dx = double.NaN, dy = double.NaN, noData = -9999.0;
                bool corner = true, haveNoData = false;
                string first = null;
                // Header: "key value" lines until a line starts with a number.
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null) throw new DemException("The .asc file ends before any elevation rows.");
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    if (char.IsDigit(line[0]) || line[0] == '-' || line[0] == '.') { first = line; break; }
                    string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !double.TryParse(parts[1], NumberStyles.Float, Inv, out double value))
                        throw new DemException("Cannot read this .asc header line: " + line);
                    switch (parts[0].ToLowerInvariant())
                    {
                        case "ncols": cols = (int)value; break;
                        case "nrows": rows = (int)value; break;
                        case "xllcorner": xll = value; corner = true; break;
                        case "yllcorner": yll = value; break;
                        case "xllcenter": xll = value; corner = false; break;
                        case "yllcenter": yll = value; break;
                        case "cellsize": cell = value; break;
                        case "dx": dx = value; break;
                        case "dy": dy = value; break;
                        case "nodata_value": noData = value; haveNoData = true; break;
                    }
                }
                if (double.IsNaN(dx)) dx = cell;
                if (double.IsNaN(dy)) dy = cell;
                if (cols <= 1 || rows <= 1 || double.IsNaN(xll) || double.IsNaN(yll) || double.IsNaN(dx) || double.IsNaN(dy) || dx <= 0 || dy <= 0)
                    throw new DemException("The .asc header is missing ncols, nrows, xllcorner/yllcorner or cellsize.");
                if (!corner) { xll -= dx * 0.5; yll -= dy * 0.5; }
                if (Math.Abs(xll) > 360.0 || Math.Abs(yll) > 90.0 || dx > 1.0)
                    throw new DemException("This .asc grid is not in latitude/longitude degrees (it looks like a projected grid in metres). Reproject it to EPSG:4326 first:\n  gdalwarp -t_srs EPSG:4326 input.tif out.tif && gdal_translate -of AAIGrid out.tif out.asc");

                var grid = new ElevationGrid { width = cols, height = rows, metres = new float[cols * rows], west = xll, south = yll, east = xll + cols * dx, north = yll + rows * dy, pixelIsArea = true, format = "Esri ASCII grid" };
                var missing = new bool[cols * rows];
                int i = 0;
                string row = first;
                var separators = new[] { ' ', '\t' };
                while (row != null && i < cols * rows)
                {
                    foreach (string token in row.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (i >= cols * rows) break;
                        if (!double.TryParse(token, NumberStyles.Float, Inv, out double v))
                            throw new DemException($"The .asc file has a value that is not a number ('{token}') at sample {i}.");
                        if (haveNoData && Math.Abs(v - noData) < 1e-6) missing[i] = true; else grid.metres[i] = (float)v;
                        i++;
                    }
                    row = reader.ReadLine();
                }
                if (i < cols * rows) throw new DemException($"The .asc file has {i} samples but its header promises {cols} x {rows} = {cols * rows}.");
                grid.notes = "CRS assumed WGS84 geographic (EPSG:4326). Heights taken as metres.";
                grid.FillNoDataAndMeasure(missing);
                return grid;
            }
        }

        [Serializable]
        public class RawMetadata
        {
            public int width, height;
            public bool littleEndian = true;
            public bool signed;
            // Either the four edges in degrees...
            public double west, south, east, north;
            // ...or a centre and a cell size in metres.
            public double centreLatitude, centreLongitude, cellSizeMetres;
            public double elevationScale = 1.0;     // metres per stored unit
            public double elevationOffset;          // metres added after scaling
            public bool hasNoData;
            public int noDataValue;
            public string rowOrder = "north-up";    // or "south-up"
            public string crs = "EPSG:4326";
            public string source = "", licence = "";
        }

        // Headerless 16-bit heightmap plus <name>.json describing it (see RawMetadata and the README).
        static ElevationGrid LoadRaw16(string path)
        {
            string metaPath = Path.ChangeExtension(path, ".json");
            if (!File.Exists(metaPath))
                throw new DemException("A .raw heightmap needs a metadata file beside it with the same name and .json: " + Path.GetFileName(metaPath) + "\nSee the terrain README for the fields.");
            RawMetadata m;
            try { m = JsonUtility.FromJson<RawMetadata>(File.ReadAllText(metaPath)); }
            catch (Exception e) { throw new DemException("Cannot read " + Path.GetFileName(metaPath) + ": " + e.Message); }
            if (m == null || m.width <= 1 || m.height <= 1) throw new DemException("The metadata must give width and height above 1.");

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != m.width * m.height * 2)
                throw new DemException($"The .raw file is {bytes.Length} bytes but {m.width} x {m.height} 16-bit samples need {m.width * m.height * 2}. Check width, height and that it is 16-bit.");

            var grid = new ElevationGrid { width = m.width, height = m.height, metres = new float[m.width * m.height], pixelIsArea = false, format = "16-bit RAW" };
            if (m.cellSizeMetres > 0.0)
            {
                double halfW = (m.width - 1) * m.cellSizeMetres * 0.5, halfH = (m.height - 1) * m.cellSizeMetres * 0.5;
                GeoMath.LocalToGeo(m.centreLatitude, m.centreLongitude, -halfW, -halfH, out grid.south, out grid.west);
                GeoMath.LocalToGeo(m.centreLatitude, m.centreLongitude, halfW, halfH, out grid.north, out grid.east);
            }
            else
            {
                grid.west = m.west; grid.south = m.south; grid.east = m.east; grid.north = m.north;
            }
            if (!(grid.east > grid.west) || !(grid.north > grid.south) || Math.Abs(grid.north) > 90.0 || Math.Abs(grid.south) > 90.0)
                throw new DemException("The metadata does not give a valid position: set west/south/east/north in degrees, or centreLatitude, centreLongitude and cellSizeMetres.");

            bool southUp = string.Equals(m.rowOrder, "south-up", StringComparison.OrdinalIgnoreCase);
            var missing = new bool[grid.metres.Length];
            for (int y = 0; y < m.height; y++)
            {
                int sourceRow = southUp ? m.height - 1 - y : y;
                for (int x = 0; x < m.width; x++)
                {
                    int b = (sourceRow * m.width + x) * 2;
                    int raw = m.littleEndian ? bytes[b] | (bytes[b + 1] << 8) : (bytes[b] << 8) | bytes[b + 1];
                    if (m.signed) raw = (short)raw;
                    int i = y * m.width + x;
                    if (m.hasNoData && raw == m.noDataValue) { missing[i] = true; continue; }
                    grid.metres[i] = (float)(raw * m.elevationScale + m.elevationOffset);
                }
            }
            grid.notes = $"CRS: {m.crs}. Source: {(string.IsNullOrEmpty(m.source) ? "not stated" : m.source)}. Licence: {(string.IsNullOrEmpty(m.licence) ? "not stated" : m.licence)}.";
            grid.FillNoDataAndMeasure(missing);
            return grid;
        }
    }
}
