# Lectern spellbook sample 01

Exactly one sample prop. Existing game assets were used only as visual references.

Prompt: One centered wooden lectern bearing an open softly glowing spellbook; cozy Stardew-like top-down three-quarter game prop, chunky pixel-friendly shapes, warm honey/caramel wood, deep brown outline, cream pages, muted teal cover and a little mint glow; upper-left lighting, transparent background, no text, no extra accessories, at most a tiny contact shadow; readable at 32×32.

Original generation used the built-in image generation tool with transparent background. `original.png` is the unchanged 1312×1199 generated PNG. SHA-256: `25c570d4b9adb98b3284d54a0693be6aa2b1de687ffc14e09cbf729a47a863b0`.

Process: local pipeline `.venv/Scripts/python.exe` ran `build_sample.py`; imported the existing `scripts/build_godot_style_studies_static_32x64.py` helper's `quantize_shared` without modifying that repository. Alpha crop → 27×29 NEAREST sample → placement in 32×32 RGBA → binary alpha → 15-color MEDIANCUT quantization with dithering disabled. Every resize uses NEAREST.

Visual QA: viewed the original, 8× preview and comparison. Initial reduction interrupted the book fold and colored the tiny contact shadow green. One cleanup pass connected the fold using existing ochre pixels, removed the tiny contact-shadow pixels, and normalized the exterior silhouette to a dark 1px boundary. Viewed the corrected 8× preview: readable open book, sloping wooden board, central pedestal and foot; no obvious stray pixels or clipped silhouette. Fine source texture and much of the subtle glow are necessarily lost at 32×32.

Verified: final 32×32 PNG, RGBA, 15 opaque RGB colors plus one transparent RGBA value (16 total); alpha values only 0/255; all four corners alpha 0. Preview is 256×256 on RGB mid-grey (128,128,128); `compare.png` is 512×256 with source left and final 8× preview right. Detailed machine checks are in `verification.json`; helper is reproducible with the local pipeline environment.
