# STATUS.md: what the Kurukshetra prototype does today

**As of:** branch `dev/modules-2026-10-19`, commit `9d95535` (2026-10-19). `main` is still at `29dba3f`.
**Sources:** PROGRESS.md (2026-10-16 to 2026-10-19 entries) and CONTEXT.md section 10. Nothing here was re-run for this file.

**Job of this file:** the one place that says what is built. The design is `solution-v3.md` (same folder). The queue is `TODO.md` and the history is `PROGRESS.md` (repo root). Anyone who changes code updates the matching row here **in the same commit**, and the solution doc never carries build status.

Status words:
- **Hand-tested**: built, and the owner checked it by hand.
- **Built**: built, self-tests pass, maybe checked live by script, never seen by the owner.
- **Partial**: some of it exists.
- **Planned**: designed, not started.
- **Not planned**: deliberately left out.

`F#` = the faculty-input row in `solution-v3.md` §0.2. Sections (§) are solution-v3 sections.

## 1. Core loop and field

| Feature | § / F# | Status | Note |
|---|---|---|---|
| Contact spawner (seeded), detection with LOS + apparent size, lock, classify 1-5, Hold / Report / Jam / Engage | §1.5, F24 | Hand-tested | |
| Fast reaction keys: E = engage all selected, Q = jam all selected, no classification needed | F24 | Built (2026-10-20, compile only) | Works on the locked contact or a whole lasso / G / Ctrl+click selection. Unclassified is scored: CLASS node 0 ("No call"), action and outcome scored as usual; Hold and Report still need a call. VR-sim moved: hold C = radial menu (was E), Z = tablet (was Q). Not run yet |
| Binoculars (60° to 8° FOV, about 7.5x) | §1.15a | Hand-tested | Continuous-zoom EO sight (F31) not built |
| 3D lasso, multi-select | — | Built | Hand test L2 open |
| ROE cards A / B / C with scheduled or random flips, banner + tone | §5.4.4, F18 | Built | Rename to Weapons Free / Tight / Hold (F18) not done; waits on owner Q1 |
| PID rule per card | §1.17a, F11 | Planned | |
| Day / dusk / night, fog, NV (N key) | §1.15c, F32 | Hand-tested (early) | Image-intensifier look (scintillation, halo) not built |
| Scenario spawn and flight options (spawn height range, spawn distance range, sector centre, flight pattern per drone type and for birds, bird roost bearing / distance / height) | trainer request 2026-10-20 | Built (2026-10-20, compile only) | SCENARIO tab and F6 panel. Patterns: straight in, orbit the post, zig-zag, pop-up, terrain-following, loiter, custom path (a path from the 2D editor). Same ScenarioConfig data as the path editor. Not run yet. |
| 2D path editor (waypoints with height and speed per point, spawn points, player posts; one-way / loop / ping-pong; single drones, swarm, bird flocks) | trainer request 2026-10-20 | Built (2026-10-20, compile only) | Menu MAP tab and F6 truth map (EDIT PATHS). Paths save in the scenario and package JSON (older saves load without). Profile strip under the map. Runtime: ContactMotion, SwarmGroup, SwarmSim (Burst goal). Not run yet; spawn points and posts are not saved with a scenario. |
| Swarms + bird flocks (boids) | F9, F13 | Built | Flock count typeable; density parameter Planned. Birds (single, small flock, big flock) are never drawn narrower than 7 px: far away they become a flat silhouette facing the camera (fix 2026-10-20: true-size flat wings were under a pixel thick and vanished). Crowd-pattern birds (CrowdJobs) and the replay do not have this yet |
| Urban / rural site (watchtower, cantonment) | §1.17d | Built | Hand test U1 open. Coastal site (F12) Planned |
| Terrain: generator, real-terrain download, map picker, satellite drape | §1.17d | Hand-tested | |
| Terrain layers: trees, bushes, grass, buildings (OSM or procedural) | §1.17d | Built (2026-10-17; bushes + real art 2026-10-20) | Hand test TL-2 open; Kargil OSM fetch failed (504). Four layers, each Off / Realistic / Procedural |
| Trees by species and height (Quaternius: broadleaf low, pine above 1500 m or on slopes over 20 deg, snow trees above 2600 m, dead trees near the tree line and in dry country) | R14 | Built (prototype, 2026-10-20) | VegetationSelfTest 35/35. Low-poly look: judge on screen. Chaukanna > Terrain > Wire real art into kit fills the kit (already run) |
| Bushes (Quaternius; 2 to 5 round a third of the trees at 3 to 8 m, plus a scatter along wood edges; GPU-instanced, no colliders) | R14 | Built (prototype, 2026-10-20) | Typeable density, budget (default 40 000) and draw distance (default 200 m). Realistic mode is an estimate from trees and cover, labelled so. Rocks are in the kit but not drawn yet |
| Building variety (wall + roof palette per building hash; Kenney houses and blocks in procedural villages) | R14 | Built (prototype, 2026-10-20) | Extruded: one shared material + palette texture. Prefab houses: wall tint by property block (the roof is the model's own colour). Real OSM footprints stay extruded |
| Blend-in hostiles (fly a patrol, then break off) | §1.17c, F15 | Built | Formation shadow and glider Planned |
| Dense sky: thousands of cooperative drones, "Crowded Sky" and "Extreme" presets | §1.17b | Built (2026-10-16) | Hand test DS-3 open; Crowd self-test 26/27 (timing, see TODO H-10) |
| Soldier walking (trainer toggle) | — | Built | |
| Range drill, true-size display (calibration) | v2 §1.5, §1.7 | Built | Hand test H9 open |

## 2. Sensors and identification (solution-v3 §1.15)

| Feature | F# | Status | Note |
|---|---|---|---|
| Sensor panel: RF, sound bearing, AI cue, degradable | F23 | Hand-tested | RF is a bar, not a spectrum view; AI cue uses one error rate |
| IFF / Remote ID challenge (I key, fallible) | F8, F14 | Built (in dense sky, DS-1) | Placeholder rates 12 % silent friendlies, **30 % spoofing hostiles**. The design (§1.15f) asks for spoofing on at most 5 % of hostiles. Reconcile: TODO FR-4. Not in the scoring tree yet (DS-4) |
| Transponder faults on own drones, datalink loss | F8, F14 | Partial | Silent friendlies exist; link loss not built |
| Continuous-zoom EO sight, mil reticle, rate-limited pan / tilt | F20, F21, F31 | Planned | |
| Drone attitude (tilt, bank) as a visual cue | F21 | Planned | |
| Laser rangefinder | F22 | Planned | |
| Thermal sight | F23 | Planned | |
| RF spectrum waterfall, jammer blinds own detector | F23 | Planned | |
| Radar (Desk) | F12 | Planned | |
| Track table with evidence chips + PID counter | §1.15g | Planned | |
| Procedural drone sound / any audio | F5 | Built | SoundEngine rebuilds its pool after a recompile in Play (2026-10-20). Original note: no AudioSource at first. Steam Audio (HRTF) would need the owner's yes |

## 3. Multiplayer, VR, HMI

| Feature | F# | Status | Note |
|---|---|---|---|
| LAN: host / join by IP, SOLO and TEAM, back to lobby, area transfer, lost-host banner | F1 | Built | Run on **one PC** only (Editor host + built client). **Two-PC test not done** (TODO N-L1, MP-2). The pitch headline depends on it (risk K29) |
| Sentry / commander roles, report relay, ReportBoard, chain scoring | F1 | Built (2026-10-17) | Team self-test 27/27, Net 11/11. Hand test MP-2 open |
| Lobby units + roles: add / rename / delete units, multi-select assign unit and role (SENTRY, COMMANDER, ANY, OBSERVER), synced | F1, F17 | Built (2026-10-20, compile only) | Observer takes no action. Not run yet |
| Voice settings (F10): mic mode, PTT key, device, level meter, output volume | F1 | Built (2026-10-20, compile only) | Saved in PlayerPrefs; no on-screen hint. Not run yet |
| Other seats drawn as capsule beans (name / unit tag, binocular state), swappable via IPlayerAvatar | F1 | Built (2026-10-20, compile only) | Animated models Planned (TODO AVA-1b). Not run yet |
| Simulated VR (F7: mouse = head, hands, wrist tablet, radial menu, binoculars by both hands) | F6, F7 | Built | Hand test D16 open |
| Real-headset VR (OpenXR 1.18.0 + XRI 3.6.1) | F6, F33 | **Partial** | Packages installed (owner approved), scripts compile; no XrSelfTest, no build probe, never run on a headset (TODO VR-1) |
| Stereoscopic fallback, IPD | F16, F33 | Planned | Waits on owner Q6 |
| Operator station layout (the "cockpit") | F19 | Partial | HUD, banner, tablet, trainer split screen exist; the §1.20 layout is not built |
| HUD readout of AZ / EL / RNG / ZOOM at sensor toggle | F25 | Planned | |

## 4. Scoring, AAR, black box

| Feature | F# | Status | Note |
|---|---|---|---|
| Full scoring (decision clock, process x outcome, report / engage d' and c with a gate) | §5.4 | Built | Hand test F4 open |
| Learner model, prevalence, Quiet Watch, Director, scenario editor, proficiency board | v2 §1.9-1.11 | Built | Hand test H9 open; validator V5 / V10 only partial |
| AAR replay with slider + "your call vs the sound call" (annotated, not re-simulated) | F17, F26 | Built | Replay green-lines bug open (FIX-2b). Backward-drag scrubber with keyframes and operator graphs Planned |
| Report filter: All players / one unit / one player | F17 | Built (2026-10-20, compile only) | Sessions and contacts carry playerId, seatId and unitId (unit defaults to "Unit 1" until the lobby-units feature sets it). F6 SCORE: SHOW chips (selected player, all players, each unit) drive the cards and charts; HTML report of a multi-player run has a Show drop-down (pre-rendered scopes, a few lines of offline script). Combined views add counts and recompute means from the contacts; trends only for one player. Not run yet |
| HTML report | F17 | Built | Hand test A4 open. Has a Cards / Charts switch (CSS only, no script): the Charts view is the same six charts as the dashboard, inline SVG, hover titles, works offline |
| 3D replay voice: Clean recording / As heard (degraded) | F17 | Built (labels 2026-10-20) | Three named buttons on the replay bar (Voice off, Clean recording, As heard (degraded)), heard = for the selected seat. Live comms degrade (range, noise, dropout, muffle; all seats or one) is in the F6 panel Comms section for the LAN host. Not run yet |
| Dashboard charts (F6 > SCORE > Charts tab, trends section, histogram and trend cards) | F17 | Built (2026-10-20) | One ChartModel + ChartLayout drawn by IMGUI (ChartKit) and by SVG (ChartSvg). Real saved sessions and this run only, never sample points; axis titles with units, legend, value labels, pass-mark / target lines, 3-session average, called-as-by-true-class stacked bars, detection histogram with median, hover tooltips, empty-state text. XCharts is no longer used by the dashboard (package and Charts folder left in place). Stray-line bug fixed: all chart lines go through GuiDraw (clipped). Hand test CH-1 open |
| Black box: view + flags at 2 Hz, decisions | F17 | Partial | 10 Hz operator state, every zoom / sensor toggle / trigger pull: Planned |
| Raw-telemetry download (zip of JSONL / CSV + manifest) | F17 | Planned | |
| Retry (R), back to lobby | F26 | Built | "Practice from this moment" and free arena Planned |

## 5. Architecture and offline (solution-v3 §5)

| Item | F# | Status | Note |
|---|---|---|---|
| Offline run, map tiles and areas bundled in `ChaukannaData/` | F4, F28 | Built | Prefetch of map tiles (CRIT-1c) not run; cable-pulled test not done |
| Fixed 50 Hz sim tick | F29 | Planned | The Architecture doc lists it as a gap |
| LAN snapshot rate | F29 | Built at 10 Hz | The design suggests 20 Hz if two PCs show stutter |
| SQLite event log | v2 §4 | Planned | |
| Interactive technical-approach flowchart (HTML) | F3 | Planned | The diagram is in solution-v3 §5.0 |

## 6. Known problems right now
- CrowdSelfTest 26/27: the 10 000-agent timing check (TODO H-10).
- Nothing painted by IMGUI since 2026-10-16 has been seen on screen (an unfocused Editor does not paint). All new UI waits for the batched hand tests.
- Git author on the machine that made `9d95535` was not set to the owner (TODO H-5).
