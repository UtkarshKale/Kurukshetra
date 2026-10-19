# Plan: Dense sky (DS-1) and Extreme mixed-sky swarm (DS-2)

Planned 2026-10-16 (Opus). Built by a Sonnet agent from this file. Rules: CLAUDE.md (only `Assets/Chaukanna/` + root docs;
no ProjectSettings / Packages / layers / tags; runtime-created materials through `Chaukanna.RuntimeMaterials`;
`System.Environment.X` always fully qualified; IMGUI only).

## 1. The training problem
Real airspace near a post will hold many cooperative drones (own ISR, civil delivery, agriculture / survey, light shows)
and birds. The few hostiles hide in that crowd. The soldier must find them without engaging friends. The module trains:
search in clutter, using IFF / Remote ID as fallible evidence, behaviour cues (off-route, heading for the post), and
restraint (no fratricide).

## 2. Architecture (the performance trick)
Today every contact is a GameObject with its own material copies, a ContactMotion, a ContactRecord with two samples per
second, and per-frame detection with a Physics.Linecast. That does not scale past a few hundred.

New: a **crowd** layer for cooperative traffic (FriendlyISR, CivilianQuad, Bird looks only; never hostile).
- **Data, not GameObjects.** `NativeArray<CrowdAgent>` (blittable struct). No per-agent managed objects.
- **Closed-form motion.** Each agent's position is a pure function `Pos(agent, t)` of its pattern parameters and the
  scenario time. No integration, no neighbour search, no drift. Consequences: deterministic from the seed; every LAN PC
  builds the identical crowd from the start packet (zero bandwidth for thousands of drones); the replay can recompute
  any instant; a promoted agent continues exactly on its path.
- **Burst jobs.** `CrowdUpdateJob : IJobParallelFor` (Burst) computes position, yaw, alive flag, frustum test and
  projected pixel size per agent; a single-threaded Burst `CrowdBucketJob : IJob` compacts visible agents into
  per-bucket `NativeArray<Matrix4x4>` lists. Burst 1.8, Collections 2.4 and Mathematics 1.3 are already in the project
  as Netcode dependencies (auto-referenced by Assembly-CSharp): no package change.
- **Terrain following in Burst.** At run start sample `TerrainBuilder.GroundHeight` into a `NativeArray<float>` height
  grid (256 x 256 over the square of side 3 x spawn ring radius around the post; NaN -> 0). The job samples it
  bilinearly and keeps every agent at least 15 m above ground.
- **Rendering.** `Graphics.RenderMeshInstanced(in RenderParams, Mesh, submesh, NativeArray<Matrix4x4>, count, start)`
  in chunks of at most 1023. `ShadowCastingMode.Off`, `receiveShadows = false`. Two LODs by projected size:
  - LOD0 (> 6 px): the class's model, as a list of parts (mesh, submesh, material, local matrix) taken from the
    prefab once at run start (MeshFilter + MeshRenderer pairs; skip SkinnedMeshRenderer). Do NOT combine meshes
    (model meshes may not be readable in a build). Matrix per part = agentTRS * partLocal.
  - LOD1 (0.3 to 6 px): a tiny procedural mesh per silhouette (quad: flat X cross; fixed-wing: cross of fuselage and
    wing; bird: shallow V), about 12 triangles, dark grey, one instanced material from `RuntimeMaterials.Lit`
    (copy, `enableInstancing = true`).
  - Below 0.3 px or outside the frustum or beyond the sensor's effective range: not drawn.
  - LOD0 materials: copies of the prefab's materials with `enableInstancing = true`; if a material's shader is
    null or unsupported, a copy of `RuntimeMaterials.Lit` with the material's colour.
  - Cameras: draw for the player camera only (RenderParams.camera = the player camera), so the trainer views stay
    clean; binocular zoom changes FOV, so the LOD naturally switches to models through the optic.
- **Pixel size.** `px = size / (distance * 2 * tan(vfov/2)) * screenHeightPixels` with the camera's current FOV.

