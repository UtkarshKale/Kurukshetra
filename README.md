# Kurukshetra

**A counter-drone threat simulation engine for training soldiers to find, identify and decide.**

Built for Smart India Hackathon, problem statement **PS 26247: AI-Enabled Drone & Counter-Drone Threat Simulation Trainer**
(Ministry of Defence / DSSC).

Kurukshetra puts a soldier at an observation post on real or generated terrain and fills the sky with hostile drones,
friendly patrol drones, civilian drones and birds. The soldier has to pick the threat out of the clutter and decide, under
the current rules of engagement, whether to hold, report, jam or engage. A trainer builds the scenario, watches the run
live, and gets a full after-action review with scores, a decision tree per contact and a 3D replay.

Everything runs offline on a LAN, with no cloud service needed during training.

> Unity 6 (6000.6.3f1) · URP · Input System · Netcode for GameObjects · OpenXR. Code namespace: `Chaukanna`.

---

## Why a simulation engine, not a quiz

Drone recognition is not a picture test. The same quadcopter is easy to spot at 200 m against a clear sky and nearly
invisible at 1.5 km at dusk behind a ridge, next to a flock of birds and two friendly patrol drones. Kurukshetra models
the things that make the real task hard:

- **Line of sight and apparent size.** Terrain blocks detection; a contact is only as visible as its size on screen at
  that range allows.
- **Clutter and deception.** Birds, civilian drones, own drones, swarms of hundreds, and hostiles that fly a friendly
  patrol pattern before breaking off.
- **Imperfect sensors.** RF, acoustic bearing and an AI cue that can be degraded or wrong; an IFF challenge that can be
  silent or spoofed.
- **Changing rules.** The ROE card can change mid-run, so the right action for the same contact changes too.
- **Time pressure.** Every decision is on a clock and scored for both process and outcome.

Every run is seeded, so a scenario can be replayed exactly and compared across soldiers.

---

## Features

### Two dashboards: trainer and soldier
- **Soldier view:** first-person at the post, binoculars (about 7.5x), lock, classify (1 to 5), Hold / Report / Jam /
  Engage, sensor panel, wrist tablet, ROE banner.
- **Trainer view:** a menu with TERRAIN, SCENARIO, MAP, EDITOR, BOARD and LOBBY tabs, plus a live split-screen panel
  (F6) with the ground-truth map, an event feed and the score dashboard.

### Terrain: real places, offline, or procedural
- **Real-world terrain download:** pick any place on a map (search plus a draggable radius); the engine fetches
  elevation, a satellite image draped over the ground, and OpenStreetMap features (trees, buildings), with an 8 km
  surround for the horizon.
- **Offline:** downloaded areas and map tiles are stored locally and bundled next to a Windows build, so the trainer
  runs with no internet.
- **Procedural, seeded terrain:** generated heightfields (real, hybrid or fully procedural), reproducible from a seed.
- **Vegetation and buildings:** species-aware trees by altitude and slope, bushes, grass, OSM building footprints or
  procedural villages, an urban or rural site (watchtower, cantonment).

### Scenario setup
- **Scenario options:** spawn height and distance ranges, sector, flight pattern per drone type (straight in, orbit,
  zig-zag, pop-up, terrain-following, loiter, custom path), bird roosts.
- **ROE cards A / B / C** with scheduled or random changes mid-run, announced by banner and tone.
- **2D path editor:** waypoints with height and speed, spawn points, player posts, one-way / loop / ping-pong, for single
  drones, swarms and bird flocks.
- **3D scenario editor:** free-fly camera to place paths, spawn points, posts, drone and bird groups, and hand-placed
  trees, bushes, buildings and rocks; undo / redo; save as a scenario package and test-run from the editor.
- **One-click DEMO:** a fixed 3.5-minute scenario on the best terrain available, with a presenter checklist.

### Conditions and sensors
- **Day, dusk and night**, fog, and **night vision** (N key).
- **Degraded sensors:** RF, acoustic bearing and an AI cue whose reliability the trainer controls.
- **IFF / Remote ID challenge** (I key) that can fail: silent friendlies and spoofing hostiles.
- **Swarms and bird flocks** (boids), and **Crowded Sky**: thousands of cooperative drones in the air at once, with
  "Crowded Sky" and "Extreme" presets.
- **3D lasso** (hold Tab) and multi-select to act on many contacts at once; fast keys E (engage) and Q (jam).

### Scoring and after-action review
- **Decision-tree scoring:** every contact is scored through detect, classify, decide, act and outcome checkpoints on a
  decision clock, combining process ("did you follow the drill for what you could see?") with outcome, plus signal
  detection measures (d′ and criterion) for report and engage.
