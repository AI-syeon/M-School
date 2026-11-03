# 초대 이벤트 진행

플레이어는 친구(서린 등)의 초대를 수락한다. 이벤트 장면에서는 NPC가 이동하고 대사가 이어진 뒤 선택지가 나온다. 고른 분기의 대사만 나오고, 끝나면 소지금과 시간이 늘어나며 결과가 저장된다. 같은 이벤트는 다시 시작할 수 없다.

## Sub-features

- `invite-accept`: 제안된 이벤트를 수락하고 즉시 디스크에 저장한다.
- `invite-scene`: 장면 진입 저장 뒤 NPC가 첫 목적지로 이동하고 대사창이 열린다.
- `invite-early-confirm`: 이동 중 Enter는 무시된다.
- `invite-branch`: 선택지(노트/차/소품)에 따라 그 분기 대사만 나온다.
- `invite-reward`: 보상이 한 번만 지급된다(base +20, a10 +30 크라운). 시계는 +10분.
- `invite-replay-blocked`: 완료한 이벤트를 다시 시작하면 실패하고 보상이 없다.
- `invite-variant`: 목적지가 바뀐 변형(`dest-moved`)과 다른 NPC 변형(`a10-mira`)도 같은 흐름으로 동작한다.

## How to get to it (user POV)

- 마당에서 친구에게 걸어가 `E`를 누르면 초대 창이 열린다. `수락`, `시작`, `수락 후 시작`은 마우스로 클릭한다.
- `J` 또는 `Tab`, 또는 화면 위 `일지(J)` 버튼으로 일지를 열어 같은 이벤트를 고른다.
- 대사 진행은 `Enter`/`Space`, 선택지 이동은 방향키, 결정은 `Enter`다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.
- 새 run-id를 쓴다(이미 `result.json`이 있으면 스크립트가 거부한다).

- **노트 분기.** `scripts\run-probe.ps1 -Cases success-notes -Run <id>`를 실행한다. 출력은 `success-notes pass=True exit=0 checks=25 failedChecks=0 skipped=0`이어야 한다(2026-11-03 실측).
- **차 분기.** `-Cases success-tea`. `branch.only-chosen` 검사에서 차 분기 대사만 관측돼야 한다.
- **변형.** `-Cases moved,a10`. `moved`는 바뀐 목적지에 NPC가 도착하는지(`scene.actor-at-first-destination`), `a10`은 보상이 +30인지(`wallet.plus-cost`) 본다.
- **결과 확인.** `Tests\Results\verify\<id>\success-notes\result.json`에서 다음 검사가 모두 통과여야 한다: `accept.saved`, `A03.entry-saved-before-walk`, `A05.early-confirm-ignored`, `branch.only-chosen`, `wallet.plus-cost`, `clock.plus-10`, `ledger.reward-once`, `snapshot.persisted`, `retry.failed-no-reward`.
- **화면 증거.** 같은 폴더의 `choose.png`(선택지 창, 첫 항목 강조), `branch.png`(분기 첫 대사), `final-world.png`(장면 종료 뒤 맵)을 연다. 상단 표시가 `● 저장됨`이어야 한다.
- **저장 증거.** `save\gen-*.mcgen`이 여러 세대 있어야 한다. 실측은 3개였다.
- **사람 입력 경로.** `scripts\play.ps1 -Action start -Run <id>`로 띄우고 사용자에게 걸어가서 `E` → `수락 후 시작` → 대사 진행을 부탁한다. 그 뒤 `-Action status`로 `candidates`에서 `event.snack`이 사라졌는지, 소지금이 바뀌었는지 확인하고, `-Action stop`으로 끝낸다.

## Gotchas

- probe는 `Rt.Accept`와 `Rt.StartEvent`를 직접 부른다. 초대 창 버튼과 `E` 상호작용은 probe로 증명되지 않는다.
- 선택지는 `--choice` 힌트(기본 `notes`)를 옵션 키와 부분 일치로 찾는다. 콘텐츠에서 옵션 키를 바꾸면 `choice.target-found`가 실패한다. 이때 probe를 고치지 말고 키 변경이 의도였는지부터 확인한다.
- `A05.early-confirm-ignored`는 이동이 너무 빨리 끝나면 `skipped`로 남는다. 통과로 세지 않는다.
- 선택지 화면의 상단 상태줄은 여전히 "친구에게 다가가 E키로 말을 걸어 보세요."를 보여 준다(2026-11-03 `choose.png`). 지금 동작이며 probe 실패가 아니다.
