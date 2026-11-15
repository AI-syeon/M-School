# NPC 루틴

NPC가 하루 일과표(`routines.json`)대로 움직인다. 정해진 시각이 되면 다른 장소로 걸어가고(걷는 동안 걷기 애니메이션), 도착하면 그 장소의 방향을 보며 행동(책 읽기·간식 먹기·필기·이야기) 애니메이션을 반복한다. 게임을 불러오거나 새 날이 시작되면 NPC는 지금 시각의 자리에 이미 서서 행동 중이다. 서린은 아침 마당에서 시작해 도서관(읽기) → 강의실(필기) → 학생 식당(식사) → 마당 공지판(읽기) → 온실(필기) → 본관 게시판(이야기) → 식당 → 마당으로 다닌다. 미라(`a10-mira` 변형)는 강의실·도서관·식당·게시판·온실·휴게실 무대·공지판을 돌고 자정 넘어 집에 간다.

## Sub-features

- 데이터: 번들 폴더마다 `routines.json`. NPC id별 단계 목록 `{ at, place, action, facing?, note? }`. 작성법은 `Content/Fixtures/FirstConnection/routines-guide.md`.
- 걷기: 단계 시각(게임 시계 분)이 되면 그 장소로 걷는다. 구역을 넘는 길도 걷는다(식당 → 본관 복도 → 마당). 걷는 동안 `Actor.Action`은 `idle`, `Actor.Goal`이 가려는 단계다.
- 도착: 방향(단계의 `facing`, 없으면 장소의 `facing`)을 보고 `Actor.Action`이 단계의 행동이 된다.
- 그림 고르기(`ActorClips.KeyFor`, Core): 걷는 중이면 `<방향>.walk`. 서 있고 행동이 있으면 `<방향>.<행동>`, 없으면 `south.<행동>`, 그것도 없으면 대기 그림. 모르는 행동은 대기 그림으로 대신한다.
- 불러오기·새 날: `Schedule.PlaceAll`이 지금 단계의 자리·방향·행동으로 바로 놓는다.
- 이벤트: 이벤트에 불려 간 NPC는 행동을 멈추고(대기), 이벤트가 끝나면 지금 단계로 걸어가 행동을 다시 시작한다. 취소하면 하던 모습 그대로 돌아온다.
- 저장: 행동은 저장하지 않는다(런타임 상태).
- 검증 보조: `Game.SetClock(분)`은 시계를 옮기고 NPC를 그 시각의 단계에 놓는다(probe와 테스트 전용).

## How to get to it (user POV)

1. 게임을 시작한다(1일 07:00, 서린은 마당 자기 자리에 있다).
2. 시간이 흐르면 서린이 09:00에 도서관으로 걸어간다. 따라가서 도서관에 들어가면 책을 읽고 있다.
3. 낮 동안 강의실(필기), 식당(식사), 본관 게시판 앞(이야기)에서 각각 다른 동작을 하는 서린을 볼 수 있다.
4. 서린에게 E로 초대를 받으면 장면이 시작하고, 끝나면 서린은 지금 일과 자리로 돌아간다.

## Driving it with verify-magiccampus scripts

Preconditions: `Builds\Stage5-verify`가 최신 빌드. 시작 상태는 기본(`start-offered.json`, 마당, 07:00). `routine-mira`는 `--variant variants/a10-mira`(스크립트가 붙인다)라 미라가 있다.

```
powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\run-probe.ps1 -Cases routine,routine-mira -Build Stage5-verify -Run <run-id>
```

- `routine`(서린): `day-starts-at-home`(07:00 마당, 대기 그림 `south.idle`), `waits-for-the-step`(`SetClock(08:59)` 뒤 아직 안 걷는다), `leaves-on-time`(09:00에 걷기 시작, 목표 단계 `dest.library_desk`, 행동 `idle`), `plays-walk`(뷰의 그림 키가 `*.walk`), `arrives-with-action`(도서관 (3.5,4.5), `Action=read`, 그림 `south.read`), `walked-action-animates`(걸어온 행동이 4프레임을 돈다), `serin-write`/`serin-eat`/`serin-chat`/`serin-read`(`SetClock`으로 놓은 뒤 `-placed`·`-clip`(`south.<행동>`)·`-animates`), `second-walk-starts`·`second-walk-crosses-maps`·`second-walk-arrives`(13:30에 식당 → 복도 → 마당 공지판, 도착해 `read`), `event-ends-the-pose`(초대 이벤트가 서린을 휴게실로 데려가면 행동이 멈춘다), `cancel-restores-the-pose`(취소하면 도서관에서 `read`), `nothing-saved`(저장 JSON에 행동·목표 없음). 스크린샷 `read-after-walk.png`(걸어서 도착한 도서관), `serin-write.png`, `serin-eat.png`, `serin-chat.png`, `serin-read.png`.
- `routine-mira`(미라): 같은 걷기·도착 검사(09:30 강의실 필기)와 `mira-eat`(식당), `mira-write`(강의실), `mira-chat`(본관 게시판). 스크린샷 `write-after-walk.png`, `mira-eat.png`, `mira-write.png`, `mira-chat.png`.
- 규칙 자체(단계 고르기, 걷기 뒤 행동, 불러오기·새 날 배치, 이벤트 복귀, 하루 시뮬레이션, 검증 규칙)는 `Tests/Core/RoutineTests.cs`가 검사한다. 하루 시뮬레이션은 실제 시계로 02:00 직전까지 돌려 모든 단계의 한 분 전에 NPC가 도착해 행동 중인지 본다.

## Gotchas

- probe는 `Game.SetClock`으로 시계를 옮긴다(플레이에는 없는 경로). 걷기는 시각을 1분 앞에 두고 실제 시계가 흐르게 해서 증명하고, 나머지 행동은 `SetClock`으로 바로 놓아 불러오기 경로를 증명한다.
- 지도가 바뀌면 화면이 0.25초 어두워졌다 켜진다. 그동안 뷰의 그림 키는 갱신되지 않으므로 probe는 `Transitioning`이 끝난 뒤 키를 읽는다.
- 행동 그림은 남쪽만 있다. 장소의 방향이 북쪽이어도 서 있는 NPC는 화면 쪽을 본 모습으로 그려진다.
- 사람이 판단할 것: 행동 애니메이션 자체의 품질(읽기·먹기·필기·이야기가 알아볼 만한지), 책상·식탁이 없는 임시 방(Stage5 `fallback` 그림)에서 NPC가 서 있는 자리가 어색하지 않은지, 하루 일과의 시간 배분이 마음에 드는지, NPC 말 걸기 범위가 일과 이동과 어울리는지는 PNG와 직접 플레이로 판단한다.
