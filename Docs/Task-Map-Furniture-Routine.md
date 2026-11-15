# 지도 개선 · 가구 배치 · NPC 루틴 (2026-11-11)

사용자 요청 원문(2026-11-11):

> 1. 지도 개선 필요. 공간 자체가 더 많아져야 하고, 지도에 그 타일의 평균 색상이 반영되면 좋겠음. 그리고 지금 한 타일이 너무 큰데 좀 작게 그려도 OK. 캐릭터 위치도 색상 표기 말고 캐릭터 얼굴이 뜨게 해줘.
> 22. 특정 공간의 경우 가구 배치를 플레이어가 할 수 있도록 했음 좋겠다. (Ex. 기숙사 내 방)
> 23. NPC 루틴 만들어줘. 몇시엔 여기서 뭘하고 몇시엔 저기로 이동해서 뭘 하고... 시스템으로 만들어서 Json으로 수정할 수 있게. 행동 애니메이션은 codex한테 부탁해서 만들어달라고 하고.

"공간 자체가 더 많아져야"는 캠퍼스 구역(맵) 수를 늘리라는 뜻으로 해석했다(AI 해석, 사용자 미확인).

## 작업 순서와 이유

코드 작업자는 한 번에 한 명이다. 세 기능이 `Game.cs`, `GameState.cs`, `WorldView.cs`, `GameRoot.cs`, `Probe.cs`, 번들 JSON을 함께 고치고, 작업 트리에 커밋되지 않은 변경이 많아 git worktree로 나눌 수 없으며, Unity 빌드는 한 프로젝트에 한 번만 돌 수 있다. Codex 애니메이션 제작은 그림 파일만 만들므로 동시에 돈다.

1. 지도 (공간 추가, 미니맵 평균 색, 작은 칸, 얼굴 표시)
2. 가구 배치 (기숙사 방)
3. NPC 루틴 (지도 단계에서 생긴 새 장소를 쓴다)
4. (병행) Codex 행동 애니메이션

## 1. 지도

- 새 구역은 `Content/Fixtures/FirstConnection/world/world.json`에 넣고 `tools/derive_world.py`로 번들을 다시 만든다. 새 방은 아트 레이아웃이 없으므로 Stage5 `fallback` 행으로 그린다. CampusGrid 아트로 다시 그리는 일은 후속(Codex 장면 작업)이다.
- 미니맵 색은 `derive_world.py`가 칸마다 그 칸의 실제 그림(바닥 + 그 칸을 덮는 기물)의 평균 색을 계산해 생성 파일에 적는다. 게임은 읽어서 칠하기만 한다.
- 칸 크기는 지금보다 작게 상한을 둔다.
- 플레이어·NPC 위치는 캐릭터 스프라이트(`UiArt/Characters/<id>/`)의 남쪽 대기 프레임에서 얼굴 부분을 잘라 그린다.

## 2. 가구 배치

- 데이터: `world.json`의 방 정의에 배치 가능 구역과 옮길 수 있는 기물(레이아웃 prop id)을 적는다. 기본 배치는 레이아웃의 원래 배치다. 플레이어가 바꾼 배치는 저장 파일에 방별로 남는다.
- 충돌: 옮길 수 있는 가구의 막힌 칸은 고정 `blocked`에서 빼고, 실행 중 배치에서 계산한다.
- 규칙: 구역 안, 벽·문·다른 가구·플레이어와 겹치지 않음, 문에서 침대까지 길이 남아 있음. 침대는 잠자기 규칙이 걸려 있어 고정한다.

### 2. 구현 결과 (2026-11-11)

- 데이터: `world.json`의 `arrangeable[]`(방, `area`, 가구 `pieces[]` = 레이아웃 배치 여러 개를 묶은 조각, 첫 부품이 기준점, 협탁의 램프·상자의 촛불은 같이 움직임). `derive_world.py`가 번들 세 개에 `arrangeable` 섹션(`area`, `keep`, 조각별 `at`·`cells`·`bounds`)을 쓰고, 움직이는 가구를 고정 `blocked`와 기본 미니맵에서 뺀다. 조각별 칸 색은 `world-art.json`의 `maps.<id>.pieces`에 있고 지도 화면이 지금 놓인 자리에 칠한다(빈 방 + 조각 위 덧칠이라 기본 배치에서 옛 미니맵과 칸 색이 조금 다를 수 있다).
- 움직이는 조각은 12개(책장, 책상, 옷장, 협탁+램프, 거울, 상자+촛불, 별 의자+촛불, 책 더미, 화분 둘, 꽃 화분, 러그). 고정: 침대, 창가 의자(+고양이), 벽, 커튼·배너·화환·덩굴·등불, 문 앞 초록 러너.
- 저장: `arrangements.<mapId>` = `placed`(piece, x, y) + `stored`. 항목이 없으면 레이아웃 그대로. 읽을 때 `Furniture.Normalize`가 정리한다(모르는 조각 버림, 규칙에 어긋나면 기본 자리, 거기도 막히면 보관).
- 충돌은 한 곳: 지금 배치가 막는 칸을 `MapDef.Furnished`에 쓰고 `World.Passable`이 읽는다(플레이어 이동과 NPC 길찾기 모두). 놓기 규칙은 `Furniture.Rule`(순수 함수, Core 테스트).
- 키: F(옮길 수 있는 방에서 열기/닫기), 위·아래 고르기, Enter 집기·놓기, 방향키 옮기기, C 보관(Delete도), Esc 취소. 열린 동안 시간이 멈춘다.
- 검증: `Tests/Core/FurnitureTests.cs`, probe `furniture`(`.claude/skills/verify-magiccampus/features/furniture-arrangement.md`).
- 남은 것(사람 판단): 화면 품질, 지도 칸 색이 가구답게 읽히는지. 배치는 다른 상태처럼 하루를 마칠 때 저장된다(배치만 바꾸고 F9를 누르면 사라진다).

