# TODO.md

## STATE 2026-10-19 (read first)
- Done and self-tested: CRIT-1b map offline, DS-1/DS-2 dense sky + Extreme, MP-1 roles, TL-1 terrain layers. Their hand tests are batched for the owner (DS-3, MP-2, TL-2).
- [ ] VR-1 is PARTLY built (packages installed, scripts exist and compile; no XrSelfTest, no build probe, never run on a headset). Finish it per Docs/plan-vr-openxr.md section 4, then write VR-2 (headset hand test).
- [ ] FIX-2b replay green lines + terrain fallback-tree warning + Kargil/surround OSM re-fetch: spec in Docs/plan-replay-fix.md. Not started.
- [ ] CRIT-1c run Chaukanna > Offline map > Prefetch map tiles (a download, owner's yes) and rebuild.
- [ ] H-10. CrowdSelfTest 26/27 on 2026-10-19: 10 000-agent jobs 7 to 8 ms, was 0.7 ms. Check Burst compilation is on (Jobs > Burst), re-run.
- [ ] H-5 still open: set git author on each dev machine to Utkarsh Kale <utkarshkale.w@gmail.com> (rule 7).
- 2026-10-19 (later): project renamed **Kurukshetra** (code names unchanged). Design = Assets/Chaukanna/Docs/solution-v3.md (council v2 + faculty review). What is built = Assets/Chaukanna/Docs/STATUS.md (update the row with every code change). New queue items from the review: section FACULTY REVIEW below.

Work top to bottom. One task per session. Tick [x] when done and log it in PROGRESS.md.
(plan) = needs Opus 5.5 in plan mode first; the rest are Sonnet 5.5 build tasks.

SCOPE SOURCE: module-1-demo.md (the agreed Module 1 demo; the user rejected the bigger module plan as over-engineered). Its key demo moments are (1) the ROE flip between 2 cards, (2) the AI cue being wrong once, (3) the red decision-tree path when the player engages an own drone: all three exist already. The file is now in Assets/Chaukanna/Docs/module-1-demo.md.
First restructured 2026-10-14 against the solution doc (solution-v2-council.md: §2 PS coverage table R1a-R9, §11.1 M1 scope, §12 demo script). No item was deleted; items were moved and tagged. New items from the gap check are numbered N1 and up.
Tags: [R1a]...[R9] = the PS line it serves. [demo] = one of the 3 key demo moments in §12: (1) cold open, night, own drone engaged, "your call vs the sound call"; (2) the card flip mid-mission; (3) night 24-swarm with a wrong AI cue. Shot 5b is the VR sentry.

## OWNER ROUND 2026-10-20 (items 1 to 4)\n- [x] DQ-1. Process score renamed Decision quality, no-action hostiles count as 0, n shown, n/a when empty.\n- [x] UNIT-1 (compile only; needs a LAN hand test). Individual and unit scores in dashboard and HTML report.\n- [x] OSM-PB-1 (compile only). Progress on the OpenStreetMap fetch button.\n- [ ] SPAWN-1. Player posts and drone spawn points in 2D and 3D, saved, sent over LAN.

## LAN SYNC FIX 2026-10-20 (owner: drones not visible on the friend's PC)
- [x] LAN-1. Trainee saw no drones: spawns sent while its field was loading were lost (FieldReady handshake + full resync), snapshot/pose origin was each PC's own post (now the host's origin in the snapshot header, world frame for poses), Director off on a team client. Compile only.
- [x] LAN-2. Big swarms and bird flocks (> 64, SwarmSky) on a trainee: host streams agents (SwarmSnapshot), trainee draws; picks go to the host (SwarmPick). Compile only.
- [ ] LAN-3. Proxy contacts do not animate (ContactMotion off): wing flap / rotor spin on a trainee's PC. Cosmetic.
- [ ] LAN-4. Two-PC hand test of LAN-1 and LAN-2 (host + friend): contacts visible from the start, same place on both screens, swarm with more than 64 drones (Scenario > Swarm size 200) and a flock with more than 64 birds show on both.
- [ ] LAN-5. Per-agent swarm stream is full-state; if bandwidth matters for tens of thousands, send only agents near the trainee's view or delta-code them.

## 3D EDITOR IN A BUILD 2026-10-20
- [x] ED-B1. Hand-placed objects drew nothing / pink in a build for models whose material has no instancing (rock FBX): now a Resources Lit copy. Open failure is caught and logged. Compile only.
- [ ] ED-B2. Build and open the 3D editor in the built game (steps in PROGRESS 2026-10-20); read Player.log for "[3D editor] Open" or "Could not open" if something is wrong.
- [ ] ED-B3. Foliage with alpha cut-out on a model material loses its cut-out in the Lit copy (opaque quads); only matters for a kit model that is not instancing-flagged.

## LATER CHANGES 2026-10-20 (owner notes, recorded here so none is lost)
- [x] LC-1. ROE banner and player-visible "placeholder" strings removed (2026-10-20, compile only): see PROGRESS.
- [x] LC-2. 3D replay fixes (2026-10-20, compile only): see PROGRESS. Time base checked (same scenario clock for seats, contacts, groups and events; no offset found); real causes found: group sampling on even ticks only (a slow frame rate in a big sky could skip groups), straight-line gaps over 0.5 s not interpolated, promoted swarm agents drawn twice, the same person recorded under two seat ids, 9 s trails for every bird, flat cyan cubes for dense-sky agents, magenta / purple and dark-blue colours, labels for every contact.
- [ ] LC-2a. Hand-test LC-2 (owner): play a run with a swarm, a flock and the dense sky, open 3D REPLAY, check markers move smoothly and stay on the labelled contacts, scrub and change speed, one Seat button, trails short and fading, click a contact, Buildings OFF.
- [ ] LC-2b. Deferred (nothing deleted): the foreground "huge flat boxes" are the real OSM buildings (LOD box meshes close to the post), not a replay scale bug; the Buildings switch hides them. If they look wrong in the live game too, fix it in the layers (LOD thresholds / footprint boxes). Per-agent class for dense-sky groups in the black box (needs a file-format change); a trail fade with real alpha (needs a vertex-colour shader); black-box groups above 64 sampled agents.
- [x] LC-3. One-click DEMO (2026-10-20, compile only): DEMO button, curated scenario, presenter checklist, AAR button on the end screen, Docs/DEMO-SCRIPT.md. See PROGRESS.
- [ ] LC-3a. Hand-test LC-3 (owner): press DEMO on the Editor, then on a build copied to a second PC (with ChaukannaData); time it against DEMO-SCRIPT.md.
- [ ] LC-3b. Deferred (nothing deleted): the demo on a LAN (host runs DEMO for a team); a DEMO button inside the F6 in-field panel; auto-open the AAR dashboard after N seconds; a recorded fallback run; a second demo scenario at night / dusk with the dense sky.
- [ ] LC-a. The 3D editor once failed to open in a build (owner: likely a one-off glitch). Recheck on the next build; collect Player.log ("[3D editor] Open" / "Could not open") if it recurs. Open failures are already caught and logged (ED-B1).
- [x] LC-b. In a build, "Back to the 3D editor" after "Test run from here" did nothing. Cause (from code): a click anywhere in the player's view locks the mouse (ObserverLook), so the press on the button locked the cursor and the release never reached the button. Fixed 2026-10-20: ObserverLook ignores presses over the button, the bar draws on top, F9 also goes back. Compile only; needs a build test (LC-b1).
- [ ] LC-b1. Hand-test LC-b in a build: Test run from here, then click "Back to the 3D editor (F9)", and also press F9.
- [ ] LC-c. OPTIMISATION LAST: distance optimisation looks like a glitch (pop-in, far silhouettes, LOD / density cut-offs). Make it subtle (fade in, larger start distances, consistent far look, no sudden pop) AFTER the owner picks the graphics quality presets, which then set the asset render distances. Not started.

