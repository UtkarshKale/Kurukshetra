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
| Binoculars (60° to 8° FOV, about 7.5x) | §1.15a | Hand-tested | Continuous-zoom EO sight (F31) not built |
| 3D lasso, multi-select | — | Built | Hand test L2 open |
| ROE cards A / B / C with scheduled or random flips, banner + tone | §5.4.4, F18 | Built | Rename to Weapons Free / Tight / Hold (F18) not done; waits on owner Q1 |
| PID rule per card | §1.17a, F11 | Planned | |
| Day / dusk / night, fog, NV (N key) | §1.15c, F32 | Hand-tested (early) | Image-intensifier look (scintillation, halo) not built |
| Swarms + bird flocks (boids) | F9, F13 | Built | Flock count typeable; density parameter Planned |
| Urban / rural site (watchtower, cantonment) | §1.17d | Built | Hand test U1 open. Coastal site (F12) Planned |
| Terrain: generator, real-terrain download, map picker, satellite drape | §1.17d | Hand-tested | |
| Terrain layers: trees, grass, buildings (OSM or procedural) | §1.17d | Built (2026-10-17) | Hand test TL-2 open; Kargil OSM fetch failed (504) |
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
| Procedural drone sound / any audio | F5 | Planned | No AudioSource in the project yet. Steam Audio (HRTF) would need the owner's yes |

## 3. Multiplayer, VR, HMI

| Feature | F# | Status | Note |
|---|---|---|---|
| LAN: host / join by IP, SOLO and TEAM, back to lobby, area transfer, lost-host banner | F1 | Built | Run on **one PC** only (Editor host + built client). **Two-PC test not done** (TODO N-L1, MP-2). The pitch headline depends on it (risk K29) |
| Sentry / commander roles, report relay, ReportBoard, chain scoring | F1 | Built (2026-10-17) | Team self-test 27/27, Net 11/11. Hand test MP-2 open |
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
| HTML report | F17 | Built | Hand test A4 open |
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
