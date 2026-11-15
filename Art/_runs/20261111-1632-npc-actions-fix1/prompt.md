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

# Job: FIX ROUND 1 for the NPC action animations (npc-serin, npc-mira)

Context: the previous run (`Art/_runs/20261111-1622-npc-actions/`, read its `prompt.md` and `last.txt`) delivered read/eat/write/chat south clips. I reviewed them at 6x and 1x. The machine checks pass and existing pixels are preserved, but the art quality is not acceptable yet. The generated ORIGINALS are good (props are clearly readable in them); the problem is the pixelization step.

## Problems found (look at `Assets/MagicCampus/Art/Characters/npc-serin/south_read_000.png` etc. enlarged, next to `south_idle_000.png`)
1. **Props are unreadable at game size.** The open book (read), the bread (eat), the notebook and pen (write) dissolve into the coat: dark brown on burgundy/dark green, 1-3 px blobs, no clear shape. `write` reads as "arms crossed", `eat` shows no visible bread, `read` shows a brown smear on the chest. A player must recognise book / bread / notebook at 1x and 2x.
2. **npc-mira head has cream/tan blotches in the hair** (read, write, chat frames especially frames 2-3): bright patches at the top and side of the hair that look like bald spots. Highlight colours were mapped to skin/cream palette entries. The idle head has none of this.
3. Mira's coat/skirt silhouette in the actions looks shorter and more boxy than in the idle (torso looks compressed, skirt hem looks detached). Serin's coat looks more speckled than the idle. Compare each action frame with the idle frame and keep the same body shape.
4. Motion is too subtle to see at 1x for `read` (page/head), `write` (pen) and the mouth in `chat`; `eat` bread motion cannot be seen at all.

## What to KEEP (do not change)
- The 8 accepted originals and their prompt JSONs (do not overwrite; if you must regenerate one, save it as `...-v2-original.png` with its own prompt entry and point `actions.json` at it).
- The file/manifest format, key order, fps (read 4, eat 5, write 6, chat 5), sheet layout (160x512, one action per row below the existing 4 rows), the StreamingAssets mirror, the review file names, `--verify-rebuild` determinism, the scale rule and feet alignment, and the existing 60 frames/sheet region pixel-identical. The `player` outputs stay byte-identical.
- Using the idle south legs/boots from the hem down is fine and stays.
- The `chat` hand gesture (arm lifting out and back) already reads; keep its poses, only improve the mouth/head readability.

## What to CHANGE
1. **Head from the idle.** Build every action frame's head (hair and face, everything above the chin/collar) from the idle south frame's pixels, then apply only small deterministic edits from the recipe: 0 or 1 px vertical bob of the whole head, eyes lowered or half closed for read/write, mouth open/closed for eat/chat (a 2-3 px dark mouth, visible at 1x), optional hair-strand shift of 1 px. No hair pixel may become a skin/cream colour (check programmatically: in the hair region of every action frame, no pixel of a skin-tone palette entry that is not in the same place in the idle). This removes the blotches and the head jump.
2. **Body and arms.** Keep the idle's coat/skirt shape. Take only the arms, hands and props from the generated frame (mask by region), and compose them over the idle body. Remove the idle's hanging-arm pixels where the arm is raised (fill with the sleeve/coat or hair colour of the surrounding area so no ghost arm remains). The torso length and skirt hem stay the same as idle.
3. **Readable props, built for 1x.** Do not rely on plain box-downscaling for props. Reduce the prop region with a method that keeps contrast (for example take the most saturated/brightest or the modal colour per cell, or redraw the prop silhouette from the downscaled mask), then map it to a small prop palette. Add up to 8 extra shared prop colours per character (they must be listed in `actions.json` or derived deterministically):
   - book: cream pages that are clearly lighter than the coat (a 2-3 px bright page wedge on each side of a darker spine), a cover colour that contrasts with the coat (muted blue-teal or leather brown lighter than the coat), 1 px dark outline; at least about 9x5 px in size, held in front of the chest with arms slightly away from the torso so the shape is separate from the body;
   - bread: golden crust with a lighter highlight pixel, about 4x3 px, visible beside the face/mouth when raised and at chest height when lowered; the bite gets smaller by frame 3;
   - notebook: cream page block about 6x5 px at chest/waist height with a darker cover edge, and a 1 px dark pen line that is clearly separate from it and moves across frames (at least 1 px per step);
   - every prop has a 1 px dark outline or a clearly darker edge between it and the coat.
4. **Visible motion at 1x.** read: the page wedge lifts 1-2 px on frame 2 and settles; the head bobs 1 px. eat: positions chest -> mouth -> mouth (bite, bread smaller) -> chest. write: the pen tip moves at least 2 px across the page over the 4 frames and the head dips 1 px. chat: the mouth is open on frames 1 and 3 and closed on 0 and 2; the gesture hand moves as now.
5. Keep palette totals sensible: the character's existing palette plus at most 8 extra prop colours. No dithering. No isolated stray pixels around props.

## Review images
Regenerate the review images (`npc-actions-sheet.png`, `npc-actions-source-vs-pixel.png`, `npc-actions-1x.png`, the loops). Add `Art/Characters/review/npc-actions-compare-idle.png`: per character, idle and the 4 frames of each action at 6x, one action per row (the same kind of image I used to review). Update `verification.json`.

## Before you finish
`--verify-rebuild` twice identical. Re-run a hash comparison of all existing 120 frames and sheet regions against `Art/_runs/20261111-1632-npc-actions-fix1/baseline/` copies (the earlier run's baseline folder `Art/_runs/20261111-1622-npc-actions/baseline/` is still valid; reuse it). `check_png.py --kind any` on all new PNGs. Open the enlarged and 1x images yourself and judge props and heads honestly against the problem list above; fix once more if they are still unreadable. In the final message list what changed per problem number and what is still weak.

## Allowed writes
Same as the previous run: `Art/Characters/` (new source files only with new names, `actions.json`, `build_characters.py`, `review/`), the `Assets/MagicCampus/Art/Characters/{npc-serin,npc-mira,player}/` and `Assets/StreamingAssets/UiArt/Characters/{npc-serin,npc-mira,player}/` folders only through the build script, and `Art/_runs/`. Nothing else. No git.


Run folder for notes and decomposition files: Art/_runs/20261111-1632-npc-actions-fix1
