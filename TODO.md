# TODO.md

Work top to bottom. One task per session. Tick [x] when done and log it in PROGRESS.md.
(plan) = needs Opus 5.5 in plan mode first; the rest are Sonnet 5.5 build tasks.

SCOPE SOURCE: module-1-demo.md (the agreed Module 1 demo; the user rejected the bigger module plan as over-engineered). Its key demo moments are (1) the ROE flip between 2 cards, (2) the AI cue being wrong once, (3) the red decision-tree path when the player engages an own drone: all three exist already. The file is now in Assets/Chaukanna/Docs/module-1-demo.md.
First restructured 2026-10-14 against the solution doc (solution-v2-council.md: §2 PS coverage table R1a-R9, §11.1 M1 scope, §12 demo script). No item was deleted; items were moved and tagged. New items from the gap check are numbered N1 and up.
Tags: [R1a]...[R9] = the PS line it serves. [demo] = one of the 3 key demo moments in §12: (1) cold open, night, own drone engaged, "your call vs the sound call"; (2) the card flip mid-mission; (3) night 24-swarm with a wrong AI cue. Shot 5b is the VR sentry.

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
