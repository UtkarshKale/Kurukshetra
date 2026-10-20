using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kurukshetra.TerrainGen;
using UnityEngine;
using UnityEngine.Networking;

namespace Kurukshetra.Trainer
{
    // A slippy map of OpenStreetMap tiles drawn with immediate-mode GUI, to pick a real place for the terrain.
    // Drag to pan, mouse wheel to zoom, click to put the centre of the area there. The radius circle and the square
    // terrain that encloses it are drawn on top. Place search goes through Nominatim.
    //
    // OSM tile usage policy: a real User-Agent, the visible credit, tiles cached on disk and only the tiles on screen
    // are ever requested (no prefetching). Nominatim: one request per second at most, only when the trainer presses Enter.
    public enum MapLayer { Map, Satellite, SatelliteLabels }

    public class MapPicker
    {
        const string SearchUrl = "https://nominatim.openstreetmap.org/search?format=json&limit=5&addressdetails=1&q=";
        public const string Credit = "© OpenStreetMap contributors";
        const int MinZoom = 2, MaxConcurrent = 3, MaxCachedTextures = 320;

        // --- Tile sources. Google's satellite tiles need its paid Map Tiles API, so they are not used. ---
        //
        // Satellite, no key: Sentinel-2 cloudless 2016 by EOX (CC BY 4.0, about 10 m per pixel, so blurry when zoomed in).
        // Satellite with a key: Esri World Imagery (0.3 to 1 m). Esri licenses it under its Master License Agreement and
        // says the layer is not meant for offline tile export, so it is only used with the trainer's own ArcGIS key
        // (environment variable ARCGIS_API_KEY, or the file %USERPROFILE%/.kurukshetra/arcgis.key) and is never cached on disk.
        // Labels over satellite: CARTO's labels-only tiles (free for non-commercial use, with credit), or Esri's with a key.
        public const string OsmUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        public const string Sentinel2Url = "https://tiles.maps.eox.at/wmts/1.0.0/s2cloudless_3857/default/g/{z}/{y}/{x}.jpg";
        public const string EsriImageryUrl = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
        public const string EsriLabelsUrl = "https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}";
        public const string CartoLabelsUrl = "https://basemaps.cartocdn.com/dark_only_labels/{z}/{x}/{y}@2x.png";     // 512 px tiles drawn at 256: crisp text
        public const string Sentinel2Credit = "Sentinel-2 cloudless 2016 by EOX (Copernicus Sentinel data 2016-17), CC BY 4.0";
        public const string EsriCredit = "Esri, Vantor, Earthstar Geographics, GIS User Community";

        class TileSource
        {
            public string set, url, credit;
            public int maxZoom;
            public bool diskCache, overlay;
        }

        static string arcGisKey;
        static bool arcGisKeyChecked;

        // The trainer's own ArcGIS key, if they set one. Never stored in the project.
        public static string ArcGisKey
        {
            get
            {
                if (arcGisKeyChecked) return arcGisKey;
                arcGisKeyChecked = true;
                arcGisKey = System.Environment.GetEnvironmentVariable("ARCGIS_API_KEY");
                if (string.IsNullOrWhiteSpace(arcGisKey))
                {
                    try
                    {
                        string file = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".kurukshetra", "arcgis.key");
                        if (File.Exists(file)) arcGisKey = File.ReadAllText(file).Trim();
                    }
                    catch (Exception) { }
                }
                if (string.IsNullOrWhiteSpace(arcGisKey)) arcGisKey = null;
                return arcGisKey;
            }
        }

        public static bool HighResSatellite => ArcGisKey != null;

