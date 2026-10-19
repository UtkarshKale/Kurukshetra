# Plan: round of 2026-10-19 (swarm, birds, multiplayer, AAR cards, black box + voice, terrain, VR)

Planned 2026-10-19 (Opus, plan only). Nothing was compiled or run for this file: the Editor was busy building. Every
"exists today" line was read from the code on branch `dev/modules-2026-10-19`; claims about behaviour on screen are
marked "read, not run" where they come from code reading only.

Rules: CLAUDE.md (only `Assets/Chaukanna/` + root docs; no ProjectSettings / Packages / layers / tags / downloads without
the owner's yes; runtime materials through `RuntimeMaterials`; IMGUI only; `System.Environment.X` fully qualified;
numbers typeable, no upper caps). Design source: `solution-v3.md` (cited as §). Status source: `STATUS.md`.

**Source labels used below.**
- **[doc]**: cited in solution-v3 or a plan doc in this folder.
- **[TK]**: from my training knowledge. The reference is real to the best of my knowledge, but nobody on the team has
  opened it for this plan. Check before it goes into the pitch.
- **[ours]**: our own design or arithmetic.

The owner's order for this round: 1 swarm, 2 birds, 3 multiplayer, 4 AAR cards, 5 black box + voice, 6 terrain
procedural, 7 VR (last).

---

## 0. Findings from reading the code (read these first)

These change the plan, so they come before the items.

| # | Finding | Where | Effect |
|---|---|---|---|
| A | **The real bird models do not flap.** `ContactMotion.Flap` only turns child objects named `WingPivotL` / `WingPivotR`. Only the grey primitive placeholder (`Placeholder_Bird`: sphere + two quads) has them. `Bird_Pigeon` and `Bird_Jay` (the looks every bird uses since 2026-10-14) have none. | `ContactMotion.cs`, `ContactVisuals.cs`, `Prefabs/Contacts/Bird_*.prefab`, `Data/Contact_Bird.asset` | The flapping the owner remembers is the placeholder from before 2026-10-14. Today's birds glide rigidly. TODO P1d already says so. |
| B | The Quaternius **Pigeon.fbx is a ground pigeon**: armature with bones Body / Head (8 limb nodes) and clips Walk, Idle, Jump, Death, Dance, Bite_Front, Yes, No, HitRecieve. **No fly clip, no wing bones** (read from the FBX contents). The "Bird" (Ernest.fbx) has no armature and imports white. | `Assets/these assets/Pigeon by Quaternius*`, `Bird by Quaternius*` | The pigeon cannot be made to flap without re-rigging. Reuse means "reuse the flap code", not "reuse the pigeon". |
| C | The crowd (dense sky) takes model parts from `MeshRenderer`s only. A rigged pigeon imports as a `SkinnedMeshRenderer`, so the pigeon look most likely has **0 parts** and those crowd birds draw as the 12-triangle "V" even up close (read, not run). Crowd birds never flap (DS-4 deferred it). | `CrowdRender.ExtractParts` | Half the crowd birds may never show a model. Check with `CrowdRenderer.ModelCount[4]`. |
| D | On a **TEAM client**, contact proxies have `ContactMotion.enabled = false` (the host moves them). `Flap` and the rotor spin live in `ContactMotion.Update`, so on every trainee PC **birds do not flap and rotors do not spin** at all. | `LanSession.OnSpawn` | Any flap work must also run on proxies. |
| E | **Swarm triage is missing on TEAM clients.** `SpawnMsg` has no group id, so proxies keep `swarmId = -1`; `swarmRank` needs `motion.Group`, which proxies do not have. | `NetProtocol.SpawnMsg`, `DecisionLoop` line ~183 | A trainee in TEAM mode gets no swarm-triage node. |
| F | Swarm members are scored **both** per contact and in triage. §5.4.6 says swarm members leave the per-contact rows and the engage SDT. Bird-flock members have no `swarmId` at all and are N separate contacts. | `ScoreCalculator.Triage`, `ScenarioSpawner` line ~610 | A flock of 40 adds 40 noise trials to the engage d'/c. |
| G | Swarms step with `Time.deltaTime` and an O(n²) neighbour loop. | `ScenarioSpawner` line ~324, `Boids.Step` | Two SOLO PCs at different frame rates fly **different** swarms from the same seed. 48 drones is fine; 500 birds is 250 000 pair tests per frame. |
| H | The snapshot packs height as absolute world Y in **-64 to 1984 m** around the post, and x/z inside **±4096 m**. The terrain root puts the play-area centre at Y ≈ 0. | `NetProtocol.Snapshot` | On a mountain area (ladakh, Kargil) a contact low over a valley more than 64 m below the post is drawn too high on trainee PCs; a typed spawn ring over ~4 km clamps. |
| I | The dashboard replay reads **this PC's** `ContactRegistry.Records` and `RunLog.view`, whatever seat row is picked. Tracks and views are not in the session file. | `ScoreDashboard.Replay`, `ScoreKeeper` | On the host, a trainee's replay shows the host's tracks and the host's view cone. A past session has no replay. |
| J | `Credits.cs` lists the Quaternius Ultimate Nature Pack and the Kenney city kits as "not used yet", but `SiteProfile_Cantonment` uses them (PROGRESS 2026-10-15). Both are CC0, so this is a correctness fix only. | `Trainer/Credits.cs` | Fix in item 6. |
| K | Both packs are already in the project: the Quaternius pack has 150 FBX (trees in 5 species x seasons, **6 bushes**, 21 rocks, grass, wheat, corn, cactus, stumps, logs); Kenney suburban + commercial kits. The terrain-layer kit uses only primitive placeholders. | `Assets/these assets/` | Item 6 needs **no download**. |

Free keys today (checked by grep): C, K, L, M, O, P, U, V, Y, Z. Used: 1-5, A, B, D, E, F, G, H, I, J, N, Q, R, S, T,
W, X, Tab, Space, Shift, Ctrl, Alt, Esc, PageUp/Down, F1, F2, F6, F7.

---

## 1. Drone swarm that flies like a real swarm

### 1.1 Exists today
- `Scripts/Scenario/Swarm.cs`: `SwarmProfile` (ScriptableObject: separation / neighbour radius, weights, max
  acceleration, spawn spacing, flap rate), `Boids.Step` (Reynolds rules with separation priority, metric neighbours,
  pure function, order-independent), `SwarmPlanner.Layout` (sunflower spiral at spawn), `SwarmGroup` (moves its
  `ContactMotion` members, drops jammed members, arrival despawn, `RankByDistance` for triage).
- `ScenarioSpawner.PlanGroups`: one hostile swarm (size, time, bearing typeable) and N bird flocks from their own seeded
  stream (`seed ^ 0x5A17`). Targets: swarm to the post, flocks across the sector.
- `SwarmSelfTest` 12 checks (determinism with a fixed dt, no collisions at 8 / 24 / 48, flock of 12, triage maths).
- Not built (TODO S1): split / feint / formation tactics. No terrain masking. No jam failsafe variety (a jammed member
  always sinks).
- Dense sky (`Scripts/Scenario/Crowd/`): thousands of cooperative agents in closed form (no neighbour search). Hostile
  swarms stay real contacts by design (plan-dense-sky §11).

### 1.2 What the docs cite
- §1.6 table and §1.9: "Boids + formations + utility AI: split, saturate, feint, terrain masking, failsafe on
  jamming" [doc]. §5.0 stage 4: "boids swarms + flocks, deception" [doc]. §5.4.6 swarm triage (saturation time, count
  error, Kendall τ priority, leakers vs a greedy reference, waste, protective timing) [doc].
- **No paper is cited** for the motion model in solution-v3 or plan-dense-sky. The papers below are [TK].

### 1.3 Algorithm choice

| Model | What it is | Use here? |
|---|---|---|
| Reynolds 1987, "Flocks, herds, and schools: a distributed behavioral model", SIGGRAPH '87 (Computer Graphics 21(4)) [TK] | Separation, alignment, cohesion per agent | **Yes, keep as the core.** It is what `Boids.Step` already does. |
| Reynolds 1999, "Steering behaviors for autonomous characters", GDC [TK] | Seek, arrive, pursuit, evade, wander, path following, leader following, obstacle avoidance | **Yes, for tactics** (approach, feint, split, terrain following). |
| Vicsek et al. 1995, Phys. Rev. Lett. 75:1226 [TK] | Constant-speed particles aligning with noise | **Only as a measure.** Its order parameter (polarisation = length of the mean unit velocity, 0 to 1) becomes a self-test number. As a motion model it is too simple to look real. |
| Couzin et al. 2002, J. Theor. Biol. 218:1-11 [TK] | Zones of repulsion / orientation / attraction plus a blind angle behind | **Yes, for birds** (item 2): the blind angle and zone widths give the swarm / milling / parallel states. |
| Olfati-Saber 2006, IEEE Trans. Automatic Control 51(3):401-420 [TK] | Flocking with a desired spacing d (alpha-lattice), consensus on velocity, navigation feedback to a goal | **Yes, for drones**: replace the separation + cohesion pair with one smooth pair force that is zero at spacing d. Drones then hold an even spacing (a lattice), which is how engineered swarms look and is different from birds. |
| Vásárhelyi et al. 2018, Science Robotics 3:eaat3536, "Optimized flocking of autonomous drones in confined environments" [TK] | A real 30-drone outdoor flock: repulsion, velocity alignment with a "friction" term, speed and acceleration limits | **Yes, for the limits and alignment term**: real UAV numbers (max acceleration, cruise speed) and a damped alignment that stops oscillation. |

