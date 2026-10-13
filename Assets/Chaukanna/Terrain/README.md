# Chaukanna Terrain Generator

One tool that builds Unity terrain three ways: from real elevation data, from seeded procedural noise, or from a mix of both.

Open it from the Unity menu: **Chaukanna > Terrain Generator**.

## What it is for in this project

- The observation post sits at the centre of the terrain. Contacts spawn on a 1 km ring and can be seen out to 2 km, so the default area is 5 km x 5 km.
- The terrain's collider blocks line of sight: a contact behind a ridge is not detected.
- Contact altitudes are heights above the ground, so nothing spawns or flies inside a hill.
- The ground under the post is placed at world height 0, so the post and camera need no changes.
- The user's own `Ground` object in `MixedSky.unity` is never touched. Generated terrain is a separate object named `GeneratedTerrain (<config>)`, saved disabled. To use it: enable it and disable `Ground`.

## The three layers

| Layer | What it is | Where it lives |
|---|---|---|
| A. Source elevation | Real heights in metres from an elevation file. Never modified. | `ElevationGrid`, loaded from the file each time |
| B. Procedural displacement | Seeded noise in metres, added to (or replacing) layer A. | Noise layers, warp and terrace on the config |
| C. Surface | Which terrain layer shows where, from height and slope. | Surface rules on the config |

`final elevation = source elevation + procedural displacement`

## Modes

| Mode | What you get |
|---|---|
| Real World Accurate | The file, untouched. Noise, warp, shaping, terracing and the post pad are all ignored. |
| Real World + Subtle Detail | The file plus low-amplitude detail in metres. Multiplier 0 (or amplitude 0) gives the accurate terrain back exactly, with the post pad off. |
| Real World + Major Variations | Relief scale, noise, warp and terraces, blended with the untouched file (0 = file, 1 = modified), optionally limited by a mask texture. |
| Pure Procedural | No file. Base elevation plus noise layers. Presets: rolling hills, plains with peaks, mountain ranges, ridged and rugged, Voronoi, mesas, terraced, warped alien, custom. |
| Custom Hybrid | Separate weights for the real landform ("source weight") and the procedural displacement ("procedural weight"). |

The same seed and settings always give the same terrain. All settings are stored on a `TerrainGenConfig` asset (`Assets/Chaukanna/Terrain/Configs/`), not in the window.

## Quick start: procedural

1. **Chaukanna > Terrain Generator**, press **New** (or pick `MixedSky_RollingHills`).
2. Mode: Pure Procedural. Choose a preset and press **Apply**.
3. **Preview heightfield** shows a shaded-relief picture, north up.
4. **Generate terrain** builds it in the open scene and saves the tile data under `Assets/Chaukanna/Terrain/Generated/<config name>/`.
5. Save the scene (Ctrl+S).

## Quick start: real elevation

1. Get an elevation file for your area (see "Getting elevation data").
2. Mode: Real World Accurate. **Browse** to the file. The window shows its size, bounds, resolution and elevation range.
3. Leave "Centre on the file" ticked, or untick it and enter the latitude and longitude you want at the centre.
4. **Generate terrain**. Read the report: it states the output spacing, the source spacing, and warns if detail was lost or the area runs off the edge of the file.
5. Switch to another real-world mode to add detail. **Restore unmodified DEM** switches back to Accurate and regenerates.

## Supported elevation files

| Format | Notes |
|---|---|
| `.hgt` (SRTM) | One-degree tile, 1 or 3 arc-second. Must keep its name (for example `N28E077.hgt`): the position comes from it. Unzip `.hgt.zip` / `.hgt.gz` first. |
| `.asc` (Esri ASCII grid, GDAL "AAIGrid") | Must be in latitude/longitude degrees (EPSG:4326). Honours `NODATA_value`. |
| `.raw` / `.r16` + `.json` | Headerless 16-bit heightmap with a metadata file of the same name (below). |

