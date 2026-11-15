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

# Job: SCENE - student lounge `room-lounge` (separate from the courtyard)

Key art for tone: `design/screens/12-club.png` (warm wood, burgundy/navy cushions and rugs, brass lanterns, star banners) and `design/screens/02-classroom.png`. Furniture and wall/floor style should match the already built dorm room (`Art/CampusGrid/room-dorm-*.json`, `Art/CampusGrid/interior.json`, review `Art/CampusGrid/review/room-dorm/`) so both rooms feel like the same academy. Reuse interior.json pieces where they fit.
Map size: 10 x 8 cells (32x32). Camera shows the whole room.
The game reads it at runtime from `Assets/MagicCampus/CampusGrid/Rooms/room-lounge/layout.json` + cell PNGs (same format as the courtyard and room-dorm). No Unity scene.

## Fixed gameplay coordinates (bottom-left cell = (0,0)) - must match exactly
- Door to the courtyard: cell (4,0), walkable doorway in the bottom (front) wall.
- Snack table: collision exactly cell (4,3) (1x1 low round tea/snack table with a teapot and honey bread plate). Cells around it (3,3), (5,3), (4,2), (4,4) should stay walkable (cushions or stools around it are decorative/non-blocking or placed elsewhere).
- Walls blocked; floor walkable except furniture collision.
- Furniture: sofa(s), the snack table, a fireplace on a wall (with animated fire if feasible: 4-6 frames, only the fire changes, fps 8), a noticeboard on a wall, bookshelf, armchair, rug, lanterns, plants. Keep a clear path from the door to the snack table and to the noticeboard and fireplace fronts.

## Look
Cozy and dense like the key art. No baked sun shafts or long cast shadows; fireplace and lanterns go into `glows`.

## Allowed writes
- `Art/CampusGrid/source/` (new files: `room-lounge-*-original.png`, prompts JSON)
- `Art/CampusGrid/room-lounge-art.json`, `Art/CampusGrid/room-lounge-map.json`, `Art/CampusGrid/interior.json` (append only; do not change existing entries)
- `Art/CampusGrid/build_<id>.py` + `Art/CampusGrid/prepared/<id>/` for the fire animation frames and any wall preparation (deterministic), `Art/CampusGrid/rebuild-rooms.ps1` (only to add the lounge's prep call), `Art/CampusGrid/check_room.py` (extend only)
- `Art/CampusGrid/review/room-lounge/`, `Assets/MagicCampus/CampusGrid/Rooms/room-lounge/`, `Art/_runs/`
- Do not change `build_grid.py`, the courtyard recipes, room-dorm outputs, C# or scenes. After building, room-dorm's build (same command as its README/run) must still be byte-identical if interior.json changed only by appending.

## Use the room tooling from room-dorm
- Build rooms with `.\Art\CampusGrid\rebuild-rooms.ps1` (it builds every `room-*-map.json`); add any new preparation script for the lounge into it the same way the dorm's is called.
- Check collision with `Art/CampusGrid/check_room.py room-lounge` (extend it with lounge-specific checks if needed, without breaking the dorm check).
- Lesson from the dorm's first pass: it looked like an empty wooden box. Fill walls (timber frame, banners, sconces, shelves, ivy) and floor (rugs, cushions, side tables, plants, books, lanterns) to the key art's density from the start.

## Do
1. `decomposition.md` in the run folder: 10x8 character grid plan, furniture footprints, collision cells.
2. Generate originals (base floor/walls and a furniture sheet) with the key art as input; save unchanged with prompts.
3. Build with `build_grid.py --art Art/CampusGrid/room-lounge-art.json --map Art/CampusGrid/room-lounge-map.json --unity-out Assets/MagicCampus/CampusGrid/Rooms/room-lounge --review-out Art/CampusGrid/review/room-lounge` twice; byte-identical. Rebuild room-dorm the same way and confirm it is unchanged.
4. Verify with a flood-fill script: door (4,0) open, (4,3) blocked, the door connects to all four neighbors of the table, the fireplace front and the noticeboard front; all outer wall cells blocked except the door.
5. Review: `review/room-lounge/room-lounge-vs-keyart.png`, `review/room-lounge/room-lounge-grid.png` (2x, grid, blocked red, door green, table yellow), and the fire loop as `review/room-lounge/fireplace.apng` if animated.
6. Final message: differences from the key art, collision summary, checks.


Run folder for notes and decomposition files: Art/_runs/20261111-0956-room-lounge
