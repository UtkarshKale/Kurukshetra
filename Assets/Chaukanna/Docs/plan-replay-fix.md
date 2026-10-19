# Plan: Replay map fix and upgrade (FIX-2b)

Planned 2026-10-17 (Opus). User: "the replay menu should show how the trainer can see the minimap and how the player is
moving and what he is doing; green lines appear and it's a glitch".

## Cause (read in ScoreDashboard.Replay and LassoMath.Line)
- The two view lines are drawn from the soldier's logged position with a fixed length `radius * scale`, and nothing
  clips them to the replay map square. When the soldier is away from the post (movement allowed) or the window is
  narrow, they run off the map across the rest of the dashboard: the stray green lines.
- The dashboard draws under a scaled `GUI.matrix`, so `LassoMath.Line` falls back to rows of small squares; correct but
  unclipped. The trainer's F6 truth map uses the same helpers (`Dotted`) and has the same risk.

## Fix
1. `Scripts/Trainer/GuiDraw.cs` (new): `ClipSegment(ref a, ref b, Rect r)` (Liang-Barsky), `Line(a, b, width, colour,
   Rect clip)` that clips first and then draws (squares under a scaled matrix, rotated strip otherwise), and
   `Wedge(apex, yawDeg, halfFovDeg, length, colour, Rect clip)` that fills the view cone with horizontal spans (2 px
   rows, clipped), cheap enough for every repaint.
2. Replay map: everything inside `GUI.BeginGroup(map)` / `EndGroup` (coordinates relative to the map), so nothing can
   draw outside it. Replace the two lines with a faint filled view wedge plus its two edges, clipped.
3. Show the soldier moving and acting (what the user asked for):
   - soldier marker: a small arrow pointing along the view yaw at the logged position;
   - soldier path: the positions from `RunLog.view` up to `replayT` as a dotted trail (fades with age);
   - contact trails: the last 10 s of each contact's track as a faint line in its class colour;
   - events: when `replayT` passes a call or action, a short label pops at the contact for 4 s ("called Hostile quad",
     "JAM: link lost", "ENGAGE: hit") and the line below the map lists the last 5 events with their times;
   - binoculars: when the logged FOV is under 20 deg, the wedge is drawn narrow and bright (the soldier was looking
     through the optic). (`RunLog.view` already has fov.)
   - optional "Follow" toggle: centre the map on the soldier instead of the post.
4. The F6 truth map (TrainerScreen.DrawMap) and the TrainerMenu map: route `Dotted` / any line through
   `GuiDraw.Line` with the map rect as the clip.
5. HTML report replay (HtmlReport.cs, plain JavaScript canvas): check it clips (canvas does) and add the same soldier
   path and event labels if simple.
6. Crowd: not drawn in the replay (DS-4 item), unchanged.

## Loose ends from TL-1 (same pass)
- The runtime fallback tree (used when the kit has no tree prefab) logs "couldn't be instanced because the prefab
  contains no valid mesh renderer" and never draws on the terrain: Unity terrain trees need a MeshRenderer (or a
  LODGroup whose LODs have renderers) on the prototype prefab. Make the fallback a real prototype (root MeshRenderer +
  MeshFilter, or LODGroup set up exactly like the kit placeholders), and add a LayersSelfTest check that no warning is
  logged for it.
- OSM: finish the approved one-time re-fetch that the Overpass server refused with HTTP 504: Kargil (whole area) and
  the surround queries of Jammu_AFS and telaviv. One more try each with the Editor menu
  `Chaukanna > Terrain > Fetch OSM layers...` (one area at a time, polite), report the counts or the failure.

## Verify
- `ReplaySelfTest` (new): ClipSegment cases (inside, crossing, outside, corner, degenerate); Wedge spans stay inside the
  clip rect for apex inside / outside the rect and fov 4 to 120 deg; event list ordering; soldier path sampling.
- Render check without the screen: draw the replay into a RenderTexture from an eval script
  (`RenderTexture.active` + `GUI` is not usable outside OnGUI, so instead unit-test the geometry and also count
  draw calls outside the map rect = 0 with a recording stub around GuiDraw).
- All existing self-tests keep their counts.
