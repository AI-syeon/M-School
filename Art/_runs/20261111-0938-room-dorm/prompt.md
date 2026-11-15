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

# Job: SCENE - dormitory room `room-dorm` (separate from the courtyard)

Key art: `design/screens/06-room.png` (main) and `design/screens/16-journal.png` (same room, more detail). Read both.
Map size: 12 x 9 cells (32x32). Camera shows the whole room (12 x 9).
The game reads this room at runtime from `Assets/MagicCampus/CampusGrid/Rooms/room-dorm/layout.json` + cell PNGs, same layout.json format as the courtyard. It does NOT use a Unity scene for it.

## Fixed gameplay coordinates (grid, bottom-left cell = (0,0), x right, y up) - must match exactly
- Door to the courtyard: cell (5,0), walkable, visibly a doorway/opening in the bottom (front) wall or balustrade, like the bottom center of the key art.
- Bed: collision exactly cells (1,5) and (1,6) (1 wide x 2 tall, head toward the top). The bed sprite may be visually wider/taller (canopy, headboard on the wall) but must NOT visually cover cell (2,5).
- Sleeping spot: cell (2,5), walkable floor right beside the bed (a small rug or slippers there is good).
- All walls are blocked. Interior floor is walkable except furniture collision.
- Furniture from the key art, adapted to 12x9: canopy bed (navy with gold stars), bookshelf, desk with chair and armillary/books, window seat under the big window on the top wall, wardrobe, round star rug, nightstand, mirror, plants, lanterns/candles. Keep a clear walking area from the door to the desk, the bed side, and the wardrobe.

## Look
Match the key art: warm wood floor and walls, timber frame, navy/purple/gold textiles, plants, candles. Cozy and dense like the art, not an empty box. Same pixel scale and camera as the courtyard props. No baked directional sun shafts or long cast shadows (only soft contact occlusion); window light / candle glow go into `glows`.

## Allowed writes
- `Art/CampusGrid/source/` (new files: `room-dorm-*-original.png`, prompts JSON)
- `Art/CampusGrid/room-dorm-art.json` (this room's own art recipe: its ground/wall tiles and `libraries`), `Art/CampusGrid/interior.json` (new library for room furniture; reusable by other rooms), `Art/CampusGrid/room-dorm-map.json`
- `Art/CampusGrid/review/room-dorm/`
- `Assets/MagicCampus/CampusGrid/Rooms/room-dorm/` (build output)
- `Art/_runs/`
- `Art/CampusGrid/build_grid.py` ONLY for a small backward-compatible extension if needed (for example a map-level `"blocked": [[x,y],...]` list for walls, written into layout.json). After any change, the courtyard build must stay byte-identical: run `.\Art\CampusGrid\rebuild.ps1` before and after and compare hashes of `Assets/MagicCampus/CampusGrid/layout.json` and `Art/CampusGrid/review/build-report.json`. Document the new field in `Art/CampusGrid/README.md` (one short paragraph).
Do not edit the courtyard `art.json` / `map.json`, C# files, or Unity scenes.

## Do
1. Write `decomposition.md` in the run folder: wall/floor plan in a 12x9 character grid, every piece of furniture with footprint, collision cells, and which cell it sits on.
2. Generate originals (room floor/wall base image and a furniture sheet, using the key art as input). Save unchanged with prompts.
3. Build with `build_grid.py --art Art/CampusGrid/room-dorm-art.json --map Art/CampusGrid/room-dorm-map.json --unity-out Assets/MagicCampus/CampusGrid/Rooms/room-dorm --review-out Art/CampusGrid/review/room-dorm` (pipeline python). Run twice; outputs byte-identical.
4. Verify in the output layout.json: door cell (5,0) not blocked; (1,5),(1,6) blocked; (2,5) not blocked; all outer wall cells blocked except the door; the walkable area is connected from the door to (2,5), the desk front and the wardrobe front (write a tiny flood-fill check script in the run folder and report its result).
5. Review images: `review/room-dorm/room-dorm-vs-keyart.png` (key art and assembled room side by side, same height) and `review/room-dorm/room-dorm-grid.png` (assembled room at 2x with a 32px grid overlay, blocked cells tinted red, door green, sleep spot blue).
6. Final message: what differs from the key art and why, collision summary, checks.


Run folder for notes and decomposition files: Art/_runs/20261111-0938-room-dorm