## FACULTY REVIEW v3 (added 2026-10-19; design in Docs/solution-v3.md, recommended order in its section 11.5; the owner sets the real order and confirms before each big step)
Waiting on the owner first: design questions Q1-Q6 (solution-v3 header notes; defaults = the recommendations).
- [ ] FR-1. [R3] Two-PC LAN test (same as N-L1 / MP-2). The pitch now leads with local multiplayer, so this comes first. Needs a second PC and the owner's hands.
- [ ] FR-2. [R6] Rename cards A / B / C to Weapons Free / Tight / Hold, letters kept in brackets (waits on Q1). Text and data only.
- [ ] FR-3. [R7] Black box: 10 Hz operator state (view az/el, FOV/zoom, sensor in use, binoculars, VR head pose, lock, card) + every action (zoom from/to, sensor toggle with az/el/zoom, lock, lase, interrogate, mark, classify, report, decisions, every trigger pull, jammer on/off) + "Download raw telemetry" (zip: events.jsonl, operator_state.csv, world_truth.csv, scorecard.csv, manifest.json with hashes). solution-v3 section 1.18.
- [ ] FR-4. [R1b][R2b] IFF interrogator as one model for the dense-sky I key and normal contacts: valid / no reply / wrong code, own-drone transponder faults, datalink loss; scoring (valid reply engaged = Critical; "no reply" alone under Tight = verification lapse). RECONCILE: the dense sky's placeholder "30 % spoofing hostiles" vs the design's "at most 5 %" (section 1.15f); ask an expert. Overlaps DS-4 (IFF in the scoring tree).
- [ ] FR-5. [R1a][R1b] EO sight: continuous zoom 1-30x (placeholder), mil reticle, pan 60 / tilt 30 deg/s limits, contrast lock that also grabs birds and clutter (no truth leak). Binoculars stay.
- [ ] FR-6. [R1b] Laser rangefinder on the EO sight: slant range, ground distance, height, closing speed; 1 mrad beam, background returns when the target does not fill the spot.
- [ ] FR-7. [R1b][R6] Track table with evidence chips (EO, TH, RF, AC, RD, IFF) and a PID rule per card (min channels, AI cue never counts). No fused "HOSTILE" verdict.
- [ ] FR-8. [R1a][R2c] Procedural drone sound (blade-pass harmonics per class, rotor beating, RPM from ContactMotion, Doppler, travel delay R/343 s, air absorption, ambient masking) with Unity's built-in 3D audio. Steam Audio HRTF needs the owner's yes (package). Same area as P2 / N7.
- [ ] FR-9. [R2a] Night vision as an image-intensifier look (phosphor, scintillation, halo, circular mask); scan lines only on an analog video feed style (waits on Q4). Same area as "Realistic night vision" in CONTEXT section 7.
- [ ] FR-10. [R2a][R1b] Thermal sight: per-renderer heat values via MaterialPropertyBlock (no new layers), white-hot / black-hot, low resolution, birds warm, gliders cold.
- [ ] FR-11. [R2b] RF spectrum waterfall (sub-GHz, 2.4, 5.8 GHz), bearing error, fibre FPV silent, own jammer blinds own RF detector.
- [ ] FR-12. [R1b] Deception: formation shadow (hostile inside a friendly formation, then peels off) and stealth glider (silent, RF-silent, near ambient on thermal). Blend-in (D11) is the template.
- [ ] FR-13. [R7] AAR scrubber: drag backward and forward via 1 s keyframes, step a frame, jump to event markers, operator graphs (camera az, zoom, sensor, trigger pulls). Builds on the replay slider (after FIX-2b).
- [ ] FR-14. [R5] Starter progression presets: Avian Wildlife (birds only, often no hostile, Weapons Hold), Patrol Spur (own patrol + a hostile that shadows then spurs off, Tight), Single Rogue Infiltrator (one low / slow or glider hostile, dusk / night, Tight to Free).
- [ ] FR-15. [R2b][R2c] Radar Desk sensor (plots, 1 s scan, micro-Doppler rotor / flap hint, bird and sea clutter, terrain masking) + coastal site (waits on Q2). Large.
- [ ] FR-16. [R1c] Effector failure modes: jam failsafe per class, GNSS jammer hits own drones too, small-arms P(hit) by range and crossing speed, AD-gun collateral over the cantonment, laser DEW later.
- [ ] FR-17. Operator station layout (compass strip, weapon-control banner, sensor stack, track table, action bar, AZ / EL / RNG / ALT / ZOOM readout) = the "UI redesign later" item (waits on Q3, Q5).
- [ ] FR-18. [R4] Stereoscopic fallback (Cardboard side-by-side; anaglyph optional) and per-user IPD (waits on Q6). Low training value at range; do last.
- [ ] FR-19. [R3] Cable-pulled offline test: unplug every PC except the LAN switch and run a full session (proves "zero runtime network dependencies").
- [ ] FR-20. Re-enter the arena: "Practice from this moment" (unscored, from the nearest keyframe) and an unscored free arena.
- [ ] FR-21. Interactive technical-approach flowchart (HTML) generated from solution-v3 section 5.0, for the PPT.
- [ ] FR-22. Drone attitude as a visual cue (quads tilt toward acceleration, fixed wings bank to turn) in ContactMotion.
- [ ] FR-23. Bird density parameter (birds per km2), typeable, no cap.

