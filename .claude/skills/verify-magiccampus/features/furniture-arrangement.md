# 가구 배치

기숙사 방(`map.dorm`)에서 F를 누르면 가구 배치 모드가 열린다. 시간은 멈추고 왼쪽에 이 방의 가구 12개 목록(책장, 책상, 옷장, 협탁, 거울, 상자, 별 의자, 책 더미, 화분 1·2, 꽃 화분, 러그)이 나온다. 고른 가구는 방 안에서 금색 테두리로 표시되고, Enter로 집으면 방향키로 한 칸씩 옮기는 반투명 유령이 되어 놓을 수 있는 자리는 초록, 놓을 수 없는 자리는 빨강으로 보이며 이유가 말풍선으로 나온다. Enter로 놓고, C로 보관하고, 보관한 가구는 목록에서 고른 뒤 Enter로 꺼낸다. 침대·벽·창문·벽 장식은 고정이다. 놓은 배치는 플레이어와 NPC 길찾기의 충돌에 바로 반영되고, 지도 화면(M)의 칸 색도 따라가며, 하루를 마칠 때 저장되어 다시 불러와도 남는다.

## Sub-features

- 열기와 닫기: 옮길 수 있는 방에서만 F(오른쪽 아래에 `F 가구 배치` 안내). 다른 방에서 F는 "이 방에서는 가구를 옮길 수 없어요."만 알린다. F·Esc·X로 닫고, 가구를 든 채 Esc는 든 것만 내려놓는다(제자리).
- 고르기와 집기: 위·아래(또는 좌·우, W·S·A·D)로 목록, Enter(또는 Space, E)로 집기. 선택한 가구는 방 안에서 이름표와 금색 테두리로 보인다.
- 옮기기: 방향키·WASD로 한 칸씩(누르고 있으면 반복). 유령은 방 밖으로 나가지 않는다. 가구를 든 동안 원래 자리 그림은 숨는다.
- 놓기 규칙(`Furniture.Rule`, Core): 놓는 칸이 구역 안, 고정 벽(창가 의자 포함)·문·침대 자리·다른 가구·서 있는 사람과 겹치지 않고, 문과 침대 자리와 서 있는 사람이 여전히 서로 걸어서 닿을 것. 러그처럼 막지 않는 가구는 구역 안이면 어디든(다른 가구 밑도) 된다. 거절 이유: 방 밖·구역 밖·벽/붙박이·비워 둘 자리·다른 가구·누가 서 있음·길이 막힘.
- 보관: C(또는 Delete). 보관한 가구는 칸을 막지 않는다. 목록에서 고르면 Enter가 "꺼내기"가 되고 플레이어 앞에서 가장 가까운 놓을 수 있는 자리에서 유령이 시작한다.
- 이벤트 중에는 옮길 수 없다(`Busy`).
- 충돌: 옮길 수 있는 가구가 막는 칸은 `MapDef.Furnished`에 있고 `World.Passable`이 한 곳에서 읽는다. 걷는 NPC의 길이 새 가구에 막히면 같은 목적지로 다시 길을 찾는다.
- 바닥 영역: 가구는 그림 칸(`bounds`)과 바닥 칸(`cells`)이 따로다. 바닥 칸만 막고 나머지 그림 칸은 걸어 들어갈 수 있으며, 그 칸에 선 사람은 가구에 가려진다(가구의 깊이는 맨 아래 줄 기준). 바닥 칸은 CampusGrid 아트 명세의 소품별 `blocked` 목록에서 온다(예: 책장 2×3 중 아래 2×1). 배치 모드의 채움과 테두리는 바닥 칸의 바깥 변만 따라간다. 막는 칸이 없는 러그는 그림 전체가 바닥이다.
- 저장: `arrangements.<mapId>` = `placed`(piece, x, y)와 `stored`. 하루를 마칠 때(잠자기·새벽 2시 기절) 파일에 쓰고, 저장이 없으면 레이아웃 그대로다. 읽을 때 모르는 가구는 버리고, 겹치거나 어긋난 자리는 기본 자리로(그 자리도 막히면 보관으로) 되돌린다. F9로 불러오면 저장 이후의 배치는 사라지고 화면도 저장된 배치로 다시 그려진다.
- 층과 올려 두기(`Layer`, `Furniture.Stand`): 놓인 가구의 층은 저장하지 않고 놓인 자리에서 정한다. 바닥 칸이 없는 가구(러그)는 Floor, 맨 아래다. 올려 둘 수 있는(`stackable`) 작은 가구(화분 1·2, 꽃 화분, 책 더미)가 바닥 칸 전체를 어느 한 가구의 윗면(`surface`) 칸에 두면 Top, 나머지는 Body다. 윗면이 있는 가구(책상은 뒤쪽 줄 3칸, 상자는 오른쪽 뚜껑 1칸)가 호스트다. 겹침은 층마다 따로 본다: 러그끼리, 가구끼리, 윗면 한 칸에 둘은 안 되지만 러그 위에 가구, 가구 위에 화분은 된다. 윗면이 아닌 칸(책상 앞쪽 줄, 상자 촛대 쪽)에 화분을 놓으면 Body로 취급되어 `다른 가구와 겹쳐요`로 거절된다. 이미 올라가 있는 칸은 `그 자리엔 이미 뭔가 올라가 있어요`(`SurfaceTaken`). Top은 호스트가 이미 막은 칸이라 막는 칸을 따로 만들지 않고 벽·문·사람·길막힘 검사를 건너뛴다.
- 호스트의 짐: 위에 뭔가 올라간 가구는 집을 수도 보관할 수도 없다(`Loaded`, `위에 올려 둔 물건부터 치워 주세요`). 올라간 것을 먼저 치우면 된다.
- 그리기: Top은 호스트의 그림 순서 바로 앞(호스트 맨 아래 줄 -0.01)이고 사람과는 호스트의 발 기준으로 깊이를 가른다. 그림은 호스트의 `lift` 픽셀(32픽셀=1칸)만큼 올라가 윗면 위에 서 있는 것처럼 보인다. 유령도 윗면에 놓을 수 있는 자리에서는 같은 높이로 올라간다. 지도 칸 색은 러그, 가구, 올려 둔 것 순서로 칠한다.
- 저장·불러오기: 저장 형식은 그대로(`placed`는 앵커 칸만). 읽을 때 올려 둘 수 있는 가구는 호스트 뒤에 놓으므로 저장 파일의 순서와 상관없이 책상 위 화분이 책상 위에 다시 선다.
- 지도: 가구 칸 색은 빈 방 지도 위에 지금 놓인 자리로 칠해진다(`world-art.json`의 `maps.<id>.pieces`).

