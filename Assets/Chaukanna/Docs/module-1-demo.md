# Module 1: the demo scene
2026-10-13 · Goal: one Unity scene that **looks and plays like the simulator** for a demo video. It is not the full system.

**What changed from `unity-module-plan.md`:** that file is the full product. This one is the advert. Most of the pipeline is cut or faked, and every fake is listed in section 6 so nobody mistakes it for the real thing later.

---

## 1. The setup in one picture

**One PC, two screens, one Unity app.** Unity can send two cameras to two monitors ([Unity Multi-display docs](https://docs.unity3d.com/Manual/MultiDisplay.html)), so there is no networking.
- **Screen 1, Trainer:** the instructor builds the scenario and watches results.
- **Screen 2, Player:** the soldier at the post.
- For recording, the same app can run **split-screen in one window**: trainer on the left, player on the right.
- **VR:** Screen 2's camera can be swapped for a Quest headset over Link (RTX 3050 PC only). This is shown as one shot in the video (section 5). Desktop is the main path.

```
┌──────────── SCREEN 1: TRAINER ────────────┐   ┌──────────── SCREEN 2: PLAYER ─────────────┐
│ [Preset ▼ Night Swarm]  [🎲 Randomize]    │   │                                            │
│ Time: Day/Night   Terrain: Rural/Urban    │   │   3D view from the post (sky, trees,       │
│ Threat: Single/Swarm(8/24)                │   │   buildings). Right-click = binoculars     │
│ Sensors: OK / Degraded                    │   │                                            │
│ Difficulty: Recruit ▪ Trained ▪ Expert    │   │   ┌ Sensor panel ┐  Contact locked:        │
│ ROE card: Weapons Hold / Under Attack     │   │   │ RF ▁▃▇▃      │  [1 Quad] [2 FPV]       │
│ Seed: 48213          [▶ START]            │   │   │ Sound ← 240° │  [3 Fixed-wing] [4 Own] │
│───────────────────────────────────────────│   │   │ AI: "Quad 82%"│ [5 Bird]               │
│ LIVE: top-down map (truth) + event feed   │   │   └──────────────┘  Action: Hold/Report/   │
│ 21:42:07 Player detected #3 (6.2 s)       │   │                     Jam/Engage             │
│ 21:42:11 Classified FPV ✓                 │   │   ROE: WEAPONS HOLD  ⏱ 00:43               │
│ 21:42:13 Engage ✗ (Own drone!)            │   │                                            │
│───────────────────────────────────────────│   └────────────────────────────────────────────┘
│ AFTER: AAR dashboard (section 3.6)        │
└───────────────────────────────────────────┘
```

---

## 2. The demo flow (what happens, start to finish)

1. **Trainer** picks a scripted preset ("Night Swarm") **or** presses 🎲 Randomize, which generates a new procedural scenario from a seed. They set the toggles and press START.
2. **Player** looks around the sky, spots a contact, raises binoculars, clicks to lock it, presses 1–5 to classify, then picks an action.
3. The **trainer screen** shows ground truth live: where every drone really is, what the player did, and a ✓ or ✗ on each call.
4. Mid-mission the **ROE card flips** ("Under Attack"), so the right answer for the same kind of drone changes.
5. The mission ends after about 3 minutes, or when every contact is resolved.
6. The **AAR dashboard** opens on the trainer screen: this session's scores, the decision-tree path for each contact, and trend lines over past sessions for this soldier and their section.
7. Trainer presses Randomize and runs it again. It's a different mix and different approach directions, so a memorised answer fails.

---

## 3. The parts to build (simple)

### 3.1 Environment (one scene)
- One terrain about 2×2 km. **Rural half:** fields, tree lines, a canal. **Urban half:** cantonment-style blocks, walls and a water tank. The player's post sits between them.
- The "Terrain" toggle **moves the post and the drone approach** to the rural or urban side. You build one scene, and both terrains appear in the demo.
- **Day/Night:** two lighting presets (directional light + skybox + ambient). Night adds a green NV post-process and darker contrast.
- **Haze/fog:** Unity fog density, set by difficulty.

### 3.2 Drones and spawner
- **Prefabs:** Quad, FPV, Fixed-wing, Own drone (friendly), Bird. Use simple low-poly models; free asset store or Blender is fine.
- **Spawner** reads a small `ScenarioData` (ScriptableObject or JSON): list of contacts with type, spawn time, start direction, path.
- **Swarm:** 8 or 24 drones with simple **boids** (separation/alignment/cohesion) plus a target point. Bird flocks use the same code with slower flapping. This is the "AI behaviour" that is actually visible on screen.
- Drones move by script along curves or with simple steering. No physics needed.

### 3.3 Player controls
- Mouse look. Right-click toggles **binoculars** (camera FOV 60° → 8°).
- Left-click on a contact **locks** it. Detection time is recorded from when it first came into view.
- Keys **1–5 classify**. Then an action menu: **Hold / Report / Jam / Engage**.
- Jam: RF drones stop or return; FPV ignores it (fibre). Engage: simple hit roll plus a muzzle flash and sound. Both are cosmetic, but they tell the story.

### 3.4 Sensor panel (player screen, bottom left)
- **RF bar** that spikes when a radio drone is near (flat for FPV).
- **Sound bearing** arrow.
- **"AI assist" label** with confidence, wrong about 15% of the time on purpose. Teaching soldiers not to blindly trust the AI is one of the selling points.
- **"Sensors: Degraded"** adds noise to the RF bar, delays the bearing and lowers the AI's accuracy.

### 3.5 Trainer panel (screen 1)
- **Scenario builder:** preset dropdown (3 scripted presets: "Day Single Quad – Rural", "Night Swarm – Urban", "Mixed Sky – Own Drones Up"), Randomize button, toggles (Day/Night, Rural/Urban, Single/Swarm, Sensors OK/Degraded, ROE card), **Difficulty** (Recruit/Trained/Expert) and a visible **Seed**.
- **What Randomize changes** (this is what makes it anti-rote):
  - drone types and mix
  - approach direction
  - spawn timing
  - number of decoys (birds, own drones)
  - sensor quality
  - ROE card, which changes what the right action is
- **Difficulty** scales: number of contacts, speed, decoy share, fog, and AI-assist error rate.
- **Auto-adjust (simple "adaptive difficulty"):** if the player's last score was above 80%, the next Randomize picks one level harder; below 50%, one level easier. This is one rule, shown as "Recommended next: Expert".
- **Live view:** top-down minimap (orthographic camera) showing every contact in true colour (red hostile, blue own, grey bird), plus a scrolling event feed.

### 3.6 Scoring: the decision tree
Each contact runs through a small tree. **Show the tree itself on the trainer screen** with the path taken highlighted. That is the PS's "decision-tree based scoring", made visible.

```
Contact
├─ 1. DETECTED?            no → MISSED (0)
│     yes → detection time: < 5 s = 3 pts · 5–10 s = 2 · > 10 s = 1
├─ 2. CLASSIFIED RIGHT?    yes → 3 pts · near-miss (Quad↔Bird far away) = 1 · wrong = 0
└─ 3. RIGHT ACTION for the ROE card?
      WEAPONS HOLD (own drones up): Hostile → Report/Jam ✓ · Engage own/bird ✗✗ (−5)
      UNDER ATTACK:                 Hostile → Jam/Engage ✓ · Hold on a hostile ✗✗ (−5)
      Any card: engage a manned aircraft = −10
```

**The three PS metrics,** per session:
1. Average **detection time**
2. **Classification accuracy** (% correct, plus a small 5×5 confusion grid)
3. **Engagement decision** score (% right for the card)

### 3.7 AAR dashboard (trainer screen, after the mission)
- **This session:** the three metrics as big numbers; per-contact rows with the tree path (✓/✗ per node); the confusion grid.
- **Replay strip:** a timeline of when each contact appeared vs when the player spotted it. This is "where seconds were lost".
- **Over sessions (individual):** a line chart of the three metrics across the last 10 sessions.
- **Unit view:** a table of 6 soldiers in a section with their latest scores and trend arrows, plus "weakest: night FPV".
- **Saving:** results go to a local JSON file per session, so repeated demo runs really add points to the chart.
- **Charts:** [XCharts](https://github.com/XCharts-Team/XCharts/blob/master/README-en.md), a free MIT-licensed Unity chart library with line, bar and heatmap charts. Its Unity 6 support is not stated explicitly, so test it on day 1. If it fails, draw simple bars yourselves.

### 3.8 Two-screen setup
- Two cameras plus two Canvases, with Camera `targetDisplay` set to 0 and 1, and `Display.displays[1].Activate()` at start ([Unity docs](https://docs.unity3d.com/Manual/MultiDisplay.html)).
- A **split-screen mode** (one key) for recording and for laptops with one screen.

---

## 4. How each PS line shows up on screen

| PS line | Where the viewer sees it |
|---|---|
| Recognise, classify, respond | Player spots, uses binoculars, presses 1–5, picks an action |
| Day/night | Trainer toggle; the shot changes to an NV look |
| Degraded sensors | Trainer toggle; the RF bar turns noisy and the AI hint becomes wrong more often |
| Urban/rural terrain | Trainer toggle; the post and approach switch sides |
| Single drone and swarm | Single Quad preset vs a 24-drone boids swarm |
| Unit level, minimal hardware | Runs on a GTX 1650 laptop; on-screen caption gives the specs |
| Desktop or VR-capable | Desktop is the main path; one shot of the player view in a Quest |
| Scripted and procedural scenarios | Preset dropdown vs the 🎲 Randomize button with a visible seed |
| Decision-tree scoring on the 3 metrics | Tree shown with the highlighted path; the three numbers |
| AAR, individual and unit, over sessions | Dashboard with trend lines and a section table |
| Difficulty plus anti-rote randomisation | Difficulty selector, auto-adjust suggestion, and a re-run with a different seed where the ROE flips |
| AI-enabled | Swarm behaviour, adaptive difficulty, and a fallible AI assist |

---

## 5. Demo video outline (about 3 min)

1. **0:00** Problem caption: "Drones and swarms are now decisive. Live drills are costly and too few."
2. **0:15** Trainer screen: picks "Mixed Sky – Own Drones Up", Rural, Day, Single, then START.
3. **0:35** Player view: spots a speck, binoculars show an *own* drone, Hold ✓. Then a hostile quad: Report ✓.
4. **1:00** **The flip:** the ROE card changes to UNDER ATTACK. The same quad type now needs Engage. The trainer feed shows ✓.
5. **1:20** Trainer presses Randomize: Night, Urban, Swarm 24, Sensors Degraded. The boids swarm comes in over the buildings, and the AI assist is wrong once; the player checks with binoculars.
6. **2:00** A mistake on purpose: the player engages an own drone, and the decision tree lights up the red path.
7. **2:15** AAR dashboard: three metrics, the tree path, the confusion grid, the trend over 10 sessions and the section table.
8. **2:40** VR shot: the same player view in a Quest.
9. **2:50** Close: "Runs offline on a unit PC. Scripted or random, every run is different."

---

## 6. What is real, what is faked (be honest in the video)

| Real in Module 1 | Faked or simplified |
|---|---|
| Scenario presets and seeded randomisation | No validator and no LLM scenario builder |
| Boids swarm, drone paths | No PX4 flight data; paths are hand-made curves |
| Decision-tree scoring on the 3 metrics, ROE flip | No signal-detection maths (d′/c) and no learner model; adaptive difficulty is one rule |
| AAR with sessions saved to JSON | Older history and other soldiers in the unit view are **sample data**. Caption it "sample data" |
| AI assist that is sometimes wrong | Its error rate is a slider, not measured from a real detector |
| Two screens on one PC | No LAN, no login, no export |
| Desktop plus one VR shot | No calibrated "true size" display and no Cardboard |

---

## 7. Build plan (2 weeks, 6 people)

| Person | Week 1 | Week 2 |
|---|---|---|
| P1 Unity lead | Scene, terrain (rural + urban halves), day/night, fog | Two-screen setup, split-screen, VR camera swap (Quest over Link), performance on the 1650 |
| P2 Player | Mouse look, binoculars, click-lock, classify keys | Action menu, jam/engage effects, ROE banner and timer |
| P3 Scoring | Decision tree in C#, three metrics, unit-tested on 5 hand cases | Save session JSON, sample history, auto-adjust rule |
| P4 Trainer UI | Scenario panel: presets, toggles, difficulty, seed, START | Live minimap and event feed; AAR dashboard with XCharts |
| P5 Content | Drone, bird and aircraft models, sounds | Effects, NV post-process, polish |
| P6 Scenarios + AI | ScenarioData format, 3 presets, Randomize generator | Boids swarm, AI-assist error logic, demo script and recording |

**End of week 1 gate:** a single drone flies in, the player spots, classifies and acts, and the trainer sees ✓/✗.

**End of week 2 gate:** the whole video outline plays without cuts.

---

## 8. Critical check

📉 **Weakest link:** drones at a realistic distance are tiny specks on screen.
- Most demo teams will make them big so the video looks good, which teaches nothing and a trained eye will notice.
- **Decision:** keep them small, and let the binoculars do the work.
- **Test:** a viewer should still see the speck in the recorded video. Record at 1080p or higher and add a zoom inset when editing.

👹 **Strongest case against:** a two-week advert with sample data can look like every other team's drone game.

What separates it is three moments that a "shoot the drone" game doesn't have. If any of these gets cut for time, the demo loses its point:
1. the ROE flip changing the right answer;
2. the AI assist being wrong;
3. the decision-tree path lighting up red.

**The uncomfortable question:** if week 2 runs short, are you willing to drop the VR shot to protect those three moments?
