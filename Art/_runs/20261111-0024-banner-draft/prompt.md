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

# Job: DRAFT variants (not registered in the recipe)

Subject: standing courtyard banner on a dark iron pole - deep navy blue cloth with a gold eight-pointed star, small gold finial on top, small stone or iron base
Footprint: 1x3 cells = 32x96 px
Reference: design/screens/01-campus.png, the navy star banners on poles beside the lamps (left middle at the walking path, and lower right near the stairs)
Number of variants: 3
Must keep: navy cloth + gold star motif from the key art
Change only: free (pole style, cloth shape and hem, base)

## Output folder
`ArtSamples/banner-draft/` only. Do not touch the recipe, `Art/CampusGrid/` or `Assets/`.

## Do
1. Make 3 genuinely different directions (silhouette, material, ornament, color emphasis), not recolors of one design. Under the same conditions so they can be compared fairly.
2. For each variant `NN` (01, 02, 03): `original-NN.png` (generated source, unchanged) and `pixel-NN.png` (final RGBA at exactly 32x96, transparent background).
   A small helper script for pixelizing is allowed here; save it in the output folder. Use the same method for every variant (nearest-neighbor only, palette limit stated in notes).
3. `sheet.png`: all pixel variants side by side at 4x nearest-neighbor on a mid-grey background, each above its number, plus one row showing each at 1x placed on a 3x3 patch of an existing ground tile (`Assets/MagicCampus/CampusGrid/Textures/ground-paving-00.png`).
4. `notes.md`: the generation prompt(s), one line per variant describing its direction, the pixelization method, and color counts.


Run folder for notes and decomposition files: Art/_runs/20261111-0024-banner-draft
