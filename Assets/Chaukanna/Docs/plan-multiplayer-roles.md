# Plan: Multiplayer roles and fixes (MP-1)

Planned 2026-10-16 (Opus). Built by a Sonnet agent from this file, AFTER the dense-sky module (DS-1) is in, because
section 5 depends on `CrowdSky`. Rules: CLAUDE.md. Approved by the user in chat: SENTRY / COMMANDER split, commander's
delay scored as its own link (solution doc §5.4.7).

## 1. Bugs found in the current LAN code (fix first)
1. **Host score polluted by trainees' decisions.** `LanSession.OnDecision` calls `DecisionLoop.RemoteAct`, which
   Classifies and Acts on the HOST's own ContactRecord, so the host seat is scored for a trainee's call. Fix:
   `RemoteAct` applies only the physical effect (jam -> `ContactMotion.Jam()`; engage -> hit roll, effect, despawn
   "EngagedHit") and writes `Data.handledBy = <seat name>`, `Data.handledTime`, `Data.handledAction` plus a history
   line. It never sets call / action on the host's record.
2. **Teammate's kill counted as my clock miss.** On any seat, a hostile neutralised by ANOTHER seat (host effect, or
   a remote despawn with reason "JammedDown" / "EngagedHit" that this seat did not cause) reached the deadline with no
   decision from this seat and was scored "Clock ran out". Fix: set `handledBy` / `handledTime` on the record (team
   client: in `OnDespawn` when this seat had not acted; host: in RemoteAct), and in `ScoreCalculator` (contact scoring,
   ACT node): if this seat did not decide in time but `handledTime >= 0 && handledTime <= deadline + 0.01`, it is NOT
   a clock miss: ACT node 0 points, reason "handled by <seat> at <t>s", actBranch "Handled by team", outcome from what
   happened (Neutralised). Non-hostiles handled by a teammate (an own drone shot by someone else) are not this seat's
   fault either: same 0-point node, but keep the outcome "Fratricide" so the team AAR shows it.
3. `HostTick` scans `announced` against `ContactRegistry.Live.Exists(...)` (O(n^2) per frame): use a HashSet of live
   ids built once per tick.
4. `FindAnyObjectByType<ScenarioSpawner>()` every frame in HostTick / OnSnapshot / OnSpawn: cache it per field load.
5. A team client's ScenarioTime is its own clock: take the host's time from `Snapshot.Unpack`'s return value and keep
   `LanSession.HostTime` (last snapshot time + seconds since it arrived). Expose it for the crowd (section 5) and show
   a clock drift warning in the client log if |local - host| > 2 s.
Self-test the scoring part in FullScoringSelfTest-style new checks (new `TeamSelfTest`, see section 7).

## 2. Roles
- `public enum SeatRole { Any, Sentry, Commander }` (Scripts/Net/NetProtocol.cs). `NetPlayer.role` (int, serialised).
  Solo mode ignores roles (always Any). TEAM default: host = Commander, trainees = Sentry.
- `RunConditions.SeatRole` set in `LanSession.EnterField` from the roster entry of this PC (Any when not on a LAN).
- **SENTRY**: may lock, classify (1-5), report (R), challenge IFF (I), lasso, binoculars. H / J / F refused with a
  short HUD message for 2 s: "SENTRY: report it (R). The commander decides." The key panel greys H / J / F and shows
  "ROLE: SENTRY".
- **COMMANDER**: everything (Hold / Jam / Engage, and also lock / classify / IFF for own verification). The key panel
  shows "ROLE: COMMANDER" and the report keys (section 3).
- Lobby (TrainerMenu Lobby tab, host only, TEAM mode): each seat row gets a role button that cycles SENTRY / COMMANDER
  / ANY; roster broadcast on change. Warning line when TEAM has no commander: "No commander: nobody can jam or fire."
  The role travels in the start packet's players list.

## 3. Report relay and the commander's picture
- Sentry presses R: the local record gets the Report as today; ALSO `LanSession.SendReport(contact, call)` ->
  host. Host stores `ReportMsg {id, call, seat, time (host scenario time), range, bearing}` and forwards it to every
  COMMANDER seat (and to itself if the host seat is a commander). Reliable sequenced.
- On a commander PC, new component `ReportBoard` (Scripts/Player/ReportBoard.cs, added to the observer camera at
  run start when the role is Commander):
  - list, top-left under the ROE banner, newest first, max 10 lines: `#14  Hostile quad   by S-014   12 s ago
    640 m  040 deg` (age counts up; a line turns grey when the contact is gone, with its fate).
  - mini-map, 220 px square (scaled by screen height): post at the centre, north up, range rings at 500 m steps,
    reported contacts as dots coloured by the REPORTED call (not the truth), the commander's view cone.
  - on-screen amber brackets on reported contacts with "RPT S-014: Hostile quad".
  - **T** slews the camera to the newest unhandled report over 0.3 s and locks that contact; **Shift+T** goes to the
    next one down the list. Works in VR-sim too (rotates the head rig).
  - the commander's record of that contact gets `reportReceivedTime` (first report), `reportedBy`, `reportedCall`.
