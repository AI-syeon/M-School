# Banner draft directions

- 01 — Straight shield-hem academy standard, side-mounted iron pole and stepped octagonal iron base.
- 02 — Asymmetric wind-swept swallowtail, curved iron bracket and square warm-stone plinth.
- 03 — Center-mounted scalloped ceremonial banner, three tassels, arched crossbar and round stone drum base.

## Method

Built-in image_gen generated all source artwork first, using design/screens/01-campus.png as the style reference. The approved daylight-times.png and courtyard-unity.png review images were inspected. Each original-NN.png is an unchanged copy of a generated PNG.

All variants use the same conversion. Threshold source alpha at 128, crop to the resulting visible bounds, proportionally fit inside 30×94 pixels using NEAREST, center horizontally with a shared bottom baseline at y=95, and retain the 32×96 RGBA canvas. Train one shared 24-color RGB median-cut palette on all visible reduced source pixels. Map each frame to that palette with dithering disabled, keep binary alpha, and clear RGB under transparent pixels. Every resizing operation uses nearest-neighbor. The palette limit is 24 opaque RGB colors plus one transparent RGBA value.

The helper uses the local 2D-pixel-asset-pipeline environment and its check_image, grid_atlas, and save_json APIs. Resizing and quantization are custom draft-helper operations. The production campus converter uses BOX filtering, so the draft helper supplies the required NEAREST filtering. There are no hand-drawn replacement shapes or symbols.

## Validation

| Variant | Canvas | Opaque RGB colors | RGBA colors with transparency | Alpha |
| --- | --- | --- | --- | --- |
| 01 | 32×96 | 22 | 23 | 0, 255 |
| 02 | 32×96 | 24 | 25 | 0, 255 |
| 03 | 32×96 | 23 | 24 | 0, 255 |

PNG bytes match a second complete in-memory conversion. Two separate full helper invocations also produced identical pixel PNGs, sheet, comparison image, and checks JSON bytes. Each final original hashes identically to its generated-tool output. Pipeline atlas packing and extracted frame bytes match exactly. Full hashes and visible bounds are in checks.json. The sheet was inspected at native 1× on existing 3×3 paving patches and at 4× on mid-grey. source-vs-pixel.png retains the original-to-pixel comparison.

One obvious issue was corrected once. Variant 03 initially lost its diagonal star rays during reduction. A targeted built-in image edit enlarged and thickened the eight-point emblem. The unchanged first generation is preserved in Art/_runs/20261111-0024-banner-draft/original-03-first-generation.png. The corrected source and final pixels were inspected again. At native size, tiny finial facets, cloth folds, and ring details necessarily simplify. Variant 02 has the smallest emblem.

This is a draft-only output. It is unregistered in the production recipe. Production rebuild was not run. No Assets, recipe, C#, scene, pipeline, or design files were edited.

## Reproduce the draft

Run from the MagicCampus project root with the existing source PNGs and local environment.

```powershell
& C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe ArtSamples/banner-draft/pixelize.py
```

## Generation prompts

prompts.json records the file, tool, reference images, exact final prompt, and first-generation prompt for the corrected variant. All generations used built-in image_gen.imagegen with transparent_background enabled.

### 01

Use case: stylized-concept. Create ONE isolated game prop for MagicCampus, a cozy fantasy academy, using the attached campus key art ONLY as visual style and banner motif reference. A freestanding courtyard banner, tall narrow silhouette, dark iron pole, deep navy cloth bearing ONE large simple GOLD EIGHT-POINT STAR (four cardinal rays and four diagonal rays), small gold finial, small base. Orthographic elevated front-facing three-quarter view about 30 degrees, verticals perfectly vertical, visible top/front faces. Refined hard-edged clustered illustrated source artwork, restrained details designed to remain legible when reduced to 32x96 pixels. Upper-left neutral material highlights. Transparent background, absolutely no cast shadow, no glow, no scenery, no text, no checkerboard. Full object uncropped, nearly filling a portrait 1:3 bounding shape with transparent gutters. Navy cloth and muted gold matching reference banners, dark cool iron and warm stone. Avoid extra stars, lamps, foliage, ground, embellishment clutter. DIRECTION 01: Classical academy standard. Single pole just left of cloth, short straight horizontal crossbar with tiny gold end caps. Broad navy hanging cloth, simple shield-shaped pointed lower hem, thin muted gold border, prominent eight-point gold compass star centered. Tapered dark iron pole rising from a low stepped octagonal dark iron base. Small round gold finial. Slight cloth fold at left edge. Silhouette mostly straight and dignified.