**GeoTIFF is not read directly.** No geospatial library is installed, and a hand-written partial TIFF reader would be unreliable. Convert with GDAL (free, from gdal.org or QGIS / OSGeo4W):

```
gdalwarp -t_srs EPSG:4326 input.tif latlon.tif        (only if the file is not already in degrees)
gdal_translate -of AAIGrid latlon.tif area.asc
```

Maintained libraries that could add direct GeoTIFF reading later (each is a new dependency, so ask first): GDAL C# bindings (MaxRev.Gdal), BitMiracle LibTiff.NET, DEM.Net.

### RAW metadata (`name.json` beside `name.raw`)

```json
{
  "width": 1025, "height": 1025,
  "littleEndian": true, "signed": false,
  "west": 77.00, "south": 28.00, "east": 77.05, "north": 28.05,
  "elevationScale": 0.1, "elevationOffset": 180.0,
  "hasNoData": false, "noDataValue": 0,
  "rowOrder": "north-up",
  "crs": "EPSG:4326",
  "source": "where it came from", "licence": "its licence"
}
```

Instead of the four edges you may give `"centreLatitude"`, `"centreLongitude"` and `"cellSizeMetres"`. Elevation in metres = stored value x `elevationScale` + `elevationOffset`.

## Getting elevation data

Nothing is downloaded automatically, and no data ships with the project.

| Source | Access | Notes |
|---|---|---|
| OpenTopography Global DEM API (portal.opentopography.org/apidocs) | Free account and API key | The window can call it (see below). Datasets include SRTMGL1, SRTMGL3, COP30, COP90, NASADEM. Rate limits apply. |
| NASA Earthdata (SRTM, NASADEM) | Free Earthdata login | Download `.hgt` tiles in a browser, then Browse to them. |
| Copernicus DEM (dataspace.copernicus.eu) | Free account | GeoTIFF: convert with GDAL as above. |

**OpenTopography from the window:** set the environment variable `OPENTOPOGRAPHY_API_KEY`, or put the key alone in a text file at `<your user folder>/.chaukanna/opentopography.key`. The key is never stored in the project. Untick "Centre on the file", enter latitude, longitude and an extent, and press Download. Files are cached under the application's persistent data folder (`dem-cache`) and reused. **This path has not been tested end to end**, because no API key was available when it was written; the request and the Esri ASCII reply format follow OpenTopography's published API.

**Licences and attribution:** you are responsible for following the terms of whatever data you use. SRTM and NASADEM are NASA products and ask to be cited. Copernicus DEM is free to use but requires its attribution notice. OpenTopography asks that both the dataset and OpenTopography be cited. Copy the exact wording from the provider's page into the config's "Licence and attribution" field; "Provenance" is filled in when a file is loaded.

## Geographic handling

- Elevation grids are held in latitude/longitude with the north row first; orientation is checked by the self-test.
- Terrain coordinates are local metres east and north of the centre. Metres per degree come from the WGS84 ellipsoid at the centre latitude, so longitude is scaled correctly for the location (98.4 km per degree at 28 N, 55.8 km at 60 N). Over a few kilometres the error is well under 0.1 %.
- Heights are used exactly as stored, in metres. There is no normalising to 0-1 that loses scale, no vertical exaggeration and no smoothing. The Unity terrain's height range is the real span of the data plus a metre of headroom.
- Missing samples (NoData, SRTM voids) are filled from their valid neighbours, growing inwards, so a gap becomes a smooth patch, never a spike. The count is reported.
- Values outside -1000 m to 9500 m are refused as a wrong format, byte order or unit.
- Resampling is bilinear. The report gives source spacing and output spacing and says which one limits the detail.
- Vertical datum: heights are whatever the source uses (EGM96 geoid for SRTM). No datum conversion is done.

## Settings

