# Dormitory decomposition

12 by 9 cells, 32 pixels per cell. Coordinates start at bottom left. Rows below run from y=8 down to y=0. Camera covers all 12 by 9 cells.

```
############
#..........#
#B...SSSS..#
#BZKK....WW#
#N....TTTA.#
#.....TTT..#
#.........M#
#P........P#
#####D######
```

The grid marks collision cells and gameplay targets. `#` is a blocked wall, `B` the blocked bed, `Z` the walkable sleep position, `K` the blocked bookshelf, `S` the blocked window seat, `W` the blocked wardrobe, `T` the blocked desk/chair, `N` the blocked nightstand, `M` the blocked mirror, `P` blocked plant pots, `D` the walkable doorway and `A` the walkable wardrobe approach. Dots are walkable floor. The desk approach is (7,2).

| Furniture | Bottom-left anchor | Visual frame | Collision cells, absolute |
| --- | --- | --- | --- |
| Canopy bed, head up | (1,5) | 1 by 3 | (1,5), (1,6) |
| Bookshelf | (3,5) | 2 by 3 | (3,5), (4,5) |
| Desk, chair, armillary, books | (6,3) | 3 by 3 | (6,3), (7,3), (8,3), (6,4), (7,4), (8,4) |
| Big window and window seat | (5,6) | 4 by 3 | (5,6), (6,6), (7,6), (8,6) |
| Wardrobe | (9,5) | 2 by 3 | (9,5), (10,5) |
| Round star rug | (3,1) | 3 by 3 | none |
| Nightstand | (1,4) | 1 by 1 | (1,4) |
| Mirror | (10,2) | 1 by 2 | (10,2) |
| Plants, two placements | (1,1), (10,1) | each 1 by 2 | (1,1), (10,1) |
| Lanterns/candles | (3,7), (9,7) | each 1 by 1 | none, wall-mounted decorations |
| Nightstand candle | (1,4) | 1 by 1 | none, layered on nightstand |
| Exterior walls | perimeter | top and front bands, side posts | all outer cells except (5,0) |

The bed visual frame occupies only x=1 so it cannot cover the sleep cell (2,5). Its canopy and headboard may extend into (1,7). Window and tall furniture overlap wall visuals. Furniture frames are pixelized whole before cell splitting. Separate glows describe window and candle illumination. The compact room reduces the key art's spacious floor, bed width and decorative clutter to preserve required movement cells.

Original generation and pixelization are assigned to gpt-6.1-sol with medium reasoning. The installed pipeline environment is used read-only. Existing courtyard recipes and builder remain untouched.
