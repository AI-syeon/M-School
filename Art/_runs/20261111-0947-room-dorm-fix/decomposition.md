# Dorm fix

Reproducibility was repaired before visual integration. The initial permanent wall preparation reproduced all four old PNGs byte-for-byte. Wall sources now resolve within prepared/room-dorm. The final wall recipe uses the new generated timber/plaster strip and timber posts. rebuild-rooms.ps1 prepares all room scripts and builds every room map, with optional Pipeline, Art and Map parameters. It is ASCII and was run under Windows PowerShell 5.1.

The new original atlas and exact prompt are preserved in source/room-dorm-fix-decor-*. Existing source images were retained. New whole-frame pixelization uses the installed pipeline Python and build_grid.pixelize before cell splitting. The approved desk, bookshelf, window seat, wardrobe and mirror source crops remain. The round star rug moved one cell left to leave a narrow green runner from the door. The new bed is anchored at (0,5), visually occupies only x=0..1, and blocks local (1,0),(1,1). Sleeping cell (2,5) is visually clear.

Rear wall is 12x2 at (0,7), blocking its upper row only. Timber side walls and the original front wall bands preserve the complete boundary. Navy curtains, moon banner, ivy, star garland, cat, brass bedside lamp, trunk, books, stool, candle group and additional plant enrich the room. Lamps/candles/cat/rugs/wall hangings are decorative placements atop appropriate surfaces. Solid trunk, stool, books and plants block their base footprints. Six glows specify separate lamp, candle and window illumination. No directional light shafts or cast-shadow layers were added.

The native 32x32 floor crop contains four plank courses and repeats without full-room stretching. Source/pixel and 3x3 floor QA are saved beside the crop metadata.

Final verification passed. Map 12x9, assembled 384x288, cells 32x32, alpha only 0/255, 14..63 colors per cell, 121 unique cell PNGs. Exactly 37 exterior wall cells, 62 total blocked cells and 46 reachable walkable cells. Door (5,0), sleep (2,5), desk (7,2) and wardrobe (9,4) are connected. Every prop frame was checked for visible coverage of the sleep cell.

Prepared output was moved into this log folder before a clean rebuild. Two final rebuilds plus review refresh and validation produced 35 byte-identical files across runtime, prepared and review outputs. reproducibility.json contains the complete hashes. The courtyard rebuild succeeded and layout hash remained A8B783E6DFDEAA2B0786F241645C917257A31BAA855AAA680D874129FE01CBCC.

Viewed assembled room at 1x and enlarged, grid, before/after and the reference. The fixed 12x9 footprint still has a smaller bed and window than the key art, and the lower-right area remains more open. Runtime Unity rendering and glow behavior were not run. The assembled PNG represents unlit art. Review refresh helper is in this run, while all production source/preparation dependencies are permanent.
