# CONTEXT.md: everything a fresh session needs

Written 2026-10-16 for the move to a new PC. A new Claude Code session has no memory of earlier sessions; this file is
that memory. Read with CLAUDE.md (rules), TODO.md (queue), PROGRESS.md (history, newest first), HANDOFF.md (the move).

## 1. The project
- Team of 6, Smart India Hackathon, problem statement 26247: "AI-Enabled Drone & Counter-Drone Threat Simulation
  Trainer" (Ministry of Defence / DSSC). Solution name **Chaukanna**; the Unity project folder is **Chanakya**.
- Owner and git identity: Utkarsh Kale (GitHub UtkarshKale), repo https://github.com/UtkarshKale/Chanakya (branch main).
- Prototype scope = "Module 1": one demo field scene (MixedSky) with a trainer screen and a player screen, plus the
  TrainerMenu scene. Spec sources:
  - `Assets/Chaukanna/Docs/solution-v2-council.md`: the solution document (PS coverage R1a-R9, scoring tree §5.4,
    display contract §1.7, demo script §12, data schemas §5.7). Copied into the repo on 2026-10-16.
  - `Assets/Chaukanna/Docs/module-1-demo.md`: the agreed Module 1 spec (the demo scene). Key demo moments: the ROE card
    flip, the AI cue wrong once, the red decision-tree path when an own drone is engaged. Note: much has since been built
    beyond it (LAN, full scoring, learner model) at the owner's request; its "faked" list is now partly real.
  - `unity-module-plan.md` was REJECTED by the owner as over-engineered. Do not follow it.
