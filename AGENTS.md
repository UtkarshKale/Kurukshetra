# AGENTS.md

Instructions for any AI coding agent (Claude Code, Codex, Cursor, others) working in this repository.
The full rules are in CLAUDE.md and apply to every agent; the full project context is in CONTEXT.md.

## Essentials
- Unity 6000.6.3f1 (URP 17.6, Input System 1.20, IMGUI UI). Open the project with exactly this version.
- Only write inside `Assets/Chaukanna/` and the root docs (`TODO.md`, `PROGRESS.md`, `CLAUDE.md`, `AGENTS.md`,
  `CONTEXT.md`, `HANDOFF.md`). Do not edit `ProjectSettings/`, `Packages/`, `Library/`, `Assets/Settings/` or third-party
  assets without the owner's explicit approval of that exact change.
- Git author: Utkarsh Kale <utkarshkale.w@gmail.com>. Never add an AI name, Co-Authored-By or AI trailer. Commit and push
  only when the owner asks.
- Keys stay in `%USERPROFILE%\.chaukanna\`. Never commit, copy or print them.
- Keep TODO.md and PROGRESS.md current every round; end each report with numbered hand-test steps and an honest
  "verified / not verified" split.

## Layout
- `Assets/Chaukanna/Scenes/` TrainerMenu (first in the build) and MixedSky (the field).
- `Assets/Chaukanna/Scripts/` Scenario, Player, Scoring, Sensors, Environment, Trainer, Session, Net.
- `Assets/Chaukanna/Terrain/` terrain generator, real-data areas (`RealData/`), configs, ground layers.
- `Assets/Chaukanna/Charts/` optional XCharts dashboard panel (own asmdef).
- `Assets/Chaukanna/Data/` presets, difficulty, scoring/site/swarm profiles.
- `Assets/Chaukanna/Resources/Chaukanna/` materials that must always be in a build.
- `Assets/Chaukanna/Docs/solution-v3.md` the solution document (design only; v2 kept beside it as history).
- `Assets/Chaukanna/Docs/STATUS.md` what is built today, per feature; update it with every code change.
- Project name: Kurukshetra. Code namespace and folders stay `Chaukanna`; do not rename them.
- `Assets/these assets/` third-party models (Kenney, Quaternius, others); some folders are not in git (see HANDOFF.md).

## Testing
There is no Test Framework package. Each area has a static self-test with `Run()` returning bool; run them from the
Editor through the Unity CLI (`unity command eval_file`). The list and expected counts are in CONTEXT.md.