## 3. Traffic patterns (closed form, all in `CrowdMath`, Burst-compatible static methods using Unity.Mathematics)
Every agent: `pattern, cls, variant, centre (float3, y = altitude above ground at centre), axis (unit float2), radius,
length, lanes, speed, phase, dir (+1/-1), spawnT, endT, size, iff`.
1. **Patrol orbit** (own ISR): circle of `radius` round `centre`, angular speed `speed / radius * dir`.
2. **Delivery corridor** (civil quads): shuttle along `axis` between `centre +- axis * length/2`, triangle wave on
   `phase + speed * t`; lane offset across the corridor by lane index; outbound and return at altitudes 15 m apart.
   2 to 4 corridors per run; most civil traffic lives in them, so corridors look like real traffic lanes.
3. **Survey / agri grid** (civil and own): lawnmower over a rectangle `length x (lanes * spacing)`.
4. **Station keeping** (photography, inspection): small Lissajous (amplitude 5 to 20 m) round `centre`.
5. **Cluster** (light show / formation survey, 20 to 200 agents): a shared anchor flying an orbit; each member has a
   fixed offset on a grid or ring (stored in the agent), rotated by the anchor heading. This is the "huge cluster".
6. **Bird flock / wander**: anchor on a slow Lissajous over a large area (radius 300 to 900 m); members offset with a
   small per-member sinusoid. Birds have no IFF and no Remote ID.
Spawn and end times: most traffic is up from t = 0 (`spawnT <= 0`); 20 % enter or leave during the run (corridor
traffic arriving, ISR going home) so the picture changes.
Yaw from the analytic derivative or `Pos(t + 0.05) - Pos(t)`.

Mix by default (shares of `crowdCount`): own ISR orbits 10 %, corridors 45 %, grids 10 %, station keeping 5 %,
clusters 15 %, birds 15 %. Generated from `System.Random(seed ^ 0x0C0FFEE)` so adding a crowd never changes the
existing plan of hostiles / decoys.

## 4. Config (ScenarioConfig, all typeable, no upper caps; Validate only clamps impossible values)
- `int crowdCount` (0 = off). `float crowdRadius` (metres; 0 = 1.5 x spawn ring radius).
- `bool iffAid = true` (the I key works). `float friendlyNoIffPercent` (transponder off / no Remote ID among
  cooperative drones; default 5). `float hostileSpoofPercent` (hostiles that answer IFF with a valid-looking reply;
  default 0, Extreme 30).
- `float crowdShareBirds` etc. are NOT needed in the config; the mix is fixed in code (documented) for now.
Add to `Warning()`: above 20 000 crowd tracks, "may be slow". Add `crowdCount` to the trainer's typed fields in BOTH
the in-field F6 panel (TrainerScreen) and the TrainerMenu Scenario tab, next to the swarm fields, with the same
NumberEntry pattern the other counts use.

## 5. Finding and promoting a crowd drone
- **Track numbers.** When `crowdCount > 0`, the spawner gives the planned contacts AND the crowd agents ids from one
  shuffled permutation of `1 .. plan.Count + crowdCount` (seeded), so a number never tells crowd from planned. With
  `crowdCount == 0` the ids stay exactly as today (self-tests depend on it).
- **Pick.** `CrowdSky.Pick(Camera cam, Vector3 aimDir, float maxAngleDeg, ContactSensor sensor)` returns the agent
  index nearest the aim within tolerance that passes the SAME detection test as a contact (range <= effective range,
  apparent size >= threshold scaled by FOV, in viewport, `Physics.Linecast` line of sight; Linecast only for the best
  few candidates). Uses the positions the job wrote this frame (Complete the job first).
