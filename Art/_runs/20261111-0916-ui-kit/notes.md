# Production UI kit

Built-in image generation produced the original concept using the bedroom, journal and courtyard references. The unchanged original and exact prompt metadata are saved in `Art/UI/source/`. The source swatches at `(120 + 110*i, 822)` supply all fourteen RGB values used in production.

The custom deterministic builder uses Pillow through `C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe`. This UI-specific build replaces the tile recipe workflow for this job. The pipeline itself was read and its Python environment reused.

## Rebuild

Run from the MagicCampus root.

```powershell
& C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe Art/UI/build_ui.py
```

## Production choices

- Panel slice borders are 16 pixels on all sides. Corner ornaments remain within these regions.
- Button slice borders are 8 pixels on all sides. Content padding is left/right 8 and top/bottom 6.
- Namebox slice borders are left/right 8 and top/bottom 6. Content padding is left/right 8 and top/bottom 4, leaving 12 pixels for runtime text.
- Slot and quickslot dimensions stay fixed at 40×40. Inventory icon rectangles are `[4,4,32,32]`.
- Quickslot digit rectangles are `[4,4,9,9]` in top-left coordinates. The empty digit area has a thin bottom/right separator.
- Bodies use flat navy at alpha 228. Transparent outside corners use alpha 0 and frame pixels use alpha 255.
- The shared palette has 14 RGB colors and 15 unique RGBA entries including transparency. Selected frames use brighter gold and pale corner highlights. Disabled buttons use muted navy, slate and bronze.
- No characters, icons, numbers or text are painted in final sprites. Review-only white rectangles represent text placement.

## Verification

`checks.json` records all nine dimensions, RGBA mode, alpha values, color counts and SHA-256 hashes. Build output and manifest were regenerated twice and matched byte for byte. Slice checks verify valid center dimensions, constant edge strips and preserved corners after stretching. The source SHA-256 matches the generated original file.

Opened the 2× courtyard mockup, the grey native/4× contact sheet and the native panel. All nine elements remain legible with crisp borders, flat centers and distinct states. The panel is stretched to 300×120, buttons to 96×24, and namebox to 80×20. The review contains six inventory slots and nine quickslots with one selected in each group.

No visual correction was needed after inspection. No remaining asset issues were observed. Unity importing and runtime integration are outside this art-only deliverable.