**Terrain**
- Area size (m): width and length of the whole terrain.
- Heightmap resolution: samples along one tile edge, 2^n + 1 (257, 513, 1025...). 513 over 5 km is a sample every 9.8 m.
- Tiles per side: splits the area into a grid of terrains. Neighbouring tiles share their edge samples and are connected with `SetNeighbors`, so there are no seams.
- Post ground at height 0: moves the whole terrain up or down so the ground at the centre is world height 0. The shape is not changed.
- Flatten post pad: levels a circle at the centre and blends it out. This changes the terrain, so it is always ignored in Real World Accurate. Turn it off in the other real-world modes if you need the exact file heights at the centre.

**Noise layers** (all sizes in metres)
- Kind: Gradient (smooth hills), Ridged (sharp crests, `1 - |noise|`), Cellular (Voronoi).
- Amplitude: the height range the layer contributes. Feature size: width of its largest features.
- Octaves, lacunarity, persistence: fractal detail. More octaves add finer detail; lower persistence keeps it subtle.
- Power: applied to the 0-1 value. Above 1 flattens the lowlands and keeps peaks. Curve: free remapping.
- Centred: the layer adds and removes height equally (use for displacement on real terrain).
- Pipeline per layer: sample > normalise to 0-1 > power > curve > centre > multiply by amplitude.

**Domain warp**: bends the terrain by moving where it is sampled. Strength and feature size in metres.

**Terrace / mesa**: steps the height. Interval in metres; blend 0 = smooth, 1 = flat treads with sharp risers.

**Surface rules**: each rule names a terrain layer and the height band (metres above the area's lowest point) and slope band (degrees) it appears in, with blend distances. "Create placeholder layers and default rules" makes flat-colour grass, dirt, rock, sand and snow layers in `Assets/Chaukanna/Terrain/Layers/`. Replace them with real layers when art exists. Slope is computed by central differences over the true sample spacing.

## Actions

Load / reload elevation file, Download (OpenTopography), Preview heightfield, Generate terrain, Restore unmodified DEM, Export heightmap (.raw + .json), Run terrain self-test.

Generate only ever replaces a terrain that this tool made from the same config, and asks first. Tile data is written only inside `Assets/Chaukanna/Terrain/Generated/<config name>/`.

## Self-test

"Run terrain self-test" in the window, or from the CLI: `Chaukanna.TerrainGen.TerrainGenSelfTest.Run()`. It covers noise range and coherence, seed repeatability and variation, each shaping stage alone, all presets, tile borders, the three file formats, orientation, NoData, accurate-mode fidelity, zero displacement, amplitude scaling in metres, hybrid weights, bad inputs, and the Unity terrain objects. The elevation files it uses are **synthetic**, written from a known formula so the results can be checked exactly; they are not real GIS data.

## Limits

- No real elevation file has been run through it yet; fidelity has been proven against synthetic files only.
- No direct GeoTIFF or 16-bit PNG import.
- The online download is untested without an API key.
- Terrain is baked in the editor. It is not yet chosen from the trainer panel or regenerated at run start.
- No satellite imagery, land cover or vegetation.
- No erosion simulation, runtime streaming or GPU generation.
- One elevation file at a time: an area crossing two `.hgt` tiles needs them merged first (`gdal_merge` or `gdalbuildvrt`, then `gdal_translate -of AAIGrid`).
- Local metre coordinates are a flat approximation around the centre: fine for a few tens of kilometres, not for whole countries.

## Files

- `Scripts/Noise.cs`: gradient, fBm, ridged and cellular noise.
- `Scripts/TerrainGenConfig.cs`: the settings asset, presets and validation.
- `Scripts/ElevationGrid.cs`: source grid, local projection, file readers, NoData filling.
- `Scripts/HeightfieldGenerator.cs`: the mode pipeline, warp, terrace, post pad, slope.
- `Scripts/TerrainBuilder.cs`: Unity terrain tiles, surface painting, ground-height lookup.
- `Scripts/TerrainGenSelfTest.cs`: acceptance checks.
- `Scripts/Editor/TerrainGenWindow.cs`: the editor window and script-callable generate.