        // CARTO now needs a key for its label tiles (without one every tile reads "API KEY REQUIRED").
        // Read from the environment variable CARTO_API_KEY or %USERPROFILE%/.kurukshetra/carto.key; never stored in the project.
        static string cartoKey;
        static bool cartoKeyChecked;
        public static string CartoKey
        {
            get
            {
                if (cartoKeyChecked) return cartoKey;
                cartoKeyChecked = true;
                cartoKey = System.Environment.GetEnvironmentVariable("CARTO_API_KEY");
                if (string.IsNullOrWhiteSpace(cartoKey))
                {
                    try
                    {
                        string file = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".kurukshetra", "carto.key");
                        if (File.Exists(file)) cartoKey = File.ReadAllText(file).Trim();
                    }
                    catch (Exception) { }
                }
                if (string.IsNullOrWhiteSpace(cartoKey)) cartoKey = null;
                return cartoKey;
            }
        }

        static TileSource Base(MapLayer layer)
        {
            if (layer == MapLayer.Map) return new TileSource { set = "osm", url = OsmUrl, credit = Credit, maxZoom = 19, diskCache = true };
            if (HighResSatellite) return new TileSource { set = "esri-imagery", url = EsriImageryUrl, credit = EsriCredit, maxZoom = 19, diskCache = false };
            return new TileSource { set = "s2cloudless-2016", url = Sentinel2Url, credit = Sentinel2Credit, maxZoom = 15, diskCache = true };
        }

        static TileSource Overlay(MapLayer layer)
        {
            if (layer != MapLayer.SatelliteLabels) return null;
            if (HighResSatellite) return new TileSource { set = "esri-labels", url = EsriLabelsUrl, credit = "labels Esri", maxZoom = 19, diskCache = false, overlay = true };
            if (CartoKey == null) return null;
            return new TileSource { set = "carto-labels", url = CartoLabelsUrl, credit = "labels © CARTO", maxZoom = 19, diskCache = false, overlay = true };
        }

        // The URL of one tile. Note the ArcGIS and EOX services take y before x.
        public static string TileUrl(string template, int z, int x, int y, string key = null)
        {
            string url = template.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
            if (!string.IsNullOrEmpty(key) && template.Contains("arcgisonline.com")) url += "?token=" + UnityWebRequest.EscapeURL(key);
            if (!string.IsNullOrEmpty(key) && template.Contains("cartocdn.com")) url += "?key=" + UnityWebRequest.EscapeURL(key);
            return url;
        }

        public MapLayer Layer { get; private set; } = MapLayer.Map;
        // Beyond a source's own last zoom level its tiles are enlarged, so the map can always zoom in like Google Earth.
        public const int MaxZoom = 19;

        public void SetLayer(MapLayer layer)
        {
            Layer = layer;
        }

        // --- Radius maths, public for the self-tests. ---
        public static float RadiusToPixels(double latitude, int zoom, float metres) => (float)(metres / WebMercator.MetresPerPixel(latitude, zoom));
        public static float PixelsToRadius(double latitude, int zoom, float pixels) => (float)(pixels * WebMercator.MetresPerPixel(latitude, zoom));
        public const float RadiusStep = 50f;
        public static float SnapRadius(float metres) => Mathf.Max(RadiusStep, Mathf.Round(metres / RadiusStep) * RadiusStep);
        public static string FormatMetres(float metres) =>
            metres >= 10000f ? (metres / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " km" : metres.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + " m";

        // The view: its centre in world pixels at the current zoom.
        public int Zoom { get; private set; } = 12;
        double viewX, viewY;

        // The pick: centre of the area and its radius (half the side of the terrain square).
        public double PickLatitude { get; private set; }
        public double PickLongitude { get; private set; }
        public bool HasPick { get; private set; }
        public float RadiusMetres = 2500f;
        public string Status { get; private set; } = "";
        public bool Offline { get; private set; }

        readonly MonoBehaviour host;
        readonly Dictionary<string, Texture2D> tiles = new Dictionary<string, Texture2D>();
        readonly LinkedList<string> recent = new LinkedList<string>();
        readonly HashSet<string> pending = new HashSet<string>();
        readonly HashSet<string> failed = new HashSet<string>();
        int active;
        float lastSearchTime = -10f;

        Vector2 pressAt;
        bool pressed, dragging, resizing;
        public bool Resizing => resizing;

        public MapPicker(MonoBehaviour host, double latitude, double longitude, int zoom = 12)
        {
            this.host = host;
            Zoom = Mathf.Clamp(zoom, MinZoom, 19);
            CentreOn(latitude, longitude);
            SetPick(latitude, longitude);
        }

        public void CentreOn(double latitude, double longitude)
        {
            WebMercator.ToPixel(latitude, longitude, Zoom, out viewX, out viewY);
        }

        public void SetPick(double latitude, double longitude)
        {
            PickLatitude = Math.Max(-WebMercator.MaxLatitude, Math.Min(WebMercator.MaxLatitude, latitude));
            PickLongitude = ((longitude + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            HasPick = true;
        }

        public void Destroy()
        {
            foreach (var t in tiles.Values) if (t != null) UnityEngine.Object.Destroy(t);
            tiles.Clear();
        }

        // Draws the map into r (GUI coordinates) and handles the mouse inside it.
        public void Draw(Rect r, GUIStyle small)
        {
            var e = Event.current;
            HandleInput(r, e);

            if (e.type == EventType.Repaint) Fill(r, new Color(0.11f, 0.13f, 0.16f));
            GUI.BeginGroup(r);
            double left = viewX - r.width * 0.5, top = viewY - r.height * 0.5;
            int n = 1 << Zoom;
            int tx0 = (int)Math.Floor(left / WebMercator.TileSize), tx1 = (int)Math.Floor((left + r.width) / WebMercator.TileSize);
            int ty0 = Math.Max(0, (int)Math.Floor(top / WebMercator.TileSize)), ty1 = Math.Min(n - 1, (int)Math.Floor((top + r.height) / WebMercator.TileSize));
            for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int wx = ((tx % n) + n) % n;                 // the world wraps east-west
                var cell = new Rect((float)(tx * WebMercator.TileSize - left), (float)(ty * WebMercator.TileSize - top), WebMercator.TileSize, WebMercator.TileSize);
                bool paint = e.type == EventType.Repaint;
                bool drawn = DrawSource(Base(Layer), cell, Zoom, wx, ty, paint);
                // No tile here (offline, or never cached): an enlarged part of a coarser tile this PC has, then the
                // satellite pictures of the areas shipped with the build, then an empty cell.
                if (!drawn) drawn = DrawAncestor(Base(Layer), cell, Zoom, wx, ty, paint);
                var cellWorld = new Rect(tx * WebMercator.TileSize, ty * WebMercator.TileSize, WebMercator.TileSize, WebMercator.TileSize);
                if (!drawn && paint) Fill(new Rect(cell.x + 1f, cell.y + 1f, cell.width - 2f, cell.height - 2f), new Color(0.15f, 0.17f, 0.2f));
                if (!drawn || Layer != MapLayer.Map) OfflineAreas.DrawInto(cell, cellWorld, Zoom, paint && (!drawn || Offline));
                var over = Overlay(Layer);
                if (over != null) DrawSource(over, cell, Zoom, wx, ty, e.type == EventType.Repaint);
            }

            if (e.type == EventType.Repaint) DrawAreaMarkers(left, top, r);

            if (HasPick && e.type == EventType.Repaint)
            {
                WebMercator.ToPixel(PickLatitude, PickLongitude, Zoom, out double px, out double py);
                var p = new Vector2((float)(px - left), (float)(py - top));
                float rpx = RadiusToPixels(PickLatitude, Zoom, RadiusMetres);
                bool hot = resizing || OnRing(e.mousePosition + r.position, r, out _);     // inside the group the mouse is relative to the map
                var ring = resizing || hot ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 0.3f, 0.25f, 0.9f);
                int dots = Mathf.Clamp(Mathf.RoundToInt(rpx * 0.6f), 24, 180);
                for (int i = 0; i < dots; i++)
                {
                    float a = i * Mathf.PI * 2f / dots;
                    Fill(new Rect(p.x + Mathf.Sin(a) * rpx - 1.5f, p.y - Mathf.Cos(a) * rpx - 1.5f, 3f, 3f), ring);
                }
                // The terrain: the square that encloses the circle.
                var sq = new Color(1f, 0.85f, 0.2f, 0.9f);
                Fill(new Rect(p.x - rpx, p.y - rpx, rpx * 2f, 2f), sq);
                Fill(new Rect(p.x - rpx, p.y + rpx - 2f, rpx * 2f, 2f), sq);
                Fill(new Rect(p.x - rpx, p.y - rpx, 2f, rpx * 2f), sq);
                Fill(new Rect(p.x + rpx - 2f, p.y - rpx, 2f, rpx * 2f), sq);
                // Handle on the east edge of the circle: drag it (or anywhere on the ring) to change the radius.
                Fill(new Rect(p.x + rpx - 7f, p.y - 7f, 14f, 14f), Color.black);
                Fill(new Rect(p.x + rpx - 5f, p.y - 5f, 10f, 10f), resizing || hot ? Color.white : new Color(1f, 0.3f, 0.25f));
                Fill(new Rect(p.x, p.y - 1f, rpx, 2f), new Color(1f, 0.3f, 0.25f, 0.6f));     // radius line from the centre to the handle
                Tag(new Vector2(p.x + rpx * 0.5f, p.y - 14f), "r " + FormatMetres(RadiusMetres), new Color(1f, 0.3f, 0.25f));
                Tag(new Vector2(p.x, p.y + rpx + 12f), "terrain " + FormatMetres(RadiusMetres * 2f) + " square", new Color(1f, 0.85f, 0.2f));
                Fill(new Rect(p.x - 7f, p.y - 1f, 14f, 3f), Color.black);
                Fill(new Rect(p.x - 1f, p.y - 7f, 3f, 14f), Color.black);
                Fill(new Rect(p.x - 4f, p.y - 4f, 8f, 8f), new Color(1f, 0.3f, 0.25f));
            }
            GUI.EndGroup();

            // Layer switch, top-right over the map.
            string[] names = { "Map", HighResSatellite ? "Satellite" : "Satellite (10 m)", "Satellite + labels" };
            var layersRect = LayersRect(r);
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(layersRect.x + i * (layersRect.width / 3f), layersRect.y, layersRect.width / 3f - 4f, layersRect.height);
                bool on = (int)Layer == i;
                if (GUI.Toggle(b, on, names[i], GUI.skin.button) && !on) SetLayer((MapLayer)i);
            }
            if (Layer == MapLayer.SatelliteLabels && Overlay(Layer) == null)
            {
                var hint = new Rect(layersRect.x, layersRect.yMax + 4f, layersRect.width, 36f);
                if (e.type == EventType.Repaint) Fill(hint, new Color(0f, 0f, 0f, 0.75f));
                GUI.Label(hint, " Labels need a CARTO key in %USERPROFILE%\\.kurukshetra\\carto.key", small);
            }

            DrawSearch(r, small);

            // The credit each source asks for, always visible.
            var over2 = Overlay(Layer);
            string creditText = Base(Layer).credit + (over2 != null ? "; " + over2.credit + (over2.set == "carto-labels" ? ", " + Credit : "") : "");
            var dark = new GUIStyle(small) { alignment = TextAnchor.MiddleRight, wordWrap = false, clipping = TextClipping.Clip };
            dark.normal.textColor = new Color(0.1f, 0.1f, 0.1f);
            float cw = Mathf.Min(r.width - 8f, dark.CalcSize(new GUIContent(creditText + "  ")).x);
            var credit = new Rect(r.xMax - cw - 4f, r.yMax - 20f, cw, 18f);
            if (e.type == EventType.Repaint) Fill(credit, new Color(1f, 1f, 1f, 0.8f));
            GUI.Label(credit, creditText + " ", dark);
            if (Offline)
            {
                var box = new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 40f);
                if (e.type == EventType.Repaint) Fill(box, new Color(0f, 0f, 0f, 0.75f));
                int areas = OfflineAreas.AreaCount;
                GUI.Label(box, "  Map offline (" + Status.Replace("Map tiles: ", "") + "). " + (areas > 0
                    ? $"Showing the {areas} area{(areas > 1 ? "s" : "")} shipped with this build (outlined, with their satellite pictures): click inside one to use it, or type the latitude and longitude."
                    : "No areas are shipped with this build: type the latitude and longitude, or download an area on a PC with internet and rebuild."), small);
            }
        }

        // Outlines and names of the shipped / downloaded areas, so they can be found and picked offline at any zoom.
        void DrawAreaMarkers(double left, double top, Rect r)
        {
            var outline = new Color(0.35f, 0.95f, 1f, 0.85f);
            foreach (var p in OfflineAreas.All)
            {
                if (p.surround) continue;
                Rect pr = OfflineAreas.PixelRect(p, Zoom);
                var box = new Rect((float)(pr.x - left), (float)(pr.y - top), pr.width, pr.height);
                if (box.xMax < 0f || box.yMax < 0f || box.x > r.width || box.y > r.height) continue;
                if (box.width < 6f) box = new Rect(box.center.x - 3f, box.center.y - 3f, 6f, 6f);    // zoomed far out: a marker
                Fill(new Rect(box.x, box.y, box.width, 2f), outline);
                Fill(new Rect(box.x, box.yMax - 2f, box.width, 2f), outline);
                Fill(new Rect(box.x, box.y, 2f, box.height), outline);
                Fill(new Rect(box.xMax - 2f, box.y, 2f, box.height), outline);
                Tag(new Vector2(box.center.x, box.y - 12f), p.area, outline);
            }
        }

        // Past what this PC has at the exact zoom: the matching part of the nearest coarser tile it does have (memory or
        // disk, never the network), enlarged. Up to 8 levels up, so a few low-zoom tiles cover the whole view offline.
        bool DrawAncestor(TileSource source, Rect cell, int z, int x, int y, bool paint)
        {
            int start = Mathf.Min(z, source.maxZoom);
            for (int up = 1; up <= 8 && start - up >= 0; up++)
            {
                int dz = z - (start - up);
                var tex = TileLocal(source, start - up, x >> dz, y >> dz);
                if (tex == null) continue;
                if (!paint) return true;
                float part = 1f / (1 << dz);
                int sx = x - ((x >> dz) << dz), sy = y - ((y >> dz) << dz);
                GUI.DrawTextureWithTexCoords(cell, tex, new Rect(sx * part, 1f - (sy + 1) * part, part, part), source.overlay);
                return true;
            }
            return false;
        }

        // A tile from memory or the disk caches only.
        Texture2D TileLocal(TileSource source, int z, int x, int y)
        {
            string key = source.set + "/" + z + "/" + x + "/" + y;
            if (tiles.TryGetValue(key, out var tex)) return tex;
            if (!source.diskCache || missingLocal.Contains(key)) return null;
            string cache = ElevationDownloader.TileCachePath(source.set, z, x, y);
            if (!File.Exists(cache)) cache = ElevationDownloader.BundledTilePath(source.set, z, x, y);
            if (!File.Exists(cache)) { missingLocal.Add(key); return null; }
            tex = new Texture2D(2, 2, source.overlay ? TextureFormat.RGBA32 : TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            if (tex.LoadImage(File.ReadAllBytes(cache))) { Keep(key, tex); return tex; }
            UnityEngine.Object.Destroy(tex);
            missingLocal.Add(key);
            return null;
        }

        readonly HashSet<string> missingLocal = new HashSet<string>();

        Rect LayersRect(Rect r) => new Rect(r.xMax - 6f - 3f * 118f, r.y + 6f, 3f * 118f, 26f);
        Rect SearchRect(Rect r) => new Rect(r.x + 6f, r.y + 6f, Mathf.Min(330f, r.width - 3f * 118f - 24f), 26f);

        // Map clicks never start on the controls drawn over the map.
        bool OverControls(Rect r, Vector2 mouse)
        {
            if (LayersRect(r).Contains(mouse)) return true;
            var s = SearchRect(r);
            if (s.Contains(mouse)) return true;
            int rows = DropdownRows();
            return rows > 0 && new Rect(s.x, s.yMax + 2f, s.width + 70f, rows * 26f + 6f).Contains(mouse);
        }

        void HandleInput(Rect r, Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !r.Contains(e.mousePosition)) return;
                    if (OverControls(r, e.mousePosition)) return;
                    showDropdown = false;
                    pressed = true; dragging = false; pressAt = e.mousePosition;
                    resizing = HasPick && OnRing(e.mousePosition, r, out _);
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!pressed) return;
                    if (resizing)
                    {
                        // Dragging the ring: radius = distance from the centre, in 50 m steps. The Radius m field follows.
                        OnRing(e.mousePosition, r, out float distance);
                        RadiusMetres = SnapRadius(PixelsToRadius(PickLatitude, Zoom, distance));
                        dragging = true;
                        e.Use();
                        break;
                    }
                    if (!dragging && (e.mousePosition - pressAt).magnitude > 4f) dragging = true;
                    if (dragging) { viewX -= e.delta.x; viewY -= e.delta.y; ClampView(); }
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!pressed) return;
                    pressed = false;
                    if (resizing) { resizing = false; e.Use(); break; }
                    if (!dragging && r.Contains(e.mousePosition))
                    {
                        // A click (not a drag) puts the centre of the area here.
                        double px = viewX - r.width * 0.5 + (e.mousePosition.x - r.x), py = viewY - r.height * 0.5 + (e.mousePosition.y - r.y);
                        WebMercator.FromPixel(px, py, Zoom, out double lat, out double lon);
                        SetPick(lat, lon);
                    }
                    e.Use();
                    break;
                case EventType.ScrollWheel:
                    if (!r.Contains(e.mousePosition)) return;
                    ZoomAt(e.delta.y < 0f ? 1 : -1, e.mousePosition - r.position, r.size);
                    e.Use();
                    break;
            }
        }

        // One tile of a source into a map cell. Past the source's last zoom, the right part of its last tile is enlarged.
        bool DrawSource(TileSource source, Rect cell, int z, int x, int y, bool paint)
        {
            int dz = Mathf.Max(0, z - source.maxZoom);
            var tex = Tile(source, z - dz, x >> dz, y >> dz);
            if (tex == null || !paint) return tex != null;
            if (dz == 0) { GUI.DrawTexture(cell, tex, ScaleMode.StretchToFill, source.overlay); return true; }
            float part = 1f / (1 << dz);
            int sx = x - ((x >> dz) << dz), sy = y - ((y >> dz) << dz);
            GUI.DrawTextureWithTexCoords(cell, tex, new Rect(sx * part, 1f - (sy + 1) * part, part, part), source.overlay);
            return true;
        }

        // Is the mouse on the circle's edge or its handle? Also gives the distance from the centre in pixels.
        bool OnRing(Vector2 mouse, Rect r, out float distance)
        {
            WebMercator.ToPixel(PickLatitude, PickLongitude, Zoom, out double px, out double py);
            var centre = new Vector2((float)(px - (viewX - r.width * 0.5) + r.x), (float)(py - (viewY - r.height * 0.5) + r.y));
            float rpx = RadiusToPixels(PickLatitude, Zoom, RadiusMetres);
            distance = Vector2.Distance(mouse, centre);
            bool onHandle = Vector2.Distance(mouse, centre + new Vector2(rpx, 0f)) <= 10f;
            return onHandle || Mathf.Abs(distance - rpx) <= 6f;
        }

        void Tag(Vector2 at, string text, Color colour)
        {
            if (tagStyle == null) tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
            var size = tagStyle.CalcSize(new GUIContent(text));
            var box = new Rect(at.x - size.x * 0.5f - 4f, at.y - 10f, size.x + 8f, 20f);
            Fill(box, new Color(0f, 0f, 0f, 0.7f));
            tagStyle.normal.textColor = colour;
            GUI.Label(box, text, tagStyle);
        }

        GUIStyle tagStyle;

        // Zoom in (+1) or out (-1), keeping the point under the mouse where it is.
        public void ZoomAt(int step, Vector2 mouseInMap, Vector2 mapSize)
        {
            int z = Mathf.Clamp(Zoom + step, MinZoom, MaxZoom);
            if (z == Zoom) return;
            double mx = viewX - mapSize.x * 0.5 + mouseInMap.x, my = viewY - mapSize.y * 0.5 + mouseInMap.y;
            WebMercator.FromPixel(mx, my, Zoom, out double lat, out double lon);
            Zoom = z;
            WebMercator.ToPixel(lat, lon, Zoom, out double nx, out double ny);
            viewX += nx - mx;
            viewY += ny - my;
            ClampView();
        }

        void ClampView()
        {
            double world = WebMercator.WorldPixels(Zoom);
            viewY = Math.Max(0.0, Math.Min(world, viewY));
            viewX = ((viewX % world) + world) % world;
        }

        // --- Tiles: memory, then disk cache, then the network (only for tiles on screen). ---

        Texture2D Tile(TileSource source, int z, int x, int y)
        {
            string key = source.set + "/" + z + "/" + x + "/" + y;
            if (tiles.TryGetValue(key, out var tex)) return tex;
            if (failed.Contains(key) || pending.Contains(key)) return null;

            string cache = source.diskCache ? ElevationDownloader.TileCachePath(source.set, z, x, y) : null;
            // No copy on this PC yet: the tiles shipped with the build, so the map works offline.
            if (cache != null && !File.Exists(cache) && File.Exists(ElevationDownloader.BundledTilePath(source.set, z, x, y))) cache = ElevationDownloader.BundledTilePath(source.set, z, x, y);
            if (cache != null && File.Exists(cache))
            {
                tex = new Texture2D(2, 2, source.overlay ? TextureFormat.RGBA32 : TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
                if (tex.LoadImage(File.ReadAllBytes(cache))) { Keep(key, tex); return tex; }
                UnityEngine.Object.Destroy(tex);
            }
            // Offline: no new requests for a while (each one would wait out its 15 s timeout), then try again, so the map
            // recovers by itself when the network comes back.
            if (Offline && Time.realtimeSinceStartup < retryAt) return null;
            if (Offline && failed.Count > 0 && Time.realtimeSinceStartup >= retryAt) { failed.Clear(); retryAt = Time.realtimeSinceStartup + RetrySeconds; }
            if (active < MaxConcurrent && host != null && host.isActiveAndEnabled)
            {
                pending.Add(key);
                host.StartCoroutine(Fetch(key, source, z, x, y, cache));
            }
            return null;
        }

        bool offlineLogged;
        float retryAt;
        const float RetrySeconds = 30f;

        IEnumerator Fetch(string key, TileSource source, int z, int x, int y, string cache)
        {
            active++;
            string url = TileUrl(source.url, z, x, y, source.set.StartsWith("esri") ? ArcGisKey : source.set.StartsWith("carto") ? CartoKey : null);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("User-Agent", ElevationDownloader.UserAgent);
                request.timeout = 15;
                yield return request.SendWebRequest();
                active--;
                pending.Remove(key);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed.Add(key);
                    Offline = request.result == UnityWebRequest.Result.ConnectionError;
                    if (Offline) retryAt = Time.realtimeSinceStartup + RetrySeconds;
                    Status = "Map tiles: " + request.error;
                    if (Offline && !offlineLogged) { offlineLogged = true; Debug.LogWarning($"[Map] Tile download failed ({request.error}) for {url.Split('?')[0]}. Areas and tiles shipped in {ElevationDownloader.BundledFolder} still work."); }
                    yield break;
                }
                Offline = false;
                byte[] png = request.downloadHandler.data;
                var tex = new Texture2D(2, 2, source.overlay ? TextureFormat.RGBA32 : TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(png)) { UnityEngine.Object.Destroy(tex); failed.Add(key); yield break; }
                Keep(key, tex);
                if (cache != null)
                {
                    try { Directory.CreateDirectory(Path.GetDirectoryName(cache)); File.WriteAllBytes(cache, png); }
                    catch (Exception) { }   // the cache is a convenience
                }
            }
        }

        void Keep(string key, Texture2D tex)
        {
            tiles[key] = tex;
            recent.AddLast(key);
            while (recent.Count > MaxCachedTextures)
            {
                string old = recent.First.Value;
                recent.RemoveFirst();
                if (tiles.TryGetValue(old, out var t)) { UnityEngine.Object.Destroy(t); tiles.Remove(old); }
            }
        }

        // --- Place search: a box over the map like Google Earth's. Coordinates jump straight there; anything else goes
        // to Nominatim (only on Enter, at most once a second) and up to five results drop down. Recent places are kept. ---

        [Serializable] class Address { public string state, country, county, city, town, village; }
        [Serializable] class Place { public string lat, lon, display_name, type, name; public Address address; }
        [Serializable] class Places { public Place[] items; }

        public class Hit
        {
            public string title, detail;
            public double latitude, longitude;
        }

        public readonly List<Hit> Results = new List<Hit>();
        public readonly List<Hit> Recent = new List<Hit>();
        string searchText = "";
        bool showDropdown, recentLoaded;
        const int MaxRecent = 8;

        static string RecentFile => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".kurukshetra", "recent_places.txt");

        int DropdownRows() => !showDropdown ? 0 : Results.Count > 0 ? Results.Count : Recent.Count;

        void DrawSearch(Rect r, GUIStyle small)
        {
            LoadRecent();
            var e = Event.current;
            var box = SearchRect(r);
            GUI.SetNextControlName("map.search");
            bool focused = GUI.GetNameOfFocusedControl() == "map.search";
            if (focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                Search(searchText);
                e.Use();
            }
            if (focused && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { showDropdown = false; e.Use(); }
            string before = searchText;
            searchText = GUI.TextField(new Rect(box.x, box.y, box.width - 62f, box.height), searchText, 120);
            if (searchText != before) { Results.Clear(); showDropdown = searchText.Length == 0 && Recent.Count > 0; }
            if (focused && searchText.Length == 0 && Results.Count == 0 && Recent.Count > 0) showDropdown = true;
            if (GUI.Button(new Rect(box.xMax - 58f, box.y, 58f, box.height), "Search")) Search(searchText);
            if (searchText.Length == 0 && !focused)
            {
                var ghost = new GUIStyle(small);
                ghost.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                GUI.Label(new Rect(box.x + 6f, box.y + 3f, box.width - 70f, box.height - 6f), "Search a place, or paste coordinates", ghost);
            }

            int rows = DropdownRows();
            if (rows == 0) return;
            var list = Results.Count > 0 ? Results : Recent;
            var area = new Rect(box.x, box.yMax + 2f, box.width + 70f, rows * 26f + 6f);
            if (e.type == EventType.Repaint) Fill(area, new Color(0.08f, 0.09f, 0.11f, 0.95f));
            for (int i = 0; i < rows; i++)
            {
                var row = new Rect(area.x + 3f, area.y + 3f + i * 26f, area.width - 6f, 24f);
                string text = (Results.Count == 0 ? "<color=#9AA4AE>recent</color>  " : "") + "<b>" + list[i].title + "</b>  <color=#9AA4AE>" + list[i].detail + "</color>";
                if (GUI.Button(row, text, new GUIStyle(small) { richText = true, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, hover = { textColor = Color.white, background = Texture2D.grayTexture } }))
                    GoTo(list[i]);
            }
        }

        public void GoTo(Hit hit)
        {
            showDropdown = false;
            Zoom = Mathf.Max(Zoom, 13);
            SetPick(hit.latitude, hit.longitude);
            CentreOn(hit.latitude, hit.longitude);
            Status = "Centred on " + hit.title + (string.IsNullOrEmpty(hit.detail) ? "" : ", " + hit.detail);
            Remember(hit);
        }

        void Remember(Hit hit)
        {
            Recent.RemoveAll(h => h.title == hit.title && Math.Abs(h.latitude - hit.latitude) < 1e-6);
            Recent.Insert(0, hit);
            if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentFile));
                var lines = new List<string>();
                foreach (var h in Recent)
                    lines.Add(string.Join("|", h.title.Replace("|", "/"), (h.detail ?? "").Replace("|", "/"),
                        h.latitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture), h.longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                File.WriteAllLines(RecentFile, lines);
            }
            catch (Exception) { }
        }

        void LoadRecent()
        {
            if (recentLoaded) return;
            recentLoaded = true;
            try
            {
                if (!File.Exists(RecentFile)) return;
                foreach (var line in File.ReadAllLines(RecentFile))
                {
                    var f = line.Split('|');
                    if (f.Length == 4 && double.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double la)
                        && double.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lo))
                        Recent.Add(new Hit { title = f[0], detail = f[1], latitude = la, longitude = lo });
                }
            }
            catch (Exception) { }
        }

        public void Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || host == null) return;
            // Coordinates need no network: decimal ("34.5735, 76.0974") or degrees-minutes-seconds (34°34'24.79"N 76°05'50.63"E).
            if (GeoParse.TryParseLatLon(query, out double lat, out double lon))
            {
                GoTo(new Hit { title = $"{lat:F5}, {lon:F5}", detail = "coordinates", latitude = lat, longitude = lon });
                return;
            }
            if (Time.realtimeSinceStartup - lastSearchTime < 1.1f) { Status = "One search per second, please."; return; }
            lastSearchTime = Time.realtimeSinceStartup;
            host.StartCoroutine(SearchRoutine(query.Trim()));
        }

        IEnumerator SearchRoutine(string query)
        {
            Status = "Searching for " + query + "...";
            using (var request = UnityWebRequest.Get(SearchUrl + UnityWebRequest.EscapeURL(query)))
            {
                request.SetRequestHeader("User-Agent", ElevationDownloader.UserAgent);
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { Status = "Search failed: " + request.error; yield break; }
                Places found;
                try { found = JsonUtility.FromJson<Places>("{\"items\":" + request.downloadHandler.text + "}"); }
                catch (Exception) { Status = "Search answer could not be read."; yield break; }
                if (found?.items == null || found.items.Length == 0) { Status = "No place found for " + query + "."; Results.Clear(); yield break; }
                Results.Clear();
                foreach (var p in found.items)
                {
                    if (!double.TryParse(p.lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lat)
                        || !double.TryParse(p.lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lon)) continue;
                    string title = !string.IsNullOrEmpty(p.name) ? p.name : (p.display_name ?? "").Split(',')[0];
                    var a = p.address;
                    string where = a == null ? "" : string.Join(", ", new[] { a.city ?? a.town ?? a.village ?? a.county, a.state, a.country }.Where(s => !string.IsNullOrEmpty(s)));
                    Results.Add(new Hit { title = title, detail = (string.IsNullOrEmpty(p.type) ? "" : p.type.Replace('_', ' ') + " · ") + where, latitude = lat, longitude = lon });
                }
                showDropdown = Results.Count > 0;
                Status = Results.Count + " result" + (Results.Count == 1 ? "" : "s") + " (search by Nominatim / OpenStreetMap)";
            }
        }

        static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