- **Promote.** `CrowdSky.Promote(index)` -> spawns a real Contact through `ScenarioSpawner.SpawnSingle(cls, cls, pos,
  pos, 0, lookSeed, id)` (add an overload that skips the despawn-on-arrival motion), disables its ContactMotion
  movement (set `Frozen = true`; jam still works because ContactMotion checks `jammed` before `Frozen`), adds a
  `CrowdFollower` that sets the transform from `CrowdMath.Pos(agent, ScenarioTime)` every frame until jammed, and
  despawns it ("ExitedSector") at `endT`. Marks the agent removed in the arrays. Sets `Data.fromCrowd = true`.
  Promoted contacts are scored like any other (an engaged friendly is fratricide).
- **Hooks.** `ContactSensor.AtCentre()`: if a crowd agent is nearer the aim than the best live contact, promote it
  and return the new contact. `LassoSelect`: crowd agents inside the loop (cone + depth band, same test) are promoted
  up to `MaxLassoPromote = 48` nearest the post; the HUD says "48 of N in the loop: tighten the loop". `G` (select all
  in view) stays planned contacts only.
- **Team client.** If `LanSession.TeamClient`, do not promote locally: send `CrowdPick(index)` to the host (MP-1 adds
  the message; in DS-1 leave a clear TODO hook `CrowdSky.RemotePromote` and skip promotion on team clients).

## 6. IFF / Remote ID (I key) and route cue
- `I` challenges the locked contact or every selected one. Reply after 0.6 to 1.4 s (seeded per contact), shown on
  the lock label and logged to the record: `iffChecked, iffTime, iffReply`.
  - FriendlyISR: "IFF: VALID (own)" unless in the transponder-off share -> "IFF: NO REPLY".
  - CivilianQuad: "REMOTE ID: civil, operator <code>, route filed" unless no-Remote-ID share -> "NO REMOTE ID".
  - Bird: "NO REPLY" (so "no reply" is not proof of hostile).
  - Hostile: "NO REPLY"; spoofers (hostileSpoofPercent) -> "IFF: VALID (own)". A blend-in (deceptive) hostile spoofs
    until its anomaly, then "NO REPLY".
- **Route cue** on the lock label (what a real air picture shows): crowd drones "ON FILED ROUTE (corridor / patrol /
  survey)"; hostiles "NO FILED ROUTE"; a blend-in "ON PATROL ROUTE" until the anomaly, then the existing anomaly line.
- With `iffAid == false` the I key says "IFF not available".
- Scoring is NOT changed by IFF in DS-1 (keep all 77 / 36 checks). The AAR gets the facts: per-record IFF reply and
  time; a crowd summary.

## 7. Crowd summary, trainer views, AAR
- `CrowdSky.Summary`: total tracks, peak airborne, promoted (checked) count, IFF challenges, friendlies engaged /
  jammed (records with `fromCrowd` and an Engage / Jam), shown in the trainer feed at the end and on the dashboard
  (one line in the session block) and in the HTML report (one line). Add matching plain fields to SessionResult
  (`crowdTracks, crowdChecked, iffChallenges, crowdFriendliesHit`).
- F6 truth map: draw the crowd into a `Texture2D` (e.g. 256 x 256, RGBA32, point filter) rebuilt at 4 Hz with
  `SetPixels32` (one pixel / 2 x 2 dot per agent, class colour, dim), drawn with ONE `GUI.DrawTexture`. Never one IMGUI
  call per agent.
- Replay: crowd not drawn in DS-1 (TODO item: recompute from the closed form).

## 8. Presets and Extreme (DS-2)
Code-defined presets appended to the trainer lists (do not edit scene files): add `BuiltInPresets.All()` returning
ScenarioConfigs; `TrainerScreen.PresetCount / PresetName / LoadPreset` and the TrainerMenu preset buttons include them
after the asset presets.
1. **"Crowded Sky - Find the Foe"**: day, clear, crowdCount 1500, hostiles 3 (quad + fixed-wing), friendly 0, civil 0,
   birds 0 planned (the crowd supplies them), card C start, flip to B at a seeded time, iffAid on, 5 % no-IFF
   friendlies, duration 420 s, rural site.
