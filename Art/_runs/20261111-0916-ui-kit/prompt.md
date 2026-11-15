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

# Job: PRODUCTION UI kit (not a tilemap asset)

The game (960x640 window, pixel art, 32x32 world tiles) needs a readable UI kit. Text will be the pixel Korean font Galmuri at 11px and 9px, rendered pixel-perfect. The kit must look hand-made pixel UI, like premium cozy pixel games, not a smooth vector UI.

Reference tone: `design/screens/06-room.png` and `design/screens/16-journal.png` (deep navy / plum with gold ornament on warm dark wood world). Panels sit on top of the bright courtyard (`Art/CampusGrid/review/courtyard-unity.png`), so text contrast matters most.

## Elements (ids are final; lowercase-hyphen)
| id | states | size (px) | 9-slice |
|---|---|---|---|
| ui-panel | normal | 48x48 | corners 16px (borders 16,16,16,16) |
| ui-button | normal, selected (brighter gold border / light rim), disabled (desaturated, darker) | 48x24 suggested | borders about 8px; you decide exact values |
| ui-slot | normal, selected | 40x40 (holds a 32x32 item icon with 4px frame) | not sliced (fixed size) |
| ui-quickslot | normal, selected | 40x40 with a small reserved corner spot for a digit 1-9 (top-left, about 9x9 px) | not sliced |
| ui-namebox | normal | 48x20 suggested (fits one line of 11px text) | borders you decide |

Look:
- Body: deep navy fill, semi-transparent (alpha about 220-235 of 255), flat or with a very subtle 1px inner bevel. No gradients that band, no noise texture behind text.
- Border: crisp 1-2 px gold pixel lines (2 tones: highlight and shadow), dark 1px outer outline so it reads on bright ground. Small gold corner ornament (star or filigree) contained inside the 16px corner of the panel.
- Selected state: clearly different at a glance (brighter gold or pale highlight ring), same size.
- Everything on exact pixel grid, nearest-neighbor only, limited palette (about 12-16 colors shared across the whole kit).

## Allowed writes
`Art/UI/` (source, build script, review) and `Assets/MagicCampus/Art/UI/` (final PNGs + manifest.json). Nothing else.

## Do
1. Generate an original UI concept sheet with the image tool (reference images above as input). Save unchanged as `Art/UI/source/ui-kit-original.png` with `Art/UI/source/ui-kit-prompt.json`.
2. Generated images will not be pixel-exact. Produce the final pixel art reproducibly: write `Art/UI/build_ui.py` (run with the pipeline python) that builds every final PNG deterministically, either by downsampling/cleaning crops of the original or by drawing the pixel shapes in code with the palette taken from the original. Clean 1px lines and symmetric corners are required for 9-slice; hand-tuned pixel data in the script is fine. Rerunning must give identical bytes.
3. Output files in `Assets/MagicCampus/Art/UI/`: `ui-panel.png`, `ui-button-normal.png`, `ui-button-selected.png`, `ui-button-disabled.png`, `ui-slot-normal.png`, `ui-slot-selected.png`, `ui-quickslot-normal.png`, `ui-quickslot-selected.png`, `ui-namebox.png`, and `manifest.json`:
   `{"schema":1,"pixelsPerUnit":32,"filter":"point","elements":{"ui-panel":{"states":{"normal":"ui-panel.png"},"size":[48,48],"slice":{"left":16,"top":16,"right":16,"bottom":16},"contentPadding":{...}}, ...}}` - include `contentPadding` (where text/icon may go) for every element, and for ui-quickslot the digit rectangle `digitRect:[x,y,w,h]` (top-left origin).
   Do not create or edit Unity .meta files.
4. Review image `Art/UI/review/ui-kit-preview.png`: on a crop of `Art/CampusGrid/review/courtyard-unity.png` at 2x nearest, show the panel stretched to about 300x120 (via 9-slice, done in the script), a row of 3 buttons in all states stretched to 96x24, a 3x2 grid of slots with one selected, a quickslot bar of 9 with one selected, and a namebox stretched to 80x20. Put plain white rectangles where text would go (no fake text). Also `Art/UI/review/ui-kit-1x.png` with every final PNG at 1x and 4x on grey.
5. Verify: every PNG RGBA with the exact listed size; 9-slice corners are not distorted when stretched (check your preview); rebuild twice gives identical bytes. Report slice values, palette count and checks.


Run folder for notes and decomposition files: Art/_runs/20261111-0916-ui-kit
