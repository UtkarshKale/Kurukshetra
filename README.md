# Kurukshetra: counter-drone recognition and decision trainer

Smart India Hackathon 2026, problem statement **PS 26247**: "AI-Enabled Drone & Counter-Drone Threat Simulation Trainer"
(Ministry of Defence / DSSC). Team **Araxes**.

Kurukshetra is a Unity simulator that trains the soldier who first sees the drone: **detect, identify, decide**. A
trainer builds a scenario; the trainee at the post searches the sky, locks contacts, uses binoculars, IFF and sensors,
classifies them and acts under the rules of engagement in force. Every step is scored, and the after-action review
shows exactly where seconds or decisions were lost. It runs offline on a unit PC, on a LAN without internet, and in VR.

> Unity project name: `Chanakya`. Code namespace and folder: `Chaukanna` (the project's earlier name).

---

## What it does

**Field (player)**
- Drones at realistic size and range: hostile quad and fixed-wing, own ISR drones, civilian drones, birds; seeded so
  every run differs but can be repeated.
- Search, lock, binoculars (60° to 8°), 3D lasso and group selection, classify 1-5, Hold / Report / Jam / Engage,
  fast keys (Q jam all, E engage all).
- Rules of engagement cards A / B / C that can flip mid-mission, so the right answer changes.
- Sensor panel: RF, sound bearing, a deliberately fallible AI cue; degraded-sensor mode.
- Day, dusk, night with night vision, haze and fog; difficulty levels up to Extreme.
- Swarms and bird flocks (boids), blend-in hostiles that fly an own-drone patrol and then break off.
- **Dense sky**: thousands of friendly and civil drones (Burst jobs, GPU-instanced, same sky on every PC from the seed)
  with a few hostiles hidden in them; IFF / Remote ID challenge (I key) that can be wrong.

**Terrain**
- Real places: elevation (AWS Terrain Tiles), Sentinel-2 imagery, OpenStreetMap buildings, trees and land cover,
  20 km horizon; or procedural landscapes. Downloaded once, then fully offline.
- Layers for trees, bushes, grass and buildings, each Off / Realistic (OSM) / Procedural; detailed near, cheap far;
  every asset is a replaceable prefab. Urban and rural posts (watchtower, cantonment).

**Trainer**
- Trainer menu (terrain, scenario presets and typed settings, map, 2D and 3D scenario editors, proficiency board,
  LAN lobby) and an in-field split screen (F6) with a live truth map and event feed.
- Score dashboard and HTML report: decision-tree scoring, decision clock, decision quality, signal-detection
  (d′, c), learner model and adaptive plan, 3D replay ("your call vs the sound call").

**Multiplayer (LAN, no internet)**
- Trainer PC hosts, trainees join by IP. SOLO (same scenario, own sky) and TEAM (one shared sky).
- Roles: SENTRY (search, classify, report) and COMMANDER (sees every report on a list and map, decides Jam / Engage /
  Hold); each link of the chain is scored on the information that seat had.

**VR**
- Simulated VR mode (F7: head, hands, wrist sensor tablet, radial menu).
- Real headsets through OpenXR + XR Interaction Toolkit: **partly built** (see Status).

## Status (honest)
The living status table is [`Assets/Chaukanna/Docs/STATUS.md`](Assets/Chaukanna/Docs/STATUS.md). In short:
hand-tested: core loop, binoculars, terrain, charts, first LAN build. Built and self-tested but not all hand-tested:
dense sky, roles, terrain layers, editors, graphics / controls settings. Partial: real-headset VR (never run on a
headset). Not done: two-PC LAN test, GTX 1650 frame-rate measurement. Placeholder numbers (ROE wording, IFF error
rates, scoring weights) still need a subject-matter expert.

## Requirements
- Windows 10/11, GPU of GTX 1650 class or better.
- **Unity 6000.6.3f1** exactly (URP 17.6, Input System 1.20, Netcode for GameObjects 2.13.3, OpenXR 1.18.0, XRI 3.6.1).
- **Git LFS** (models, textures, audio and terrain data are LFS files).

## Getting started
```bash
git lfs install
git clone https://github.com/arp1tz/Kurukshetra.git
```
1. Unity Hub > Projects > Add > pick the folder > open with 6000.6.3f1 (the first open resolves packages; internet once).
2. Open `Assets/Chaukanna/Scenes/TrainerMenu.unity` and press Play. Pick a scenario on the SCENARIO tab, press START.
3. Build: File > Build Profiles > Windows > Build. Every build gets a `ChaukannaData/` folder (map tiles and downloaded
   areas) next to the exe: copy the whole folder.
4. Optional keys for map labels / sharper imagery live outside the project in `%USERPROFILE%\.chaukanna\`
   (`carto.key`, `arcgis.key`). They are never in git.

### Main controls
| Key | Action |
|---|---|
| Mouse / right click | look / binoculars |
| Left click, Ctrl+click, hold Tab + drag, G, X | lock, add to group, lasso, all in view, clear |
| 1-5 | classify: hostile quad, hostile fixed-wing, own ISR, civilian, bird |
| H R J F | hold, report, jam, engage |
| Q / E | jam / engage everything selected |
| I | IFF / Remote ID challenge |
| N | night vision |
| F6 | trainer split screen |
| F7 | VR mode |
| F10 | settings (graphics, controls, voice) |

LAN test from the command line: `Kurukshetra.exe -chkName S-014 -chkJoin <host IP> -chkReady`.

## Repository map
| Path | What |
|---|---|
| `Assets/Chaukanna/Scenes/` | TrainerMenu (first in the build) and MixedSky (the field) |
| `Assets/Chaukanna/Scripts/` | Scenario (spawner, swarms, crowd), Player, Sensors, Scoring, Trainer (UI, dashboard), Net (LAN), Session, Environment |
| `Assets/Chaukanna/Terrain/` | terrain generator, real-data areas (`RealData/`), OSM layers, layer kit |
| `Assets/Chaukanna/Data/` | presets, difficulty levels, scoring / site / swarm profiles |
| `Assets/Chaukanna/Resources/Chaukanna/` | materials and kits that must ship in every build |
| `Assets/Chaukanna/Docs/` | design (`solution-v3.md`), status (`STATUS.md`), demo script, module plans |
| `CLAUDE.md`, `AGENTS.md` | rules for anyone (or any AI agent) working in this repo |
| `CONTEXT.md`, `HANDOFF.md` | project memory and how to move to another PC |
| `TODO.md`, `PROGRESS.md` | the queue (top = current state) and the dated history |

## Testing
There is no Unity Test Framework package; each area has a static self-test (`Run()` returns true and logs
"[Name] n/m passed"), run from the Editor, for example through the Unity CLI:
`return "x=" + Chaukanna.Scoring.ScoreSelfTest.Run();`. The list and expected counts are in `CONTEXT.md`.

## Working on it (team and AI agents)
Read `CLAUDE.md` first (rules: where files may be changed, no new packages without the owner's yes, keys never in git,
update `TODO.md` and `PROGRESS.md` every round). Then `CONTEXT.md`, the top of `TODO.md` and of `PROGRESS.md`.
Big features are planned in a doc under `Assets/Chaukanna/Docs/` first, then built.

## Data and credits
Terrain Tiles (Mapzen / AWS Open Data), Sentinel-2 cloudless 2016 by EOX (CC BY 4.0), map data © OpenStreetMap
contributors (ODbL), models by Kenney, Quaternius and Poly Pizza authors (see `CREDITS` in builds and the asset
folders). Third-party assets keep their own licences.

**Training use only. Not for ACRs or selection.**
