# DEMO-SCRIPT.md: the 5-minute Kurukshetra demo

One click does the set-up: **DEMO** (amber button, top right of the trainer menu, left of START). It picks the best terrain already on the
PC (the Jammu air force station if present, else any real area with map features, else a procedural plain), loads a curated 3.5-minute
scenario, switches sound on, and starts. Nothing is downloaded. The presenter checklist shows in the run (F8 hides it).

What the demo run contains (all fixed, so every showing is the same):

| When | What |
|---|---|
| 0:08 to 2:00 | 4 hostile drones (quads on a custom low run-in path over the left flank, fixed-wing), 2 own ISR drones, 2 civilian drones, 3 single birds |
| about 0:35 | a flock of 40 birds |
| 0:55 | a swarm of about 200 hostile drones (second custom path, over the right flank) |
| 1:30 | ROE card changes from C (own drones up: report and track) to A (attack in progress): banner flashes red, tone plays |
| 3:30 | run ends, RUN COMPLETE box with an **AAR dashboard** button |

Seed 2026, day, clear. Difficulty is whatever the menu was on (Normal by default).

## The 5-minute script

Times are on the presenter's clock, not the run's. The run starts at 0:20 on it.

**0:00 to 0:20. Menu.** "This is the trainer's menu. A trainer picks terrain, scenario and players. For the demo, one click." Press **DEMO**.
Loading screen: "It is building real terrain from open map data, with real buildings and trees from OpenStreetMap."

**0:20 to 0:50. The view.** Run starts. Hands off the mouse for a moment: "A sentry at a post. Small drones are specks; we look
through binoculars." Right mouse = binoculars. Point at the ROE banner: "The rules of engagement card is always on screen." Sound is on: "The
drones are heard before they are seen; direction and loudness are real."

**0:50 to 1:15. The core job: recognise, call, act.** First hostile quad comes in. Click it (lock). Right mouse: binoculars.
Press **1** (call it hostile quad). Then **J** (jam) or **F** (engage). Say: "Call first, then act; the call is scored separately from the
action, and from the rules of engagement in force." Then an own ISR drone: press **3**, do **not** engage, say "an own drone engaged is the worst error, it shows in
red in the decision tree afterwards."

**1:15 to 1:50. Groups.** The bird flock and then the swarm of 200 arrive (the swarm is on the run clock at 0:55, so 1:15 on yours). Hold **Tab** and drag a
loop round the swarm (lasso), press **K**, then **1** (drone swarm). "One call for the whole group, scored as a group." Then **E** engage or **Q** jam on
everything selected: "fast reaction keys."

**1:50 to 2:10. The card flips (run clock 1:30).** The banner turns red and flashes, a tone plays. Say: "Rules change mid-run. The
same engagement that was right a minute ago is now judged under Card A. The report measures how fast the soldier adapts."

**2:10 to 3:50. Keep working the sky while you talk.** Call and act on the remaining contacts. Press **F6** for split screen: left is the trainer's truth map and
feed (the trainer sees everything, the soldier sees only what a real sensor shows); press **F6** again to close. Optional: **F10** voice settings
("trainees talk to each other; the trainer can replay the voice").

**3:50 (run ends, 3:30 on its clock). After the run.** RUN COMPLETE appears. Click **AAR dashboard**. Cards: score, the decision tree, timeline,
charts. "Player and unit select: one soldier, a unit, or everyone." Move the mouse over a chart for its numbers.

**4:15. 3D replay.** Click **3D REPLAY**. Press **4** (top view), then **1** (what the soldier saw), **Space** to play. Click a contact to label it.
"A reconstruction from the black box: every drone, every call, every shot, scrub to any moment." **Esc** returns to the dashboard.

**4:40. The report.** On the dashboard header, click **HTML report**: it opens the one-file **HTML report** in the browser (works offline, for the debrief).
Close: "Same engine, on a LAN, with several trainees on one sky, and a trainer who sees all of it."

Keys cheat sheet (also on the in-game checklist): Right mouse binoculars, click lock, **1-5** call, **H** hold, **R** report, **J** jam, **F** engage,
**E** engage and **Q** jam everything selected, **I** IFF challenge, **Tab** + drag lasso, **G** select all in view, **X** clear, **K** group call,
**F6** trainer panel, **F8** presenter checklist, **F10** voice, **Esc** frees the mouse.

If something goes wrong: **Esc** frees the mouse; **R** on the end screen retries the same scenario; closing the game and pressing DEMO again gives the same run.

## Pre-demo checklist

Do this the day before, then again 30 minutes before.

Build and files
- [ ] Build made from the committed branch (File > Build Profiles > Build). Note the commit hash on a sticky note.
- [ ] Copy the **whole build folder** to the demo PC, not only the `.exe`: it must contain `Kurukshetra.exe` (or the product name), `Kurukshetra_Data`, and the
      **`KurukshetraData`** folder next to the exe (map tiles and the real terrain areas, for example `KurukshetraData/RealData/Jammu_AFS`). Without it DEMO falls
      back to a procedural plain and the map tab is empty.
- [ ] On the demo PC, start the game once, press **DEMO**, and check the message line at the bottom says "real terrain ..." (not "procedural").
- [ ] The report folder is writable (Documents or the game's data folder). After a run an HTML report appears; double-click it once to see it opens.

Machine
- [ ] Windows power plan High performance, laptop on mains, notifications off, display scaling 100 %, resolution 1920 x 1080 (what the layout was tuned on).
- [ ] Speakers or headphones work and the system volume is up; the game's own sound is on (DEMO switches it on).
- [ ] Projector or second display: set before starting the game, window focused (an unfocused window freezes IMGUI).
- [ ] Mouse (not a trackpad): the demo uses right mouse and drag.

Voice (only if you show F10)
- [ ] Windows Settings > Privacy and security > Microphone: **allow desktop apps**. Press F10 in the game, pick the microphone, speak, watch the level meter move.
- [ ] Push to talk is **V** unless changed.

LAN test on two PCs (only if you show team play)
- [ ] Both PCs on the **same network** (a phone hotspot or a small router is safer than the venue Wi-Fi, which often isolates clients).
- [ ] **The same build on both** (same commit, copy the same folder). A different build refuses to join or desyncs.
- [ ] Windows Firewall: allow the game on **private** networks when asked (UDP port **7777**). Do this once on both PCs before the day.
- [ ] Trainer PC: Lobby tab > Host, mode TEAM. Second PC: type the trainer PC's IP (shown on the host), Join, **I'm READY**. Trainer presses START. Both PCs see the same
      drones at the same places, the swarm and the flock on both. Run it end to end once with the demo scenario.
- [ ] After the run both PCs show the end screen; trainer opens the dashboard and sees both seats.

Rehearsal
- [ ] One full run-through with a stopwatch against the script above. Aim to finish with 30 seconds spare.
- [ ] Know the recovery: Esc frees the mouse, the end screen has Restart (back to the menu), pressing DEMO again restarts everything.
- [ ] Have a backup: a screen recording of a good run, and the HTML report of it, on the desktop.

What is honest to say
- The map data is OpenStreetMap and open elevation; the rules cards, deadlines and score bands are training settings the trainer can change, to be
  confirmed by an officer (the report says so). It is a recognition trainer, not a weapons simulation and not for selection or ACRs.
