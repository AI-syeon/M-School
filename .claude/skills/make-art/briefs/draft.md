# Job: DRAFT variants (not registered in the recipe)

Subject: {{SUBJECT - e.g. "magic library bookshelf prop"}}
Footprint: {{CELLS - e.g. 2x3 cells = 64x96 px}}
Reference: {{KEY ART FILE AND AREA - e.g. design/screens/01-campus.png, left library entrance}}
Number of variants: {{N - usually 5 to 15}}
Must keep: {{FIXED ELEMENTS FROM PREVIOUS FEEDBACK, or "none"}}
Change only: {{WHAT TO VARY / WHAT WAS CRITICIZED, or "free"}}

## Output folder
`ArtSamples/{{NAME}}/` only. Do not touch the recipe, `Art/CampusGrid/` or `Assets/`.

## Do
1. Make {{N}} genuinely different directions (silhouette, material, ornament, color emphasis), not recolors of one design. Under the same conditions so they can be compared fairly.
2. For each variant `NN` (01, 02, ...): `original-NN.png` (generated source, unchanged) and `pixel-NN.png` (final RGBA at exactly the footprint size, transparent background).
   A small helper script for pixelizing is allowed here; save it in the output folder. Use the same method for every variant (nearest-neighbor only, palette limit stated in notes).
3. `sheet.png`: all pixel variants side by side at 4x nearest-neighbor on a mid-grey background, each above its number, plus one row showing each at 1x placed on a 3x3 patch of an existing ground tile (`Assets/MagicCampus/CampusGrid/Textures/ground-paving-00.png`).
4. `notes.md`: the generation prompt(s), one line per variant describing its direction, the pixelization method, and color counts.