- Team clients without the contact proxy yet (report arrived first): keep it in the list and resolve when the proxy
  spawns.

## 4. Scoring the chain (doc §5.4.7, each link only on the information that seat had)
- SENTRY seat (`RunConditions.SeatRole == Sentry`): DET and CLASS nodes as today. The ACT node becomes REPORT: a
  hostile reported (R) before its deadline = Sound (soundPoints); not reported by the deadline = clock miss (same
  severity rule as today, priced as Hold); a non-hostile reported or left alone = correct rejection (Sound); a
  reported non-hostile with a HOSTILE call = Minor ("false report"). Report latency (lock -> report) is already
  computed (reportLatency / reportBand) and feeds the dashboard.
- COMMANDER seat: ACT node as today, plus:
  - `commanderDelay = actionTime - reportReceivedTime` (when a report came before the action) -> ContactResult and
    SessionResult (`commanderDelayMean`, `commanderDelayCount`), shown on the dashboard and the HTML report as "Report
    to decision".
  - **First-fault rule**: a clock miss is waived (0 points, reason "upstream: report came too late (<seat>)",
    `firstFault = "sentry"`) when the report arrived later than `deadline - rules.commanderMinSeconds` (new rule
    field, default 8 s) AND the commander had not locked the contact themselves before then. If the commander had
    their own view (locked it earlier), no waiver (`firstFault = "commander"`).
  - **Verification lapse** (doc rule 1): Engage under Card B or C on a report alone (no own lock, no optics, no IFF
    challenge) = Minor under B, Major under C, reason "engaged on a report alone".
  - **Fratricide by hand-off flag**: engaging an OWN drone that a sentry reported as hostile -> `handoffFratricide`
    true, shown in the AAR.
- Host dashboard roster table (already lists every seat's live / final result): add the role column and, for
  commanders, the mean report-to-decision delay; for sentries, report count and mean lock-to-report.
- ScoreSelfTest (77) and FullScoringSelfTest (36) must not change: role scoring only applies when SeatRole != Any.

## 5. Crowd over the LAN (needs DS-1)
- Team clients build the crowd from the start packet like the host (same seed and config -> same agents) and run it
  on `LanSession.HostTime`, so the sky matches without sending positions.
- Picking a crowd drone on a team client sends `CrowdPick {index}` to the host instead of promoting locally; the client
  remembers `pendingPick` and the aim. The host promotes it (as if its own player had locked it, but without locking on
  the host) and its SpawnMsg carries `crowdIndex` (new field, -1 for planned contacts). Every client removes that agent
  from its own crowd when the SpawnMsg arrives and makes the proxy; the requesting client locks the proxy if it is
  still its pendingPick.
- Host promotions caused by the host's own lock also send crowdIndex, so the agent disappears from every client's
  crowd.

## 6. Test bot (testing only) for roles
`-chkTestBot` today calls and jams what it can see. With roles: a SENTRY bot calls right and Reports; a COMMANDER bot
waits for reports and Jams / Engages hostiles reported to it (Engage only under card A), using T to slew.

## 7. Verification
- New `Chaukanna.Net.TeamSelfTest` (no network): records built by hand for (a) a sentry report in time / late /
  missing, (b) a commander with an in-time report, a late report (waiver), own lock (no waiver), report-alone engage
  under B and C, (c) handledBy waives the clock miss, (d) handoff fratricide flag, (e) role refusal in DecisionLoop for
  a sentry (H/J/F refused, R works), (f) ReportMsg / CrowdPick / SpawnMsg with crowdIndex JSON round trip, (g) roster
  role survives the start packet. Target about 20 checks.
- NetSelfTest (9) still passes; add 2 checks for HostTime and the live-id set if natural.
- Live: Editor host (COMMANDER) + a built player trainee (SENTRY) on this PC: `Chaukanna.exe -chkName S-014 -chkJoin
  127.0.0.1 -chkReady -chkTestBot` (build into the scratch folder, never into the project). TEAM run of 90 s on the
  "Crowded Sky - Find the Foe" preset: the trainee's reports appear on the host's ReportBoard (read its static list),
  the host's T + J works (script), the trainee's record shows the commander's jam as handled, both results arrive with
  role columns. Report the numbers.
