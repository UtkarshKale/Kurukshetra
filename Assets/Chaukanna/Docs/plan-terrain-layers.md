# Plan: Terrain layers (trees, grass, buildings) with Realistic / Procedural modes (TL-1)

Planned 2026-10-17 (Opus). Built by a Sonnet agent. User decisions (chat, 2026-10-17):
- Three layers: **trees, grass, buildings**. Each has its own toggle: **Off / Realistic / Procedural**.
  "Realistic" = one-to-one with the real place; "Procedural" = generated from the seed and the terrain.
- Realistic source: **OpenStreetMap** (building footprints with height / levels, forests and woods, single trees, tree
  rows, grass / meadow / park / farmland) fetched when an area is downloaded (internet once, then offline), with a
  **satellite-colour fallback** for trees and grass where OSM has nothing. The user approved the download, including one
  re-fetch for the 6 areas already on disk.
- Coverage: the whole map, **near = high detail, far = less** (distance LOD and density fall-off), not a hard cut.
- Art: everything is a **prefab slot** the user can replace quickly. Placeholders are built from primitives now.
Rules: CLAUDE.md. No new packages, layers or tags. Runtime materials via `RuntimeMaterials`. Honest labels: never say
"real" unless OSM data was actually loaded for that area (rule 4).

## 1. Data: fetch and store with the area
- `Terrain/Scripts/OsmLayers.cs`: Overpass API client (`https://overpass-api.de/api/interpreter`, POST, the project's
  User-Agent `ElevationDownloader.UserAgent`, timeout 120 s). Two queries per area, 2 s apart:
  1. Play-area bbox (the area's own bounds): `way/relation[building]` and `[building:part]` with `out geom` (footprints,
     tags `height`, `building:levels`, `building`), `node[natural=tree]`, `way[natural=tree_row]` geom,
     `way/relation[landuse=forest]`, `[natural=wood]`, `[landuse=orchard]`, `[landuse=grass|meadow|recreation_ground|
     village_green]`, `[leisure=park]`, `[landuse=farmland]` geom.
  2. Surround bbox (the `_surround` picture bounds): the same landcover polygons with `out geom`, and buildings with
     `out center` only (points + tags; far buildings are drawn as simple blocks). If this query fails or times out, keep
     the play-area result and record the failure.
- Save the raw responses (`<area>_osm.json`, `<area>_osm_surround.json`) and a compact processed file
  `<area>_layers.json` next to the area's other files: local metres around the area centre (same WGS84-local metric as
  the terrain), buildings `{poly: [x,z,...], height, type}`, trees `{x,z}`, tree rows `{line}`, forest `{poly}`, grass
  `{poly}`, far buildings `{x,z,height,type}`, plus `source = "OpenStreetMap contributors, ODbL 1.0"`, fetch date,
  counts, and any failure note. Height: `height` tag, else `building:levels * 3.2 m`, else 6 m (houses) / by type.
- The area download flow (Editor TerrainGenWindow and the trainer menu's download in a build: find where
  `ElevationDownloader` downloads an area) gets an extra step "Map features (OpenStreetMap)" after the imagery, so new
  areas always have layers. The LAN area transfer already sends every file in the area folder, so trainee PCs get it.
- Editor menu `Chaukanna > Terrain > Fetch OSM layers for downloaded areas`: fetches for every area in
  `Assets/Chaukanna/Terrain/RealData/` that has no `_layers.json` (one area at a time, 2 s between queries, progress
  bar, cancel). **Run it once for the 6 existing areas** (approved) and report the counts per area.
- `BundleOfflineData` already copies the area folders into builds: check the new files arrive in `ChaukannaData`.

## 2. Satellite-colour fallback (no download)
`Terrain/Scripts/SatelliteCover.cs`: reads `<area>_satellite.jpg` (and the surround picture) with its bounds and
classifies each pixel: excess green `ExG = 2g - r - b` on normalised rgb; vegetation if ExG > t1; **tree** if
vegetation and dark (luma below t2) or high local variance in a 3x3 window; **grass** if vegetation and lighter;
else none. A 10 m pixel is a cell. Used for the tree and grass layers in Realistic mode only where no OSM landcover
polygon covers the cell (forest / wood / grass / park / meadow / farmland). Labelled "estimated from satellite colour"
in the UI and the session file. Buildings never come from satellite colour.

