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

## Boundaries
- Write only to the paths this brief allows, plus `Art/_runs/`. Never overwrite existing source art; use a new name or `-v2`.
- No git commands. No paid external APIs. No edits to C# code, scenes outside `Assets/MagicCampus/CampusGrid/Generated/`, or design documents.
- If the request cannot be met within these rules, stop and explain why in the final message instead of working around it.

## Before you finish
- Open and look at your results at 1x and enlarged. Fix obvious problems once.
- Final message (plain text, may be Korean): files written, checks run with their results (sizes, alpha, color counts, rebuild success, reproducibility), and remaining problems. Do not claim anything you did not check.


---

# Job: FIX round for scene `room-dorm` (built in run `Art/_runs/20261111-0938-room-dorm/`)

Read that run's `decomposition.md`, `last.txt`, `build_art.py`, and the review images `Art/CampusGrid/review/room-dorm/room-dorm-vs-keyart.png` and `room-dorm-grid.png` first. Keep what works.

## Keep fixed (do not change)
- 12x9 map; door (5,0) walkable; bed collision exactly (1,5),(1,6); sleeping spot (2,5) walkable and not visually covered; all outer walls blocked except the door; walkable area connected from the door to (2,5), desk front, wardrobe front.
- The good parts: desk with armillary and chair, window seat under the arched window, wardrobe, bookshelf, round star rug, mirror. Their style and colors stay.

## Problem 1 - reproducibility (must fix)
`Art/CampusGrid/interior.json` references `../../Art/_runs/20261111-0938-room-dorm/prepared/wall-*.png`. `Art/_runs/` is a log folder and may be deleted. Move the preparation into the recipe folder:
- `Art/CampusGrid/build_room_walls.py` (deterministic; same output as now unless Problem 2 changes walls) writing `Art/CampusGrid/prepared/room-dorm/wall-*.png`.
- Update `interior.json` sources to `prepared/room-dorm/...` (paths relative to `Art/CampusGrid/`).
- Add `Art/CampusGrid/rebuild-rooms.ps1` (ASCII only, PowerShell 5.1, same parameter style as `rebuild.ps1`) that runs the prep scripts and then builds each room: `build_grid.py --art room-<id>-art.json --map room-<id>-map.json --unity-out Assets/MagicCampus/CampusGrid/Rooms/room-<id> --review-out Art/CampusGrid/review/room-<id>` for every `room-*-map.json` present. `Art/CampusGrid/prepared/` is git-ignored, so the script must regenerate it.
- Add a short "Rooms" section to `Art/CampusGrid/README.md`: how to rebuild rooms, where outputs go, and that collision comes from prop `blocked` cells (walls are props) - state the layout.json fields the game reads for collision (`props[].blocked` with prop-local bottom-left cells + `placements[].x/y`, unless `decorative`).

## Problem 2 - looks sparse next to the key art (must fix)
Compared with `design/screens/06-room.png` the room reads as an empty wooden box: large bare floor in the middle and lower half, plain walls, almost no warm light or small clutter. The key art is dense and cozy. Add, within the fixed constraints:
- Walls: timber-frame posts and beams with plaster panels like the key art, a hanging navy moon banner, wall sconces/lanterns, hanging ivy, a star garland near the bed canopy. Rear wall should feel like the key art's top band (window with curtains, shelf items).
- Floor: a second rug (green patterned runner leading from the door upward), a trunk/chest, a nightstand with a lamp beside the bed (not on (2,5)), a stool/ottoman, stacked books, potted plants in corners, candles; a sleeping cat on the window seat or bed is welcome.
- Bed: richer like the key art (navy canopy with gold stars, white pillow, star quilt) while collision stays (1,5),(1,6); the canopy/headboard may extend visually upward and to x=0 but not over (2,5).
- Use `glows` for lanterns, candles, window light; no baked light shafts.
- Decoration that is walkable must be `decorative: true`; anything solid must block its footprint and the connectivity check must still pass.
- Keep the floor plank pixel scale consistent with the props (no stretched/upscaled-looking planks).

## Allowed writes
`Art/CampusGrid/source/` (new files only), `Art/CampusGrid/interior.json`, `Art/CampusGrid/room-dorm-art.json`, `Art/CampusGrid/room-dorm-map.json`, `Art/CampusGrid/build_room_walls.py`, other new `Art/CampusGrid/build_room_*.py`, `Art/CampusGrid/prepared/room-dorm/`, `Art/CampusGrid/rebuild-rooms.ps1`, `Art/CampusGrid/README.md` (Rooms section only), `Art/CampusGrid/review/room-dorm/`, `Assets/MagicCampus/CampusGrid/Rooms/room-dorm/`, `Art/_runs/`. Not the courtyard recipes, `build_grid.py`, `rebuild.ps1`, C#, scenes.

## Do
1. Fix Problem 1, then Problem 2. Generate new originals if needed (key art as input), saved unchanged with prompts.
2. Run `.\Art\CampusGrid\rebuild-rooms.ps1` twice: success and byte-identical room outputs. Run `.\Art\CampusGrid\rebuild.ps1` once and confirm the courtyard `layout.json` hash is unchanged from before your work.
3. Rerun the collision/connectivity check (copy the check script into `Art/CampusGrid/check_room.py` so it is reusable, taking the room id as argument).
4. Refresh `room-dorm-vs-keyart.png` and `room-dorm-grid.png`, and add `room-dorm-before-after.png` (previous vs new assembled room).
5. Final message: what changed, remaining differences from the key art, checks.


Run folder for notes and decomposition files: Art/_runs/20261111-0947-room-dorm-fix
