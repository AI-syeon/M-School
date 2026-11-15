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

# Job: NPC ACTION ANIMATIONS (south-facing) for npc-serin and npc-mira

Why: a daily-routine system will make NPCs walk to places and then act (read, eat, write, chat). The game needs a short looping action animation for each, using the same 32x64 character cells as the existing idle/walk clips. This is NOT a CampusGrid tile/prop job; ignore the CampusGrid parts of the shared rules (rebuild.ps1, art.json, map.json). The "How assets are made" steps still apply: original first, then local pixelization, originals and prompts kept, reproducible rebuild.

## Existing character pipeline (read all of it first)
- `Art/Characters/build_characters.py` builds the three characters (player, npc-serin, npc-mira) from `Art/Characters/source/<id>-original.png` (+ `<id>-prompt.json`) into `Assets/MagicCampus/Art/Characters/<id>/` (frames `<facing>_<clip>_NNN.png`, `sprite-sheet-alpha.png`, `manifest.json`) and QA images in `Art/Characters/review/`. Study how `place()`, `outline()`, `quantize_shared()` and the atlas work, and look at `Art/Characters/review/source-vs-pixel.png`, `characters-sheet.png`, and `Assets/MagicCampus/Art/Characters/npc-serin/manifest.json`.
- Frame facts: 32x64 RGBA, binary alpha, transparent border rows/columns, a 1px warm dark outline (57,39,38) added by script, feet on row 62 (pivot [16,62]), figure height 44..52, one shared palette of 24..32 colours per character, nearest-neighbour only, no dithering, no cast/contact shadow.
- The game loads every clip under `animations.<facing>.<name>` of the manifest as key "<facing>.<name>" (`WorldView.cs LoadCharacter`, read only; do not edit C#).
- Existing source images have the character in 4 evenly spaced 384px-wide cells of a 1536x1024 image (see `normalize()` and the crop in `build()`).

## Deliver, for BOTH npc-serin and npc-mira
Four actions, SOUTH-facing only (front view toward the camera, same elevated 3/4 front camera and same upper-left light as the existing south idle), each a 4-frame seamless loop:
| id | what the character does (standing, feet planted in the idle stance) | fps |
|---|---|---|
| `read` | holds an open book in both hands at chest height, head tilted slightly down; loop = small head dip plus a page lifting and settling or a thumb moving, 1-2 px of motion per step | 4 |
| `eat` | eats a small bread roll or snack held in one hand; loop = hand at chest, raised to mouth, bite (cheek puffs / mouth open), lowered while chewing | 5 |
| `write` | holds a small notebook in one hand at waist/chest height and writes with a pen in the other; loop = pen tip moves across the page, head bowed slightly, tiny shoulder motion | 6 |
| `chat` | talks with a hand gesture; loop = mouth open/closed alternating, one hand lifts to gesture and returns, slight head tilt | 5 |

Hard requirements:
1. Same character: same face, hair, coat, proportions (same head size and body height as the idle), same palette, same outline. Swapping from idle to an action must not make the character jump, resize or shift: boots in the same pixel columns and on the same rows as the idle (within 1 px). Props (book, bread, notebook, pen) must read clearly at 1x and stay inside the 32x64 cell with transparent border. No extra people, no readable text or letters on the book/notebook (use abstract lines), no ground shadow.
2. Generated in the same art style as the existing source images (clean cel-shaded chibi, warm muted palette). Props use the existing warm palette: cream paper, oak/honey brown, muted brass, one muted accent colour per prop at most.
3. Existing clips must keep IDENTICAL pixels: all 20 frames of each of player, npc-serin, npc-mira (idle and walk, all four facings) and their sheet pixels. Do not add action frames to the `quantize_shared` call that quantizes the existing frames (that would shift the palette). Quantize action frames separately: map them onto the character's existing palette (nearest colour, no dithering) and add at most 8 extra shared prop colours per character if props cannot be drawn with the existing palette. Everything for `player` must stay byte-identical (files and sheet).
4. Sheet and manifest: keep the existing 5-column x 4-row region and its rectangles exactly as they are; append the new frames BELOW it, one action per row, left-aligned (rows y=256, 320, 384, 448 -> sheet 160x512), in order read, eat, write, chat. In `manifest.json` add, inside `animations.south` after `walk`, the keys `read`, `eat`, `write`, `chat`, each `{ "frames": 4, "fps": <fps above>, "loop": true, "rectangles": [ {x,y,w:32,h:64} x4 ] }` in the same key order and JSON formatting as existing clips, and update `sheetSize`. Also save the individual frames next to the others as `south_read_000.png` ... `south_chat_003.png`. npc-serin and npc-mira only.
5. Originals and prompts: for each character and action save the generated original unchanged under `Art/Characters/source/` with names like `npc-serin-read-original.png` (one image with 4 frames in a row, 4 evenly spaced 384px-wide cells in a landscape ~1536x1024 image, transparent background, figures at least 380 px tall; you may put two actions in one image as two rows if the figures stay that large; your call, but the recipe must record it). Write `Art/Characters/source/npc-serin-actions-prompt.json` and `npc-mira-actions-prompt.json` (list of entries: file, tool, agentModel, reasoning, references, prompt, attempts/notes). Use the character's `*-original.png` south figure (and its idle pixel frame) as reference images for identity; the style prompt should reuse the wording in `npc-serin-prompt.json` / `npc-mira-prompt.json`. Check every generated image yourself; regenerate a sheet if frames are inconsistent in size or look, if the pose does not read, or if hands/props are mangled (at most 2 retries per sheet; keep only the accepted original and note rejected attempts in the prompt json). Never overwrite existing source files.
6. Pixelization must be a deterministic recipe, not hand edits: add a recipe file `Art/Characters/actions.json` (per character and action: source file, crop box per frame, fps, any fixed per-frame x/y offsets) and extend `Art/Characters/build_characters.py` to read it. Scale rule: use the SAME source-to-pixel scale as the character's idle south figure, corrected by the ratio between the head-top-to-boot-bottom height of the idle source figure and that of the action sheet's figures (use one scale per action sheet, not per frame, so frames do not jitter). Align horizontally by the boots, vertically by the feet row. Recommended and allowed: copy the idle south frame's pixels from the hem down (legs and boots) unchanged into each action frame so the feet never differ from idle; if you do, keep the seam invisible. Rerunning `.venv/Scripts/python.exe Art/Characters/build_characters.py --verify-rebuild` must give byte-identical outputs twice and leave the existing 60 frames pixel-identical. No randomness, no network, no image generation at rebuild time.
7. The game reads `Assets/StreamingAssets/UiArt/Characters/<id>/` (a mirror of `Assets/MagicCampus/Art/Characters/<id>/`, currently byte-identical copies of the png and manifest files, plus Unity `.meta` files). Make `build_characters.py` also mirror the built png and manifest.json files of all three characters into that folder (overwrite same-named files, never touch or delete `.meta` files, never delete anything).
8. Review images in `Art/Characters/review/`: (a) `npc-actions-sheet.png`: per character, a row for idle and one row per action, 4 frames at 4x nearest-neighbour on the neutral grey (98,100,104) backdrop with labels; (b) `npc-actions-source-vs-pixel.png`: for each character and action, the 4 original crops above the 4 pixel frames; (c) `npc-actions-1x.png`: all frames at 1x and 2x for game-size judgement, idle included for comparison; (d) a `.gif` and `.apng` per character and action loop in the same style as the existing `npc-*-walk-*.gif/apng` (e.g. `npc-serin-read-south.gif`). Update `review/verification.json` so it also lists the new files and checks (frame counts, sizes, alpha, rectangles inside the sheet, palette counts, `identical_rebuild`).

## Allowed writes
`Art/Characters/` (new source files, new `actions.json`, edits to `build_characters.py`, `review/`), `Assets/MagicCampus/Art/Characters/{npc-serin,npc-mira,player}/` and `Assets/StreamingAssets/UiArt/Characters/{npc-serin,npc-mira,player}/` only through the build script, plus `Art/_runs/`. Do not change existing files in `Art/Characters/source/`, anything else in the repo, the pipeline folder, any `.cs`, any JSON under `Content/`, scenes, or `.meta` files. No git commands.

## Before you finish
Run `--verify-rebuild`. Compare every existing frame's pixels (sheet and individual files, all clips of all three characters) with the versions from before your change (take a copy of the character folders into `Art/_runs/20261111-1622-npc-actions/baseline/` before editing, compare with a script, and report the result). Run `.claude/skills/make-art/scripts/check_png.py --kind any` on all new PNGs. Open and look at the review images at 1x and enlarged; fix obvious problems once. In the final message list files written, the checks and their results, and remaining problems; claim only what you checked.


Run folder for notes and decomposition files: Art/_runs/20261111-1622-npc-actions
