# CLAUDE.md: Kurukshetra (Unity project "Chanakya", code namespace Chaukanna)

Read this first, then CONTEXT.md (everything a fresh session would otherwise lose), TODO.md (the queue) and PROGRESS.md
(what was done, newest first). HANDOFF.md explains how this project moves to another PC.

Four docs, one job each (do not mix them):
- `Assets/Chaukanna/Docs/solution-v3.md`: the design (what and why). No build status in it.
- `Assets/Chaukanna/Docs/STATUS.md`: what the prototype does today, per feature. Update its row in the same commit as the code.
- `TODO.md`: the queue. `PROGRESS.md`: the history.

## What this is
Kurukshetra (the project name since 2026-10-19; formerly "Chaukanna", which stays as the code namespace and folder name) is a Unity counter-drone recognition trainer for Smart India Hackathon PS 26247 ("AI-Enabled Drone &
Counter-Drone Threat Simulation Trainer", MoD / DSSC). The prototype is "Module 1": a field scene (MixedSky) with a
player view and a trainer panel, plus a TrainerMenu scene. Unity 6000.6.3f1, URP, Input System, IMGUI for all UI.

## Rules the owner (Utkarsh Kale) set. Follow them exactly.
1. Create or edit files only inside `Assets/Chaukanna/`, plus the root `TODO.md`, `PROGRESS.md`, `CLAUDE.md`,
   `AGENTS.md`, `CONTEXT.md`, `HANDOFF.md`.
2. Never touch `ProjectSettings/`, `Packages/` (manifest or lock), `Library/`, the URP settings assets in
   `Assets/Settings/`, `SampleScene`, or any template / third-party asset, unless the owner approves that specific change
   in their own words. If Unity rewrites a ProjectSettings file on its own, restore it with git (but the build-export
   changes listed in HANDOFF.md are the owner's: leave those alone).
3. No new packages, layers or tags without the owner's explicit yes. Netcode for GameObjects 2.13.3 + Unity Transport
   2.6.0 were approved ("yes netcode").
4. Do not download anything without the owner's yes. Never claim real GIS data was processed unless it was.
5. API keys live outside the project in `%USERPROFILE%\.chaukanna\` (carto.key, arcgis.key). Never copy, print or commit them.
6. If something needs the owner's hands in the Unity Editor (or a second PC), stop and ask with a "NEEDS USER:" numbered,
   beginner-level list. Unity UI pointers must be very concrete (menu > item > button).
7. Git: commit or push only when asked. Author is Utkarsh Kale <utkarshkale.w@gmail.com> only. No Claude name, no
   Co-Authored-By, no AI trailer in any message. Never commit the files HANDOFF.md lists as deliberately uncommitted.
8. Every round: update TODO.md (tick or add, never delete; log every deferred idea) and PROGRESS.md (dated entry at the
   top: what changed, what was verified, what was not).
9. Every report ends with a numbered hand test and says plainly what was verified and what was not. Do not call work
   done until it was verified in the running scene (self-test or Play mode) or say that it was not.
10. Numeric trainer settings are typeable and have no upper caps (only physically impossible values are corrected).
11. The latest owner message wins on build order. Confirm before starting a new big step.
12. Style the owner asked for: short visible progress keywords while working; critical-thinking partner (no flattery,
    weakest link first, end substantive answers with one hard question) until the owner says "stand down".

## How to work in this project
- Drive the open Editor with the Unity CLI (`unity command ...`, needs the `com.unity.pipeline` package, already in the
  project): `unity command recompile` then poll `unity command recompile_status`; run C# with
  `unity command eval_file --file x.cs` (no `using` lines, fully qualified names, about a 5 s main-thread limit);
  `unity command editor_play`, `unity command capture_game_view --source camera`, `unity command console`.
- An unfocused Editor freezes the game at frame 2 and IMGUI does not paint. Step frames with
  `EditorApplication.update += ...; EditorApplication.Step()`. Never use `set_timescale` (it rewrites ProjectSettings).
- Namespace trap: `Chaukanna.Environment` shadows `System.Environment`; always write `System.Environment.X`.
- Self-tests (no Test Framework package): see CONTEXT.md "Self-tests". Run all of them after every change.
- Built games: anything created at runtime must get its shader from `Assets/Chaukanna/Resources/Chaukanna/*.mat`
  (`Chaukanna.RuntimeMaterials`), or it renders pink in a build.
