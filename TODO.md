# TODO.md

Work top to bottom. One task per session. Tick [x] when done and log it in PROGRESS.md.
(plan) = needs Opus 5.5 in plan mode first; the rest are Sonnet 5.5 build tasks.

## Doing now
- [x] D1. Hold is reversible: a held contact can be acted on again (Report/Jam/Engage); scored by the final action, with a hold-delay rule.
- [x] D2. Trainer dashboard replaces the F5 AAR: SCORE button opens it in the trainer window; player roster (Player 1 live), pick a player for their stats; graphs; unit stats placeholder behind a swappable interface.
- [x] D3. Decision tree drawn on screen per contact with the path lit (spec 3.6).
- [x] D4. Sky and night fixes: use the All Sky night sky box at NIGHT and a dusk one at DUSK; night should be moonlit-dark, not pitch black.
- [x] D5. Terrain generator, pass 1 (Assets/Chaukanna/Terrain/): real-world accurate, real-world + detail, pure procedural and custom hybrid from one tool; own seeded noise engine; local DEM import; Unity Terrain with seam-free tiles; slope/height surface layers; editor window; self-tests; README. Spec: Assets/Chaukanna/Terrain/README.md.

## Mixed Sky: remaining
- [ ] M1. Hand-test everything built this session (see PROGRESS) and fix what breaks. The score dashboard's look has never been seen on screen by the builder; check layout, text size and the charts first.
- [x] M2. Day/night lighting presets + fog (haze by difficulty), NV look at night.
- [ ] M3. Ground/terrain pass: the user's sandbox + textures stay; later procedural terrain and rural/urban post switch.
- [ ] M4. ROE flip surfaced in the trainer UI as a clear event (banner flash already exists).
- [ ] M5. Ammo / burst limit for Engage? (open question)
- [ ] M6. Late-spawn and unresolved contacts: decide how they count (currently: unseen late = MISSED 0 pts, no penalty).
- [ ] M7. Should following a wrong AI hint cost points? (open question; data is logged already)
- [ ] M8. Real ROE rules for Weapons Tight / Weapons Free and "Report on a hostile under Under Attack" (needs subject-matter expert).
- [ ] M9. Confirm the hold-delay numbers (15 s blocks, -1 each, cap -3, hostiles under Under Attack only). They are the builder's defaults, not from the spec.
- [ ] M10. Hold-delay time assumes at most one ROE flip per run. Track time under each card properly if runs get several flips.
- [ ] M11. Placeholder identification cues (payload box, camera ball, antenna mast) are a stand-in: decide the real cues.
- [ ] M12. Nothing happens when a hostile reaches the post except a log line. Decide the consequence (score, end of run, effect).
- [ ] M13. Night and night-vision looks are first-pass numbers tuned from one screenshot each; tune by eye (Data/EnvironmentProfile_Default.asset). Night vision is a green wash plus brighter green lighting, not a real image-intensifier effect.
- [ ] M14. Dusk keeps the user's sunset sky box and only dims and tints it. A proper night sky (stars, moon) needs a sky asset.
- [ ] M16. Night vision visual redo to look like a real image intensifier: strong monochrome green phosphor tint, bright centre with a soft dark vignette and falling-off edges, fine grain, slight bloom on light sources, small HUD readouts at the left edge. Reference: the first-person game NV screenshot the user supplied. Needs post-processing, which is off limits for now: ask before touching any Volume or renderer asset.
- [ ] M17. A real night-sky asset if the All Sky night sky box is not good enough; revisit the whole night look (moonlight level, stars) then.
- [ ] M15. Fog distance is tied to the sensor's visible range (hazy 60%, foggy 35%). Confirm those shares, and whether fog should also cut the RF / sound sensors (it does not now).