## 3. NPC 루틴

- 데이터: 번들 폴더마다 `routines.json`. NPC마다 시각(`"HH:MM"`), 장소(destination id), 행동(action id)의 목록. 기존 `schedules` 스크립트 문자열은 지우고 이것으로 옮긴다.
- 실행: 시각이 되면 그 장소로 걸어가고, 도착하면 행동 애니메이션을 재생한다. 애니메이션이 없는 행동은 대기 애니메이션으로 대신한다.

### 3. 구현 결과 (2026-11-11)

- 데이터: 번들 폴더마다 `generated/<번들>/routines.json`(`base`, `variants/a10-mira`, `variants/dest-moved`). NPC id를 키로 단계 목록 `{ "at": "HH:MM", "place": "<장소 id 또는 home>", "action": "<행동 id>", "facing": 선택, "note": 선택 }`. 시각은 `25:59`까지 쓸 수 있다(24를 넘기면 자정 이후). 번들의 옛 `schedules` 문자열·`ScheduleDef`·옛 파싱은 지웠다. 작성법: `Content/Fixtures/FirstConnection/routines-guide.md`.
- 규칙(`Assets/MagicCampus/Core/Schedule.cs`): 지금 단계 = 그 시각 이전의 가장 늦은 단계(첫 단계 전이면 마지막 단계). 단계 시각이 되면 걸어가고(`Actor.Goal`), 도착하면 방향(단계 `facing`, 없으면 장소)을 보고 `Actor.Action`이 시작된다. 행동은 런타임 상태이며 저장하지 않는다. `World.Place`·`World.WalkTo`가 행동을 멈춘다. 불러오기·새 날은 지금 단계로 바로 놓는다. 이벤트 뒤 NPC는 지금 단계로 걸어가 행동을 다시 시작하고, 취소하면 하던 모습으로 돌아온다.
- 그림 고르기: `ActorClips.KeyFor`(Core, 순수 함수). 걷는 중 `<방향>.walk`, 서 있고 행동이 있으면 `<방향>.<행동>` → `south.<행동>` → 대기. 모르는 행동은 대기로 대신하고 멈추지 않는다. `WorldView.DrawActor`가 이것만 쓴다.
- 서린(기본·변형)은 07:00 마당 → 09:00 도서관 읽기 → 11:00 강의실 필기 → 12:30 식당 식사 → 13:30 마당 공지판 읽기 → 15:00 온실 필기 → 16:30 본관 게시판 이야기 → 18:00 식당 → 19:30 마당 필기 → 22:00 마당. 미라(`a10-mira`)는 09:30 강의실 → 11:30 도서관 → 13:00 식당 → 14:30 게시판 → 16:00 온실 → 18:00 휴게실 무대(이벤트 장소) → 21:00 공지판 → 24:30 집. 첫 외출을 09:00 이후로 둔 이유: 초대 이벤트와 probe가 아침 마당에서 서린에게 말을 건다.
- 검증: `Tests/Core/RoutineTests.cs`(번들 셋 모두의 루틴 검증, 단계 고르기, 걷고 나서 행동, 불러오기·새 날 배치, 이벤트 복귀·취소, 실제 시계로 하루 시뮬레이션, 나쁜 입력을 잡는지), probe `routine`·`routine-mira`(`.claude/skills/verify-magiccampus/features/npc-routine.md`). 시계를 옮기는 `Game.SetClock`은 probe와 테스트에서만 쓴다.
- 남은 것(사람 판단): 행동 애니메이션 품질, 임시 방(Stage5 `fallback` 그림)에서 NPC 자리가 자연스러운지, 일과 시간 배분.

## 4. 행동 애니메이션 (Codex)

- 대상: `npc-serin`, `npc-mira`. 행동: `read`(책 읽기), `eat`(간식 먹기), `write`(수첩에 필기), `chat`(손짓하며 이야기). 남쪽을 보는 프레임만 만든다. 다른 방향을 볼 때 행동하면 남쪽 프레임을 쓴다.
- 결과는 캐릭터 manifest의 `animations.south.<action>`에 들어간다. `WorldView.LoadCharacter`가 이미 모든 클립을 읽으므로 키는 `south.read` 같은 모양이 된다.

## 진행 상태

| 단계 | 담당 | 상태 |
|---|---|---|
| 1 지도 | Sonnet 서브에이전트 | 완료 (`map` probe 통과). 구역 8개(새 방 5개는 `fallback` 그림), 미니맵 칸 색, 얼굴 표시. 새 장소 `dest.hall_board`·`library_desk`·`classroom_seat`·`cafeteria_table`·`greenhouse_bed` |
| 2 가구 | Sonnet 서브에이전트 | 구현 완료 (Core 테스트 ALL PASS, `furniture` probe 통과, 아래 "2. 구현 결과"). 사람이 볼 것: 배치 모드 화면 품질 |
| 3 루틴 | Sonnet 서브에이전트 | 구현 완료 (Core 테스트 ALL PASS, `routine`·`routine-mira` probe 통과, 위 "3. 구현 결과"). 사람이 볼 것: 일과 배분, 임시 방에서의 모습 |
| 4 애니메이션 | Sonnet 서브에이전트 → Codex CLI | 완료. 서린·미라 `animations.south.read\|eat\|write\|chat` 4프레임씩. 사람이 볼 것: 그림 품질 |
