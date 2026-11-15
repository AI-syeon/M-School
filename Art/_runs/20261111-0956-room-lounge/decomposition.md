# Student lounge

10 × 8 cells, bottom-left origin. Each character is one 32 × 32 cell.

```text
y7 ##########
y6 #..NNB...#
y5 #KK...FFP#
y4 #.....ff.#
y3 #s..T....#
y2 #...r.cA.#
y1 #SSS.rCCP#
y0 ####D#####
   0123456789
```

`#` perimeter wall, `D` courtyard doorway, `T` snack table, `S` sofa collision,
`K` bookshelf collision, `F` fireplace collision, `f` fireplace approach,
`N` noticeboard wall decoration, `B` star banner, `A` armchair collision,
`P` plant collision, `C` low chest collision, `s` side table, `c` decorative cushion,
`r` rug or runner. Decoration symbols preserve walkability unless listed below.

Furniture frames and bottom-left anchors

- Sofa 3 × 2 at (1,1), collision (1,1), (2,1), (3,1).
- Snack table 1 × 1 at (4,3), collision exactly (4,3).
- Fireplace 2 × 2 at (6,5), collision (6,5), (7,5), front (6,4).
- Noticeboard 2 × 2 at (3,6), decorative, front (3,5).
- Bookshelf 2 × 3 at (1,5), collision (1,5), (2,5).
- Armchair 1 × 2 at (7,2), collision (7,2).
- Rug 3 × 3 at (3,2), decorative.
- Nightstand 1 × 1 at (1,3), collision (1,3).
- Oak chest 2 × 1 at (6,1), collision (6,1), (7,1).
- Plants 1 × 2 at (8,1), (8,5), collision at their anchors.
- Lanterns, books, cushions, star banners, ivy and garlands add wall/floor density.

Perimeter collision is every outer cell except (4,0). Required open table
neighbors are (3,3), (5,3), (4,2), (4,4). Entrance path follows x=4 and
branches through the open central floor to the wall interactions.

Fire and lantern illumination are separate `glows`. No directional cast shadows.
