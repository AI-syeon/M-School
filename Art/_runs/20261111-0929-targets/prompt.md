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

# Job: PRODUCTION assets (registered in the CampusGrid recipe) - magic target props, 2 states each

Reference: `design/screens/01-campus.png` and `design/screens/03-magic.png` for style; must sit naturally in the courtyard (`Art/CampusGrid/review/courtyard-unity.png`) next to existing props (bench, lamp, planters).
These are objects the player casts spells on. The two states of each object must share the SAME canvas and anchor (pixel-aligned base), only the changed parts differ, and the difference must be obvious at 1x.

## Assets
| id (state A) | id (state B) | cells [w,h] | blocked | state A | state B |
|---|---|---|---|---|---|
| target-signboard-dark | target-signboard-lit | [1,2] | [[0,0]] | wooden direction signboard on a post with a small unlit lantern, dull | same, lantern lit warm, sign glyphs faintly glowing gold |
| target-pillar-broken | target-pillar-mended | [1,2] | [[0,0]] | short cream limestone pillar, cracked, a chunk missing, rubble at base | same pillar whole and mended, faint gold seam where the crack was |
| target-seedpot-dry | target-seedpot-sprout | [1,1] | [[0,0]] | terracotta pot with dry cracked soil | same pot, dark moist soil with a fresh green sprout |
| target-moss-plain | target-moss-bloom | [1,1] | [[0,0]] | low mossy boulder | same boulder, moss covered in tiny white and lavender flowers |
| target-plaque-dim | target-plaque-glow | [1,1] | [[0,0]] | small stone record tablet on a low base, worn faint engraving | same tablet, engraving lines glowing cyan-blue (no readable letters) |
| target-rune-stone-off | target-rune-stone-on | [1,1] | [[0,0]] | standing rune stone, dark grey, carved rune dull | same stone, rune resonating violet-blue with a small glow halo inside the cell |

Library file: `Art/CampusGrid/targets.json` (new - add it to `art.json` `libraries`).
IMPORTANT: register only. Do NOT place these in `map.json`; positions come from game content.

## Allowed writes
`Art/CampusGrid/source/` (new files only), `Art/CampusGrid/targets.json`, `Art/CampusGrid/art.json` (only to add the library), `Art/CampusGrid/review/`, and whatever `rebuild.ps1` writes under `Assets/MagicCampus/CampusGrid/`. If you need a deterministic preparation script for aligning the two states, name it `Art/CampusGrid/build_targets.py`, write prepared frames to `Art/CampusGrid/prepared/targets/`, and call it from `rebuild.ps1` the same way `build_fountain.py` is called (that is the only allowed edit to rebuild.ps1).

## Do
1. Generate originals into `Art/CampusGrid/source/targets-original.png` (+ `targets-prompt.json`). Best: generate state A and state B of each object together so shape and camera match; record crop rectangles.
2. Make sure each pair is aligned: if crops differ, prepare aligned frames deterministically with `build_targets.py`.
3. Add 12 recipe entries (each state its own id, single frame, no fps).
4. Run `.\Art\CampusGrid\rebuild.ps1` twice; it must succeed and `Assets/MagicCampus/CampusGrid/layout.json` + `Art/CampusGrid/review/build-report.json` must be byte-identical between runs. Existing assets must not change (compare the existing ids' cell PNG hashes before and after).
5. Review `Art/CampusGrid/review/targets-sheet.png`: each pair A|B at 1x and 4x, and each standing on paving next to the existing bench for scale. Report ids, cells, collision and checks.


Run folder for notes and decomposition files: Art/_runs/20261111-0929-targets