2. **"Extreme - Mixed Sky Swarm"**: dusk, hazy, sensors degraded, crowdCount 4000, hostiles 4 with blend-in 50 %,
   swarm 48 at 150 s, birdFlocks 4 x 20, ROE card C -> A (random time), iffAid on, friendlyNoIff 12 %,
   hostileSpoof 30 %, duration 600 s, urban site, difficulty "Extreme".
`Data/Difficulty/Difficulty_Extreme.asset` (a new DifficultyProfile asset in Assets/Chaukanna/Data/Difficulty, created
with the CLI `create_asset` or an Editor script): contacts x1.5, speed x1.25, blend-in 50, extra haze 1, min visible
size x1.25, AI error 0.30 / 0.50, degraded 0.6, flip 0.8. Add it to the difficulty lists in the TrainerMenu and
TrainerScreen components IF those lists are serialized in the scenes; if adding it would need a scene edit, instead
load it in code with a fallback when the list has no "Extreme" entry (prefer code; keep scene edits out).

## 9. Performance budget and how to check it
Target: 4000 crowd drones + 150 live contacts at <= 2 ms CPU for the crowd (job + bucket + draw submission) in the
Editor on this PC, and no GC allocation per frame from the crowd (reuse NativeArrays; `Allocator.Persistent`, disposed
in OnDestroy / on restart).
`CrowdSelfTest` (new, `Chaukanna.Scenario.CrowdSelfTest.Run()`), no Play mode needed for most checks:
1. `Pos(agent, t)` deterministic: same seed same positions; different seed differs.
2. Every pattern stays inside its bounds over 600 s (orbit radius, corridor length, grid, cluster offsets).
3. Generation counts match the mix shares (+-1) for 1000 and 10 000.
4. Height grid sampling: bilinear value between corners; above-ground clamp holds.
5. Job output equals the managed `CrowdMath.Pos` for 1000 agents (max error < 1 mm).
6. 10 000 agents: CrowdUpdateJob + CrowdBucketJob together under 2 ms (Stopwatch, after one warm-up for Burst).
7. Bucketing: counts per LOD add up to the visible count; nothing behind the camera is in any bucket.
8. Track-number shuffle: permutation of 1..N, no duplicates; ids unchanged when crowdCount == 0.
9. IFF reply table: own valid / no-reply share, civil Remote ID, bird no reply, hostile no reply, spoof share.
10. Promotion bookkeeping (managed only): promoted agent is marked removed and never drawn or picked again.
Live check in Play mode (script, `EditorApplication.Step()`): start "Extreme - Mixed Sky Swarm", step 120 frames,
read `CrowdSky.LastCpuMs`, the bucket counts, and promote one agent with `Pick` aimed at it; log the numbers.

## 10. Files
New: `Scripts/Scenario/Crowd/CrowdAgent.cs` (struct + enums), `CrowdMath.cs`, `CrowdJobs.cs`, `CrowdSky.cs`
(MonoBehaviour on the spawner's GameObject, created by the spawner when crowdCount > 0), `CrowdRender.cs` (meshes,
parts, materials, draw), `CrowdFollower.cs`, `CrowdSelfTest.cs`, `Scripts/Trainer/BuiltInPresets.cs`.
Changed: ScenarioConfig (fields, Validate, Warning), ScenarioSpawner (create / reset crowd, ids, SpawnSingle overload),
ContactRecord (fromCrowd, iff fields), ContactSensor (AtCentre hook, I key handled in DecisionLoop or a small
`IffChallenge` component, lock label lines), Lasso (crowd candidates), DecisionLoop (I key), TrainerScreen and
TrainerMenu (crowd fields, presets, truth-map texture), ScoreCalculator.SessionResult + ScoreKeeper (summary fields),
HtmlReport + ScoreDashboard (one line each). Keys: I is free (checked 2026-10-16).

## 11. Not in DS-1 (log in TODO)
Crowd in the replay; IFF in the scoring tree (verification credit / penalty); crowd sync over LAN (MP-1); hostile
swarms as crowd data (they stay real contacts, 48 per swarm is fine); per-agent birds flapping (LOD1 only for birds
far away); sound per crowd drone.
