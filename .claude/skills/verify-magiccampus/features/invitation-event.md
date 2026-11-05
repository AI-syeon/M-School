# 초대 이벤트 진행

플레이어는 친구(서린 등)에게 걸어가 E로 초대 창을 열고 초대를 수락한다. 이벤트 장면에서는 화면이 잠깐 가려진 뒤 NPC가 약속 장소로 걸어가고 대사가 이어진 다음 선택지가 나온다. 고른 분기의 대사만 나오고, 끝나면 소지금과 시간이 늘어난다. 같은 이벤트는 다시 시작할 수 없다. 저장은 하루를 마칠 때 일어난다.

## Sub-features

- `invite-walk-interact`: W로 걸어가 서린 1.5칸 안에서 E를 누르면 초대 창이 열린다.
- `invite-accept-start`: `수락 후 시작`으로 수락하고 장면을 시작한다.
- `invite-early-confirm`: NPC가 걷는 동안 누른 Enter는 첫 대사를 건너뛰지 않는다.
- `invite-branch`: 선택지(노트/차/소품)에 따라 그 분기 대사만 나온다.
- `invite-reward`: 보상(base +20, a10 +30 크라운), 시계 +10분, 완료 기록, 약속 삭제.
- `invite-replay-refused`: 완료한 이벤트는 다시 시작되지 않고 보상도 없다.
- `invite-variant`: 목적지가 바뀐 변형(`dest-moved`)과 다른 NPC 변형(`a10-mira`)도 같은 흐름으로 동작한다.

## How to get to it (user POV)

- 마당에서 서린에게 걸어가 `E`를 누르면 초대 창이 열린다. 마우스로 `수락`, `시작`, `수락 후 시작`을 누르거나, 방향키로 고르고 `Enter`로 수락 후 시작한다.
- `J`/`Tab` 또는 화면 위 `일지(J)` 버튼으로 일지를 열어도 같은 약속을 고를 수 있다.
- 대사는 `Enter`/`Space`로 넘긴다. 선택지는 방향키나 숫자키로 고르고 `Enter`로 정한다. 마우스 클릭도 된다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.
- 새 run-id를 쓴다.

- **노트·차 분기.** `scripts\run-probe.ps1 -Cases success-notes,success-tea -Run <id>`. 케이스마다 `pass=True exit=0 checks=15 failedChecks=0`이어야 한다(2026-11-06 실측, a10은 걷기 검사 2개가 빠져 13).
- **변형.** `-Cases moved,a10`. `moved`는 바뀐 목적지 도착(`mover-at-destination`), `a10`은 보상 +30(`wallet.plus-30`)을 본다. `a10`은 걷기와 E를 건너뛴다. 미라와 서린이 같은 칸에 서 있어 E로는 서린의 초대가 열리기 때문이다.
- **결과 확인.** `result.json`에서 `input.walk-north`, `interact.invite-panel`, `event.started`, `early-enter-ignored`, `mover-at-destination`, `branch.only-chosen`, `wallet.plus-20`, `clock.plus-cost`, `completed`, `appointment.removed`, `retry.refused`, `render.integer-pixel-scale`이 통과여야 한다.
- **화면 증거.** `invite.png`(초대 창), `choose.png`(선택지 창, 고른 항목 강조), `branch.png`(분기 첫 대사), `final-world.png`(장면 뒤 라운지 맵과 인물).

## Gotchas

- 선택지 키(`notes`, `tea`, `props`)는 probe가 콘텐츠의 `choice` 인자에서 찾는다. 콘텐츠에서 키를 바꾸면 `choice.found`가 실패한다. probe를 고치기 전에 키 변경이 의도였는지 확인한다.
- 이벤트를 마쳐도 저장되지 않는다. 저장 증명은 `day-save` 케이스다.