## How to get to it (user POV)

1. 게임을 시작하고 기숙사 방으로 간다(안뜰 서쪽 문, 또는 잠자고 일어난 침대 옆).
2. F를 누른다. 왼쪽 목록에서 위·아래로 가구를 고르고 Enter로 집어 방향키로 옮긴 뒤 Enter로 놓는다. C로 보관한다. F로 끝낸다.
3. 침대에서 E로 자면 배치가 저장된다. F9로 다시 불러와도 같은 배치다.

## Driving it with verify-magiccampus scripts

Preconditions: `Builds\Stage5-verify`가 최신 빌드. 시작 상태는 기본(`start-offered.json`, 안뜰). probe가 안뜰에서 기숙사까지 `WalkTo`로 걸어간다(문을 실제로 지난다).

```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\run-probe.ps1 -Cases furniture -Build Stage5-verify -Run <run-id>
```

- `furniture`: `furniture.in-dorm`, `default-collision`, `f-opens-arrange`, `outline-is-floor`(선택 테두리가 책장의 바닥 2×1만 감쌈), `time-paused`(열린 동안 시계 정지), `enter-lifts`, `bed-cell-refused`(침대 자리로 가면 거절 사유 `Keep`), `invalid-enter-refused`(Enter가 거절되고 상태 불변), `esc-puts-it-back`, `valid-ghost`, `ghost-outline-is-floor`, `placed`, `cells-moved`(옛 칸 열림·새 칸 막힘), `cursor-on-plant`, `c-stores`, `rug-stored`, `enter-takes-out`, `rug-out`, `f-closes`, `walks-into-new-spot`·`walks-through-old-spot`(진짜 키로 걸어서 새 자리에 막히고 옛 자리는 지남), `behind-piece-hidden`(책장 바닥 위 그림 칸에 선 플레이어를 책장이 가림), `minimap-follows`, `bed-reachable`, `sleep-prompt`, `slept`, `save-has-arrangement`(저장 파일의 `arrangements`), `reloaded`·`reloaded-collision`(F9 뒤 같은 배치와 충돌). 스크린샷 `dorm-default.png`, `arrange-browse.png`, `arrange-invalid.png`(빨강), `arrange-refused-toast.png`, `arrange-valid.png`(초록), `arrange-placed.png`, `arrange-stored.png`, `arrange-take-out.png`, `dorm-arranged.png`, `behind-bookshelf.png`, `map-dorm-arranged.png`, `morning-arranged.png`, `arrange-reloaded.png`.
- `stacking`: `stacking.data`(책상 윗면 3칸·상자 1칸, 화분이 stackable), `cursor-on-plant`, `valid-ghost-on-desk`(유령이 책상 윗칸 6,4에서 초록), `ghost-is-lifted`(유령 높이 = 책상 `lift`), `plant-on-desk`(화분 1이 Top, 호스트 책상, 책상은 Loaded, 칸은 여전히 막힘), `cursor-on-desk`, `loaded-desk-cannot-lift`(Enter가 거절되고 `올려 둔 물건부터` 알림, 책상 제자리), `loaded-desk-cannot-store`(C도 거절), `cursor-on-plant-b`, `second-plant-same-cell-refused`(같은 윗칸은 `SurfaceTaken`), `refused-enter-changes-nothing`, `chest-lid-ghost`·`plant-on-chest`(상자 뚜껑), `minimap-paints-top-last`, `slept`, `reloaded`(F9 뒤 화분 1은 책상 위, 화분 2는 상자 위, 둘 다 Loaded). 스크린샷 `plant-on-desk-ghost.png`, `plant-on-desk.png`, `desk-loaded-refused.png`, `plant-second-refused.png`, `plant-on-chest.png`, `plant-on-desk-reloaded.png`. 화분이 윗면 위에 서 있는 것처럼 보이는지(`lift`)는 PNG를 보고 사람이 판단한다.
- 규칙 자체(놓기 규칙, 층과 올려 두기, 저장 왕복, 통과·길찾기, 나쁜 저장 정리)는 `Tests/Core/FurnitureTests.cs`가 검사한다.

## Gotchas

- 저장은 하루를 마칠 때만이다. 배치만 바꾸고 F9를 누르면 바꾼 배치는 사라진다(다른 모든 상태와 같다).
- 서 있는 자리에는 놓을 수 없고, 배치 모드 중에는 걸을 수 없다. 서 있는 자리에 놓으려면 F로 닫고 비켜선 뒤 다시 연다.
- 침대 옆 한 칸(침대 자리 바로 아래)은 기본 배치에서 침대로 가는 유일한 길목이다. 거기에 놓으면 `길이 막혀요`로 거절된다. 책장을 옮기면 침대 오른쪽이 열려 놓을 수 있게 된다.
- 목록의 가구는 이름이 짧다(화면 폭 때문). 보관한 가구는 이름이 흐려지고 작은 상자 표시가 붙는다.
- 사람이 판단할 것: 유령 색(초록/빨강)과 말풍선이 읽히는지, 선택 테두리의 맥동, 가구를 벽 장식 앞에 놓았을 때의 겹침이 어색하지 않은지, 옮긴 가구의 깊이 순서(앞뒤)가 자연스러운지, 지도의 칸 색이 가구답게 읽히는지는 PNG를 보고 사람이 판단한다.
