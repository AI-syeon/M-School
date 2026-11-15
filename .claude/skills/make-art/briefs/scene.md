# Job: SCENE from key art (tilemap composition)

Key art: {{design/screens/NN-name.png}}
Scene scope: {{e.g. "the courtyard plaza: library left, stairs center, greenhouse right"}}
Map size: {{W x H cells}}, camera {{w x h cells}}
Existing assets to reuse: {{ids from art.json and libraries, or "check and reuse what fits"}}
Feedback to address: {{SPECIFIC USER COMPLAINTS, or "none"}}

## Allowed writes
Same as a production asset job (`Art/CampusGrid/source/` new files, `prepared/`, library JSONs, `art.json` additions, `review/`, rebuild outputs under `Assets/MagicCampus/CampusGrid/`), plus `Art/CampusGrid/map.json`. If this scene should not replace the current courtyard, write a new map file `Art/CampusGrid/{{NAME}}-map.json` and build it with `build_grid.py --map ... --unity-out <separate folder> --review-out <separate folder>` instead of touching the courtyard.

## Do
1. Decompose the key art first and write the decomposition to `Art/_runs/` as `decomposition.md`: repeating ground, ground with continuous pattern (groundImage), architecture, props with footprints, animated parts, people/decoration, light sources (glows), canopy shadow casters. Note what is hidden behind props and how it will be filled.
2. The goal is that the whole scene reads like the key art at a glance: same composition and walking paths, same architecture framing the space, same depth layering front-to-back, same density of garden detail. Matching only one prop is a failure.
3. Reuse existing assets where they match; create missing ones following the production asset rules.
4. Keep lighting out of the tiles; set `daylight`, `shadows`, `glows` in the map instead.
5. Run `rebuild.ps1` twice (byte-identical check). Produce `review/{{NAME}}-vs-keyart.png`: key art and the assembled scene preview side by side at the same height.
6. In the final message, list what still differs from the key art and why.
