# Plan: Real-headset VR on OpenXR (VR-1)

Planned 2026-10-16 (Opus). Built by a Sonnet agent from this file, after DS-1 and MP-1. Approved by the user in chat
(2026-10-16): add OpenXR Plugin + XR Plug-in Management + XR Interaction Toolkit ("Yes, OpenXR + XRI"); headset is a
PC-tethered SteamVR-type headset the user calls "Festo" (model unknown), so target GENERIC OpenXR and keep it
switchable to any OpenXR runtime (SteamVR, Meta / Oculus PC, WMR, Pico Connect, Vive).

## 0. What this touches outside Assets/Chaukanna (all covered by the user's yes, list them in PROGRESS)
- Packages/manifest.json + packages-lock.json: `com.unity.xr.openxr`, `com.unity.xr.management` (its dependency),
  `com.unity.xr.interaction.toolkit`. Use the versions `unity command package_search` reports as compatible with
  6000.6 (prefer the verified / recommended one). Install with `unity command package_add --identifier <id> --confirm
  true`, poll `package_status`, then recompile.
- XR Plug-in Management creates `Assets/XR/` (loader settings, OpenXR settings) and registers the loader settings in
  `ProjectSettings/EditorBuildSettings.asset` (configObject). That is expected; report it.
- NOTHING ELSE in ProjectSettings. In particular: "Initialize XR on Startup" OFF (the game must start on the desktop and
  enter VR only when asked); no change to Player settings, quality, URP assets, layers or tags. If Unity rewrites any
  other ProjectSettings file, restore it with git and say so.

## 1. Design: three view rigs, one switch
`IViewRig` already exists (Scripts/Player/ViewRig.cs) with DesktopViewRig and VrSimViewRig. Add `OpenXrViewRig`.
- `ViewRigSwitch` gains a mode: `Desktop`, `VrSim`, `Headset`. F7 cycles Desktop -> VR-sim -> Headset (Headset only
  offered when an OpenXR runtime initialises) -> Desktop. Trainer setting `ScenarioConfig.vrMode` (bool, keep for old
  presets and packages) plus new `int viewMode` (0 desktop, 1 VR-sim, 2 headset; -1 = derive from vrMode). The
  TrainerMenu and F6 panel VR button becomes a three-way selector.
- Tier strings: add `TierHeadset = "vr-openxr"`; mixed when switched during a run (existing rule).
- **XR lifecycle at runtime** (`XrRuntime` static class, Scripts/Player/XrRuntime.cs): `TryStart()` uses
  `XRGeneralSettings.Instance.Manager.InitializeLoaderSync()` then `StartSubsystems()`; returns false with a reason
  ("no OpenXR runtime: install SteamVR or the headset's PC app and set it as the OpenXR runtime", "headset not
  connected") and the switch stays on the previous rig. `Stop()` = StopSubsystems + DeinitializeLoader. Always stop
  on scene unload and on quit. Log the active runtime name (`OpenXRRuntime.name`, version) when it starts.
- OpenXR settings (Editor-only setup script, Scripts/Player/Editor/XrSetup.cs, menu "Chaukanna > VR > Set up
  OpenXR"): add the OpenXR loader for Standalone, enable interaction profiles Oculus Touch, Valve Index, HTC Vive,
  HP Reverb G2, Microsoft Motion Controller, Khronos Simple Controller; render mode Single Pass Instanced; depth
  submission on. Run it once through the CLI (`unity command menu` or eval).

## 2. The headset rig
- Head: the existing player camera, with `UnityEngine.InputSystem.XR.TrackedPoseDriver` (Input System, already in
  the project) added while in Headset mode and removed after. The camera stays under the existing observer object, so
  the post / watchtower placement and ObserverLook still own the body yaw; mouse look is off in Headset mode.
- Hands: two `TrackedPoseDriver`s on hand objects bound to `<XRController>{LeftHand}` / `{RightHand}` device
  position / rotation. Reuse the VR-sim hand models (Rig.Part primitives) so both VR modes look the same.
- Use XRI only where it saves work: `XRInteractionManager` is not needed for IMGUI-free world UI; the plan uses plain
  Input System bindings for robustness. If an XRI type is a clean fit (e.g. `XRInputModalityManager` for
  controller vs hand tracking), use it; otherwise leave XRI installed for later (ray UI on the tablet).
- Input map (Input System actions created in code, bindings to the generic OpenXR controller paths so any profile works):
  - Right trigger: lock the contact nearest the right-hand ray within tolerance (same detection test as ContactSensor;
    the ray replaces the screen centre). Through the binoculars the tolerance scales with FOV as on the desktop.
  - Both grips held near the face (existing HandGesture rule: both hands up) OR left trigger: binoculars (the zoomed
    camera FOV cannot change in a headset: render the optic as a magnified view on a quad in front of the eyes from a
    second camera with the zoomed FOV into a RenderTexture; 8 deg FOV, round mask, steady while held).
  - Right thumbstick press or primary button (A): radial call / action menu (existing RadialMenu maths); stick
    direction picks, trigger confirms. Items: 1-5 calls, Hold, Report, Jam, Engage, IFF (I), Lasso-in-view (G).
  - Left primary (X): tablet closer (existing). Left thumbstick: snap turn 30 deg (comfort), no smooth locomotion.
  - Menu button: pause overlay (resume / recenter / back to desktop).
- **World-space HUD** (IMGUI does not render in the headset): `VrHud` world-space canvas at 2 m, follows the head
  lazily (only re-centres when the view turns more than 35 deg away): ROE card banner, lock label (id, range, call,
  action, IFF / route line), group count, role line, report list for commanders (MP-1). Lock brackets as four small
  quads at the contact, scaled to constant angular size. Font: `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
  as the tablet does. All materials through RuntimeMaterials.
- Desktop mirror: the main display shows the left eye (default XR mirror), so the trainer can still watch; the F6
  trainer panel keeps working in split-screen on the desktop window.
- Comfort: target 72 fps minimum; fps shown on the tablet; the existing comfort question at the end stays.

## 3. Display contract and scoring
- `RunConditions.DisplayTier = "vr-openxr"` while the headset is active; the session file keeps the tier (existing).
- Detection threshold in the headset: use the headset's per-eye vertical FOV and pixel height
  (`XRSettings.eyeTextureHeight`, the camera's FOV while XR is on) in the apparent-size test so a speck has the same
  angular rule as on the calibrated desktop. Log arcmin/px at start.

## 4. Verification without a headset on this PC
- `XrSelfTest` (new): rig switching Desktop -> VrSim -> Headset when XR cannot start falls back with a reason and
  leaves the desktop rig intact; radial items map to the right calls / actions; viewMode / vrMode compatibility for
  old presets; VrHud re-centre rule (35 deg); bracket angular size constant at 100 m and 1000 m; input actions have
  bindings for both hands; tier strings.
- VrSelfTest (19 edit-mode checks) must still pass.
- Build: Windows build with the XR packages succeeds; starting the built game with no headset stays on the desktop
  and the F7 cycle reports "no OpenXR runtime" (run the exe with `-logFile` and read the log).
- The user tests on the real headset (hand test list in the report): F7 to Headset, look around, lock with the right
  trigger, binoculars, radial menu, tablet, 72 fps.