## 3. Procedural mode (from the terrain seed)
`Terrain/Scripts/LayerScatter.cs`, deterministic from the terrain seed + layer:
- Trees: clumps and tree lines from low-frequency noise thresholds, no trees above the tree line height or on slopes
  over 35 deg or in water / snow areas (reuse SurfaceKit's biome where possible); density per hectare from settings.
- Grass: everywhere slope < 30 deg below the snow line, density by a noise field.
- Buildings: villages: a few cluster centres on flat ground (slope < 6 deg), houses on a jittered grid around each,
  sizes from the kit; keep clear of the post's site (inside `SiteProfile.halfWidth / depth` around the post) and of
  any steep ground.

## 4. Prefab kit (replaceable art)
`Terrain/Scripts/TerrainLayerKit.cs` ScriptableObject + asset `Data/TerrainLayerKit_Default.asset`:
- `treePrefabs` (list, picked per tree from the seed; each scaled by a height range), `treeHeight` range.
- `grassPrefabs` (detail meshes) and/or `grassTextures` (billboard grass); colour range; size range.
- `buildingPrefabs` by type: `house, apartments, commercial, industrial, religious, shed, generic` (lists); a building
  of that OSM type uses one of them scaled to its footprint box and height. Empty list = the footprint is extruded
  exactly (true 1:1 shape, flat roof) with `buildingMaterial`.
- `buildingMaterial`, `roofMaterial` (default: RuntimeMaterials.Lit copies).
Placeholders (Editor script `Terrain/Scripts/Editor/BuildLayerPlaceholders.cs`, menu `Chaukanna > Terrain > Build layer
placeholders`, run once) into `Assets/Chaukanna/Prefabs/LayerPlaceholders/`: 2 trees (conifer cone + trunk; broadleaf
sphere + trunk), each with a `LODGroup` (LOD0 primitives, LOD1 an 8-triangle crossed quad impostor, culled below ~1 %
screen height) and a CapsuleCollider on the trunk; 1 grass clump (crossed quads); 6 building placeholders (house,
apartments, commercial, industrial, religious, shed: boxes with a roof wedge, LOD1 single box) with BoxColliders.
Every placeholder material must be a saved .mat under Assets/Chaukanna (so it ships in builds); create from
RuntimeMaterials.Lit settings. The kit asset references them. README line in the kit's tooltip: "Drag your own prefabs
in; nose / front along +Z, pivot at the ground, 1 unit = 1 metre".

## 5. Rendering and level of detail (near detailed, far cheap)
- **Trees: Unity terrain trees** (TerrainData.treePrototypes from the kit prefabs, treeInstances). Settings per
  terrain: `treeDistance` 6000 m, `treeBillboardDistance` 250 m (LODGroup prefabs switch by screen size),
  `treeMaximumFullLODCount` 200, instancing on in the materials. Density fall-off with distance from the post:
  full density inside 1.5 km, then `density * clamp(1.5 km / d, 0.15, 1)` on the surround terrain. Tree colliders on
  (`TerrainCollider` tree colliders) so trees block line of sight; setting "Trees block line of sight" (default on).
- **Grass: terrain detail layers** (instanced detail meshes, `DetailRenderMode.VertexLit` for meshes / `GrassBillboard`
  for textures), play-area tiles only (grass is never visible past `detailObjectDistance` = 180 m anyway), density
  `detailObjectDensity` 1.0 near, Unity's own distance thinning. Respect the 4-paint-layer limit: grass is a detail
  layer, not a paint layer, so it does not compete.
- **Buildings**: extruded footprints are merged into one mesh per 250 m cell (our own readable meshes, so
  CombineMeshes is fine), with a MeshCollider per cell (blocks line of sight). Each cell gets a LODGroup: LOD0 the merged
  footprints, LOD1 a merged set of their bounding boxes (roof-flat), culled at ~0.5 % screen height. Prefab buildings
  (types with a prefab, and all procedural buildings) are instantiated under the cell root with their own LODGroup;
  above 3000 prefab buildings, far ones (beyond 1.5 km) use the box LOD only (no prefab instance). Far surround
  buildings (`out center` points) are boxes merged per 1 km cell, no colliders.
- All layer objects under one root `"Terrain layers (generated)"` with children per layer, so toggling destroys and
  rebuilds just that layer.
- Budget (report measured numbers): Editor Play mode on this PC, "Jammu_AFS" or the densest area: build time for all
  three layers, counts (trees, detail patches, buildings), frame time with all layers on vs off.

## 6. Settings and UI
- New `TerrainLayerSettings` (serializable) inside `TerrainGenConfig` (so it travels in the LAN start packet's
  terrain JSON): `trees, grass, buildings` each `LayerMode { Off, Realistic, Procedural }`; `treeDensity` (per ha,
  typeable, no cap), `grassDensity` (0-1 share is physical: clamp 0..1), `buildingDensity` (procedural villages per
  km2, typeable), `treesBlockSight` (bool), `falloffStartKm` (typeable), `fillGapsProcedurally`
  (bool, default OFF: Realistic shows only what OSM / the satellite estimate has; ON adds procedural buildings in 500 m
  cells that OSM left empty, labelled "filled procedurally" in the source line and the session file). Defaults: trees Realistic, grass Procedural,
  buildings Realistic on real terrain; Procedural for all on procedural terrain (Realistic needs OSM data).
- TrainerMenu TERRAIN tab: a "Layers" block with three rows of three-way buttons `OFF | REALISTIC | PROCEDURAL` and
  the density fields (NumberEntry). If Realistic is chosen for an area without `_layers.json`, show "No OSM data for
  this area yet: Realistic uses satellite colour for trees and grass, no buildings" (and do exactly that).
- In the field, F6 trainer panel: the same three toggles under the terrain line; a change rebuilds that layer live
  (show "Rebuilding trees..." for the frames it takes; spread big builds over frames with a coroutine).
- Credit: when any OSM data is shown, the trainer views and the HTML report carry "Map features © OpenStreetMap
  contributors (ODbL)".
- Session file: record the three modes and the data source actually used ("OSM", "satellite estimate", "procedural",
  "off").

## 7. Interaction with the rest
- Detection already uses `Physics.Linecast` for line of sight, so buildings and trees with colliders hide drones
  automatically. The crowd's pick uses the same test.
- The post's own Site (watchtower, cantonment) stays; the building layer keeps clear of the Site's footprint.
- Terrain following for the crowd uses the terrain height only (buildings ignored); fine for the 15 m floor in DS-1;
  note in TODO if tall OSM buildings exceed it (crowd floor could read the building cells later).

## 8. Self-test `Chaukanna.TerrainGen.LayersSelfTest`
1. Overpass JSON parse on a small embedded sample: buildings, levels -> height, tree nodes, forest relation with an
   inner hole.
2. Lat/lon -> local metres matches the terrain's own conversion at the area centre and corners (< 0.5 m).
3. Footprint extrusion: closed, outward normals, triangle count = expected for a concave L-shape; holes respected.
4. Point-in-polygon scatter puts no tree outside its forest and none in a hole.
5. Satellite classification on a synthetic image (dark green -> tree, light green -> grass, grey -> none).
6. Procedural scatter: same seed same result; different seed differs; no building in the Site footprint or on steep
   ground; no tree above the tree line.
7. Density fall-off monotonic with distance.
8. Kit: empty building list for a type -> extrusion; filled -> prefab; missing tree prefab -> placeholder.
9. Toggle: switching a layer Off destroys its root and leaves the others; switching back rebuilds the same counts.
10. Mode / source label honesty: Realistic without `_layers.json` reports "satellite estimate" for trees and grass and
    builds 0 buildings.
All existing self-tests must still pass with their counts (CONTEXT.md list plus Crowd 27, MapOffline 8, Team, Net 11).