## Trainer and dashboard (later)
- [ ] T1. Two-monitor mode (trainer canvas on Display 2, fall back to split-screen). Needs the trainer UI rebuilt on a canvas.
- [x] T2. Difficulty settings (Recruit/Trained/Expert) driving counts, speed, decoy share, fog, AI error rate.
- [x] T3. Auto-adjust rule: last score over 80% = one level harder, under 50% = one easier; show "Recommended next".
- [ ] T4. (plan) Hybrid randomized mode: scenarios mixed so the trainee doesn't know what's coming; each with difficulty adjustment.
- [ ] T5. Scenario builder routes: scripted, procedural, form-based (LLM-assisted builder is for the full product, not the demo).
- [ ] T6. Saved session history feeding the trend charts; sample history for the demo (captioned "sample data"). Partly done: the dashboard reads the last 10 saved sessions and pads with labelled sample points.
- [ ] T22. Soldier walking is a simple ground-follower (no jump, crouch, slope limit or collision with objects). Upgrade to a CharacterController when there is something to collide with.
- [ ] T23. Typed number boxes exist for whole numbers only; sliders (terrain weights) and text paths are unchanged. Tab between boxes is Unity's default behaviour, not tested.
- [x] T24. XCharts charts on the score dashboard (Charts tab), in its own optional assembly (Assets/Chaukanna/Charts); built-in charts stay on the Overview tab. Needs a hand test: the tab's layout could not be seen from the CLI.
- [ ] T25. XCharts needed one line changed to compile on Unity 6000.6 (SerieLabelPool.cs, EntityId cast). Re-apply it if XCharts is updated.
- [ ] T16. Choose the networking stack for LAN multiplayer (Netcode for GameObjects + Unity Transport, or another). None is installed; adding one is a new package and needs the user's yes.
- [ ] T17. Lobby: trainer hosts, players join over the LAN, press Ready, START sends terrain + scenario + posts to everyone, each player spawns at their post. The Lobby tab and the PlayerSlot list are placeholders shaped for this.
- [ ] T18. Per-player state in the field: today only the local player (Player 1) has a camera, sensor and score; other posts are placed and flattened only.
- [ ] T19. Trainer menu and in-field trainer panel duplicate the scenario controls; share one implementation when the UI moves to a canvas (T1).
- [ ] T20. Drone spawn points are plain points; add spawn arcs / sectors and per-class spawn points.
- [ ] T21. Built player: add TrainerMenu and MixedSky to Build Settings (a ProjectSettings change, so the user does it).
- [ ] T7. Multiplayer / LAN and unit-level stats replace the placeholders (after the demo). Swap points: IPlayerSource and IUnitStatsSource in Scripts/Trainer/DashboardData.cs.
- [ ] T8. Truth map through an orthographic camera and render texture. Needs a dedicated layer, which means a ProjectSettings change.
- [ ] T9. Typed seed field and a real preset dropdown on the trainer panel (now -/+ buttons and preset buttons, because typed digits would hit the player's 1-5 keys).
- [ ] T10. Sessions are saved per machine with no player name. Add a player id to the session file before multiplayer.
- [ ] T11. Dashboard draws at most 60 contacts on the timeline and tree list. Add paging or filtering for large runs.
- [ ] T12. Decide whether the run should pause while the trainer has the dashboard expanded (it keeps running now; player input is ignored while expanded).
- [ ] T13. Difficulty numbers (Data/Difficulty/) are the builder's defaults; Trained equals the pre-difficulty behaviour. Confirm Recruit and Expert with a subject-matter expert.
- [ ] T14. The recommendation uses the last run only. Consider averaging the last few sessions and keeping the level per player once there is more than one.
- [ ] T15. Trainer panel is getting crowded (map is small in split-screen). Rework the layout when the trainer UI moves to a canvas (T1).

## Terrain (after pass 1)
- [ ] G1. GeoTIFF import. No reader is built in: convert with GDAL for now (steps in the terrain README). A maintained library means a new package, so ask first.
- [ ] G2. Online DEM download tested end to end (OpenTopography needs the user's free API key; Copernicus and NASA need accounts).
- [x] G3a. Satellite imagery as terrain colour (PNG / JPG with bounds), kept separate from elevation.
- [ ] G3b. ESA WorldCover class raster driving the surface layers (trees, grass, crops, built-up, bare, water).
- [ ] G4. Vegetation and props on the generated terrain.
- [ ] G5. Contour lines and terrain shading on the trainer's truth map.
- [ ] G6. Urban and rural scenario terrain presets; pick real locations for the demo.
- [x] G7. Terrain chosen in the trainer menu and built at run start (scene TrainerMenu, Terrain tab).
- [ ] G8. Erosion, runtime streaming and GPU generation are deliberately out of scope until needed.
- [ ] G9. Run a real elevation file through the tool (pass 1 was proven on synthetic files only). Needs the user to pick a location and supply or approve a download.
- [ ] G10. 16-bit PNG heightmap import (not built; RAW, HGT and ASC are).
- [ ] G11. Areas that cross two elevation tiles: load and stitch several files.
- [ ] G12. Rolling Hills gives only about 85 m of relief over 5 km at amplitude 160 m, because fractal noise rarely reaches its extremes. Consider rescaling presets so amplitude reads closer to the relief you get.
- [x] G13. One-click Show terrain / Hide terrain in the Terrain Generator window (also hides and restores the flat Ground). Doing it from the trainer panel is still G7.
- [ ] G15. Placeholder terrain layers are flat colours tiled every 20 m; replace with real ground textures.
- [ ] G14. The truth map and sound / RF sensors ignore terrain; decide whether hills should mask them too.

## Other scenarios (all reuse the same engine)
- [ ] S1. Swarm recognition (8 and 24 contacts) with boids; behaviours: saturation, split, feint, formation.
- [ ] S2. Multi-axis swarm; night strike on a fixed installation.
- [ ] S3. Night recognition (naked eye / NV), degraded-sensors scenario, single-contact identification, friendly-drone deconfliction.
- [ ] S4. Manned-aircraft safety (new contact class; engaging it is -10 under every card). The scoring rule already exists in data.
- [ ] S5. Bird-mimic drone (deception class, rare, tied to the ROE card).
- [ ] S6. Quiet Watch (45 min in M1 spec), Changing ROE variants (cards A/B/C), Night Drop (later BSF variant).
- [ ] S7. Boids for the birds in Mixed Sky too (they wander individually now), reusing the swarm boids.

## Polish, demo and team
- [x] P1a. Contact placeholders replaced with the downloaded models: NateGazzard drone (both quads), Predator (both fixed-wings), Quaternius pigeon and jay (birds, two types picked by seed). Wrapper prefabs in Prefabs/Contacts/.
- [ ] P1b. Manned aircraft: the Helicopter and Small Airplane are .glb only. Re-download as OBJ from Poly Pizza (or approve the glTFast package), then add the contact class (S4).
- [ ] P1c. Only one quad model and one fixed-wing model exist, so hostile and friendly/civilian differ only by the added cue (payload box, antenna). Get the FPV racing drone and a second fixed-wing for real variety; a Shahed-style delta wing is still missing.
- [ ] P1d. The pigeon is a standing cartoon bird with no flying animation and the jay has no texture (white). Find flying bird models with a flap clip.
- [ ] P1e. Environment art (nature pack, city kits), effects (particle pack) and sounds (Sonniss, Kenney) are downloaded but not used yet.
- [ ] P1f. Delete the nature pack's Blends folder: Unity logs 'Blender could not be found' for every .blend file.
- [ ] P1. Replace placeholders with models (see assets-download-list.md in the cloud project files; CC-BY models need end credits).
- [ ] P2. Sound and effects pass (muzzle flash, engage, jam); better UI look.
- [ ] P3. VR shot (Quest over Link) - drop if time runs short.
- [ ] P4. Demo video: record at 1080p or higher, add a zoom inset; script in module-1-demo.md section 5.
- [ ] P5. Team workflow: decide Git + LFS or Unity Version Control for six people; everyone on Unity 6000.6.3f1. Note: nothing built so far is committed anywhere; it exists only on this machine.
- [ ] P6. Get the professor's (Pasarkar) approval for VR scope if VR stays in.
- [ ] P7. Proper EditMode tests once the Unity Test Framework package is allowed; the self-tests (scoring, sensors, trainer, dashboard) stand in for now.

## Done
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
