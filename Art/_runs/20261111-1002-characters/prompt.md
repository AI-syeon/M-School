# MagicCampus art job - shared rules (always apply)

You are making art resources for MagicCampus, a cozy top-down magic-academy life sim (Stardew-like) built in Unity on a 32x32 px tile grid (1 cell = 1 Unity unit, 32 PPU, point filtering).

## Read first
- `Art/CampusGrid/README.md` (recipe format, coordinates, rebuild steps).
- `C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/AGENTS.md` and `README.md`.
- Use only `C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe` for Python. Do not modify anything inside the pipeline folder.

## Visual target
- The key art in `design/screens/` is the reference. The approved in-engine look is `Art/CampusGrid/review/daylight-times.png` and `Art/CampusGrid/review/courtyard-unity.png`; new assets must sit next to those without looking foreign.
- Camera: orthographic, elevated front-facing 3/4 view (about 30 degrees), verticals vertical, tops and front faces visible. Light from upper left.
- Palette: warm cream/taupe stone, oak and honey wood, muted brass/gold, moss and olive greens, autumn orange/gold foliage, peacock blue water/crystal accents.
- Refined clustered pixel art with readable hard-edged color clusters. Must read clearly at real game size.

## Avoid the "AI look"
Muddy gradients and airbrushing; noisy single-pixel dithering; mixed pixel scales; mixed perspectives inside one asset; random detail with no material logic; soft or blurry edges after downscale; fake text or letters; checkerboard or white halos around transparent edges; everything perfectly symmetric and samey. Check your own output for these and fix them.

## Light and shadow
Do NOT bake directional cast shadows, sun rays, dappled leaf shadows or time-of-day color into tiles or props. Only a small soft contact occlusion under a prop is allowed. Time-of-day light and shadows are separate Unity layers (`CampusDaylight`, `map.json` daylight / shadows / glows).

## How assets are made
1. Generate the ORIGINAL with your built-in image generation tool, using the key art as reference input when relevant. Transparent background for props; isolated items with wide transparent gutters for sheets.
2. Save the original unchanged and record the generation prompt (JSON: file, tool, reference, prompt) next to it.
3. Pixelize and cut to 32x32 cells. For production assets this must go through the recipe (`art.json` / a library JSON / `map.json`) and `Art/CampusGrid/rebuild.ps1` so that rerunning reproduces identical bytes. Multi-cell props: pixelize the whole frame first, then cut. Animated props: all frames share canvas size and anchor; only moving parts change.
4. Keep the original and the pixel result side by side for comparison.
5. Recipes must never reference files under `Art/_runs/` (a log folder that may be deleted). Preparation scripts live next to the recipe (`Art/CampusGrid/build_*.py`) and write to `Art/CampusGrid/prepared/` (git-ignored, so a rebuild script must regenerate it).
6. Scenes must be as dense and cozy as the key art: walls with structure and decoration, small clutter, plants, light sources. A large bare floor or plain wall is a failure even if collision checks pass.

## Boundaries
- Write only to the paths this brief allows, plus `Art/_runs/`. Never overwrite existing source art; use a new name or `-v2`.
- No git commands. No paid external APIs. No edits to C# code, scenes outside `Assets/MagicCampus/CampusGrid/Generated/`, or design documents.
- If the request cannot be met within these rules, stop and explain why in the final message instead of working around it.

## Before you finish
- Open and look at your results at 1x and enlarged. Fix obvious problems once.
- Final message (plain text, may be Korean): files written, checks run with their results (sizes, alpha, color counts, rebuild success, reproducibility), and remaining problems. Do not claim anything you did not check.


---

# Job: PRODUCTION character sprites at 32px scale (player, npc-serin, npc-mira)

Why: the current characters (`Art/Stage5-v1/characters/<id>/`) are 16x32 pixel sprites; the world is now 32x32 tiles (courtyard `Art/CampusGrid/review/courtyard-unity.png`), so they look low-res next to it. Redraw them at the courtyard's pixel scale.

## Characters
- `player`: the protagonist of the key art (`design/screens/06-room.png`, `design/screens/03-magic.png`): short silver-white hair, purple robe with gold trim, brown satchel. Keep the key art's proportions (chibi-ish, big head).
- `npc-serin` and `npc-mira`: keep each one's identity (hair color/style, outfit colors) from the existing sprites `Art/Stage5-v1/characters/npc.serin/` and `npc.mira/` (look at `pose_*.png` and `Art/Stage5-v1/character-review.png`), but draw them in the same style as the courtyard students (`Art/CampusGrid/students.json`, source `Art/CampusGrid/source/students-original.png`). The two NPCs must be clearly distinguishable from each other and from the player at 1x.

## Frames
- Frame size 32x64 (same cell footprint as the courtyard student props: 1 cell wide, 2 tall). Figure height about 44-52 px, feet at the bottom center: pivot = (16, 62) in top-left pixel coordinates for every frame of every character (feet baseline at y=62).
- Directions: south, north, east, west (west may be a mirrored east if the design is symmetric enough; the satchel side may swap, that is fine).
- Clips per direction: `idle` 1 frame; `walk` 4 frames (contact, passing, contact, passing), fps 8, loop. Walk must read as walking at 1x: leg alternation, small body bob (1 px), arms/robe swing.
- Same palette discipline as the courtyard (about 24-32 colors per character), 1px dark outline (warm dark, not pure black), light from upper left, no cast shadow in the frame (the game draws a contact shadow).

## Allowed writes
`Art/Characters/` (source, build script, review) and `Assets/MagicCampus/Art/Characters/` (final). Nothing else. No Unity .meta files.

## Do
1. Generate originals: per character a turnaround / pose sheet (south, north, east, plus a walking pose) with the image tool, using the key art and existing sprites as input. Save unchanged in `Art/Characters/source/<id>-original.png` + `<id>-prompt.json`.
2. `Art/Characters/build_characters.py` (pipeline python) builds all frames deterministically: pixelize the base poses to the frame size, clean them, and derive walk frames reproducibly (hand-tuned pixel edits and part offsets in code are fine; no unseeded randomness). Rerun must be byte-identical.
3. Output per character in `Assets/MagicCampus/Art/Characters/<id>/`: individual frames `<dir>_<clip>_<nnn>.png`, `sprite-sheet-alpha.png`, and `manifest.json` in the SAME format as `Art/Stage5-v1/characters/npc.mira/manifest.json` (`id`, `type`, `game_input`, `size`, `sheetSize`, `origin: top-left`, `animations.<dir>.<clip>.{frames,fps,loop,rectangles}`) plus `"pivot": [16, 62]` and `"pixelsPerUnit": 32`.
4. Review: `Art/Characters/review/characters-sheet.png` (every frame of the three characters at 3x on grey, rows = character x direction), `Art/Characters/review/<id>-walk-<dir>.gif` or apng loops, and `Art/Characters/review/in-courtyard.png` (the three idle-south at 1x and 2x placed on the courtyard image next to existing student props for scale comparison), and `Art/Characters/review/old-vs-new.png`.
5. Verify: all frames 32x64 RGBA, transparent edges, feet baseline at y=62 in every frame (measure lowest opaque row), manifest rectangles match the sheet, identical rebuild. Report.


Run folder for notes and decomposition files: Art/_runs/20261111-1002-characters
