# MagicCampus inventory items

Nine standalone 32x32 RGBA inventory icons, centered with transparent margins,
point filtering and 32 pixels per unit. Their longest occupied extent is 24-28
pixels. They are inventory sprites, not Tilemap cells.

Original: `source/items-original.png`, unchanged from the built-in imagegen
output. `source/items-prompt.json` records the tool, complete prompt, references,
and source crop rectangle for every icon. Keep the original for comparison.

Rebuild from the MagicCampus project directory with the existing local pipeline
environment (no package installation or pipeline changes are required):

```powershell
& ../2D-pixel-asset-pipeline/.venv/Scripts/python.exe Art/Items/build_items.py --verify
```

The builder uses `pixel_pipeline.cli` palette, validation, and JSON helpers with
item-specific crop/BOX downsampling, binary alpha, a fixed shared 32-color
palette, detached sampling-dust cleanup, and a one-pixel warm dark contour.
The only reviewed hand-pixel adjustment simplifies the moonbeans to silver-blue
and makes their three tiny crescent symbols explicit. It is encoded in the
builder and reproduced on every run.

Outputs are `Assets/MagicCampus/Art/Items/item-*.png` and the object-keyed
`manifest.json`. Existing UI and paving PNGs are read only as review backgrounds.
Review files:

- `review/items-preview.png`: each icon at 1x and 4x, existing normal slot at 1x
  and 3x, existing courtyard paving at 1x and 3x.
- `review/items-1x.png`: compact native-resolution row for silhouette inspection.
- `review/source-vs-pixel.png`: original crop beside nearest-neighbor 4x result.

`Art/_runs/20261111-0922-items/validation.json` records dimensions, occupied
bounds, hard-alpha/transparent-edge checks, source and output hashes, and palette
counts. `rebuild-check.json` records the byte-identical second build. All nine
icons and review sheets were opened at native and enlarged sizes; the moonbean
cleanup was visually checked again. At 32px, small rune/star marks are necessarily
abstract pixel symbols; the note and card have no readable text.
