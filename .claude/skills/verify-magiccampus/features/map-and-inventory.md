# 지도와 가방

M을 누르면 지금 서 있는 구역의 전체 지도가 열린다. 칸 색은 그 칸에 실제로 그려진 그림의 평균 색이고(막힌 칸은 어둡게), 한 칸은 UI 픽셀 9개(960×640에서 18px)를 넘지 않는다. 플레이어와 친구(NPC)는 색 네모가 아니라 캐릭터 얼굴로 나오고, 플레이어는 맥동하는 금색 고리·바라보는 쪽 눈금·"나" 이름표가 붙는다. 문과 그 문이 이어지는 구역, 잠자리, 마법 대상도 표시된다. 구역은 여덟 곳이다(중앙 안뜰, 학생 휴게실, 기숙사 방, 본관 복도, 도서관, 강의실, 학생 식당, 온실). 가 본 구역만 지도에 나오고, 구역 탭이 한 줄을 넘으면 줄을 바꾼다. 아직 안 가 본 구역으로 가는 문은 "???"로 보인다. I를 누르면 가방(24칸), 수첩에 보관하는 필수품, 기숙사 우편 보관함이 열린다. 가방이 가득 차면 넘친 물건은 우편 보관함으로 가고 상태줄에 한 번 알린다. 가방·필수품·우편함·가 본 구역은 하루를 마칠 때 저장된다.

## Sub-features

- 지도: 현재 구역으로 열기, 좌우 방향키로 가 본 다른 구역 보기, 열려 있는 동안 이동 멈춤, M/Esc/X로 닫기(Esc는 메뉴를 열지 않음).
- 지도 색과 얼굴: 색 표는 `world-art.json`의 `maps.<id>.minimap`(derive_world.py가 계산), 얼굴은 캐릭터 manifest의 `animations.south.idle` 첫 프레임에서 머리 띠를 잘라 만든다(다른 클립은 쓰지 않는다).
- 가 본 구역 기록: 포털을 지나 처음 들어간 구역이 `visitedMaps`에 추가된다.
- 가방: 방향키 커서, Enter로 들기/놓기(다른 물건은 자리 바꿈, 같은 물건은 99개까지 합침), Delete로 1개 버리기.
- 받기: 같은 물건 칸에 먼저 쌓고, 빈칸, 그다음 우편 보관함. 필수품은 칸을 쓰지 않고 수첩에 들어가며 버릴 수 없다.
- 우편 보관함 꺼내기: 잠자리 옆에서만, 가방에 들어가는 만큼만.
- 주문 조건 `itemCountAtLeast`는 가방 수량을 읽는다.

## How to get to it (user POV)

1. 게임을 시작한다. HUD의 `가방(I)` / `지도(M)` 버튼이나 I / M 키.
2. 물건이 들어 있는 상태로 보려면 `--initial start-items.json`으로 시작한다(기본 시작 상태는 가방이 비어 있다).

## Driving it with verify-magiccampus scripts

Preconditions: `Builds\Stage5-verify`가 최신 빌드. 두 케이스 모두 스크립트가 `--initial start-items.json`을 붙인다.

```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\run-probe.ps1 -Cases map,inventory -Build Stage5-verify -Run <run-id>
```

- `map`: `map.start-visited`, `map.m-opens-on-current-area`, `map.player-holds-still-while-open`, `map.m-closes`, `map.lounge-recorded`, `map.opens-on-lounge`, `map.arrow-switches-area`, `map.arrow-wraps`, `map.esc-closes-without-menu`. 이어서 새 구역 다섯 곳(본관 복도, 도서관, 강의실, 학생 식당, 온실)과 기숙사 방의 서 있는 자리로 `WalkTo`로 걸어가(문을 실제로 지난다) 구역마다 `map.visited-<구역>`(문 표시 개수 포함), `map.opens-on-<구역>`을 검사하고, `map.minimap-matches-size`, `map.minimap-colours-differ`, `map.faces-cut`, `map.tabs-listed`(탭 8개, 두 줄)를 확인한다. 스크린샷 `map-courtyard.png`, `map-lounge.png`, `map-courtyard-from-lounge.png`, `map-hall.png`, `map-library.png`, `map-classroom.png`, `map-cafeteria.png`, `map-greenhouse.png`, `map-dorm.png`, `map-eight-tabs.png`.
- `inventory`: `bag.start`, `bag.i-opens`, `bag.enter-picks-up`, `bag.enter-swaps`, `bag.add-merges`, `bag.overflow-to-mailbox`, `bag.overflow-notice`, `bag.key-item-to-notebook`, `bag.cursor-back-on-slot-1`, `bag.delete-discards-one`, `bag.mailbox-needs-bed`, `bag.mailbox-at-bed`, `bag.i-closes`, `bag.day-ends`, `bag.reload-keeps-bag`. 스크린샷 `bag.png`, `bag-holding.png`, `bag-full.png`, `bag-mailbox-taken.png`.

## Gotchas

- 물건을 얻는 콘텐츠(이벤트 보상 `item.add`)는 아직 기존 이벤트에 붙어 있지 않다. probe는 `G.Inventory.Add`를 직접 불러 받기를 흉내 내고, 그 호출은 `steps`에 남는다.
- probe는 포털 옆과 잠자리로 가는 길을 `World.Place`로 건너뛴다. 걸어가는 경로 자체는 다른 케이스와 `Tests/Core`가 검사한다.
- 물건 아이콘 그림이 아직 없다. 칸은 분류 색 위에 이름을 쓴다. 지도는 타일 그림이 아니라 평균 색 칸이다. 새 다섯 구역은 CampusGrid 그림이 없어 Stage5 타일(16px) 평균 색이다(그림이 생기면 `derive_world.py`를 다시 돌린다). 색이 그 장소답게 읽히는지, 얼굴을 알아볼 수 있는지는 사람이 PNG로 판단한다.
