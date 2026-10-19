using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // What covers the ground in each cell of a grid: mapped landcover (OpenStreetMap polygons) first, and where the map has
    // none, an estimate from the satellite picture's colour. The grid is square, centred on the area centre.
    public class CoverGrid
    {
        public const byte None = 0, OsmForest = 1, OsmGrass = 2, OsmFarm = 3, OsmOrchard = 4, SatTree = 5, SatGrass = 6;

        public readonly float cell, half;
        public readonly int n;
        public readonly byte[] cls;

        public CoverGrid(float halfMetres, float cellMetres)
        {
            half = halfMetres;
            cell = Mathf.Max(1f, cellMetres);
            n = Mathf.Max(1, Mathf.CeilToInt(2f * half / cell));
            cls = new byte[n * n];
        }

        public static bool IsMapped(byte c) => c >= OsmForest && c <= OsmOrchard;

        public byte At(float x, float z)
        {
            int ix = (int)((x + half) / cell), iz = (int)((z + half) / cell);
            if (ix < 0 || iz < 0 || ix >= n || iz >= n) return None;
            return cls[iz * n + ix];
        }

        public Vector2 CellCentre(int ix, int iz) => new Vector2(-half + (ix + 0.5f) * cell, -half + (iz + 0.5f) * cell);

        // Marks the cells whose centre lies in the polygon. Later calls overwrite earlier ones, so rasterise grass first and forest last.
        public int Rasterize(LayerPoly poly, byte value)
        {
            var index = new PolyIndex(poly.p, poly.holes);
            int x0 = Mathf.Max(0, (int)((index.MinX + half) / cell)), x1 = Mathf.Min(n - 1, (int)((index.MaxX + half) / cell));
            int z0 = Mathf.Max(0, (int)((index.MinZ + half) / cell)), z1 = Mathf.Min(n - 1, (int)((index.MaxZ + half) / cell));
            int count = 0;
            for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
                if (index.Contains(-half + (ix + 0.5f) * cell, -half + (iz + 0.5f) * cell)) { cls[iz * n + ix] = value; count++; }
            return count;
        }

        public int Count(byte value)
        {
            int c = 0;
            for (int i = 0; i < cls.Length; i++) if (cls[i] == value) c++;
            return c;
        }
    }

    // Satellite colour -> tree / grass / none, for the cells the map says nothing about. An estimate, labelled as one.
    // Excess green on normalised colour: ExG = 2g - r - b. Vegetation above VegetationExg; a tree when it is also dark or
    // the cell's brightness varies a lot (canopy texture); lighter smooth vegetation is grass. Buildings never come from this.
    public static class SatelliteCover
    {
        public const float VegetationExg = 0.05f, DarkLuma = 0.30f, TextureStd = 0.07f;

        // r, g, b in 0..1 (cell averages); lumaStd = spread of brightness inside the cell.
        public static byte Classify(float r, float g, float b, float lumaStd)
        {
            float sum = r + g + b;
            if (sum < 0.05f) return CoverGrid.None;                        // black: no data
            float exg = 2f * g / sum - r / sum - b / sum;
            if (exg <= VegetationExg) return CoverGrid.None;
            float luma = 0.299f * r + 0.587f * g + 0.114f * b;
            return luma < DarkLuma || lumaStd > TextureStd ? CoverGrid.SatTree : CoverGrid.SatGrass;
        }

        // Fills the cells of `grid` that are still None from a picture: pixels row 0 = south edge, bounds in degrees.
        // Runs a slice at a time (yield null between slices) so a loading screen keeps moving.
        public static IEnumerator Fill(CoverGrid grid, Color32[] pixels, int width, int height, double west, double south, double east, double north, GeoFrame frame, int cellsPerSlice = 40000)
        {
            double lonPerPx = (east - west) / width, latPerPx = (north - south) / height;
            int done = 0;
            for (int iz = 0; iz < grid.n; iz++)
            for (int ix = 0; ix < grid.n; ix++)
            {
                int i = iz * grid.n + ix;
                if (grid.cls[i] != CoverGrid.None) continue;
                float x0 = -grid.half + ix * grid.cell, z0 = -grid.half + iz * grid.cell;
                double lon0 = frame.lon0 + x0 / frame.mLon, lon1 = frame.lon0 + (x0 + grid.cell) / frame.mLon;
                double lat0 = frame.lat0 + z0 / frame.mLat, lat1 = frame.lat0 + (z0 + grid.cell) / frame.mLat;
                int px0 = (int)Math.Floor((lon0 - west) / lonPerPx), px1 = (int)Math.Ceiling((lon1 - west) / lonPerPx) - 1;
                int py0 = (int)Math.Floor((lat0 - south) / latPerPx), py1 = (int)Math.Ceiling((lat1 - south) / latPerPx) - 1;
                if (px1 < px0) px1 = px0; if (py1 < py0) py1 = py0;
                if (px1 < 0 || py1 < 0 || px0 >= width || py0 >= height) continue;       // the picture does not reach this cell
                px0 = Math.Max(0, px0); py0 = Math.Max(0, py0); px1 = Math.Min(width - 1, px1); py1 = Math.Min(height - 1, py1);
                // Sample at most 8 x 8 pixels of the cell.
                int sx = Math.Max(1, (px1 - px0 + 1) / 8), sy = Math.Max(1, (py1 - py0 + 1) / 8);
                double sr = 0, sg = 0, sb = 0, sl = 0, sl2 = 0; int cnt = 0;
                for (int py = py0; py <= py1; py += sy)
                for (int px = px0; px <= px1; px += sx)
                {
                    var c = pixels[py * width + px];
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f, l = 0.299f * r + 0.587f * g + 0.114f * b;
                    sr += r; sg += g; sb += b; sl += l; sl2 += l * l; cnt++;
                }
                if (cnt == 0) continue;
                double ml = sl / cnt;
                float std = (float)Math.Sqrt(Math.Max(0.0, sl2 / cnt - ml * ml));
                grid.cls[i] = Classify((float)(sr / cnt), (float)(sg / cnt), (float)(sb / cnt), std);
                if (++done % cellsPerSlice == 0) yield return null;
            }
        }
    }
}
