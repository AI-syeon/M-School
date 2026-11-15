# Job: PRODUCTION assets (registered in the CampusGrid recipe)

Reference: {{KEY ART FILE AND AREA, and/or chosen draft e.g. ArtSamples/<name>/pixel-03.png}}
Must keep: {{FIXED ELEMENTS, or "none"}}

## Assets
| id | kind (ground / prop / animated prop) | cells [w,h] | blocked cells (bottom-left = [0,0]) | frames / fps | notes |
|---|---|---|---|---|---|
{{ROWS}}

Library file: `Art/CampusGrid/{{LIBRARY}}.json` ({{"existing - append" or "new - add it to art.json libraries"}}). Ground tiles go in `art.json` `tiles`.

## Allowed writes
`Art/CampusGrid/source/` (new files only), `Art/CampusGrid/prepared/`, `Art/CampusGrid/{{LIBRARY}}.json`, `Art/CampusGrid/art.json` (only to add the library or tiles), new `Art/CampusGrid/build_<id>.py` for animation frame preparation, `Art/CampusGrid/review/`, and whatever `rebuild.ps1` writes under `Assets/MagicCampus/CampusGrid/`. Do NOT change `map.json` unless this brief says to place assets.

## Do
1. Generate originals into `Art/CampusGrid/source/<name>-original.png` with `<name>-prompt.json`. A sheet of several isolated items is fine; record crop rectangles in the recipe.
2. Animated props: prepare equal-size frames under `prepared/<id>/` with a deterministic `build_<id>.py` (seeded, no randomness without a fixed seed) and make `rebuild.ps1` call it the same way it calls `build_fountain.py`, only if needed.
3. Add the recipe entries (`id`, `size`, `frames` with `source`/`crop`/`fit`/`colors`, `fps`, `blocked`).
4. Run `.\Art\CampusGrid\rebuild.ps1`. It must finish without errors. Run it a second time and confirm `Assets/MagicCampus/CampusGrid/layout.json` and `Art/CampusGrid/review/build-report.json` are byte-identical between runs.
5. Make `Art/CampusGrid/review/{{NAME}}-sheet.png`: each new asset as original vs pixel result, plus each at 1x and 3x standing on existing paving next to the existing bench prop for scale. For animated props also write `review/<id>.apng`.
6. Report the new ids, their cell counts, collision cells, frame counts and the verification results.