**Pick:** one engine, two profiles.
- **Drone profile** = Reynolds steering + Olfati-Saber spacing (desired distance d, typeable) + Vásárhelyi-style damped
  velocity alignment + acceleration / speed limits. On top: a **formation layer** (virtual structure: each member is
  given a slot in a shape that moves with the group's anchor; the flocking rules keep spacing between slots).
- **Bird profile** = Couzin zones with a blind angle + topological neighbours (Ballerini 2008, item 2) + the
  StarDisplay flight rules (Hildenbrandt et al. 2010, item 2).
Why not a new model: every rule above is a small change to the existing `Boids.Step` (a different pair force, a
neighbour list instead of a radius test, a damping term). Nothing is invented.

### 1.4 Attack behaviours (the tactics §1.9 asks for)
Implemented as a small state machine per group on top of the steering rules (Reynolds 1999 seek / arrive / wander /
pursuit). All times and distances are trainer fields (typeable, no caps) with [ours] placeholders.

| Tactic | Behaviour | What it trains |
|---|---|---|
| **Formation** | Members hold slots: V, line abreast, column, wedge, box. Spacing d (default 15 m). Formation turns as one. | Count and shape recognition; a formation looks engineered, a flock does not. |
| **Saturate (time on target)** | The group splits into k sub-groups that approach from k bearings, timed so they arrive within a window (default 10 s). Speeds are set per sub-group from its path length. | Swarm triage: priority by time to impact (§5.4.6). |
| **Split** | At a trigger range (default 800 m) the group splits into 2-3 sub-groups, each with its own aim point (post, cantonment, gate). | Re-counting; allocation. |
| **Feint** | One sub-group runs in, turns away at a trigger range, and loiters; the main group comes from another bearing. | Not fixating on the first threat. |
| **Terrain masking** | Members fly a height above ground (sampled with `TerrainBuilder.GroundHeight`, or the crowd's height grid) and prefer valleys: a steering term toward lower ground along the path. | Late detection behind ridges. |
| **Jam failsafe** | On jam, each member picks a failsafe by class distribution: hover, land, return home, fall (§1.19 [doc]). Today every jammed member sinks. | "Jam worked" is not always "drone gone". |

The swarm engine never reads the player's state (no cheating AI). Tactic parameters come from the seed and the
config only.

### 1.5 How it plugs into the code
- `SwarmProfile`: add `neighbourMode` (Metric / Topological), `topologicalK` (default 7), `blindAngleDeg` (birds 60,
  drones 0), `desiredSpacing` (drones), `alignDamping`, `cruiseSpeed`, `bankLimitDeg`, `formation` (enum),
  `tactic` (enum) + tactic numbers. Old assets keep working: new fields default to today's behaviour (`Metric`, no
  formation, no tactic).
- `Boids.Step` becomes `FlockStep.Step(state, profile, dt)` in a new `Scripts/Scenario/FlockEngine.cs`: pure static,
  arrays in, arrays out, no Unity objects. **Neighbour search through a uniform grid** (cell = neighbour radius, or a
  k-nearest search in the 27 cells round the agent for topological mode). Cost drops from n² to about n x 27 x (agents
  per cell). Keep `Boids.Step` as a thin wrapper so the 12 swarm checks still compile.
- **Fixed time step.** `SwarmGroup.Step` takes the scenario time and runs whole 20 ms ticks (50 Hz, the tick §5.1d and
  F29 plan) with an accumulator, instead of one step of `Time.deltaTime`. Render position = interpolation between the
  last two ticks. Same seed + same decisions = the same flight on every PC and at every frame rate (expected on the
  same build and x64 CPUs; floating point across CPU models is not guaranteed: test it, finding G).
- `SwarmPlanner.Layout` stays for spawn; add `SwarmPlanner.Slots(formation, count, spacing)`.
- `ContactMotion.FlyGroupStep(position, velocity, flapRate)` gains the attitude (item 2.4): quads tilt toward their
  acceleration (tilt = atan(a/g)), fixed wings bank (bank = atan(v²/(g·r))), both from §1.15a [doc] (F21).
- `ScenarioConfig`: `swarmFormation`, `swarmTactic`, `swarmSpacing`, `swarmSubgroups`, `swarmSplitRange`,
  `swarmCount` (more than one swarm). Trainer fields in the F6 panel and SCENARIO tab (NumberEntry pattern).
- **Dense sky:** unchanged. Crowd clusters (light shows) already hold grid / ring slots in closed form. Hostile swarms
  stay real contacts (they must be scorable and jammable one by one).
- **LAN:** TEAM = host-authoritative as today; the snapshot carries every member (90 per message; a 48-swarm is one
  message). SOLO = every PC runs the same fixed-step engine from the start packet's seed.
- Fix E on the way: `SpawnMsg` gets `groupId` and `groupKind` (swarm / flock); the proxy gets `swarmId`; the client's
  `swarmRank` comes from a host-side rank sent with the decision's Effect reply (the host knows the group), or from
  the proxies' distances to the group's target sent in SpawnMsg. Prefer the second (no extra round trip).

### 1.6 Verification
`SwarmSelfTest` grows (target +10 checks): same seed same paths at 30 fps and 144 fps (fixed step); drone lattice
spacing within ±20 % of d after 20 s; Vicsek polarisation > 0.9 for a formation, < 0.9 for a feint split; saturate
arrivals inside the window; split produces k sub-groups; terrain-following never under the floor; no collisions
(closest approach > 3 m) at 48 and 200; grid search equals brute force on 300 agents; 500 agents step in under 1 ms
(Editor, managed; Burst later if needed). The 12 existing checks keep their counts.

### 1.7 Effort, risks, yes needed
- Effort: engine + grid + fixed step 2 days; tactics 3 days; tests and presets ("Night Swarm - Saturate", "Split and
  Feint") 1 day. **About 6 days.**
- Risks: tactics can look scripted if the trigger ranges are fixed (randomise within a band from the seed); the
  formation layer and flocking rules can fight (slot force must be weaker than separation); fixed step changes the
  current swarm paths, so any saved package with a swarm replays differently (version the swarm in the package).
- Owner's yes: none (no package, no download).

---

## 2. Birds: realistic flapping, flocks that fly as flocks, classified as swarms

### 2.1 (a) Audit of the flapping today

| Piece | What it does | Honest look | Cost per bird |
|---|---|---|---|
| `ContactMotion.Flap(rate)` | Rotates `WingPivotL/R` about Z: angle = sin(22·rate·t + phase) x 40°. Lone bird 22 rad/s = **3.5 Hz**; in a flock `flapRate 0.55` = **1.9 Hz**. Called in `Erratic` (lone bird) and `FlyGroupStep` (flock). | Pure sine, no glide, both wings in sync, same amplitude for every bird, no body bob tied to the stroke, no pitch or bank (`Face` flattens the direction). "Flocks flap slower" has no source I know of. | 2 `localRotation` writes per frame: negligible. |
| `ContactVisuals.BuildBird` (placeholder) | Sphere body 0.15 x 0.4, two quad wings on shoulder pivots | Reads as a bird shape at a distance; crude in binoculars; quads are one-sided. | 3 renderers per bird. |
| `Bird_Pigeon`, `Bird_Jay` (used today) | Wrapper prefabs round the Quaternius models | **Do not flap** (finding A); the pigeon is a standing cartoon (finding B); the jay is white (TODO P1d). | Skinned mesh per pigeon even though no clip plays. |
| `Contact_Bird` | size 1.8 m, speed 8-14 m/s, altitude 15-60 m, Erratic | 1.8 m is kite-sized; fine for a black kite, large for a pigeon. | — |
| Crowd birds (`CrowdPattern.Flock`) | Anchor on a slow Lissajous (radius 300-900 m), members on fixed offsets ± a 3 m wobble; yaw only | A rigid cloud sliding across the sky; no flap; every member has the same heading. | ~0 CPU (Burst job); 12-triangle V at LOD1, model parts at LOD0 (but see finding C). |
| On TEAM clients | Proxies' `ContactMotion` is disabled | No flap, no rotor spin (finding D). | — |

What the eye can see at these ranges ([ours], desktop 60° vertical FOV on 1080 px = 18 px per degree; binoculars 8° =
135 px per degree): a pigeon (0.65 m span) at 300 m covers 0.12°, so **about 2 px with the naked eye and 17 px in the
binoculars**. A kite (1.5 m) at 600 m: 2.6 px / 19 px. So flapping matters **mostly through the binoculars** and as a
"twinkle" of a dot at the naked-eye level. That is exactly §1.5's demo moment: "trainee raises optics, sees flap
cadence, rejects" [doc].

### 2.2 (a) Smallest changes (reuse, no rewrite)
1. **A code-built bird mesh** (`Scripts/Scenario/BirdMesh.cs`): body, head, tail and a two-segment wing per side
   (inner + outer, so the outer wing folds on the upstroke), about 80-120 triangles, double-sided, dark grey with a
   slightly lighter underside. Built at run start like the crowd silhouettes (`RuntimeMaterials.Lit` copy, instancing
   on), so it ships in a build and needs no download. Three shapes from one function: pigeon / crow (short broad
   wings), kite (long fingered wings, forked tail), egret (long neck, slow beat). It keeps the `WingPivotL/R` names, so
   the existing `Flap` code drives it on GameObject birds unchanged.
2. **Flap poses for instanced birds**: bake the same mesh at 8 wing angles (one stroke) once at run start. The crowd's
   bucket table already sorts agents into (look, part) buckets; add a pose index: bucket = (look, pose). Pose =
   floor(8 x frac(f·t + memberPhase)). The crowd already stores `memberPhase` per agent. Distance LOD: LOD0 8 poses,
   LOD1 (dot) 2 poses (wings up / down V), so a far bird twinkles. No shader work, no per-instance data beyond the
   matrix, deterministic from the seed (a replay can recompute it).
3. **Real cadence by species** from Pennycuick 1990, "Predicting wingbeat frequency and wavelength of birds", J. Exp.
   Biol. 150:171-185 [TK]: f ≈ m^(3/8) g^(1/2) b^(-23/24) S^(-1/3) ρ^(-3/8). My arithmetic for a pigeon (m 0.35 kg,
   span 0.65 m, wing area 0.06 m², air 1.2 kg/m³) gives about 7.6 Hz [ours, check]. Table [TK, approximate]: pigeon 5-8 Hz,
   crow 3-4 Hz, egret 2.5-3.5 Hz, black kite 2-3 Hz with long glides and soaring circles. Per species in a small table
   in code; the 0.55 "flock" factor goes.
4. **Flap-glide**: intermittent flight (Rayner 1985, "Bounding and undulating flight in birds", J. Theor. Biol.
   117:47-77 [TK]): a bird alternates bursts of beats with glides. Per bird: burst 2-6 beats then glide 0.3-2 s
   (species ranges); more flapping when climbing or accelerating, gliding when descending. Kites soar: long glides and
   slow circling.
5. **Per-bird variation**: phase offset (already there: `bobPhase`, `memberPhase`), size x 0.9-1.1, frequency x
   0.95-1.05, amplitude 35-55°. Body bob of ±2 % span in phase with the downstroke.
6. **Attitude**: pitch along the climb angle and **bank into turns** (bank = atan(v²/(g·r)), §1.15a [doc]); the crowd
   gets bank from the analytic second derivative of `CrowdMath.Pos` (or a 3-point finite difference). `Face` stops
   flattening for birds.
7. **Flap on TEAM proxies** (finding D): move flap and rotor spin into a small `ContactAnimator` component that runs
   even when `ContactMotion` is disabled, driven by the proxy's interpolated velocity. Zero network cost.
8. **Pigeon / jay**: keep them selectable as "standing / perched" looks for later (birds on the wire, the cantonment
   roof); remove them from the flying list. Crowd: if a look has a `SkinnedMeshRenderer`, bake it once with
   `SkinnedMeshRenderer.BakeMesh` instead of skipping it (fixes finding C for any rigged model a teammate drags in).
9. Optional (needs a yes): download a CC0 flying-bird model with a flap clip (TODO P1d). Not needed if 1-6 look right.

**Cost check at sizes** (targets [ours], to be measured with the CPU / GPU profiler):
| Case | Path | Expected cost |
|---|---|---|
| 8-40 birds per flock, 4 flocks (planned contacts) | GameObjects, `Flap` + attitude per frame | < 0.1 ms CPU for the motion; detection per contact is the real cost (existing). |
| 200-500 birds as planned contacts | Same path | Detection, `LateUpdate` ground sample and 2 Hz logging per contact dominate. **Keep planned birds ≤ ~300**; above that use the crowd path. |
| 4000-crowd with 15 % birds (600) | Burst job + bucketed instancing | Pose buckets add up to 8 draw calls per look at LOD0 and 2 at LOD1. Job time unchanged. |

### 2.3 (b) Flocks that fly like flocks (same engine as item 1)
- **Topological neighbours**: Ballerini et al. 2008, PNAS 105(4):1232-1237 [TK], from 3D field data on starlings:
  each bird responds to its nearest **6-7 neighbours**, not to everyone within a fixed distance. This keeps the flock
  cohesive when its density changes (it stretches and compresses without breaking). Use k = 7 by default (typeable).
- **StarDisplay rules**: Hildenbrandt, Carere & Hemelrijk 2010, "Self-organized aerial displays of thousands of
  starlings: a model", Behavioral Ecology 21(6):1349-1359 [TK]. What we take: topological interaction (6.5 neighbours),
  a cruise speed that each bird returns to, **banked turning** (a bird changes heading by rolling, not by sliding
  sideways), attraction to a roost / home area so the flock stays over the sector, and small random perturbation. That
  is what makes the flock's shape flow and fold, and it is the model's claim to realism.
- **Couzin zones** for small flocks (crows, egrets): repulsion inside 2 body lengths, alignment to ~10, attraction
  beyond; blind angle 60° behind.
- **Density waves** in the crowd's closed form (no simulation, keeps LAN sync free and the replay recomputable): member
  offset += a travelling wave along the flock's long axis, amplitude 2-5 m, speed ~10-20 m/s. Starling flocks show such
  waves under predator attack (Procaccini et al. 2011, Animal Behaviour 82(4):759-765 [TK]). This is a look only.
- **Murmurations are not normal over an Indian post.** Big wheeling starling or rosy-starling flocks happen at dusk near
  roosts [TK]; the common hazards near Indian cantonments and airfields are black kites, pigeons, crows, mynas and
  egrets [TK]. So: species profiles (kite: soaring circles, loose groups of 3-20; pigeon / crow: flocks of 10-60;
  egret: lines and V; starling: murmuration of hundreds to thousands only in a "dusk roost" scenario). Ask an officer
  which birds his post sees (open question).
- Same engine as the drones: `FlockEngine` with the bird profile. Planned flocks run it (fixed step, seeded, TEAM by
  snapshot). Crowd flocks stay closed form with the wave look. Upgrade path if the owner says they look fake: run crowd
  flocks through the same engine in a Burst job with 1 s keyframes (then the replay needs the keyframes, not a formula).

### 2.4 (c) Classifiable as swarms

**Today:** each flock bird is its own contact and record. The trainee can lasso (Tab) or select all in view (G) and
press 5 (Bird) once; the records get `group = true`. Scoring is per bird: 40 birds = 40 rows and 40 noise trials in
the engage SDT (finding F). Bird flocks never enter swarm triage (`swarmId` only for hostile classes). Hostile swarm
members are scored per contact **and** in triage. The report proforma in §1.5 has "what (class, **count**)" [doc]; no
count is captured today.

**Design: one group call, one group score.**
1. Every member of a planned swarm or flock gets `groupId` and `groupKind` (Swarm / Flock / Formation / Mixed) in
   `ContactRecord` (new fields, keep `swarmId` for the old tests). Sent in `SpawnMsg` (fixes E).
2. **Group call: K key** ("kind and count"), with 3 or more contacts selected (lasso / G / Ctrl+click): a small strip
   "DRONE SWARM | BIRD FLOCK | MIXED" (1 / 2 / 3) and a count field pre-filled with the number selected (the trainee
   can type an estimate; the true number is never shown). VR: the radial menu gets a "Group" item. Logged as a
   `swarm_declare` event (§5.7.3 already names it [doc]).
3. **Scoring (new group node, §5.4.6 extended to flocks):**
   | Truth \ group call | Drone swarm | Bird flock | Mixed | none by deadline |
   |---|---|---|---|---|
   | Hostile swarm | correct (T_sat, count error, triage as §5.4.6) | **missed swarm**: Critical under every card | partial | clock miss |
   | Bird flock | **false swarm**: Minor; Major if it leads to Jam / Engage (waste, §5.4.6 "W") | correct rejection | Minor | correct rejection (no call needed) |
   | Flock with a bird-mimic inside | partial | **missed hostile** of its class (same as a missed formation shadow, §1.17c) | correct | clock miss |
   Count error = |estimate - true| / true [doc]; T_sat = time from the group entering the sector to the K call [doc].
4. **Rows and SDT**: members of a group the trainee called leave the per-contact rows (one expandable group row in the
   AAR) and count as **one** trial in the engage SDT (signal = hostile group). Members acted on one by one (jammed,
   engaged) still get their own ACT node, because shooting a bird out of a flock is a real decision. This matches
   §5.4.6 ("members leave the per-contact N1-N4 rows and the engage SDT") and stops one flock swamping d'/c.
5. **AAR / black box**: group row with kind, count truth vs estimate, T_sat, first member in view, members engaged,
   leakers; the 3D replay (item 5) draws a hull round each group with its called kind as a label; the event list shows
   `swarm_declare` with the time.
6. **Bird-mimic drone** (TODO S5; §1.17c "Bird-mimic ornithopter: still later"; Appendix X2 "excluded from the
   prototype" [doc]). If the owner says yes: a hostile that flies **inside a bird flock** on the same engine, uses the
   bird mesh, but flaps at a constant frequency with no glides, does not bank with the flock, and climbs straight. Cues
   a trainee can learn: too-regular cadence in the binoculars, no flap-glide rhythm, RF link present on the sensor
   panel. Default share 0; Expert preset only. Needs the owner's yes because the doc excludes it.

### 2.5 Effort, risks, yes needed
- Effort: bird mesh + pose buckets + attitude + flap-glide + proxy animator 3 days; flock profiles (StarDisplay-lite,
  Couzin, crowd wave) 1.5 days; group call + scoring + AAR rows + LAN fields 3 days; mimic 1 day if approved.
  **About 7.5 days (8.5 with the mimic).**
- Risks: a code-built bird can look like a paper plane in the binoculars (judge on screen early, day 1); moving swarm
  rows out of the per-contact table changes the 77 / 36 scoring checks (add a rules flag `groupScoring`, default ON for
  new runs, OFF inside the old self-tests); species frequencies need a check; grouping must not leak truth (the K strip
  appears for any selection of 3, including mixed or crowd tracks).
- Owner's yes: bird-mimic in the prototype (Q1); optional flying-bird model download (Q2).

---

## 3. Working multiplayer

### 3.1 Exists today
- `Scripts/Net/LanSession.cs` (948 lines), `NetProtocol.cs`, `NetSelfTest` (11), `TeamSelfTest` (27). NGO 2.13.3 named
  messages over Unity Transport, direct IP, port 7777, no relay, no networked objects.
- Lobby (TrainerMenu LOBBY tab), host / join by IP, READY, SOLO (same scenario, own sky) and TEAM (host's sky, 10 Hz
  snapshots, 90 contacts per message), back to lobby, area transfer (256 KB chunks), lost-host banner, results to the
  host (live every 5 s, final at the end), roles SENTRY / COMMANDER / ANY, report relay and `ReportBoard`, crowd over
  LAN (host clock, CrowdPick, crowdIndex), handled-by-team, command line `-chkName -chkJoin -chkPort -chkReady
  -chkTestBot`.
- **Verified only on one PC** (Editor host + built client over loopback): Solo and Team runs, area transfer of Kargil
  (11.4 MB), roles, report relay, crowd picks, T + J (PROGRESS 2026-10-16 / 17).
- **Never verified:** two PCs, the Windows firewall prompt, a real switch / Wi-Fi, a commander on a client PC,
  `RemoteAct` live (remoteDecisions stayed 0), the handled and late-report waivers live, lost host live, any of the IMGUI
  (Lobby, board, key panel, roster rows), real-time play (the Editor was stepped).

### 3.2 Broken or missing (from reading the code)
| # | Problem | Fix | Days |
|---|---|---|---|
| M1 | Snapshot height range -64..1984 m and ±4096 m box (finding H) | Height -2048..+2048 m (6 cm steps); box from the config (spawn ring x 2, at least 4096) sent in the start packet | 0.25 |
| M2 | No build check: two different builds can join and silently disagree | `Hello` carries `Application.version` + `Application.buildGUID` + protocol number; the host refuses a mismatch with a clear Lobby line | 0.25 |
| M3 | Join only by typed IP | LAN discovery: the host answers a UDP broadcast on port 47777 ("Kurukshetra host <name> <ip>:<port>"); the Lobby lists hosts found; typing an IP still works. `System.Net.Sockets.UdpClient`, no package | 0.75 |
| M4 | Clock: "last snapshot + elapsed" only | 4-ping median offset at start (§5.6 [doc]) and every 10 s; host time = local time + offset; drift line kept | 0.5 |
| M5 | No player state on the wire | `PoseMsg` at 10 Hz from every seat to the host (position, head yaw / pitch / roll, FOV, binoculars, lock id, PTT): about 40 B, 400 B/s per seat. Feeds the 3D replay and voice (item 5); the host relays it to TEAM seats so teammates can be drawn | 0.5 |
| M6 | Swarm triage missing on TEAM clients (finding E) | `SpawnMsg.groupId / groupKind / rank data` (item 1.5) | in item 1 |
| M7 | Flap and rotors frozen on proxies (finding D) | `ContactAnimator` (item 2.2) | in item 2 |
| M8 | SOLO skies diverge by frame rate (finding G) | fixed step (item 1.5) | in item 1 |
| M9 | Host is always a playing seat | Optional `SeatRole.Observer` for the instructor: unscored, sees the truth map and every seat, no own sky decisions (owner question Q6) | 0.5 |
| M10 | Snapshots at 10 Hz may stutter | Keep 10 Hz; raise to 20 Hz only if the two-PC test shows stutter (§5.1d [doc]) | 0.1 |
| M11 | Late join / rejoin during a run | Refuse with a clear message (today's behaviour is unknown); rejoin later | 0.25 |

### 3.3 Two-PC test plan (owner's hands; about 90 minutes)
Needs: two Windows PCs, one wired switch or router (Wi-Fi works but test wired first), the same build folder copied to
both (including `ChaukannaData/`), a headset or speakers not needed.
1. Build once (File > Build Profiles > Windows > Build) into a folder outside the project. Copy the whole folder to
   PC2 (USB stick).
2. On PC1, start `Chaukanna.exe -logFile host.log`. TrainerMenu > LOBBY > HOST. When Windows asks "Allow access",
   tick **Private networks** and press **Allow**. Write down the address the Lobby shows (for example 192.168.1.10).
   Pass: Lobby says hosting on port 7777.
3. On PC2: Start > type `cmd` > Enter > type `ping 192.168.1.10` > Enter. Pass: replies. If not: both PCs on the same
   network, network type "Private" (Settings > Network & internet > your network > Private).
4. On PC2, start `Chaukanna.exe -logFile client.log`, LOBBY > type a name > type PC1's address > JOIN. Pass: PC1's
   seat list shows PC2 within 3 s.
5. **SOLO**: PC1 mode SOLO, PC2 READY, PC1 START, preset "Crowded Sky". Play 3 minutes on both. Pass: both PCs run the
   same scenario; PC1's SCORE dashboard shows PC2's live result updating every 5 s and the final result at the end.
6. **TEAM, host commander**: PC1 mode TEAM, PC1 = COMMANDER, PC2 = SENTRY. START. On PC2 press H / J / F. Pass: the
   refusal line for 2 s. On PC2 lock a contact and press R. Pass: PC1's report list shows it within a second, amber
   bracket on it; press T on PC1: the view swings to it and locks.
7. PC1 presses J on that reported quad. Pass: on PC2 the same drone sinks; PC2's AAR says "handled by team" for it.
8. **Positions match**: both PCs look at the same contact and say its number and rough bearing aloud. Pass: same id,
   same place, smooth motion (no jumps; write "stutter yes / no").
9. **TEAM, commander on the client** (never run): back to lobby, swap roles (PC2 = COMMANDER, PC1 = SENTRY). Repeat 6-7
   with the roles swapped. Pass: PC2 jams and the drone falls on PC1.
10. **Area transfer**: on PC1 pick a real-terrain area PC2 does not have, START. Pass: PC2 shows a progress line, then
    the same terrain.
11. **Lost host**: during a TEAM run pull PC1's network cable for 10 s. Pass: PC2 shows the lost-host banner and saves
    its run.
12. **Cable-pulled offline** (FR-19): unplug the switch's uplink to the internet (keep the two PCs on the switch) and
    repeat step 5. Pass: everything works.
13. Send back: `host.log`, `client.log`, the two `Player.log` files from
    `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Chanakya\`, a phone photo of each screen at step 6, and the
    stutter yes / no from step 8.

### 3.4 Effort, risks, yes needed
- Effort: M1-M5, M9, M11 **about 3 days**; the test session half a day of the owner's time; fixes found by it 1 day
  (budget).
- Risks: firewall or "Public network" profile blocks the port (the owner must allow it by hand: changing firewall
  rules is a system security setting, so I will not script it); Wi-Fi jitter; NGO refuses client-to-client named
  messages, so everything (reports, poses, voice) is relayed through the host (already the pattern).
- Owner's yes: the second PC and a date (Q5); the Observer seat (Q6).

---

## 4. AAR cards: dashboard, report, exports

### 4.1 Exists today
- `Trainer/ScoreDashboard.cs` (IMGUI, 720p layout units scaled by `GUI.matrix`): header buttons (Expand, Close,
  HTML report, Charts / Overview), roster with roles, big numbers, counts, confusion heatmap, DECISION QUALITY (process
  score, 2 x 2 process x outcome grid, clock, d'/c sliders gated at 20), LEARNER MODEL, flowchart decision tree with 5
  checkpoints, timeline, REPLAY (2D), every contact, trends; `DashboardCharts.cs` built-in charts; optional XCharts tab.
- `Scoring/HtmlReport.cs`: one self-contained HTML file in `<persistentDataPath>/reports/aar_<time>_seed<seed>.html`
  (tiles, TC 25-20 debrief, decision quality, SDT, prevalence, dense sky, team, vigilance, card change, swarm triage,
  SVG timeline, canvas replay, contact table, confusion, A4 print CSS, the disclaimer). Opened with `Application.OpenURL`.
- Session JSON in `sessions/`. **Missing**: card layout, PNG cards, raw telemetry (F17 / §1.18c), unit pack for all
  seats, "open folder", copy to USB, the one-page company summary (TODO A2), the signed batch export (§1.11).
- Nothing on the dashboard has been seen on screen since 2026-10-16 (unfocused Editor).

### 4.2 Card layout (IMGUI)
- **Grid**: 12 columns in layout units. Cards span 4 (third), 6 (half) or 12 (full). Columns: 3 at ≥ 1600 layout px,
  2 at ≥ 1100, 1 below. Card heights in two sizes (180 and 380 units) so rows line up.
- **Order: weakest link first.** The top-left card is the node that lost the most points this run ("Biggest problem:
  clock ran out on 3 hostiles"), then the fixed order below. This is the owner's working style turned into UI.
- **Card anatomy** (same for every card):
  1. Title strip: 11 px caps, muted, a coloured status square (good / warn / bad / neutral) **and** a word, so colour is
     never the only signal.
  2. Hero number: 30 px bold (for example `0.62 s`, `78 %`, `d' 1.4`).
  3. One plain sentence (officer words, §5.4.8 [doc]): "Can tell them apart: yes", "Trigger-happy", "Missed 2 of 3
     swarm leakers".
  4. Mini chart, 60 units tall: sparkline, bar, 2 x 2 grid or slider. Drawn into a cached `Texture2D` that is rebuilt
     only when its data changes (the crowd truth-map trick), so a repaint is one `GUI.DrawTexture`.
  5. Footer: `n = 23 · 95 % interval 61-82 %` (§1.11 "every figure shows n and an interval" [doc]) and "Details >".
- **Style tokens** (dark, matches the HTML report's `#0e141b` replay background): page `#0E141B`, card `#16202B`,
  border `#26323F`, text `#E6EDF3`, muted `#8B98A5`, accent `#4C9AFF`, good `#3FB950`, warn `#D29922`, bad `#F85149`.
  Rounded corners: one 16 x 16 runtime texture with rounded alpha corners used as a 9-slice `GUIStyle.normal.background`
  (`border = 6`); a second one offset by 2 units as the shadow. No asset, no package.
- **Interaction**: click a card = the existing detailed section opens in a drawer on the right (all current section
  code is reused as the detail view); hover = tooltip with the symbol definitions (d', c, τ) as §5.4.8 asks; small
  "PNG" button on each card.

### 4.3 The cards (all from metrics that exist today unless marked new)
| Card | Shows | Source fields |
|---|---|---|
| Mission | trainee (pseudonymous), package, seed, conditions, card schedule, display tier, disclaimer | `SessionResult` header fields |
| Detection | avg time to detect, search failures (N1a), perception failures (N1b), sparkline of detection times | `averageDetectionSeconds`, `searchFailures`, `perceptionFailures`, `ContactResult.detectionSeconds` |
| Classification | accuracy %, mini confusion heatmap, worst confusable pair | `classificationAccuracy`, `confusion*` |
| Decision clock | clock misses, hold delay, decisions after release point | `clockMisses`, `holdPenalty`, decision clock fields |
| Process x outcome | the 2 x 2 grid, process score | `gridSound*`, `gridLucky`, `gridUnsound`, `processScore` |
| Report and engage SDT | d', c on a "Trigger-happy ↔ Hesitant" slider, gated at 20 | `reportSdt`, `engageSdt` |
| ROE card change | how the trainee adapted after the flip | `cardChange.summary` |
| Swarm / flock triage | T_sat, count error, nearest-first, leakers, flocks called right (new from item 2) | `swarm`, new group fields |
| Team chain | sentry reports + lock-to-report, report-to-decision, first-fault waivers, report-alone engages, hand-off fratricide | MP-1 fields |
| Dense sky / IFF | tracks, checked, IFF challenges, friendlies hit | `crowd*`, `iffChallenges` |
| AI cue | wrong hints shown / followed / overridden | `aiHintsShown`, `wrongAiHints`, `followedWrongHint`, `overrodeWrongHintCorrectly` |
| Learner model | weakest factors with intervals, next suggested run | `LearnerModel` |
| Vigilance | thirds of a quiet watch | `vigilance` |
| Debrief | the four TC 25-20 questions (templated sentences, exists in HtmlReport) | `HtmlReport.Debrief` |
| Comms (new, item 5) | talk time per seat, PTT count, overlaps, report-to-voice lag | voice log |
| Unit (host only) | one row per seat with its hero numbers; click = that seat's cards | `NetPlayerSource` |

### 4.4 HTML report
Same cards in a CSS grid (3 / 2 / 1 columns by width; 2 per row in print), same tokens, the existing inline SVG for mini
charts. Keep: one file, no network, A4 print, the disclaimer on every page (`@page` footer). Add: "Print / Save as
PDF" button (`window.print()`; the browser's own "Save as PDF" makes the PDF, no library), and a link
"Raw telemetry (zip)" to the file next to it (relative link, works from a USB stick).

### 4.5 Exports and where files go
One folder per session: `<persistentDataPath>/reports/<yyyyMMdd_HHmmss>_<seed>_<player>/` (persistentDataPath =
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Chanakya`).
| File | Made by | Note |
|---|---|---|
| `aar.html` | HtmlReport (exists, restyled) | opens in any browser |
| `cards/<name>.png` | `ScreenCapture.CaptureScreenshotAsTexture()` at end of frame, cropped to each card rect, `EncodeToPNG` | `screencapture` module is already in the manifest. Cards render one by one off the visible scroll if needed. |
| `telemetry.zip` | item 5 black box: `events.jsonl`, `operator_state.csv`, `world_truth.csv`, `scorecard.csv`, `manifest.json` with SHA-256 per file (§1.18c [doc]) | `System.IO.Compression.ZipArchive` + `System.Security.Cryptography.SHA256`. Use `ZipArchive` only (no `ZipFile`, which may need a `csc.rsp` outside Assets/Chaukanna). Check it compiles on day 1. |
| `session.json` | ScoreKeeper (exists) | |
| `voice/*.wav` | item 5 | only if recording is on |
Dashboard header gets one **Export** menu: Open HTML, Print / PDF, Card PNGs, Raw telemetry, **Open folder**
(`Application.OpenURL("file:///<folder>")` opens Explorer), **Copy to USB** (lists removable drives with
`System.IO.DriveInfo.GetDrives()` where `DriveType == Removable`, copies the folder, shows the path). On the host in a
LAN session: **Unit pack** = every seat's session folder (each trainee PC sends its folder at the end over the existing
chunked transfer, the area-transfer code path) under `reports/<date>_unit_<seed>/`. That is how a trainer "downloads"
offline: Open folder or Copy to USB. The one-page company summary (TODO A2) and the signed export (§1.11) stay later.

### 4.6 Effort, risks, yes needed
- Effort: card framework (grid, styles, textures, drawer) 2 days; 16 cards 3 days; HTML restyle 1.5 days; exports
  (PNG, folder, USB, unit pack) 1.5 days. **About 8 days.**
- Risks: IMGUI layout has never been seen on screen this month (book the owner's eyes after day 2); `ScreenCapture`
  captures the whole window (cards hidden behind the scroll need a second pass); USB rules on a unit PC (§1.11 "ask
  the officer contact" [doc]).
- Owner's yes: none for code. The removable-media rule is a question for the unit (Q8).

---

## 5. Black box, 3D replay, proximity voice and comms review

### 5.1 Exists today
- Logged at **2 Hz**: each contact's `track` (x, y, z, inView) and `rangeTrack`; the soldier's `RunLog.view` (yaw,
  pitch, horizontal FOV, x, z). Every call / action / anomaly as text in `ContactRecord.history`; events in
  `ContactRegistry.Events`.
- 2D top-down replay in the dashboard (slider, play, 10x speed, view lines, click for "your call vs the sound call")
  and a canvas replay in the HTML. Lines are unclipped: the **green-lines bug** (FIX-2b).
- Only in memory: tracks and views are not in the session file, and the host shows its own records for every seat
  (finding I).
- No audio except the card-change tone (`RoeBanner`, a generated `AudioClip`). No microphone code anywhere.
- solution-v3: **F17** black box (§1.18): operator state at **10 Hz**, every operator action, world truth at 2 Hz + a
  1 s keyframe, scrubber (play / pause, x0.25-x8, drag back and forward, step, jump to event), first-person replay with
  the HUD, operator graphs, raw telemetry zip; **F25** sensor toggle with AZ / EL / ZOOM logged; **F26** practice from
  this moment. Voice: §1.5 and §1.14 "push-to-talk, recorded, unscored" (chairman ruling R2) and §5.6 "recorded locally
  per seat for the AAR, not sent through the scoring path" [doc]. TODO FR-3, FR-13, MP-3 (voice), A2.

### 5.2 Black-box recorder (FR-3)
- New `Scripts/Scoring/BlackBox.cs`: a ring buffer flushed to disk once a second on a worker thread (§5.1d "never
  blocks a frame > 1 ms" [doc]). Files in the session folder (4.5).
- Channels and sizes [ours]:
  | Channel | Rate | Fields | Size |
  |---|---|---|---|
  | Operator state (per seat) | 10 Hz | t, x, y, z, head yaw / pitch / roll, FOV, zoom, binoculars, view mode, VR head pose, lock id, selected count, sensor, card, PTT | ~60 B binary = 36 KB/min |
  | Operator actions | each | lock, unlock, classify, report, hold / jam / engage, every shot, IFF, lasso, group call, sensor toggle with AZ / EL / ZOOM, binoculars up / down, F7, PTT on / off | small |
  | World truth | 5 Hz + 1 s keyframe | id, x, y, z (16-bit quantised like the snapshot), yaw, pitch, roll, state, group, inView per seat | 150 contacts ≈ 0.5 MB/min |
  | Crowd | none | recomputed from `CrowdMath.Pos` and the seed (closed form); promoted ids from the events | 0 |
  | System | each | card changes, Director injections, frame spikes, LAN join / leave, clock offset | small |
  A 20-minute TEAM run with 6 seats and 150 contacts ≈ 10 MB truth + 4 MB seats before zip. Kept for every session.
- 5 Hz truth instead of the doc's 2 Hz: at 2 Hz a turning swarm or a banking bird cuts corners in a 3D replay. Cubic
  interpolation between samples using the logged velocity.
- **LAN aggregation**: each seat records its own operator state; in TEAM it also streams `PoseMsg` (3.2 M5) so the host
  holds the unit's picture live; at the end every seat sends its files to the host (unit pack). The host records world
  truth for TEAM. In SOLO each seat has its own sky, so each seat's black box replays separately.

### 5.3 3D replay viewer
- **Where**: the MixedSky field scene in a replay mode (a flag in `SessionSetup`), not a new scene: a new scene would
  need a Build Settings change (ProjectSettings), which needs the owner's yes. The terrain is rebuilt from the saved
  terrain config + layers (as a LAN client does from a start packet).
- **What is drawn**: contacts as ghost models (`ContactVisuals.Build`, no `ContactMotion`, no scoring, flapping from
  item 2's pose maths); crowd recomputed; each seat as a soldier marker (capsule + head + a view frustum mesh from the
  logged FOV: wide = eye, narrow and bright = binoculars, as plan-replay-fix §3 does in 2D); a line from the seat to its
  locked contact; 3D labels at events ("S-014 REPORT Hostile quad", "CMDR JAM", "ROE C > A"); group hulls with the
  called kind (item 2.4); voice bars on the timeline.
- **Controls (IMGUI bar at the bottom)**: play / pause (Space), speed x0.25 / x0.5 / x1 / x2 / x4 / x8, scrub slider
  that drags back and forward (seek = nearest 1 s keyframe before, then interpolate), step one sample (, and .), next /
  previous event (PageUp / PageDown), event markers on the slider coloured by type, operator graphs under it (camera
  azimuth, zoom, sensor, trigger pulls, §1.18a [doc]).
- **Cameras**: **Follow seat** (first person from the logged pose and FOV, with that seat's HUD redrawn from logged
  state: lock brackets, ROE banner, lock label) = "what they saw"; **Chase** (behind and above a seat); **Free**
  (WASD + mouse, like `ObserverMove`); **Top-down** (the fixed 2D replay map). Truth overlay toggle (class colours).
  Label it honestly: "reconstructed view from the logged pose, not a screen recording". A screen video per seat would
  need a video encoder and gigabytes; not planned.
- **Replays of past sessions** load from the black-box files (fixes finding I).

### 5.4 Proximity voice over the LAN
**Model.** Two ways to talk, both push-to-talk (§1.14 [doc]):
- **Voice (V)**: what a person next to you hears. 3D, attenuated by distance (inverse distance, full volume inside 1 m,
  silent beyond ~40 m [ours]). Seats on the same post hear each other plainly.
- **Radio**: half-duplex net: no distance loss, band-pass 300-3000 Hz, light distortion, squelch click at start and
  end, one talker at a time (a second talker gets a busy tone). For seats far apart (a commander off the post) and for
  the realistic "radio report" habit.
Keys: **V** voice, **Y** radio (both free on the keyboard; B is taken). VR: the controller's Y button is already the
commander's slew, so PTT goes on a free combination (for example left grip + left trigger held), chosen on the headset
in `XrInput`.

**Unity APIs (all built-in modules, no package):**
- Capture: `Microphone.Start(device, loop: true, lengthSec: 1, frequency: 16000)` returns a looping `AudioClip`;
  `Microphone.GetPosition` + `AudioClip.GetData` read new samples every frame into 20 ms frames (320 samples).
- Encode: **G.711 µ-law** (ITU-T G.711, 1972 [TK]): 16-bit to 8-bit, about 20 lines of code, public algorithm. 16 kHz
  mono µ-law = 16 000 B/s = **128 kbit/s**.
- Send: `CustomMessagingManager.SendNamedMessage("chk.voice", host, writer, NetworkDelivery.UnreliableSequenced)`;
  header: seat id, channel (voice / radio), sequence, sender scenario time (ms). The host relays to every other seat
  (NGO clients cannot address each other; same pattern as reports).
- Play: per speaker an `AudioSource` on that seat's marker, `spatialBlend = 1`, `rolloffMode = Logarithmic`,
  `minDistance 1`, `maxDistance 40`, fed by `AudioClip.Create(name, len, 1, 16000, stream: true, PCMReaderCallback)`
  from a 60-100 ms jitter buffer (drop late frames, fill gaps with silence). Radio: `spatialBlend = 0` plus built-in
  `AudioHighPassFilter` (300 Hz), `AudioLowPassFilter` (3 kHz), `AudioDistortionFilter` (0.2).
- Position of each speaker: from `PoseMsg` (3.2 M5).
- No `Microphone` permission dialog exists in a Windows desktop build, but Windows can block it: Settings > Privacy &
  security > Microphone > "Let desktop apps access your microphone" must be On (NEEDS USER step).

**Codec options:**
| Codec | Rate (16 kHz mono) | Storage per minute of talk | Needs |
|---|---|---|---|
| PCM 16-bit | 256 kbit/s | 1.92 MB | nothing |
| **µ-law (recommended)** | 128 kbit/s | 0.96 MB | nothing (own 20 lines) |
| µ-law at 8 kHz | 64 kbit/s | 0.48 MB | nothing; phone / radio quality |
| Opus (RFC 6716 [TK]) | ~16-24 kbit/s | ~0.15 MB | a third-party library (for example the C# port "Concentus" [TK]): a **download, needs the owner's yes** |
| Vivox (Unity Gaming Services) | — | — | **Rejected**: a cloud service; breaks "zero runtime network dependencies" (§5.1c [doc]) |
On a wired LAN 128 kbit/s is nothing: one talker relayed to 5 seats = 0.64 Mbit/s from the host; all 6 talking at once
(rare) = 3.8 Mbit/s. With push-to-talk the average is far lower.

### 5.5 Comms review: two tracks
1. **Clean track per speaker**: each PC writes its own microphone frames (only while PTT is held) to
   `voice/<seat>_clean.wav` with each frame's scenario time in a side file `voice/<seat>_frames.csv`. The host also
   keeps every relayed frame, so the unit pack has all speakers even if a PC dies.
2. **What a player heard**: **rendered on demand at replay time** from the clean tracks + the logged positions + the
   same filter chain (distance gain, radio filters). The chain is deterministic, so this is exact for what we did to
   the sound; it cannot include the room or the player's headphones. Optional switch "also record the heard mix" (off
   by default): an `OnAudioFilterRead` tap on the AudioListener object writes the voice mix at 16 kHz mono µ-law
   (0.96 MB/min per PC, continuous) for checking.
- **Sync**: every frame carries the sender's scenario time (on clients converted with `HostToLocal`, so all speakers
  share the host clock). The replay plays a speaker's frames through the same streamed `AudioSource` path with the
  replay time as the clock; a seek drops the buffer and starts at the first frame at or after the new time. Speed
  other than x1 mutes voice (or plays x0.5-x2 with pitch change; decide on screen). The timeline shows one bar per
  speaker per PTT press; clicking a bar jumps there.
- **Privacy**: voice is personal data. A one-line notice in the Lobby and on the field's first frame: "Voice is recorded
  on this PC and the trainer's PC for the after-action review." Recording is a trainer setting (default on for TEAM
  runs, as §1.5 asks), files stay in the session folder on local PCs only, pseudonymous seat names, a "delete voice of
  this session" button, the disclaimer on the comms card, and an age-out setting (delete after N days). India's Digital
  Personal Data Protection Act 2023 [TK] may apply; the unit decides its rules (Q7). Voice is never scored (§1.5
  ruling) and never leaves the LAN.

### 5.6 Replay green-lines fix (FIX-2b, separate small item)
Unchanged spec in `plan-replay-fix.md`: `GuiDraw.ClipSegment` (Liang-Barsky), clipped `Line` and `Wedge`, the replay map
inside `GUI.BeginGroup`, soldier arrow + path, contact trails, event labels, binocular wedge, Follow toggle, the same in
the F6 truth map and TrainerMenu map, `ReplaySelfTest`; plus the terrain fallback-tree warning and the Kargil / surround
OSM re-fetch (a download already approved once; ask again before more tries). **1 day.** Do it first in this block: the
2D replay stays as the top-down camera of the 3D viewer.

### 5.7 Effort, risks, yes needed
- Effort: FR-3 recorder 2.5 days; LAN aggregation + unit pack 1 day; 3D replay viewer 5 days; voice capture / codec /
  relay / 3D playback 4 days; clean + heard tracks and sync 2.5 days; FIX-2b 1 day. **About 16 days.**
- Risks: scenario-time stamps across PCs (depends on M4 clock sync); `PCMReaderCallback` runs on the audio thread
  (lock-free buffer needed); echo and feedback with open speakers (headsets required; no echo cancellation is built
  in); microphone device names differ per PC (pick the default, show it in the Lobby); 3D replay needs the same
  terrain and layers (a session whose area folder was deleted cannot rebuild: store the area name + terrain config +
  layer settings in the black box and refuse clearly); IMGUI + 3D replay never seen on screen.
- Owner's yes: voice recording and its retention rule (Q7); Opus only if µ-law is not good enough (Q7); no package
  otherwise. Headsets with microphones per PC (hardware).

---

## 6. Procedural trees, bushes and buildings

### 6.1 Exists and works (self-tested, not seen on screen by the owner)
- TL-1 (`Terrain/Scripts/`): `TerrainLayerKit` (prefab slots, `Resources/Chaukanna/TerrainLayerKit_Default.asset`),
  `TerrainLayerSettings` (trees / grass / buildings each Off / Realistic / Procedural, typeable densities, tree budget
  60 000, LOD bias 0.2, fall-off 1.5 km, trees block sight), `OsmLayers` (Overpass, curl.exe fallback), `LayerData`,
  `LayerGeometry` (ear clipping with holes), `SatelliteCover` (excess-green estimate), `LayerScatter` (hash randomness:
  mapped trees, tree rows every 6 m, forest polygons on a jittered grid, satellite cells, procedural woods from a
  low-frequency noise mask + tree lines, slope / tree-line / snow rules, villages on flat ground as jittered grids),
  `GrassField` (own GPU instancing; terrain detail layers drew nothing), `TerrainLayers` (terrain trees with LOD prefabs
  and trunk colliders; buildings extruded per 250 m cell with MeshCollider + LOD boxes; far buildings merged per 1 km).
  `LayersSelfTest` 58/58.
- `Scenario/Site.cs`: the cantonment / rural site already uses Kenney buildings and Quaternius trees and bushes.
- Measured (Editor, this PC's GPU, not a GTX 1650): layers cost about +3 ms (telaviv) to +8 ms (Area, dense woodland)
  per frame; build 0.6-4.2 s spread over ~70 loading frames.

### 6.2 Missing
1. **Art**: the kit has 2 primitive trees, 6 box buildings, 1 grass clump, while 150 Quaternius models and two Kenney
   city kits (all CC0) sit unused in the project (finding K).
2. **Bushes / shrubs and rocks**: no layer at all.
3. **Species by place**: one tree mix everywhere (no pine on hills, palm on the coast, dead / snow variants high up).
4. **Natural spacing**: the jittered grid shows rows at low density; no clustering of bushes round trees.
5. **OSM tags** not used: `natural=scrub`, `natural=heath`, `landuse=residential` (for procedural fill), `roof:shape`,
   `building:colour` (TL-3 g).
6. **Building variety**: all extruded footprints are flat boxes with one roof colour.
7. **GTX 1650 numbers**: never measured (TL-2 step 10).

### 6.3 Design (smallest steps)
1. **Wire the existing art** (0.5 day): kit tree list = Quaternius PineTree, CommonTree, BirchTree, Willow (5 variants
   each) + Snow / Dead variants; building lists for procedural villages = Kenney suburban houses (house, shed) and
   commercial (commercial, apartments); add a `bushPrefabs` and `rockPrefabs` list (Quaternius Bush, BushBerries,
   Bush_Snow, Rock, Rock_Moss, Rock_Snow). Wrap each in a prefab under `Prefabs/LayerKit/` with LODGroup + trunk
   collider (BuildLayerPlaceholders already does this for primitives). Fix `Credits.cs` (finding J).
2. **Species rules by biome** (1 day): reuse `SurfaceBiome` (already used by `ClimateTreeFactor`): pine above 1500 m and
   on slopes > 20°; birch / common on plains; willow near water (OSM water when TL-3 g lands); palm in the coastal site
   (FR-15); snow variants above the snow line, dead variants near the tree line.
3. **Poisson-disc spacing** (0.5-1 day): Bridson 2007, "Fast Poisson disk sampling in arbitrary dimensions", SIGGRAPH
   sketches [TK]: random points with a minimum gap r in O(n). Use it inside forest polygons and procedural woods
   (r from the density: r ≈ 0.8 x sqrt(10 000 / perHectare) [ours]), seeded per polygon and cell so it stays
   deterministic on every LAN PC. Clumping: modulate r by the existing noise mask (dense centre, sparse edge).
4. **Bush layer** (1-1.5 days): a fourth `LayerKind.Bushes` with Off / Realistic / Procedural. Realistic: OSM
   `natural=scrub|heath` polygons + forest edges (a 10-20 m band inside each forest outline) + the satellite estimate's
   "vegetation, not tree" cells. Procedural: bushes round trees (2-5 per tree within 3-8 m, Poisson) and along field
   edges. **Rendering**: not terrain trees (they cost per instance, TL-3 d); reuse `GrassField`'s instancing with a
   200 m radius and a crossed-quad impostor beyond 60 m; no colliders (bushes under 2 m do not hide a drone from a
   tower). Rocks: same renderer, few, with colliders only inside 100 m.
5. **Building variety** (1 day): roof colour and wall tint from the building hash (a palette per type), Indian-style
   detail on flat roofs: parapet ring and a water tank box on 40 % of houses [TK: common on Indian flat-roof houses];
   `building:colour` / `roof:colour` when OSM has them; `roof:shape=gabled|hipped` makes a ridge roof. Procedural
   villages use Kenney prefabs fitted to the footprint box (`realisticUsesPrefabs` stays OFF for real footprints, so
   real shapes are never replaced by boxes).
6. Later (needs a yes): ESA WorldCover 10 m land cover (shrubland, grassland, tree cover classes; CC BY 4.0 [TK]) as a
   better source than the colour estimate (CONTEXT §7 item 6).

### 6.4 Performance on a GTX 1650
§6.2 [doc] baseline: 1080p, MSAA 4x, trees to 400 m, 1 shadow cascade, 60 fps cap, p95 ≥ 58 (16.7 ms). Budget for all
layers [ours, to measure]: ≤ 4 ms GPU. Means: trees beyond 400 m only as billboards / impostors (`treeBillboardDistance`
lowered for the "GTX 1650" quality preset), bushes ≤ 5 000 full models within 60 m and impostors to 200 m, rocks ≤ 500,
grass radius 120 m, no shadows from bushes and grass. A "Layers: Low / Medium / High" preset in the TERRAIN tab sets
these numbers (still typeable). Measure on the owner's GTX 1650 with the built game (TL-2 step 10): frame time with
layers off / Low / High on Area and telaviv.

### 6.5 Effort, risks, yes needed
- Effort: **about 5 days** (wire art 0.5, species 1, Poisson 1, bushes 1.5, buildings 1).
- Risks: Quaternius trees are stylised low-poly (they will look "game-like" next to the satellite drape; judge on
  screen); terrain tree cost on the 1650; the GPU reset incident (TL-3 e): never loop-render hundreds of thousands of
  trees on the dev PC.
- Owner's yes: none for steps 1-5 (art already in the project, CC0). WorldCover download later (Q).

---

## 7. Finish the VR version (last)

### 7.1 Exists today
Packages approved and installed: `com.unity.xr.openxr` 1.18.0, `com.unity.xr.interaction.toolkit` 3.6.1; `Assets/XR`,
`Assets/XRI`, the XR loader in EditorBuildSettings. Code (compiles, never run on a headset):
- `Player/XrRuntime.cs`: `TryStart` / `Stop` / `Alive`, readable failure reasons, `RegisteredRuntime()` (asks reg.exe
  for the Khronos active runtime), a `StartHook` for tests; `XrDisplay` (eye FOV from the projection matrix, arcmin per
  pixel, `ThresholdFov` so a speck keeps the desktop's angular rule); **`XrProbe` for `-chkVrProbe` exists in code but
  has never been run in a build**.
- `Player/OpenXrViewRig.cs`: head on a TrackedPoseDriver under an origin at the post, hands, aim ray from the pointer
  pose, right trigger lock, left trigger or both grips = binoculars, radial menu, X tablet, Y commander slew, snap turn,
  pause menu, comfort question.
- `Player/XrInput.cs` (code-built Input System actions on generic OpenXR usages, debounce, snap turn, `XrMenu`),
  `XrOptic.cs` (binoculars as a second camera into a RenderTexture in a round window), `VrHud.cs` (world-space HUD,
  35° lazy re-centre, constant-angle brackets), `Player/Editor/XrSetup.cs` (menu Chaukanna > VR > Set up OpenXR).
- `ViewRigSwitch`: `ViewMode { Desktop, VrSim, Headset }`, tier `vr-openxr`; `ScenarioConfig.viewMode` with the old
  `vrMode` kept; `ContactSensor.Headset` override.
- `VrSelfTest` (19-22 checks) covers the simulator only.

### 7.2 Missing (plan-vr-openxr.md §4)
1. `XrSelfTest`: Headset falls back with a reason when `StartHook` fails and leaves the desktop rig intact; radial items
   map to the right calls; `viewMode` / `vrMode` compatibility; `HudMath.NeedsRecentre` at 35°; bracket angle constant
   at 100 m and 1000 m; every action bound for both hands (`XrInput.BothHandsBound`); tier strings; `ThresholdFov`
   cases. About 15 checks.
2. Build probe: build, run `Chaukanna.exe -chkVrProbe -logFile vr.log` on a PC without a headset. Pass: the game starts
   on the desktop, `[VrProbe] TryStart -> False` with a readable reason, `[VrProbe] done`.
3. TODO / PROGRESS / STATUS rows for VR-1 (never written).
4. Headset test VR-2 (owner's hands): F7 to Headset, look around, lock with the right trigger, binoculars, radial menu,
   tablet, Y slew as commander, ≥ 72 fps.
5. New from this round: voice PTT on a controller button (item 5), the K group call in the radial menu (item 2),
   bird flap visible in the `XrOptic` view (comes free with item 2).

### 7.3 Effort, risks, yes needed
- Effort: XrSelfTest 1 day; probe build 0.5 day; docs 0.25 day; headset session 1 day with the owner; fixes 1-2 days.
  **About 4 days.**
- Risks: the headset model is unknown ("Festo"); a GTX 1650 cannot run Quest Link (§6.2 [doc]); XRI is installed but
  unused (fine; ray UI later).
- Owner's question: **which headset exactly** (make, model, PC software), and which PC drives it (Q9).

---

## 8. Recommended build order (owner's order kept; VR last)

| Step | Item | What | Days | Depends on |
|---|---|---|---|---|
| 1 | 1 | Flock engine: fixed step, grid / topological neighbours, drone spacing, tests | 2 | — |
| 2 | 1 | Swarm tactics: formation, saturate, split, feint, terrain masking, jam failsafe; presets; SpawnMsg group fields | 4 | 1 |
| 3 | 2a | Bird mesh, flap poses (GameObject + crowd), species cadence, flap-glide, attitude, proxy animator. **Owner looks at it on screen** | 3 | 1 |
| 4 | 2b | Bird profiles on the engine (StarDisplay-lite, Couzin), crowd density wave | 1.5 | 1, 3 |
| 5 | 2c | Group call (K), group scoring node, AAR group rows, LAN fields; mimic if yes | 3 (+1) | 2, 4 |
| 6 | 3 | MP fixes M1-M5, M9, M11 | 3 | 2 (group fields) |
| 7 | 3 | **Two-PC test** (owner's hands) + fixes | 0.5 + 1 | 6 |
| 8 | 4 | AAR card framework + cards + HTML restyle + exports | 8 | 5 (group card) |
| 9 | 5 | FIX-2b replay green lines | 1 | — |
| 10 | 5 | Black box FR-3 + unit pack | 3.5 | 6 (PoseMsg) |
| 11 | 5 | 3D replay viewer | 5 | 10 |
| 12 | 5 | Proximity voice + radio + comms review (if Q7 = yes) | 6.5 | 6, 10 |
| 13 | 6 | Terrain: wire art, species, Poisson, bushes, buildings | 5 | — |
| 14 | 7 | VR finish: XrSelfTest, probe, docs, headset test, fixes | 4 | headset |
| | | **Total** | **about 51 days** (52 with the mimic) | |

That is roughly ten working weeks for one developer with an AI assistant. Every step ends with all self-tests at their
counts, a PROGRESS entry, a STATUS row and a numbered hand test (CLAUDE.md rules 8, 9). Hand tests should not be
batched to the end again: the owner should see steps 3, 7, 8 and 11 on screen when they land, because nothing drawn by
IMGUI since 2026-10-16 has been seen.

---

## 9. Open questions for the owner
1. **Bird-mimic drone** in the prototype (solution-v3 X2 excludes it)? Default: no, Expert preset only later.
2. **Bird art**: accept a code-built bird (no download), or approve downloading a CC0 flying-bird model with a flap
   clip (TODO P1d)? Default: code-built first, decide after seeing it.
3. **Which birds** does a real post see? Default: black kite, pigeon, crow, egret; starling murmuration only in a dusk
   roost scenario. An officer's answer beats mine.
4. **Group scoring**: one call and one SDT trial per swarm / flock (as §5.4.6 says) instead of per member. Default: yes.
5. **Second PC**: when and where is the two-PC test (step 7)? Wired switch available?
6. **Instructor seat**: should the trainer's PC be able to host as an unscored Observer? Default: yes.
7. **Voice**: record it? Who keeps the files and for how long? Headsets with microphones on every PC? Default: record
   in TEAM runs, keep on local PCs, delete after 30 days, µ-law codec (no download).
8. **Exports**: may files leave the PC on a USB stick under the unit's rules? Default: "Copy to USB" button, off if the
   unit says no.
9. **Headset**: exact make and model of "Festo", and the PC it runs on.

## 10. Weakest link
Items 3 and 5 stack the most new code on the least-tested layer. The LAN has only ever run on one PC over loopback,
with the Editor stepped by script, and none of its screens have been seen; the black box, the unit pack, the 3D replay
of every seat and proximity voice all ride on that layer (poses, clock sync, relayed frames). If the two-PC test fails
on the firewall, on Wi-Fi jitter or on clock drift, about 16 days of item 5 are built on sand, and the pitch's headline
(local multiplayer, risk K29) has no proof. The owner's order puts the two-PC test at step 7, after about 13 days of
swarm and bird work. It needs no code, only the owner's hands and half a day, and it would tell us now whether the
foundation holds. Second weakest: realism of birds is judged by eye, and the eye can only see flapping through the
binoculars at these ranges (2 px naked eye at 300 m), so step 3 must be shown on screen before steps 4-5 are built on
it.

**One hard question:** if the two-PC test could be run this week, before any swarm work, is there a reason not to,
given that the pitch now leads with local multiplayer and nothing in items 1-2 depends on it?
