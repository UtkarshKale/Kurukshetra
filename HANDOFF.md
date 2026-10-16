# HANDOFF.md: moving Chaukanna development to a new PC

Written 2026-10-16 from the real state of the dev PC (C:\Users\dhruv\Chanakya). Everything below was checked, not
assumed, unless it says otherwise.

## 0. Specs in the repo
- `Assets/Chaukanna/Docs/module-1-demo.md`: the agreed Module 1 spec. The older `unity-module-plan.md` was rejected as
  over-engineered and is not used.
- `Assets/Chaukanna/Docs/solution-v2-council.md`: the full solution document.

## 1. Unity
- Editor: **6000.6.3f1** (revision 45d8eee7de74), from `ProjectSettings/ProjectVersion.txt`. Install exactly this
  version through Unity Hub (Installs > Install Editor > Archive / "other versions" if it is not listed).
- Modules: Windows build support is built into the Windows Editor (Mono); nothing else is required. The dev PC also has
  Android modules, which the project does not use. Add "Microsoft Visual Studio" or Rider only if you want an IDE.
- Render pipeline: URP 17.6.0 (`Assets/Settings/PC_RPAsset.asset`). Input System 1.20.0. UI is IMGUI.
- Packages (all resolve automatically on first open, internet needed once): see `Packages/manifest.json`. Note two
  special ones: `com.unity.ides` comes from a GitHub URL (https://github.com/LILQK/com.unity.ides.git), and
  `com.unity.pipeline` 0.8.0-exp.1 is the package that lets the Unity CLI drive the Editor. Netcode for GameObjects
  2.13.3 + Unity Transport 2.6.0 were added with the owner's approval.
- Unity CLI (used by Claude to compile, run self-tests and play): version 1.0.0-beta.8 on the dev PC. Install on the new
  PC in PowerShell: `irm https://unity.com/install.ps1 | iex`, then `unity auth login` (sign in to your Unity account),
  then check with `unity doctor` and, with the project open in the Editor, `unity status`.

## 2. Claude Code and its add-ons (as installed on the dev PC)
- Claude Code **2.1.296**. Install: `irm https://claude.ai/install.ps1 | iex` (or the desktop app), then sign in.
- Plugin **effortless 1.71.0** (user scope), from the marketplace HeyCubit/effortless:
  `claude plugin marketplace add HeyCubit/effortless` then `claude plugin install effortless@effortless`.
- Plugin **frontend-design**: synced from claude.ai automatically when you sign in with the same account; nothing to do.
- Marketplace `claude-plugins-official` (anthropics/claude-plugins-official) is configured but no plugin from it is
  installed; add it only if you want it: `claude plugin marketplace add anthropics/claude-plugins-official`.
- **Unity agent skills** (35 skills from Unity-Technologies/skills, installed with the "skills" CLI, plus find-skills from
  vercel-labs/skills): `npx skills add Unity-Technologies/skills` (needs Node.js; choose Claude Code when it asks). The
  ones this project actually uses: unity-cli, unity-pipeline, unity-package-management, ui, ui-imgui.
  After the project is open, also run `unity skill install claude-code` and, in the project folder,
  `unity skill install claude-code --local` (mirrors the pipeline package's own skill).
- MCP: only the claude.ai "Claude Docs" connector, which comes with the account. No other MCP servers.
- Claude Code project memory: the memory folder for this project is empty; all context is in CONTEXT.md. Old session
  transcripts (about 46 MB) are in `C:\Users\dhruv\.claude\projects\C--Users-dhruv-Chanakya\`. They are not needed; copy
  them only if you want the old chat history (they only show up if the project sits at the same path).

## 3. Git: what a fresh clone has, and what it does not
Checked on 2026-10-16 by cloning https://github.com/UtkarshKale/Chanakya.git into an empty folder: the clone took
about 7 minutes, is about 1 GB, and all 1852 Git LFS files downloaded (`git lfs fsck` OK). `.gitignore` already ignores
Library, Temp, Obj, Build(s), Logs, UserSettings, IDE files; `.meta` files are tracked; `.gitattributes` routes models,
audio, video and images through LFS. **Install Git LFS before cloning** or the models arrive as tiny pointer files.

Not in git (move by hand if you want them):
| What | Where on the dev PC | How to move | Needed? |
|---|---|---|---|
| Sonniss GDC 2026 audio bundle | `Assets/these assets/Sonniss.com-GDC2026-GameAudioBundle1of5/` (ignored, large) | copy the folder to the same path | Not used by any scene yet; only for the planned jam / engage sounds |
| Quaternius nature pack Blender files | `Assets/these assets/Ultimate Nature Pack by Quaternius/Blends/` (ignored) | copy, or skip | No (the FBX versions are in git) |
| `.exr` files in `Assets/these assets/` | ignored | copy if you want them | No |
| API keys | `C:\Users\dhruv\.chaukanna\carto.key` (and `arcgis.key` if you add one) | copy the `.chaukanna` folder by hand (USB), never by git or chat | Only for map labels / ArcGIS imagery. ROTATE the CARTO key first: it was pasted into a chat once |
| Map tile cache and recent places | `C:\Users\dhruv\.chaukanna\tiles\` (about 75 MB), `recent_places.txt` | copy the `.chaukanna` folder | No, but without it the map needs internet the first time |
| Saved sessions, learner profiles, packages, roster, drills, reports | `C:\Users\dhruv\AppData\LocalLow\DefaultCompany\Chanakya\` | copy the folder to the same place | Only if you want the history and the board data |

Deliberately left uncommitted (they were changed by Unity or by the owner, not by the agent; decide yourself):
- `ProjectSettings/EditorBuildSettings.asset`, `GraphicsSettings.asset`, `ProjectSettings.asset`,
  `UnityConnectSettings.asset` and `Assets/Settings/Build Profiles/`: from the owner's build export. The build-scene list
  in them is TrainerMenu then MixedSky (SampleScene off). If you do not carry them over, set the build scenes again on
  the new PC: File > Build Profiles > Windows > Scene List: TrainerMenu first, then MixedSky.
- `Assets/Settings/PC_RPAsset.asset`, `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`: rewritten by Unity.
- `Assets/Chaukanna/Terrain/RealData/Area/*` (re-downloaded) and `Assets/Chaukanna/Terrain/RealData/ter/` (a new area).
- `Assets/_Recovery/` (Unity's scene backup) and an empty `Assets/Resources/` folder: safe to ignore.
To carry them over, either commit them (owner's call) or copy those files by hand.

Builds: every Windows build now gets a `ChaukannaData/` folder next to `Chaukanna.exe` (map tiles + downloaded areas),
so copy the whole build folder.

## 4. Step-by-step on the new PC
1. Install Git for Windows and Git LFS, then run `git lfs install` once.
2. Install Unity Hub, sign in, install Editor **6000.6.3f1**.
3. Clone: `git clone https://github.com/UtkarshKale/Chanakya.git C:\Users\<you>\Chanakya` (any path works; keep it
   short and without spaces).
4. Copy the outside-git items you want (table above): the `.chaukanna` folder to `C:\Users\<you>\.chaukanna\` (after
   rotating the CARTO key), the Sonniss folder if wanted, the LocalLow data if wanted.
5. Unity Hub > Projects > Add > pick the Chanakya folder > open with 6000.6.3f1. The first import takes a while.
   Wait until the progress bar at the bottom right is gone. If Unity asks about the Input System backend or an API
   update, answer as on the old PC (the project already uses the new Input System).
6. Open `Assets/Chaukanna/Scenes/TrainerMenu.unity`, press Play once: the menu should appear. Stop.
7. Install the tools: Unity CLI (`irm https://unity.com/install.ps1 | iex`, `unity auth login`), Node.js, Claude Code
   (`irm https://claude.ai/install.ps1 | iex`), the effortless plugin, the Unity skills (section 2).
8. With the project open in Unity, in a terminal in the project folder: `unity status` should list the Editor.
9. Start Claude Code in the project folder (`cd C:\Users\<you>\Chanakya` then `claude`). It reads CLAUDE.md
   automatically. Paste the first prompt below.
10. Ask Claude to compile and run all self-tests; every one should pass (CONTEXT.md section 8).
11. Make a build (File > Build Profiles > Windows > Build into a new empty folder) to check the new PC builds.

## 5. First prompt for the fresh Claude
```
You are continuing development of Chaukanna (Unity project Chanakya) on a new PC. Read CLAUDE.md, CONTEXT.md,
TODO.md (top sections), PROGRESS.md (newest entries) and HANDOFF.md before doing anything. Follow every rule in
CLAUDE.md. First: check `unity status` sees the open Editor, recompile, and run all self-tests listed in CONTEXT.md
section 8; report the results. Do not change ProjectSettings or Packages. Then tell me in a few lines where we left off
and the open decision (sentry/commander roles in TEAM mode), and wait for my go before building anything.
```

## 6. Checks done for this handoff
- Fresh clone from GitHub at e7f703b: all LFS objects present. A headless Unity import of that clone was started to
  confirm it compiles; see PROGRESS.md 2026-10-16 "handoff" for the result.
- Tools inventory from `claude --version`, `claude plugin list`, `claude plugin marketplace list`, `claude mcp list`,
  `~/.agents/.skill-lock.json`, `unity --version`, `unity editors --installed`, `git lfs version`.