- **AAR replay:** a timeline with "your call vs the right call" per contact, and a **3D replay** of the logged run
  with follow, chase, free and top views.
- **Black-box recorder** of view, flags and decisions during the run.
- **HTML report:** a single offline file with score cards and charts, filterable by player and unit.
- **Proficiency board** with pseudonymous IDs.

### Learner model and adaptive difficulty
- A **learner model** tracks accuracy by task, contact class, light and range across sessions and suggests what to
  train next.
- **Prevalence settings** (low / medium / high threat rates, including null missions) and a **Quiet Watch** mode for
  vigilance training.
- A **Scenario Director** adapts a training run live: when the soldier is doing well it adds a wave, a decoy or a sensor
  drop; when they struggle it holds back. It cannot be gamed by playing badly, and every change is logged.

### LAN multiplayer
- Host / join by IP on a local network, **SOLO** and **TEAM** modes, area transfer to every PC, lost-host handling.
- **Sentry / commander roles:** sentries search, lock, classify and report; the commander sees all reports on a list and
  map and is the only one who can jam or engage. The chain from report to action is scored.
- Lobby units and roles (sentry, commander, any, observer), push-to-talk voice with simulated comms degradation.

### VR
- **Simulated VR mode** (F7): mouse as head, hands, a wrist tablet, a radial menu, binoculars held with both hands.
- Real-headset VR on OpenXR is in progress (see the roadmap).

---

## Roadmap

| Planned | What it adds |
|---|---|
| Real-headset VR (OpenXR + XR Interaction Toolkit) | Vendor-neutral support for PC VR headsets; packages and scripts are in, headset testing is next |
| Offline AI scenario builder | Describe a scenario in plain words; a local model (no cloud) turns it into a validated scenario |
| Sensor suite | Continuous-zoom EO sight with mil reticle, laser rangefinder, thermal sight, RF spectrum waterfall, radar |
| Positive identification rule per ROE card | The card states how many independent sensors must agree before engaging |
| Track table | Evidence chips per contact and a PID counter |
| Richer black box | 10 Hz operator state, every zoom and trigger pull, raw telemetry export |
| Coastal site, formation shadowing, gliders | More terrain types and deception tactics |
| Two-PC field test of TEAM mode | Multiplayer has been run on one PC (host and client); the two-PC run is next |

---

## Getting started

1. Install **Git LFS** before cloning (`git lfs install`); models, textures and audio are stored in LFS.
2. Clone this repository.
3. In Unity Hub, add the project folder and open it with **Unity 6000.6.3f1**. The first open resolves packages
   (internet needed once) and imports assets.
4. Open `Assets/Chaukanna/Scenes/TrainerMenu.unity` and press Play.
5. Press **DEMO** (top right) for a ready-made run, or build your own on the TERRAIN and SCENARIO tabs and press START.

### Optional map keys
Map labels (CARTO) and ArcGIS imagery are optional. Keys are never stored in the project: put them in
`%USERPROFILE%\.chaukanna\carto.key` and `%USERPROFILE%\.chaukanna\arcgis.key`, or in the environment variables
`CARTO_API_KEY` and `ARCGIS_API_KEY`.

### Main keys in a run
| Key | Action |
|---|---|
| Right mouse / binoculars | Zoom |
| Tab (hold) | 3D lasso |
| 1 to 5 | Classify |
| R / H / J / F | Report / hold / jam / engage the locked contact |
| E / Q | Engage / jam everything selected |
| I | IFF challenge |
| N | Night vision |
| F3 | Degrade or restore sensors |
| F6 | Trainer panel |
| F7 | Simulated VR |
| F10 | Voice, graphics and controls settings |

---

## Project layout

```
Assets/Chaukanna/
  Scenes/      TrainerMenu (trainer dashboard), MixedSky (the field)
  Scripts/
    Scenario/  contacts, swarms, birds, crowded sky, Director, editors
    Sensors/   RF, acoustic, AI cue, IFF
    Player/    observer, binoculars, lasso, simulated VR
    Scoring/   decision tree, learner model, prevalence, proficiency
    Replay/    black box, AAR, 3D replay, HTML report
    Net/       LAN lobby, roles, voice, settings
    Trainer/   menu, map picker, scenario setup
  Terrain/     terrain generator, real-data import, vegetation, buildings
```

## Credits
3D models and art: Quaternius (Ultimate Nature Pack, birds), Kenney (city kits, particles, sounds, UI),
AllSkyFree skyboxes, game-icons.net, and the model authors named in `Assets/these assets/`. Charts: XCharts.
Map data © OpenStreetMap contributors.