## TERRAIN LAYERS (user, 2026-10-17: trees, grass, buildings with Realistic / Procedural modes; plan in Assets/Chaukanna/Docs/plan-terrain-layers.md)
- [x] TL-1. (BUILT 2026-10-17, see PROGRESS.md; hand test TL-2 below) Trees, grass and buildings, each Off / Realistic / Procedural. Realistic = OpenStreetMap (Overpass, fetched once per area, `<area>_layers.json` beside the area; raw answers `_osm.json`, `_osm_surround.json`) with a satellite-colour estimate for trees and grass where the map has nothing; Procedural = seeded. Terrain trees (LOD prefab, trunk colliders block sight), grass drawn by an own instanced renderer, buildings extruded from footprints per 250 m cell with MeshCollider + LOD boxes, far buildings as merged boxes. Kit asset `Resources/Chaukanna/TerrainLayerKit_Default.asset` + placeholders (menu Chaukanna > Terrain > Build layer placeholders). Menu TERRAIN tab block, F6 row, session file and HTML report line + credit. LayersSelfTest 58/58.
- [ ] TL-2. Hand-test TL-1: (1) Trainer menu > TERRAIN tab, scroll to "Trees, grass and buildings": three rows OFF / REALISTIC / PROCEDURAL, the number boxes (trees per ha, grass cover 0-1, villages per km2, tree budget, tree draw distance, fall-off km) accept typing; on a Pure procedural terrain REALISTIC is greyed. (2) Press Use on Jammu_AFS, Area, ladakh, telaviv, ter: the line "OpenStreetMap data fetched 2026-10-17: N buildings ..." appears; on Kargil the amber "No OSM data for this area yet" line and the button "Fetch map features (OpenStreetMap) for this area" (needs internet; it works through curl.exe). (3) START telaviv: loading text "Trees, grass and buildings: ...", then the field has real building footprints (red roofs), trees, grass near the post. (4) F6: the row "Trees: REALISTIC | Grass: PROCEDURAL | Buildings: REALISTIC" cycles Off > Realistic > Procedural; while it rebuilds the line says "Rebuilding trees..."; the OpenStreetMap credit shows under it. (5) Line of sight: stand behind a tree trunk or a building, the contact behind is not detected (and is when you step out). (6) Urban / rural Site on a layered terrain: no tree or building inside the cantonment, the field strip or within 60 m of the post. (7) Finish a run: the HTML report ends with "Terrain layers: ..." and "Map features (c) OpenStreetMap contributors (ODbL)". (8) Download a new area from the map: the last step is "Map features (OpenStreetMap)". (9) LAN: a trainee PC that has no copy of the area receives `<area>_layers.json` and shows the same buildings. (10) Frame rate in a built game on the GTX 1650 with all three layers on (Area is the heaviest), then tune "tree draw distance" and "tree budget". (11) Drag your own tree / building prefab into the kit asset (nose / front +Z, pivot at ground, 1 unit = 1 m) and see it in the field.
- [ ] TL-3. Deferred from TL-1 (nothing deleted): (a) OSM fetch gaps: Kargil play area, and the surround of Jammu_AFS and telaviv, answered HTTP 504 twice (Overpass busy); re-run Chaukanna > Terrain > Fetch OSM layers (Kargil), and force the surround for the other two, when the server is quieter (the owner's yes is needed beyond the two tries). (b) Unity's own TLS (UnityTLS) cannot complete a handshake with overpass-api.de (TLS 1.3 only), so the fetch falls back to Windows' curl.exe; a trainer PC without curl.exe cannot fetch (a TLS 1.2 mirror or a bundled client would fix it). (c) Grass: Unity terrain detail layers did not draw at all in this project's pipeline (a minimal test terrain too), so GrassField draws instanced clumps; no wind, no shadows, no per-clump colour, blade texture is a placeholder; check on a focused Game view that terrain detail layers really do not draw, and if they do, decide which to keep. (d) Trees: every terrain tree costs frame time whatever its distance (about 0.1 to 0.6 ms per 1000 in the Editor on this PC); the tree budget (60 000) and the tree draw distance (LOD bias 0.2) are a compromise; an own far-forest impostor renderer would allow 6 km views. (e) The Editor lost its GPU (D3D11 device removed, "Unity Error" dialog, Editor exited) when I rendered 345 000 terrain trees in a loop; do not run heavy camera loops on this PC. (f) Satellite colour thresholds (excess green 0.05, dark luma 0.30, texture 0.07) are untuned against ground truth; no NDVI (needs near infrared); roofs, shadows and snow read as none. (g) More OSM: building:colour / roof:shape / roof:colour, tree species, height, crown diameter, natural=scrub, water and rivers, roads, bridges, walls and fences, landuse=residential to place procedural fill. (h) Procedural buildings only in the play area (no procedural surround villages); Realistic buildings use the kit's prefabs only when kit.realisticUsesPrefabs is on (not exercised with real art). (i) The raw OSM answers are big (ladakh 8 MB): they are excluded from builds and from the LAN transfer, but sit in the project folder; consider LFS or .gitignore. (j) F6 truth map does not draw buildings or trees. (k) Contacts, crowd and spawn points ignore buildings: crowd drones follow terrain only (15 m floor, tall OSM towers exceed it) and a spawn point can land inside a building; the crowd floor could read the building cells. (l) TerrainGenWindow has no area download, so no OSM step there. (m) Layer settings are not in the EDITOR tab packages or scenario presets; a trainer's F6 change is not sent to trainee PCs. (n) Far buildings over 80 000 are thinned, trees stop at 2 000 000, grass cover is coarse (8 m, more on huge areas). (o) Menu and F6 UI were compiled and logic-tested, not looked at on screen (IMGUI does not paint in the unfocused Editor). (p) Tree colliders: the API has no per-tree switch, so "Trees block the line of sight" off uses collider-free copies of the prefabs; a tree prefab needs its own collider on the root to block sight.
## ORDER OF WORK (user, 2026-10-16 evening: HIGH PRIORITY, plan with Opus 5.5 high, build with Sonnet 5.5; paused by the user until "continue development")
Approved by the user in chat 2026-10-16: OpenXR Plugin + XR Plug-in Management + XR Interaction Toolkit packages; headset is a PC-tethered / SteamVR one the user calls "Festo" (exact model unknown), so target generic OpenXR and keep it switchable; TEAM roles: SENTRY (search, lock, classify 1-5, report R; no jam/fire) and COMMANDER (sees every sentry's reports as list + map; only one who can Jam / Engage / Hold), commander delay scored as its own link (doc §5.4.7). Hand tests are batched: the user tests everything at once at the end.
- [x] CRIT-1b. Map empty / offline in a copied build (2026-10-16). Cause: a build ships only the tiles cached on the PC that made it (this PC: 9 OSM tiles at zoom 12). Fix: the map draws the shipped areas' own satellite pictures (play area + 20 km surround) where it has no tile, outlines and names every shipped area, enlarges a coarser cached tile where the exact one is missing, and stops re-trying the network for 30 s at a time when offline. Editor menu Chaukanna > Offline map > Prefetch map tiles around downloaded areas fills the cache before a build (downloads only when clicked; not run yet). MapOfflineSelfTest 8/8.
- [ ] CRIT-1c. Run the prefetch menu once on a PC with internet (needs a yes for the download, about 1500 tiles at most), rebuild, and check the map on a PC with no network.
- [x] DS-1. (BUILT 2026-10-16, see PROGRESS.md; hand test DS-3 below) (plan) Dense-sky friend-or-foe module: hundreds to thousands of cooperative drones (own ISR, civil delivery corridors, agri / survey grids, clusters, bird flocks) and very few hostiles. Design so far: crowd drones are data, not GameObjects; positions in closed form from seed + time (Burst jobs; same sky on every LAN PC from the seed, so no extra network traffic); GPU instancing with screen-size LOD (model near, low-poly speck far, no shadows); a coarse height grid for terrain following; a crowd drone becomes a real contact only when the player locks or lassoes it (lasso promotion capped); track numbers shuffled so an id never tells crowd from planned; IFF / Remote ID challenge (I key) with fallible replies (transponder-off friendlies, spoofing hostiles in Extreme) plus a route-conformance cue; trainer map draws the crowd as a texture, not thousands of IMGUI calls; crowd summary in the AAR (checked, challenged, friendlies engaged). Burst, Collections and Mathematics are already in the project.
- [x] DS-2. (BUILT 2026-10-16) Extreme difficulty: "Extreme - Mixed Sky Swarm" preset (thousands of crowd tracks, several hostile swarms, blend-in hostiles, birds, night or dusk, fog, degraded sensors, ROE flips) and a Difficulty_Extreme level.
- [ ] DS-3. Hand-test the dense sky (look of the crowd and the tiny silhouettes on screen, pick / lasso feel, I key and lock label, the truth-map crowd dots, presets "Crowded Sky - Find the Foe" and "Extreme - Mixed Sky Swarm", Crowd fields on the F6 panel and the SCENARIO tab, frame rate in a built game on a GTX 1650).
- [ ] DS-4. Deferred from the dense-sky plan (section 11): crowd in the replay (recompute from the closed form); IFF in the scoring tree (verification credit / penalty); crowd sync over LAN (done in MP-1); hostile swarms as crowd data; per-agent bird flapping; sound per crowd drone. Also: crowd fields in the EDITOR tab form (ScenarioSpec) and package JSON; promoted contacts take their spawn time from the moment of promotion (the AAR does not know when the drone first flew); a rule to keep the route cue and IFF reply from making hostiles trivial (tune after the hand test).
- [x] MP-1. (BUILT 2026-10-17, see PROGRESS.md; hand test MP-2 below) Multiplayer roles: SENTRY / COMMANDER seats (Lobby role button, refusal of H/J/F for a sentry, key panel), report relay sentry > host > commander(s) with the commander's ReportBoard (list, mini-map, brackets, T / Shift+T), scoring of the chain (REPORT node, report-to-decision, first-fault waiver, report-alone lapse, hand-off fratricide), teammate's kill no longer a clock miss (handledBy), host score no longer polluted by trainees' decisions, crowd sync on team clients (host clock, CrowdPick, crowdIndex), O(n^2) despawn scan and per-frame FindAnyObjectByType removed.
- [ ] MP-2. Hand-test MP-1 on two PCs: (1) Lobby: TEAM, role buttons cycle SENTRY / COMMANDER / ANY; all-sentry shows "No commander: nobody can jam or fire". (2) Sentry PC: H / J / F show the refusal for 2 s and are greyed, R works, key panel says ROLE: SENTRY. (3) Commander PC: report list, mini-map dots (colour = REPORTED call), amber brackets, T and Shift+T slew in under half a second and lock. (4) Commander jams a reported hostile; the sentry's contact is not a clock miss; dashboard roster shows the role and the second line (reports and lock-to-report / report to decision). (5) Crowded Sky: a sentry clicks a crowd drone, it turns into a track for everyone within a moment, crowds match on both PCs. (6) HTML report "Team" line. (7) A late report: commander's miss waived, AAR says so. (8) Feel of the 0.3 s slew and of the board layout at 1080p and in VR-sim.
- [ ] MP-3. Deferred from MP-1: team client crowd uses the host's clock but each PC has its own post, so crowd heights come from each PC's terrain (fine on one seed, check on real terrain); a commander seat on a client PC (reports are forwarded and handled, only the host-commander path was run live); the board is not drawn in the replay; Handled message is only sent for effects that landed (a miss or a jam on a fixed-wing is not "handled" by design); ReportBoard keeps only the last 10 lines on screen; commander bot does not use the T key path on the host; clock drift between host and client is only logged (a late-joining client always shows it); roles in Solo mode are ignored by design; saved session files do not yet record who reported what beyond the contact result fields; role-based dashboard charts; voice or text chat between sentry and commander.
- [x] UI-1. CREDITS tab hidden from the TrainerMenu tab bar on request (2026-10-17). Code kept: TrainerMenu.ShowCredits = false; set it to true to bring the tab back (it is the last tab, no index moves).
- [ ] VR-1. (plan) Real-headset VR on OpenXR + XRI (approved): XR rig behind the existing view-rig switch (desktop / VR-sim / OpenXR), controller binoculars, lock, radial menu, tablet; switchable to any OpenXR runtime; frame-rate check.
- [ ] FIX-2b. Replay green lines still look wrong to the user (later pass, after the modules).
## ORDER OF WORK (user, 2026-10-16: learner model + adaptive difficulty, prevalence + Quiet Watch, Range drill + true-size display, Scenario Director + editor + proficiency board; then LAN multiplayer; sensor desk stays paused; UI redesign later)
## ORDER OF WORK (user, 2026-10-14)
1. Swarm. DONE 2026-10-15.
2. Urban / rural. DONE 2026-10-15 (cantonment edge, Site toggle on the menu and F6, "Day Single Quad - Rural"; Night Swarm - Urban uses the urban site).
3. Then the paused module list, in this order:
   - [x] Full scoring (module 4). DONE 2026-10-15: ON by default (ScoringRules.fullScoring; off = classic tree). Decision clock with release points per class (strike quad 150 m, recce 300 m, loiter limits 90 / 120 s; non-hostile 30 s in view or when it leaves; all placeholders), clock miss priced by the card in force at the deadline, correct rejection, search / perception failures, unverified engagement, light-dependent near-miss, process on belief, outcome, process x outcome grid (with 'Pending' for results not known yet), report / engage d'/c with the 20-trial gate. Dashboard: DECISION QUALITY section and per-contact N1a / N1b / clock / process; tree: fifth checkpoint DECISION vs RESULT, 'Clock ran out' and 'Left alone' branches. FullScoringSelfTest 36/36, ScoreSelfTest 77/77. Still open: F1-F4 below.
   - Sensor desk (module 7): per-channel failures and a structured AI cue.
   - [x] AAR replay ("your call vs the sound call") + HTML report export (module 8). DONE 2026-10-15 (prototype: annotated replay of the logged run, not a re-simulation; see N6 / N17).
   - Learner model + adaptive difficulty (module 9).
   - Prevalence control + Quiet Watch (module 5).
   - Range drill (module 1).
   - Calibrated true-size display (module 6).
   - Scenario Director pacing (module 10).
   - Scenario editor, form and validator (module 11; the offline LLM builder needs the user's go-ahead for a local model runtime).
   - Sim proficiency board (module 12).
   Skipped for now: LAN (waits on the networking choice), real-headset VR (waits on the headset).
- [x] Module 3: ROE cards A / B / C (2026-10-14).
## Demo-critical (a PS line or a key demo moment depends on it)
- [x] CRIT-1. (FIXED 2026-10-16: every build now gets ChaukannaData/ next to the exe with the map tiles and areas; areas recorded with the trainer PC's paths are found there; the offline message names the cause.) [all] CRITICAL (user, 2026-10-16): after exporting a build and copying it to another PC, the trainer map said "Map offline" and did not work. Investigate after the modules are finished. Things to check: (a) MapPicker sets Offline when a tile request returns ConnectionError, so any failed fetch (no internet, firewall/proxy, TLS or certificates, OSM refusing the request) shows that message; log the actual error. (b) The CARTO labels key lives outside the project in %USERPROFILE%\.chaukanna\carto.key, so it is missing on a new PC. (c) Downloaded areas, tile caches and real-terrain data may point at the dev PC's paths (persistentDataPath vs the project's Assets/Chaukanna/Terrain/RealData, which is not in a build). Fix: make the build self-contained (ship the downloaded areas and a tile cache in StreamingAssets or a data folder next to the exe), fall back to them with no network, and show a clear message naming the real cause.
- [x] L1. [R3] 3D lasso selection (2026-10-15, user request): hold Tab, drag a loop round drones; Shift add, Ctrl remove; mouse wheel / Alt+wheel set a depth band; only contacts the player can see right now are picked; works through binoculars and in VR mode. Trainer: drag a loop on the F6 truth map round contacts and site objects, count + DELETE + CLEAR. LassoSelfTest 16/16.
- [ ] L2. Hand-test the lasso (look of the loop and fill, cursor feel with the mouse captured, Tab not clashing with anything, the trainer map lasso).
- [ ] U1. [R2c] Hand-test urban / rural: look of the town and watchtower, whether buildings hide drones as expected, frame rate with 774 site objects.
- [ ] U2. [R2c] Site sound: town clutter vs open fields (needs the jam / engage sound work first).
- [ ] M1. [all] Hand-test everything built this session (see PROGRESS) and fix what breaks. The score dashboard's look has never been seen on screen by the builder; check layout, text size and the charts first.
- [ ] M18. [all] Hand-test the new input paths: Ctrl+click and sweep select, G, X, changing a resolved decision, and the look of the tree, loading screen and charts picture (none can be seen from the command line).
- [ ] D16. [R4][demo 5b] Hand-test VR mode keys (B, both buttons, Q, E menu, F7) and measure frame rate (72 fps minimum for comfort).
- [x] N1. [R2d][demo 3] Swarm: groups of 8 and 24 arriving together (done with S1), one spawner call per group (today every contact is planned on its own; custom spawn points only bunch them roughly).
- [x] S1. [R2d][demo 3] Swarm (2026-10-15): boids (separation with priority, alignment, cohesion, target) in Scripts/Scenario/Swarm.cs with a SwarmProfile ScriptableObject; any swarm size (8 / 24 / 48 tested) arriving from one bearing on the normal detection and scoring path; bird flocks on the same code with slower flaps; seeded from its own random stream; trainer settings Swarm size / Swarm at s / Bird flocks / Flock size; "Night Swarm - Urban" preset; simple triage score. Not yet: split / feint / formation tactics.
- [ ] N3. [R1b] FPV class (RF and fibre-optic) with its model. The spec's class list is quad, FPV, fixed-wing + own ISR, civil, bird, manned. Today: quad, fixed-wing, own ISR, civil, bird. (Loitering munition moved to N3b under Should have.)
- [ ] S4. [R1b][R6] Manned-aircraft safety (new contact class; engaging it is -10 under every card). The scoring rule already exists in data.
- [ ] P1b. [R1b] Manned aircraft: the Helicopter and Small Airplane are .glb only. Re-download as OBJ from Poly Pizza (or approve the glTFast package), then add the contact class (S4).
- [ ] P1c. [R1b] Only one quad model and one fixed-wing model exist, so hostile and friendly/civilian differ only by the added cue (payload box, antenna). Get the FPV racing drone and a second fixed-wing for real variety; a Shahed-style delta wing is still missing.
- [ ] P2. [R1c] Sound and effects pass (muzzle flash, engage, jam); better UI look. Doc: jam ring, AD burst, "report sent" tick; there is no audio at all yet (no AudioSource anywhere).
- [ ] N7. [R2b][R1a] Drone sound per class with direction (3D audio; Steam Audio HRTF in the doc), audible only above ambient; acoustic masking near the generator / town. Sensors today: one sound bearing on the panel, no audio.
- [x] M3. [R2c] Ground/terrain pass: the user's sandbox + textures stay; procedural terrain done earlier; rural/urban post switch done 2026-10-15 (Site: NONE / RURAL / URBAN).
- [ ] G6. [R2c] Urban and rural scenario terrain presets; pick real locations for the demo. (Site builder done; real demo locations still to pick.)
- [x] N10. (2026-10-15: Scripts/Scenario/Site.cs + Data/SiteProfile_Cantonment.asset; urban = watchtower post inside a walled cantonment with buildings, water tank, trees; rural = post on open ground, cantonment behind; SiteSelfTest 9/9. Not seen by eye yet. No sound changes yet.) [R2c][demo 1] Cantonment edge on the plains map: perimeter walls, water tank, generator, tree lines, a few buildings, so "urban" changes clutter and sound. Today there are no buildings, walls, trees or vegetation in any scene and no rural/urban switch.
- [ ] G4. [R2c] Vegetation and props on the generated terrain.
- [x] N11. (2026-10-15: all three exist and are in both scenes' preset lists; the "graded package with one Start button" part is left for the scenario library UI.) [R5] The three spec presets by name: "Day Single Quad - Rural", "Night Swarm - Urban", "Mixed Sky - Own Drones Up" (only the last exists, plus "Quiet Mixed Sky" and "Mixed Sky - Under Attack"), as graded packages with one Start button.
- [ ] N13. [R3] Built Windows player measured on a GTX 1650-class PC: 60 fps with p95 >= 58 (doc §6.2). Nothing has been measured outside the Editor.
- [ ] T21. [R3] Built player: add TrainerMenu and MixedSky to Build Settings (a ProjectSettings change, so the user does it).
- [ ] P4. [demo] Demo video: record at 1080p or higher, add a zoom inset; script in module-1-demo.md section 5 (and solution doc §12).

## Should have (improves the demo; no PS line depends on it)
- [ ] N25. Trainer UI redesign for many modules: scenario library with search and filters (user, 2026-10-14). Details to be confirmed with the user.
Moved here from Demo-critical on 2026-10-14 (not in the module-1-demo.md scope; they come from the larger solution-v2 design):
- [ ] N2. [R2d][R6][demo 3] Swarm triage scoring node (time to recognise saturation, priority order, leakers) instead of 24 separate rows (doc §5.4.6).
- [ ] N3b. Loitering munition class (split from N3 on 2026-10-14; not in the module-1-demo scope).
- [x] N4. (Module 3 built 2026-10-14: cards A/B/C with the doc's severity matrix in ScoringRules, scheduled or random change, banner + tone, scored card-change node.) [R6][R8][demo 2] ROE cards A/B/C (placeholder wording, editable) with a mid-mission card change node: adaptation time and stale-card errors scored. Today: Weapons Hold / Under Attack with one optional flip; the flip is not scored as its own node.
- [x] M4. (2026-10-14: banner names the card, flashes and plays a tone; trainer feed note; dashboard card-change line.) [R6][demo 2] ROE flip surfaced in the trainer UI as a clear event (banner flash already exists). Add the audio tone the doc asks for.
- [ ] N5. [R1c] Protective decision beside the effector decision (alarm + stand-to / take cover + disperse / none), logged and scored (doc §1.5, §5.4).
- [~] N6. (2026-10-15: positions, on-screen flags and the soldier's view are logged twice a second; dashboard REPLAY section and the HTML replay play the run back; every contact gets "your call" vs "the sound call" (least escalatory Sound action for the card, by the deadline) and a modelled consequence. This is the doc's K10 fallback; a true re-simulation from a snapshot is still open.) [R1c][R7][demo 1] Counterfactual replay in the AAR: "your call" vs "the sound call" from the same moment (needs the run logged as state + inputs, not just a seed).
- [ ] N8. [R2b] Sensor health per channel: RF jammed / absent, acoustic masked, AI cue reliable / degraded / bad; fibre FPV shows nothing on RF. Today one switch: Sensors OK / DEGRADED.
- [ ] N9. [R9][demo 3] AI cue with a structured error model: P(correct) by range bin, light and look-alike pair (doc §1.9), seeded per contact. Today: one error rate, wrong labels drawn at random.
- [ ] T5. [R5] Scenario builder routes: scripted, procedural, form-based (LLM-assisted builder is for the full product, not the demo).
- [ ] N12. [R1a] Display contract, minimum version: calibrated vertical FOV from screen size and distance, and "in view" / "displayed-detectable" times per contact in the log (doc §1.7a, d). Today detection uses an apparent-size threshold at 60 deg FOV, not a calibrated view.
- [ ] D19. 'Subtle detail' hybrid adds only about 0.6 m RMS on flat ground (6 m noise, 120 m features): decide if it should be stronger.
- [ ] M5. Ammo / burst limit for Engage? (open question)
- [ ] M6. (Never-detectable contacts are now left out of the max, 2026-10-14; ScoringRules.excludeNeverDetectable.) Late-spawn and unresolved contacts: decide how they count (currently: unseen late = MISSED 0 pts, no penalty).
- [ ] M7. Should following a wrong AI hint cost points? (open question; data is logged already)
- [ ] M8. Real ROE rules for Weapons Tight / Weapons Free and "Report on a hostile under Under Attack" (needs subject-matter expert).
- [ ] M9. Confirm the hold-delay numbers (15 s blocks, -1 each, cap -3, hostiles under Under Attack only). They are the builder's defaults, not from the spec.
- [ ] M10. Hold-delay time assumes at most one ROE flip per run. Track time under each card properly if runs get several flips.
- [ ] M11. Placeholder identification cues (payload box, camera ball, antenna mast) are a stand-in: decide the real cues.
- [ ] M12. Nothing happens when a hostile reaches the post except a log line. Decide the consequence (score, end of run, effect).
- [ ] M15. Fog distance is tied to the sensor's visible range (hazy 60%, foggy 35%). Confirm those shares, and whether fog should also cut the RF / sound sensors (it does not now).
- [ ] M19. Find out which objects the user still sees as placeholders (the spawn data and every spawn path checked use models). Candidates: the grey cue parts (payload box, antenna, camera ball), the engage tracer / burst effects, the post marker, the flat Ground.
- [ ] M20. Confirm the new scoring numbers with a subject-matter expert: second guess -1, right-to-wrong -1, replaced action -1 (cap -2), deception caught +1; blend-in shares 0 / 10 / 30 %; break-off after 25 to 70 s at 1.35x speed.
- [ ] M21. Blend-in hostiles have one anomaly (leaving the orbit for the post). Add others: RF signature change on the sensor panel, wrong IFF reply, loitering over the post.
- [ ] M22. Group select has no drag box (the mouse is captured for looking); sweep-select and G stand in for it. Add a box if a free-cursor mode is wanted.
- [ ] M24. Models dragged into the lists are auto-scaled but not auto-turned: the nose must point along +Z. Add a per-model yaw offset if needed.
- [ ] N14. Prevalence as a scenario parameter (share of hostiles: low / medium / high) and null missions allowed in training runs (doc §1.6, R8).
- [ ] N15. Seed families: isomorphic variants per trainee (mirrored axis, +-15 % timing) so neighbours cannot share answers (R8).
- [ ] N16. Scenario Director for training runs (pacing from performance, logged injections, off in tests) (R9).
- [x] N17. (2026-10-15: Scripts/Scoring/HtmlReport.cs; written to <persistentDataPath>/reports/ at the end of every run and from the dashboard's "HTML report" button; one self-contained file, no network, dark/light, A4 print stylesheet (replay hidden in print). ReportSelfTest 19/19. Not done: the one-page company summary, signed export, Hindi.) AAR as a static HTML report and a one-page print (doc §1.11, R7).
- [ ] P-Urban. Towns and the airport have no buildings or runway: the data has none. Add OSM building footprints / runways as simple shapes if needed for a base scenario.
- [ ] S3. Night recognition (naked eye / NV), degraded-sensors scenario, single-contact identification, friendly-drone deconfliction.
- [ ] S7. Boids for the birds in Mixed Sky too (they wander individually now), reusing the swarm boids.
- [ ] T1. Two-monitor mode (trainer canvas on Display 2, fall back to split-screen). Needs the trainer UI rebuilt on a canvas.
- [ ] T4. (plan) Hybrid randomized mode: scenarios mixed so the trainee doesn't know what's coming; each with difficulty adjustment.
- [ ] T6. Saved session history feeding the trend charts; sample history for the demo (captioned "sample data"). Partly done: the dashboard reads the last 10 saved sessions and pads with labelled sample points.
- [ ] T9. Typed seed field and a real preset dropdown on the trainer panel (now -/+ buttons and preset buttons, because typed digits would hit the player's 1-5 keys).
- [ ] T11. Dashboard draws at most 60 contacts on the timeline and tree list. Add paging or filtering for large runs.
- [ ] T12. Decide whether the run should pause while the trainer has the dashboard expanded (it keeps running now; player input is ignored while expanded).
- [ ] T13. Difficulty numbers (Data/Difficulty/) are the builder's defaults; Trained equals the pre-difficulty behaviour. Confirm Recruit and Expert with a subject-matter expert.
- [ ] T14. The recommendation uses the last run only. Consider averaging the last few sessions and keeping the level per player once there is more than one.
- [ ] T15. Trainer panel is getting crowded (map is small in split-screen). Rework the layout when the trainer UI moves to a canvas (T1).
- [ ] T19. Trainer menu and in-field trainer panel duplicate the scenario controls; share one implementation when the UI moves to a canvas (T1).
- [ ] T20. Drone spawn points are plain points; add spawn arcs / sectors and per-class spawn points.
- [ ] T22. Soldier walking is a simple ground-follower (no jump, crouch, slope limit or collision with objects). Upgrade to a CharacterController when there is something to collide with.
- [ ] T23. Typed number boxes exist for whole numbers only; sliders (terrain weights) and text paths are unchanged. Tab between boxes is Unity's default behaviour, not tested.
- [ ] T25. XCharts needed one line changed to compile on Unity 6000.6 (SerieLabelPool.cs, EntityId cast). Re-apply it if XCharts is updated.
- [ ] T8. Truth map through an orthographic camera and render texture. Needs a dedicated layer, which means a ProjectSettings change.
- [ ] G2. Online DEM download tested end to end (OpenTopography needs the user's free API key; Copernicus and NASA need accounts). (Keyless AWS Terrain Tiles download works since D17.)
- [ ] G3b. ESA WorldCover class raster driving the surface layers (trees, grass, crops, built-up, bare, water).
- [ ] G5. Contour lines and terrain shading on the trainer's truth map.
- [ ] G9. Run a real elevation file through the tool (pass 1 was proven on synthetic files only). Needs the user to pick a location and supply or approve a download. (Done in practice for Jammu_AFS and Kargil via the downloader; a user-supplied .hgt/.asc file is still untested.)
- [ ] G10. 16-bit PNG heightmap import (not built; RAW, HGT and ASC are).
- [ ] G11. Areas that cross two elevation tiles: load and stitch several files.
- [ ] G14. The truth map and sound / RF sensors ignore terrain; decide whether hills should mask them too.
- [ ] P1d. The pigeon is a standing cartoon bird with no flying animation and the jay has no texture (white). Find flying bird models with a flap clip.
- [ ] P1e. Environment art (nature pack, city kits), effects (particle pack) and sounds (Sonniss, Kenney) are downloaded but not used yet.
- [ ] P1f. Delete the nature pack's Blends folder: Unity logs 'Blender could not be found' for every .blend file.
- [ ] P5. Team workflow: decide Git + LFS or Unity Version Control for six people; everyone on Unity 6000.6.3f1. Note: nothing built so far is committed anywhere; it exists only on this machine. (Since 2026-10-14 the work up to f1948c0 is on GitHub main.)
- [ ] P6. Get the professor's (Pasarkar) approval for VR scope if VR stays in.
- [ ] P7. Proper EditMode tests once the Unity Test Framework package is allowed; the self-tests (scoring, sensors, trainer, dashboard) stand in for now.

## Polish
- [ ] OPT-1. Spawn and flight options built 2026-10-20 (compile only): hand-test (PROGRESS.md "Polish 9"). Follow-ups: patterns for swarms and flocks (they use a path or the roost today); a pattern for Director and crowd contacts; per-type spawn height instead of one range; show the spawn height / distance band on the F6 map; LAN clients only see proxies so patterns are the host's.
- [ ] PATH-1. 2D path editor built 2026-10-20 (compile only): hand-test it (see PROGRESS.md "Polish 8"), then check a 5000-agent swarm on a loop path for frame time. Follow-ups logged: editing players in the F6 map (posts only move in the menu); spawn points and posts are not saved with a scenario (only paths are); per-path spawn time and count; a path for the Crowd and the Director's extra contacts; terrain clearance check (waypoint under the ground) is only the contact's 2 m floor (big swarms 15 m, flocks 20 m).
- [ ] P-ArcGIS. ArcGIS API key for Google-Earth-sharp imagery (Esri World Imagery 0.3-1 m) in the map preview and the terrain drape. The map already switches to it when %USERPROFILE%/.chaukanna/arcgis.key (or ARCGIS_API_KEY) exists; the drape still uses Sentinel-2 until Esri's terms for stored imagery are checked.
- [ ] D21. Higher-resolution satellite preview: needs the user's own ArcGIS key (free tier) for Esri World Imagery, or another source with clear terms. CARTO labels are free for non-commercial use only: revisit before any commercial use.
- [ ] P-Rotate. The CARTO key was posted in the project chat: rotate it in the CARTO dashboard and put the new one in %USERPROFILE%/.chaukanna/carto.key.
- [ ] P-Textures. Ground materials are generated in code (Chaukanna > Terrain > Build Detail Materials). Real photo-scanned CC0 textures (for example Poly Haven rock, gravel, dry ground, grass, snow) would look better close up: needs the user's yes to download them.
- [ ] P-Close. Close up, a slope seen at a grazing angle is smooth (the terrain is 30 m data interpolated to 5 m) and the picture is 10 m: add small rocks, boulders and grass clumps as details (scatter), and a higher-resolution picture with the ArcGIS key.
- [ ] G15. Placeholder terrain layers are flat colours tiled every 20 m; replace with real ground textures. (Generated rock / scree / soil / grass / snow / sand materials exist since D22; photo textures are P-Textures.)
- [ ] G12. Rolling Hills gives only about 85 m of relief over 5 km at amplitude 160 m, because fractal noise rarely reaches its extremes. Consider rescaling presets so amplitude reads closer to the relief you get.
- [ ] M13. Night and night-vision looks are first-pass numbers tuned from one screenshot each; tune by eye (Data/EnvironmentProfile_Default.asset). Night vision is a green wash plus brighter green lighting, not a real image-intensifier effect.
- [ ] M16. Night vision visual redo to look like a real image intensifier: strong monochrome green phosphor tint, bright centre with a soft dark vignette and falling-off edges, fine grain, slight bloom on light sources, small HUD readouts at the left edge. Reference: the first-person game NV screenshot the user supplied. Needs post-processing, which is off limits for now: ask before touching any Volume or renderer asset.
- [ ] M14. Dusk keeps the user's sunset sky box and only dims and tints it. A proper night sky (stars, moon) needs a sky asset.
- [ ] M17. A real night-sky asset if the All Sky night sky box is not good enough; revisit the whole night look (moonlight level, stars) then.
- [ ] P1. Replace placeholders with models (see assets-download-list.md in the cloud project files; CC-BY models need end credits).

## Later (full design, after the prototype)
- [ ] N18. Two separate SDT analyses with gauges: report (sentry) d'/c and engage (commander) d'/c against c_opt, behind a minimum-trials gate (doc §5.4.3).
- [ ] N19. Decision clock per contact: holding a hostile past its release point is a miss (doc §1.10). (Partly covered today by the hold-delay penalty.)
- [ ] N20. Factored learner model (PFA-style) choosing Range items and writing prescriptions (doc §1.9).
- [ ] N21. Range drill (3-6 min spot-and-name presentations, fluency gate, ~20 % real clips on a separate scale).
- [ ] N22. Mark-and-Rewind (mark key any time, 2 s looped inset, WEFT tags) and the structured report widget.
- [ ] N23. Sim proficiency board (locked reference packages only, pseudonymous IDs, "Training use only").
- [ ] N24. LLM scenario builder (offline llama.cpp, enum-only intent spec, validator, 50-prompt evaluation) (M2a in the doc).
- [ ] S6. Quiet Watch (45 min in M1 spec), Changing ROE variants (cards A/B/C), Night Drop (later BSF variant).
- [ ] S2. Multi-axis swarm; night strike on a fixed installation.
- [ ] S5. Bird-mimic drone (deception class, rare, tied to the ROE card). (The doc excludes the ornithopter from the prototype.)
- [ ] D15. VR Layer B, real headset (DEFERRED). On hold until the team knows which headset it has (the user, 2026-10-14: "we will do development on it later as we dont know which headset we got, also we gotta make it so that it works of most of the vr headsets out there"). Before touching Packages or ProjectSettings, get the user's typed yes and the headset model ("Which headset do you have (Quest 2, 3 or 3S over Link or Air Link)?"). Keep it vendor-neutral: Unity OpenXR + XR Interaction Toolkit with standard interaction profiles (Meta Quest Touch, Pico, HTC Vive, Valve Index, WMR / HP Reverb, Khronos simple controller), no Meta-only SDK in the core path. Head = HMD, hands = controllers, tablet on the left controller, radial menu on the right stick, XR started only when VR MODE is on and a headset is present. Google Cardboard is not OpenXR and would need its own mobile plugin: a separate later option. The desktop VR simulator (layer A) stays the default until then.
- [ ] P3. VR shot (Quest over Link) - drop if time runs short.
- [ ] T16. Choose the networking stack for LAN multiplayer (Netcode for GameObjects + Unity Transport, or another). None is installed; adding one is a new package and needs the user's yes.
- [ ] T17. Lobby: trainer hosts, players join over the LAN, press Ready, START sends terrain + scenario + posts to everyone, each player spawns at their post. The Lobby tab and the PlayerSlot list are placeholders shaped for this.
- [ ] T18. Per-player state in the field: today only the local player (Player 1) has a camera, sensor and score; other posts are placed and flattened only.
- [ ] T7. Multiplayer / LAN and unit-level stats replace the placeholders (after the demo). Swap points: IPlayerSource and IUnitStatsSource in Scripts/Trainer/DashboardData.cs.
- [ ] T10. Sessions are saved per machine with no player name. Add a player id to the session file before multiplayer.
- [ ] G1. GeoTIFF import. No reader is built in: convert with GDAL for now (steps in the terrain README). A maintained library means a new package, so ask first.
- [ ] G8. Erosion, runtime streaming and GPU generation are deliberately out of scope until needed.

## Done
- [x] D1. Hold is reversible: a held contact can be acted on again (Report/Jam/Engage); scored by the final action, with a hold-delay rule.
- [x] D2. Trainer dashboard replaces the F5 AAR: SCORE button opens it in the trainer window; player roster (Player 1 live), pick a player for their stats; graphs; unit stats placeholder behind a swappable interface.
- [x] D3. Decision tree drawn on screen per contact with the path lit (spec 3.6).
- [x] D4. Sky and night fixes: use the All Sky night sky box at NIGHT and a dusk one at DUSK; night should be moonlit-dark, not pitch black.
- [x] D5. Terrain generator, pass 1 (Assets/Chaukanna/Terrain/): real-world accurate, real-world + detail, pure procedural and custom hybrid from one tool; own seeded noise engine; local DEM import; Unity Terrain with seam-free tiles; slope/height surface layers; editor window; self-tests; README. Spec: Assets/Chaukanna/Terrain/README.md.
- [x] D6. Charts tab fixed (XCharts shared-theme bug; charts drawn into a texture the dashboard paints).
- [x] D7. Drag-and-drop model lists on the spawner and the contact definitions; any model auto-fitted; Console count of models per run.
- [x] D8. No upper limits on trainer numbers; amber warning instead.
- [x] D9. Group select (Ctrl+click, sweep, G, X) and classify / act on the whole group.
- [x] D10. Decisions can be changed after resolving; second-guess scoring; full decision history.
- [x] D11. Blend-in hostiles (fly an own-drone patrol, then break off), by difficulty or a trainer percentage; "caught the deception" bonus.
- [x] D12. Flowchart decision tree on the dashboard, foldable per checkpoint and as a whole.
- [x] D13. Loading screen from the trainer menu's START.
- [x] D14. VR mode layer A (desktop simulator): one-click switch, hands, both-hands binoculars, left-hand sensor tablet, radial menu, display tier, comfort check.
- [x] D17. Real places: OpenStreetMap map picker with search on the trainer menu, elevation download from AWS Terrain Tiles (no key), offline cache, Jammu AFS example area and configs.
- [x] D18. Satellite picture for a downloaded area (Sentinel-2 cloudless 2016, see D22). Candidate: EOX Sentinel-2 cloudless 2016 (CC BY 4.0; later years are non-commercial). The terms of using EOX's tile service itself were not clear, so nothing was built: check them (or download the 2016 mosaic once) before adding it. The terrain already takes a PNG/JPG picture with a .json for its bounds.
- [x] D20. Map picker layers (Map / Satellite / Satellite + labels) and a labelled, draggable radius circle.
- [x] D22. Terrain texturing: satellite picture draped on downloaded terrain (Sentinel-2 cloudless 2016, CC BY), six generated ground materials with normal maps, biome rules (high desert, plains, desert, temperate) with slope, height, wetness, ridges, north faces and noise, max 4 layers per tile, and a 4x coarse surround for the horizon. Map: search box with up to 5 results, coordinates (decimal or DMS), recent places; CARTO labels with the user's key (high-DPI); zooming past a source's last level.
- [x] M2. Day/night lighting presets + fog (haze by difficulty), NV look at night.
- [x] M23. Terrain generation is one blocking step behind the loading screen; spread it over frames (per tile) so the bar keeps moving.
- [x] T2. Difficulty settings (Recruit/Trained/Expert) driving counts, speed, decoy share, fog, AI error rate.
- [x] T3. Auto-adjust rule: last score over 80% = one level harder, under 50% = one easier; show "Recommended next".
- [x] T24. XCharts charts on the score dashboard (Charts tab), in its own optional assembly (Assets/Chaukanna/Charts); built-in charts stay on the Overview tab. Needs a hand test: the tab's layout could not be seen from the CLI.
- [x] G3a. Satellite imagery as terrain colour (PNG / JPG with bounds), kept separate from elevation.
- [x] G7. Terrain chosen in the trainer menu and built at run start (scene TrainerMenu, Terrain tab).
- [x] G13. One-click Show terrain / Hide terrain in the Terrain Generator window (also hides and restores the flat Ground). Doing it from the trainer panel is still G7.
- [x] P1a. Contact placeholders replaced with the downloaded models: NateGazzard drone (both quads), Predator (both fixed-wings), Quaternius pigeon and jay (birds, two types picked by seed). Wrapper prefabs in Prefabs/Contacts/.
- [x] Spawner, seed determinism, 1 km ring, Inspector override fields.
- [x] Binoculars, detection, lock, classify 1-5, actions H/R/J/F, ROE banner, timer, [Decision] log.
- [x] Engage after a miss; score hidden from the player.
- [x] Scoring (decision tree, three metrics, confusion grid, session JSON), self-tests.
- [x] Sensor panel (RF, sound bearing, AI hint, OK/DEGRADED).
- [x] Trainer screen: presets, Randomize, START, truth map, event feed, F6 split-screen, SCORE button.
- [x] Hold reversible with hold-delay rule; score dashboard (roster, stats, heatmap, timeline, decision-tree boxes, trends, unit placeholder); F5 removed.
- [x] Day / Dusk / Night, Clear / Hazy / Foggy, night vision (N); difficulty levels Recruit / Trained / Expert; "Recommended next" with Apply.
- [x] Night and dusk sky boxes from All Sky; moonlit night. Terrain generator pass 1 (Chaukanna > Terrain Generator).
- [x] Trainer main-menu scene (Terrain / Scenario / Map / Lobby, START); field waits for START; player posts and drone spawn points placed on a map; ground imagery.
- [x] Trainer setting for soldier movement (W A S D within a radius, or fixed at the post); night-vision surround hidden in binoculars; typed number boxes on both trainer panels.

## Lasso follow-ups (added 2026-10-15)
- [ ] L3. VR hand-ray lasso (draw the loop with the controller) once the real headset is chosen.
- [ ] L4. Trainer map lasso bulk edits beyond delete: move the selection, change class, set a path, make them a swarm, pause / resume them.
- [ ] L5. Lasso on the TrainerMenu map tab: today that map is the area picker and has nothing to select; add it when spawn points and posts are placed there (scenario editor, module 11).
- [ ] L6. A clickable lasso button on the HUD / VR tablet (Tab only today).

## Full scoring follow-ups (added 2026-10-15)
- [ ] F1. Officer / instructor to set the release ranges, loiter limits, the 30 s non-hostile deadline and the latency bands (all placeholders).
- [ ] F2. Cohort-based N1b and report bands (doc: vs cohort P50 / P80) once there are enough saved sessions; today fixed seconds, shown but not scored.
- [ ] F3. d'/c pooled across a cadre block of sessions with a bootstrap interval, c_opt from the card costs and prevalence, Δc at card changes (one session rarely reaches 20 of each).
- [ ] F4. Hand-test the DECISION QUALITY section and the fifth tree checkpoint on screen.
- [ ] F5. Only the latest action is recorded, so a report in time followed by a late jam counts as late; keep the first in-time decision if instructors want that.

## AAR follow-ups (added 2026-10-15)
- [ ] A1. True counterfactual re-simulation from the logged state at the decision time (doc K10), instead of the modelled consequence.
- [ ] A2. Company one-page summary and the signed batch export (gzipped JSONL + manifest + signature) from doc §1.11.
- [ ] A3. Hindi debrief text in the HTML report (browsers shape Devanagari; templated sentences only).
- [ ] A4. Hand-test the dashboard REPLAY section (play, slider, click a contact) and open an HTML report on another PC.
- [ ] A5. Replay for older saved sessions: today only the latest run has tracks (they are kept in memory and in its HTML report, not in the session JSON).

## Modules 9-12 (user order 2026-10-16) - BUILT AND TESTED 2026-10-16 (self-tests + live checks; hand tests still open)
- [x] Module 9: learner model + adaptive difficulty (Scripts/Scoring/LearnerModel.cs, LearnerSelfTest.cs; dashboard LEARNER MODEL section; F6 "Use plan").
- [x] Module 5: prevalence (Custom / Low / Medium / High) + null missions + Quiet watch (20/45/90 min, 1-4 per hour) + vigilance thirds; realised share vs band (V7). PrevalenceSelfTest.cs.
- [x] Module 1: Range drill (Scripts/Scenario/RangeDrill.cs): one item at a time in view, 1-5 to call, tiers with exposure and freezes, fluency 10 of 12 within 3 s, ghost times, feeds the learner. Real clips (20 %) not in: need open-licence footage and the owner's yes to download.
- [x] Module 6: true-size display (Scripts/Player/DisplayCalibration.cs): diagonal, aspect, distance -> vertical FOV and arcmin/px, comparable band 1.0-2.0, binoculars 7x, pan capped at 60 deg/s. RangeSelfTest.cs checks the doc's table.
- [x] Module 10: Scenario Director (Scripts/Scenario/Director.cs), training only, logged injections, capped easing, sandbagging flag.
- [x] Module 11: scenario editor form + validator V1-V4, V6-V9 (V5, V10 noted as not checked) + expander + packages on disk (Scripts/Scenario/ScenarioSpec.cs; menu EDITOR tab).
- [x] Module 12: proficiency board (Scripts/Scoring/ProficiencyBoard.cs; menu BOARD tab): pseudonymous ID + PIN sign-in (salted hash), reference packages only with Director off, n and 95 % interval, not-yet-trained list, duplicate-timing KS check, HTML export with the training-use-only line. PackageSelfTest.cs.
- [x] LAN multiplayer, first slice (2026-10-16): Netcode for GameObjects 2.13.3 + Unity Transport 2.6.0 (owner's yes). See LAN follow-ups.
- [ ] Menu SCENARIO tab is now long; part of the deferred UI redesign.
- [ ] H9. Hand-test modules 9-12 (steps in PROGRESS 2026-10-16): the EDITOR and BOARD tabs, the Range drill HUD and keys, the true-size view, the LEARNER MODEL section, Quiet watch length.

## LAN follow-ups (added 2026-10-16)
- [ ] N-L1. Hand-test on two PCs on the LAN (steps in PROGRESS 2026-10-16), including the Windows firewall prompt.
- [x] N-L2. (done 2026-10-16, tested live) Solo mode run end to end over the network (Team mode was tested live; Solo shares the start/end/results path).
- [x] N-L3. (done 2026-10-16: BACK TO LOBBY (all) on the trainer's F6 panel; tested live) Next run without restarting: today the trainees return to the menu by restarting the app; add a "back to lobby" for everyone.
- [x] N-L4. (done 2026-10-16: a trainee PC without the area gets it from the trainer over the LAN before starting; Kargil 11.4 MB tested live) Real-terrain areas: send the area files to trainees (or a shared folder) instead of requiring the same path on every PC.
- [ ] N-L5. Pair roles from the doc (sentry reports, commander engages, credit chain) instead of every trainee doing both.
- [x] N-L6. (done 2026-10-16: banner; Team run ends and is saved, Solo carries on) Host drops: trainees should keep their result and show "host lost" instead of just stopping.
- [x] FIX-1. Pink terrain in the built game (2026-10-16): built-in materials in Resources; trainee PCs keep their own ground materials. Owner to rebuild and confirm.
- [x] FIX-2. Replay green lines (2026-10-16): lines are drawn without rotation on scaled GUI (dashboard, trainer map). Owner to re-check on screen.

## Handoff to a new PC (2026-10-16)
- [x] H-1. CLAUDE.md, AGENTS.md, CONTEXT.md, HANDOFF.md written from the real repo state; solution-v2-council.md copied to Assets/Chaukanna/Docs/.
- [ ] H-2. Owner: follow HANDOFF.md section 4 on the new PC, rotate the CARTO key, decide whether to commit the deliberately uncommitted files.
- [x] H-3. module-1-demo.md added to Assets/Chaukanna/Docs/ (2026-10-16).
- [x] H-4. New PC (D:\Chanakya\Chanakya) brought from e7f703b to 29dba3f with a fast-forward pull (owner's yes, 2026-10-16). Compiles with 0 errors; all 17 self-tests pass.
- [ ] H-5. Git identity on the new PC is not Utkarsh Kale (user.name / user.email point at another person). Owner to say whether to set the repo-local author to Utkarsh Kale <utkarshkale.w@gmail.com> before any commit.
- [ ] H-6. %USERPROFILE%\.chaukanna on the new PC has the tile cache but no carto.key and no arcgis.key, so map labels are off. Owner: copy in the ROTATED CARTO key by hand (never by git or chat).
- [ ] H-7. CONTEXT.md section 8 check counts are out of date: TrainerSelfTest has 56 (not 51), DashboardSelfTest 23 (not "about 15"), VrSelfTest 19 in Edit mode (22 only in Play mode). Fix the table.
- [ ] H-8. The first eval_file calls after the Editor opens can hit the pipeline's 5 s main-thread limit (Score, FullScoring and Report timed out once, then passed on retry). Retry once before calling a test failed.
- [ ] H-9. Assets/Chaukanna/Docs.meta and Docs/*.md.meta are not in git, so each PC makes its own GUIDs. Owner to decide whether to commit them.

## Round 2026-10-19 (owner's list, VR last)
- [x] R1. Rename to Kurukshetra (visible names only).
- [x] R2. Remove Credits tab; attribution to CREDITS.txt + report footer.
- [x] H-10. Crowd speed check passes on this PC (27/27); Burst setting on the other PC.
- [ ] R3. Plan doc plan-round-2026-10-19.md for: realistic drone swarm, realistic flapping bird flocks classifiable as swarms, working multiplayer, AAR cards + export, black-box 3D replay with proximity voice and comms review, procedural trees/bushes/buildings, VR (last). Owner approves before building.
- [x] R4. Phase 0: trainer windows scroll and fit (2026-10-19). Hand-test at 1920x1080, 1366x768.
- [ ] R5. Phase 1 drone swarm prototype (thousands of agents: Burst jobs, spatial grid, instanced rendering, stress test with typeable count), then birds, multiplayer, AAR cards, black box, trees/bushes/buildings, VR last. Full-scale versions later.
- [x] R6. Phase 1 drone swarm prototype (2026-10-20).
- [ ] R6b. Full-scale swarm later: un-promoted arrivals as scored leakers, staggered spawn, big swarm on LAN team clients, formation tactics beyond 3.
- [x] R7. Delete downloaded terrains + clear tile cache.
- [ ] R8. POLISH PASS after the black-box replay and trees/buildings prototypes, before VR (owner, 2026-10-19).
- [x] R9. Phase 2 birds prototype (2026-10-20).
- [ ] R9b. Full birds later: flocks on LAN team clients, group calls over LAN, density waves, flap harder when climbing, bird-mimic drone.
- [ ] POLISH PASS (stop before it; the owner sets its content). Already in it: 2D trainer-view path editor (custom paths for drones, swarms, bird flocks; place and move players), owner 2026-10-19.
- [x] POLISH-1. (2026-10-20) Birds were invisible: far birds are real-size flat wings under a pixel thick. Now a minimum on-screen size (BirdView: 7 px, flat silhouette facing the camera under 8 px) for single birds, small flocks and big flocks. Hand test PB-1 open.
- [x] POLISH-2. (2026-10-20) Stray blue lines in the SCORE > TRENDS charts: DashboardCharts.Line used a rotated strip under the scaled GUI.matrix. All chart lines now go through GuiDraw (clipped). Hand test CH-1 open.
- [x] POLISH-3. (2026-10-20) Real charts: ChartModels / ChartLayout / ChartKit (IMGUI) / ChartSvg (HTML Charts view, Cards / Charts switch). Hand test CH-1 open.
- [ ] POLISH-3b. Charts later: click a trend point to open that session; per-chart PNG export; second player on the trends (multi-player compare); bird min-size for crowd-pattern birds (CrowdJobs) and the replay; same minimum on-screen rule for drones if the owner wants it; Bars / Stacked / Heatmap cards through ChartKit too; remove the unused ChartPayload (DashboardData.cs) and the XCharts panel if XCharts is dropped.
- [x] POLISH-8. 3D scenario editor (2026-10-20, compile check only): "3D editor" button on the SCENARIO and MAP tabs and F6 in the menu; free-fly camera, tool panel; paths, spawn points, posts, placed drone and bird groups, hand-placed trees / bushes / buildings / rocks (instanced, brush), cleared generated areas, undo / redo, save as package, test run from here. Same ScenarioConfig / ScenarioSpec data as the 2D editor. See PROGRESS.md.
- [ ] POLISH-8a. Hand-test the 3D editor (owner): see the numbered list in the PROGRESS.md entry of 2026-10-20 (3D editor). Not run yet.
- [ ] POLISH-8b. Deferred from the 3D editor (nothing deleted): (a) manual objects have no per-object LOD or culling (one instanced batch per mesh part: thousands are fine, hundreds of thousands are not tested); a 64 m cell grid with frustum and distance culling like BushField would scale further; (b) gizmo is a screen-space arrow handle, not a full translate / rotate / scale gizmo; no multi-select, no box select, no copy / paste, no snap to grid; (c) paths are drawn as straight lines between waypoints (no terrain-clearance check, no profile strip in 3D); (d) a placed group's path is an index into the paths list (a deleted path unlinks it, a re-ordered list is not tracked); (e) placed swarms are capped at 64 drones (the single big swarm stays the scenario's own); the species of a small bird flock is chosen through the contact seed; (f) cleared areas are circles that also clear buildings, not grass; 300 at most; they rebuild the tree and building layers (seconds) after the stroke, and the rebuild is not undone separately; (g) moving a post in 3D does not rebuild the flat pad (it is built with the terrain); (h) typed fields (height, speed, count) are undone with the 0.8 s grouping, so two quick edits are one step; (i) manual trees get one capsule trunk collider and buildings one box at run start (cap 6000), no rock or bush colliders; (j) not saved: spawn points and posts are not part of a package (as before); the scenario's paths, objects, cleared areas and groups are; (k) the editor reloads the field to come back from a test run (a few seconds); (l) no LAN: the editor needs a solo menu; (m) terrain editing (raise / lower / paint), water, roads and a minimap are not in it; (n) no drone model preview in the palette, only prefab names.
- [ ] R8a. 2D path editor: the trainer sets custom drone, swarm and bird paths on the 2D trainer map (owner moved it to the polish pass, 2026-10-20). Also place and move players on that map.
- [ ] Order after birds (owner 2026-10-19): AAR dashboards with real data visualisations + export > multiplayer + proximity voice (trainer can degrade it live) > sound engine (solution-v3 1.16) > full replay (black box, 3D, both voice versions) > trees / bushes / buildings > STOP for the polish pass > VR. (Phase 7 trees / bushes / buildings DONE 2026-10-20 as R14; next is the STOP for the polish pass.)
- [x] R10. Phase 3 AAR cards prototype (2026-10-20).
- [ ] R10b. Full AAR later: per-card PNG, hover tooltips, officer-checked verdicts and thresholds, unit pack.
- [x] R11. Phase 4 multiplayer + proximity voice prototype (2026-10-20). Two-PC test pending (owner).
- [ ] R11b. Full later: Observer seat, voice on busy Wi-Fi test, exact heard-audio rebuild, voice codec upgrade if needed.
- [x] R12. Phase 5 sound engine prototype (2026-10-20).
- [ ] R12b. Full sound later: ambient-noise audibility gate (solution-v3 1.16), ground reflection, sound travel delay, real recorded samples option.
- [x] R13. Phase 6 black box + 3D replay + voice review prototype; green lines fixed (2026-10-20).
- [ ] R13b. Full replay later: remote seats' locks/actions attributed, per-seat HUD in FOLLOW, sample-exact HEARD audio, HTML replay in 3D.
- [x] R14. Phase 7 trees / bushes / buildings prototype (2026-10-20): Quaternius trees by species and height (broadleaf / pine / snow / dead), Quaternius bushes as a fourth layer (Off / Realistic / Procedural, typeable density, budget, draw distance; GPU-instanced, culled), per-building wall + roof palette, Kenney houses and blocks in procedural villages; kit filled by Chaukanna > Terrain > Wire real art into kit; VegetationSelfTest 35/35. Hand test: see PROGRESS.md.
- [ ] R14b. Deferred from phase 7 (nothing deleted): (a) Poisson-disc spacing for tree placement (plan 6.3 step 3) and clumping by the wood mask; (b) rocks drawn (kit lists them, no renderer yet) with colliders only within 100 m; (c) OSM tags natural=scrub / heath, landuse=residential, roof:shape, building:colour, roof:colour (TL-3 g) and Indian flat-roof detail (parapet, water tank on 40 % of houses); (d) palm trees for the coastal site (FR-15) and willows near OSM water; (e) separate roof colour on Kenney prefab houses (needs a two-material copy of each model); (f) a crossed-quad impostor for bushes beyond 60 m and a "Layers: Low / Medium / High" preset; (g) full-LOD tree range is short (0.04 screen height, about 50 m at tree LOD bias 0.2) kept short for safety: the Editor crashed (D3D12 device removed during a render + readback timing loop) while 0.01, about 250 m, was being tested on 2026-10-20, cause not isolated: make low-poly LOD1 meshes (decimate or simplify the Quaternius crowns) so full detail can reach 150 m; (h) measure on the owner's GTX 1650 with a build (TL-2 step 10), including 10x bush density; (i) bushes under the snow line use snow bushes only above 2 600 m, no dead bushes; (j) bush draw distance and budget are read when the layer is built, not live; (k) Kenney house colours are the model's own on the roof (all green today): add a roof-tint variant.
- [x] R15. Polish 2026-10-20 (compile check only): report filter All / unit / player (F6 SCORE chips + HTML drop-down), fast keys E engage / Q jam without a call (VR-sim radial now C, tablet now Z), replay voice buttons named Clean recording / As heard (degraded).
- [ ] R15a. Hand-test R15 (owner): see the PROGRESS.md entry of 2026-10-20 (polish 4-7); no self-test was run for it.
- [ ] R15b. Deferred from R15 (nothing deleted): (a) lobby unit assignment (NetPlayer.unit and RunConditions.UnitId are in place, nothing sets them yet); (b) a combined replay timeline and per-contact replay in the multi-player HTML report (needs every PC's tracks); (c) summed trends for a unit or all players; (d) E / Q engage-or-jam hint in the tablet and headset HUD; (e) a self-test for unclassified engage / jam scoring and for SessionFilter.Combine (waits for the owner's go); (f) per-player and per-unit filter in the Details view; (g) the F6 Comms panel also for a solo run (voice is LAN-only today).
- [x] SND-1. SoundEngine NullReference after a recompile in Play (pool rebuilt in Update when missing). 2026-10-20.
- [x] LOB-1. Lobby units (add / rename / delete) and multi-select assign of unit + role incl. OBSERVER, synced over LAN. 2026-10-20, compile only.
- [ ] LOB-1a. Hand-test LOB-1 (see PROGRESS 2026-10-20 lobby units).
- [ ] LOB-1b. Deferred (nothing deleted): observer gets a free-look view without a sensor lock; observer HUD text greyed like the sentry's; self-test for the unit edit rules (NetRoster.AddUnit / RenameUnit / DeleteUnit).
- [x] VOI-1. F10 voice settings (mic mode, PTT key, device, level meter, output volume), saved in PlayerPrefs. 2026-10-20, compile only.
- [ ] VOI-1a. Hand-test VOI-1 (see PROGRESS 2026-10-20 F10).
- [ ] VOI-1b. Deferred (nothing deleted): a mic input gain slider; a separate radio key binding; noise suppression; a VAD threshold slider; show the chosen PTT key in the lobby text (it still says V).
- [x] AVA-1. Player beans at synced seat poses with name / unit tag and binocular state (flag bit 2), behind IPlayerAvatar. 2026-10-20, compile only.
- [ ] AVA-1a. Hand-test AVA-1 (two PCs, or host + client build).
- [ ] AVA-1b. Deferred (nothing deleted): animated 3D avatar models through PlayerAvatars.Factory: walk, run, hold-binoculars, binoculars-up animations (drive from the smoothed speed and the binocular flag); true interpolation with a render delay instead of the exponential follow; feet snapped to the terrain height; observer / trainer avatars styled differently.
- [x] TP-1. Preview terrain (relief + satellite drape + layer overlays, layer toggles, Regenerate, coarse then 384 px). 2026-10-20, compile only.
- [ ] TP-1a. Hand-test TP-1 (see PROGRESS 2026-10-20 Preview terrain).
- [ ] TP-1b. Deferred (nothing deleted): oblique 3D view of the preview; satellite-estimated trees for realistic mode when the area has a picture but no map file; real tree / bush counts in the status line; surround ring in the picture; an auto-refresh when settings change.
- [x] LOOP-1. Game loop: end screen with Restart (solo: menu; trainee: lobby, state waiting), trainer Back to lobby and Restart scenario (new seed optional), wrap-up before scene change. 2026-10-20, compile only.
- [ ] LOOP-1a. Hand-test LOOP-1 (two PCs or host + built client).
- [ ] LOOP-1b. Deferred (nothing deleted): the trainer's lobby shows who is waiting vs ready with a Start-for-everyone button; a late joiner during a run is still refused; restart with changed settings; a trainee-side countdown on Restart scenario; per-seat result tabs on the end screen.

- [ ] VR-HOLD. VR is on hold by the owner's decision (2026-10-20: "do not build vr"). No VR work, packages or tests until the owner says so.
- [x] POLISH-FIX-LINES (2026-10-20). Stray lines on the MAP tab fixed (rotation-free GuiDraw.Line everywhere); 2D map reduced to paths + heights with a pointer to the 3D editor. Hand test pending.
- [x] DEMO-OFF (2026-10-20). DEMO button and presenter checklist hidden (TrainerMenu.ShowDemo = false) by the owner's decision: the demo will be recorded. DEMO-SCRIPT.md kept as a doc.
