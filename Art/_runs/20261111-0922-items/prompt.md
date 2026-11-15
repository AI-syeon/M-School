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

# Job: PRODUCTION item icons (not a tilemap asset)

Nine inventory item icons for MagicCampus. They sit in a 40x40 UI slot (`Assets/MagicCampus/Art/UI/ui-slot-normal.png`, deep navy with gold frame) and may also lie on the courtyard ground, so they must read on both dark navy and light stone.

## Icons (ids are final)
| id | what it is |
|---|---|
| item-lens | practice lens for magic class: round brass-rimmed magnifying lens with a short handle, faint blue glint |
| item-rune-chalk | rune chalk: stubby white chalk stick with a faint glowing violet rune mark, maybe a paper wrap |
| item-moonbean-seed | moonbean seeds: 2-3 pale silver-blue bean seeds with a crescent mark |
| item-frost-mint | frost mint: sprig of mint leaves with icy pale-cyan frosted edges |
| item-crystal-dust | crystal dust: small corked glass vial or pouch of sparkling lilac crystal powder |
| item-honey-bread | honey bread: small golden round loaf with honey drizzle |
| item-focus-potion | focus potion: round-bottom flask, deep teal liquid, cork, tiny star label |
| item-student-card | student ID card: small cream card with navy band and gold academy star emblem, tiny portrait square (no text) |
| item-torn-note | torn note: torn parchment scrap with scribble lines (no readable text), slight curl |

## Look
- 32x32 RGBA, transparent background, object fills about 24-28 px, centered, 1px dark outline (warm dark brown/navy, not pure black), light from upper left, 2-3 shade steps per material.
- One shared palette across the 9 icons (about 32 colors total), consistent with the courtyard props and the UI kit.
- Clear silhouettes; distinguishable from each other at 1x at a glance (colors and shapes differ).

## Allowed writes
`Art/Items/` (source, build script, review) and `Assets/MagicCampus/Art/Items/` (final PNGs + manifest.json). Nothing else. Do not create or edit Unity .meta files.

## Do
1. Generate one original sheet with all nine items isolated with wide transparent gutters, same camera and lighting, using the key art as style input. Save unchanged as `Art/Items/source/items-original.png` with `Art/Items/source/items-prompt.json` (record crop rectangle per item).
2. `Art/Items/build_items.py` (pipeline python) builds the final 32x32 icons deterministically from the crops (downsample, palette quantize, outline cleanup; hand-tuned pixel fixes in code are fine). Rerun must give identical bytes.
3. Output `Assets/MagicCampus/Art/Items/<id>.png` and `manifest.json`: `{"schema":1,"pixelsPerUnit":32,"filter":"point","items":{"item-lens":{"file":"item-lens.png","size":[32,32]}, ...}}`.
4. Review: `Art/Items/review/items-preview.png` showing each icon at 1x and 4x, inside the UI slot (1x and 3x), and on a 32x32 paving tile (`Assets/MagicCampus/CampusGrid/Textures/ground-paving-00.png`); `Art/Items/review/source-vs-pixel.png`.
5. Verify sizes, alpha (fully transparent edges), palette count, identical rebuild. Report.


Run folder for notes and decomposition files: Art/_runs/20261111-0922-items
