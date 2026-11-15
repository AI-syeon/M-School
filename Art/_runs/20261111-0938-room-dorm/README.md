# Dorm art rebuild

From the MagicCampus root in PowerShell:

```powershell
& 'C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe' 'Art/_runs/20261111-0938-room-dorm/build_art.py'
& 'C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe' 'Art/_runs/20261111-0938-room-dorm/check_room.py'
```

This preserves both generated source originals and regenerates recipes, prepared architectural bands, assets, and reviews through the local pipeline. The two complete builds include all final review images before SHA-256 comparison; `reproducibility.json` records both hash sets.

Architectural bands contain one transparent corner pixel to satisfy the pipeline's transparent prop contract. Bands are composited over opaque floor, so this introduces no transparent hole in the map. The prepared bands are regenerated from the unchanged base original.

The grid review is exactly 768×576: blocked cells have translucent red tint, the door green tint, and the sleep approach blue tint. Window, lantern, and candle glows are runtime metadata; these flat reviews contain no baked glow or directional shadows.