- Hardware target: GTX 1650 to RTX 3050 class laptops/desktops. Quest Link is not supported on a GTX 1650.
- VR is a core feature: a simulated VR mode (mouse = head, hands, wrist tablet, binoculars by raising both hands) is
  built. Real-headset OpenXR support is deferred until the team knows which headset it gets (needs a Packages change and
  the owner's yes).
- No paid or request-only datasets. Air-gapped target: LAN-only multiplayer, offline map data shipped with the build,
  an offline local AI scenario builder later (needs the owner's OK to download a local model).

## 2. The owner's rules and working style (also in CLAUDE.md)
- Only `Assets/Chaukanna/` + root docs. No ProjectSettings / Packages / Library / URP settings / template edits unless
  the owner approves that exact change in their own words. No new packages, layers or tags without a yes.
- Anything needing the owner's hands in the Editor: stop, "NEEDS USER:" numbered beginner steps with concrete Unity
  menu paths.
- Git: author Utkarsh Kale only; no Claude name, no Co-Authored-By, no AI trailer; commit / push only when asked.
- Dev loop: build one feature, give hand-test steps, the owner tests and says something like "works lets work on next";
  confirm each next step; never call work done until verified in the running scene; progress as short visible keywords.
- Trainer numbers are typeable with no upper caps. Keep TODO.md / PROGRESS.md current, including every deferred idea.
- Keys in `C:\Users\<user>\.chaukanna\` (carto.key, arcgis.key), never in git. The CARTO key was once pasted into a chat:
  the owner was told to rotate it.
- Latest owner message wins on build order. Critical-thinking partner (no sycophancy, weakest link first, one hard
  question at the end) until the owner says "stand down".

## 3. Decisions already made
- Scoring depends on the ROE card in force (cards A / B / C with a severity matrix; old cards Weapons Hold / Under
  Attack still work). The score is never shown to the player; only the trainer's dashboard shows it.
- Decisions can be changed by re-selecting (call any time; action unless it physically happened); changes are scored.
- Full scoring is ON: decision clock with release points per class (placeholders), correct rejection, search /
  perception failures, unverified engagement, process (on the player's belief) x outcome grid, report and engage d'/c with
  a 20-trial gate. Each contact still tops out at 8 points.
- AAR lives in the trainer panel (SCORE dashboard): replay with "your call vs the sound call" (an annotated replay of the
  logged run, not a re-simulation) and a one-file HTML report carrying "Training use only. Not for ACRs or selection."
- Terrain: real GIS (AWS Terrain Tiles elevation, Sentinel-2 cloudless 2016 imagery, OSM map, CARTO labels with a key)
  plus procedural; builds bundle `ChaukannaData/` (map tiles + downloaded areas) next to the exe.
- LAN: trainer PC hosts; trainees join by IP; modes SOLO (same scenario, own sky) and TEAM (one shared sky, host
  authoritative); Netcode named messages only, no relay or internet.

## 4. What is built (details and verification in PROGRESS.md)
Field and player: contact spawner (seeded), detection with line of sight and apparent size, binoculars, lock, 3D lasso
(hold Tab), multi-select (Ctrl+click, sweep, G, X), classify 1-5, Hold / Report / Jam / Engage, ROE banner and cards
A/B/C with scheduled or random flips, AI cue, sensors panel (RF / sound, degradable), day / dusk / night / NV, fog,
difficulty levels, blend-in (deceptive) hostiles, swarms and bird flocks (boids), urban / rural site (watchtower,
cantonment, fields), soldier walking, simulated VR mode (F7), true-size display (screen calibration), Range drill.
Trainer: TrainerMenu tabs TERRAIN / SCENARIO / MAP / EDITOR / BOARD / LOBBY / CREDITS; F6 split-screen panel with truth
map (and trainer map lasso), feed, START; SCORE dashboard (stats, DECISION QUALITY, LEARNER MODEL, decision tree with 5
checkpoints, timeline, REPLAY, every contact, trends, XCharts tab, HTML report button).
Systems: full scoring, learner model (logistic, pooled) + adaptive plan at 75 %, prevalence (Low/Medium/High) + null
missions + Quiet watch with vigilance thirds, Scenario Director (training only), scenario editor form + validator
V1-V4/V6-V9 + packages on disk, proficiency board (pseudonymous ID + PIN, reference packages only, intervals,
not-yet-trained, duplicate-timing KS check), LAN multiplayer (lobby, SOLO/TEAM, back to lobby, area transfer, lost-host
banner), terrain generator (real / hybrid / procedural, tiles, painter, satellite drape, 20 km horizon surround),
OSM map picker with search and draggable radius, area downloads, offline bundling for builds.

## 5. Verified vs not
- Every self-test passes (list below). Most features were also checked live in Play mode by script.
- Hand-tested by the owner: early player features (lock, binoculars, HUD), charts, terrain generation, the first LAN
  build (which found the pink-terrain bug, now fixed).
- NOT yet hand-tested by the owner (see TODO "Hand-test" items): lasso look, urban/rural look, VR keys and frame rate,
  dashboard DECISION QUALITY / LEARNER / REPLAY on screen (replay green-lines fix), EDITOR and BOARD tabs, Range drill HUD,
  true-size view, two-PC LAN (Solo, Team, back to lobby, area transfer, lost host), the new build after the pink fix.

## 6. Open decisions waiting for the owner
- Sentry / commander roles in TEAM mode. Proposed: SENTRY searches, locks, classifies and reports (1-5, R), cannot jam
  or fire; COMMANDER sees all sentries' reports (list + map) and is the only one who can Jam / Engage / Hold. Until the
  owner answers, every seat can do everything.
- Poly Haven ground textures and scattered rocks: asked, no yes yet (download needs approval).
- Minimum-visibility time before a contact counts for scoring (raised earlier; not decided).
- Commit state: everything is committed and pushed up to e7f703b except the deliberately uncommitted files listed in
  HANDOFF.md; the handoff docs are committed locally (not pushed) at the time of writing.

## 7. Remaining queue (owner's order; TODO.md has everything)
1. Sentry / commander roles (after the owner's answer).
2. Sensor desk / per-channel sensors and structured AI cue (PAUSED by the owner).
3. UI redesign with a scenario library (search, filters): deferred until the scenarios are built.
4. Real-headset VR (OpenXR + XR Interaction Toolkit): waits for the headset model and a Packages yes.
5. Offline local LLM for the AI scenario builder (fills the EDITOR form from a sentence): needs the owner's OK to download.
6. Realistic night vision; ESA WorldCover surface types for the terrain painter.
Also open in TODO: FPV and manned-aircraft classes and models, jam / engage sounds, counterfactual re-simulation, company
one-page summary and signed export, Hindi debrief, cohort-based bands, ArcGIS key, built-player frame rate on a GTX 1650.

## 8. Self-tests (run after every change)
Run each with `unity command eval_file --file t.cs`, where t.cs is one line such as
`return "x=" + Chaukanna.Scoring.ScoreSelfTest.Run();` (each also logs a "[Name] n/m passed" summary to the Console).
| Class | Checks |
|---|---|
| Chaukanna.Scoring.ScoreSelfTest | 77 |
| Chaukanna.Scoring.FullScoringSelfTest | 36 |
| Chaukanna.Scoring.ReportSelfTest | 19 |
| Chaukanna.Scoring.LearnerSelfTest | 14 |
| Chaukanna.Scoring.PrevalenceSelfTest | 13 |
| Chaukanna.Trainer.TrainerSelfTest | 51 |
| Chaukanna.Trainer.DashboardSelfTest | about 15 |
| Chaukanna.Trainer.PackageSelfTest | 27 |
| Chaukanna.Sensors.SensorSelfTest | 21 |
| Chaukanna.Environment.EnvironmentSelfTest | 18 |
| Chaukanna.TerrainGen.TerrainGenSelfTest | 93 |
| Chaukanna.Player.VrSelfTest | 22 |
| Chaukanna.Player.LassoSelfTest | 16 |
| Chaukanna.Scenario.SwarmSelfTest | 12 |
| Chaukanna.Scenario.SiteSelfTest | 9 |
| Chaukanna.Scenario.RangeSelfTest | 12 |
| Chaukanna.Net.NetSelfTest | 9 |

## 9. Technical notes worth knowing
- Unity CLI 1.0.0-beta.8 + package `com.unity.pipeline` 0.8.0-exp.1 drive the open Editor. An unfocused Editor freezes
  at frame 2 and IMGUI does not paint, so overlays need the owner's eyes; step frames with `EditorApplication.Step()`.
- Never use the CLI's `set_timescale` (rewrites ProjectSettings/TimeManager.asset).
- Session flow: static `SessionSetup` + `FieldBootstrap` (scene-loaded hook) builds the terrain and starts the run in
  MixedSky; the loading screen runs from the menu's START.
- Terrain: layers A (source DEM) / B (procedural displacement) / C (surface rules + imagery); WGS84 local metric;
  heightmaps 2^n+1; max 4 paint layers per tile; TerrainData must be saved before alphamaps.
- Downloads: AWS Terrain Tiles (Terrarium PNG, zoom 13, no key); OSM tiles + Nominatim with User-Agent
  "Chaukanna-SIH-trainer/0.1 (counter-drone training simulator)"; Sentinel-2 (EOX, CC BY 4.0); CARTO labels with
  `?key=` from `~/.chaukanna/carto.key`; ArcGIS imagery optional via `~/.chaukanna/arcgis.key`.
- Map tile cache: `%USERPROFILE%\.chaukanna\tiles` (about 75 MB now). Downloaded areas: in the Editor
  `Assets/Chaukanna/Terrain/RealData/`, in a build `<persistentDataPath>/RealData` plus the bundled
  `<build>/ChaukannaData/RealData`; LAN-received areas `<persistentDataPath>/ReceivedAreas`.
- Saved data (persistentDataPath = `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Chanakya`): sessions/, reports/,
  learner/, range/, packages/, roster.json, display.json.
- LAN command line for tests: `Chaukanna.exe -chkName S-014 -chkJoin 127.0.0.1 -chkReady [-chkTestBot]`.
- Built-game pink: runtime-created objects must use `Chaukanna.RuntimeMaterials` (materials in Resources).
- Netcode: the NetworkManager must be a root object (NGO refuses a nested one); no networked objects are used.