### 02

Use case: stylized-concept. Create ONE isolated game prop for MagicCampus, a cozy fantasy academy, using the attached campus key art ONLY as visual style and banner motif reference. A freestanding courtyard banner, tall narrow silhouette, dark iron pole, deep navy cloth bearing ONE large simple GOLD EIGHT-POINT STAR (four cardinal rays and four diagonal rays), small gold finial, small base. Orthographic elevated front-facing three-quarter view about 30 degrees, verticals perfectly vertical, visible top/front faces. Refined hard-edged clustered illustrated source artwork, restrained details designed to remain legible when reduced to 32x96 pixels. Upper-left neutral material highlights. Transparent background, absolutely no cast shadow, no glow, no scenery, no text, no checkerboard. Full object uncropped, nearly filling a portrait 1:3 bounding shape with transparent gutters. Navy cloth and muted gold matching reference banners, dark cool iron and warm stone. Avoid extra stars, lamps, foliage, ground, embellishment clutter. DIRECTION 02: Wind-shaped forked pennon. Dark iron pole stands at left, crooked gooseneck bracket and simple angled top bar supporting a navy cloth visibly swept to the RIGHT in a gentle breeze, with a deep swallowtail split into two clearly separate long points. Gold border along lower tips, ONE large gold eight-point compass star. Small spear-shaped muted gold finial. Compact square warm taupe stone plinth with dark iron socket. Asymmetric silhouette, clean and readable, not a shield-shaped hanging standard. Keep cloth entirely visible within narrow portrait bounding box.

### 03

Initial generation

Use case: stylized-concept. Create ONE isolated game prop for MagicCampus, a cozy fantasy academy, using the attached campus key art ONLY as visual style and banner motif reference. A freestanding courtyard banner, tall narrow silhouette, dark iron pole, deep navy cloth bearing ONE large simple GOLD EIGHT-POINT STAR (four cardinal rays and four diagonal rays), small gold finial, small base. Orthographic elevated front-facing three-quarter view about 30 degrees, verticals perfectly vertical, visible top/front faces. Refined hard-edged clustered illustrated source artwork, restrained details designed to remain legible when reduced to 32x96 pixels. Upper-left neutral material highlights. Transparent background, absolutely no cast shadow, no glow, no scenery, no text, no checkerboard. Full object uncropped, nearly filling a portrait 1:3 bounding shape with transparent gutters. Navy cloth and muted gold matching reference banners, dark cool iron and warm stone. Avoid extra stars, lamps, foliage, ground, embellishment clutter. DIRECTION 03: Compact centered ceremonial gonfalon. Dark iron upright pole is CENTERED behind the navy cloth, a curved arch-like crossbar supports the cloth from two visible small gold suspension rings. Broad short navy cloth with a rounded scalloped lower hem and three tiny muted-gold tassels. ONE large simple eight-point gold star centered. Cloth has gold upper corner accents with otherwise mostly plain navy area. Small acorn-shaped gold finial above arch. Pole extends visibly below cloth into a heavy two-tier ROUND warm stone drum base with visible elliptical top and dark iron collar. Rounded silhouette and centered support distinguish it from side-mounted pointed and swallowtail banners. No extra poles.

Final targeted edit

Use case: precise-object-edit. Edit the attached single isolated standing navy academy banner source. Preserve its silhouette, dark iron centered pole, small gold acorn finial, curved crossbar, rings, scalloped hem with 3 tassels, stone drum base, orthographic elevated front 3/4 camera, material light and genuine transparent background. Change ONLY the gold star emblem. Make it a noticeably larger, chunky EIGHT POINT compass star whose four DIAGONAL RAYS are nearly as long and thick as its four cardinal rays, a broad eight-ray gold silhouette that remains unmistakably eight-pointed at 32x96 total prop size. All eight points should be clearly separated by deep notches. Gold emblem occupies most of the navy cloth width. No additional symbols, no cast shadow, no background.
