# 취소와 실패 복구

이벤트 장면을 취소하거나 장면이 진행될 수 없으면, 보상과 시간 변화 없이 장면 전 상태로 돌아간다. 수락한 약속은 그대로 남는다. 조건이 맞지 않는 이벤트는 시작되지 않고 진단 메시지를 남긴다.

## Sub-features

- `cancel-scene`: 선택지에서 취소하면 장면이 끝나고 NPC 위치가 복원된다. 수락 상태는 유지된다.
- `cancel-once`: 같은 프레임의 중복 취소에도 종료 알림은 한 번만 온다.
- `fail-map`: 목적지 맵 로드에 실패하면 장면이 실패하고 진입 상태로 복원된다.
- `fail-closed`: 목적지로 가는 문이 닫혀 있으면 실패하고 복원된다.
- `fail-blocked`: 목적지 칸이 막혀 있으면 실패하고 복원된다.
- `fail-condition`: 수락하지 않은 이벤트를 시작하면 `ConditionFalse`로 실패하고 상태와 저장이 바뀌지 않는다.

## How to get to it (user POV)

- 장면 중 `X` 또는 `Backspace`로 취소한다. 선택지 창에도 "취소 닫기"가 표시된다.
- 맵 실패, 닫힌 문, 막힌 칸은 콘텐츠나 맵 상태가 잘못됐을 때 일어난다. 플레이어가 직접 만들 수 있는 경로는 없다.
- 조건 불충족은 수락하지 않은 초대를 `시작`할 때 생긴다. `시작` 버튼은 수락 전에는 비활성화되어 있다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.

- **취소.** `scripts\run-probe.ps1 -Cases cancel -Run <id>`를 실행한다. `cancel.terminal`, `cancel.terminal-once`, `cancel.accepted-kept`, `cancel.actors-restored`, `cancel.leaks0`가 통과여야 한다.
- **장애 3종.** `-Cases map-fail,closed,blocked`. 케이스마다 `<mode>.terminal`(Failed 또는 Cancelled), `<mode>.no-effects`(소지금·시계 그대로), `<mode>.entry-restored`(맵·배우 위치 복원), `<mode>.leaks0`가 통과여야 한다.
- **조건 불충족.** `-Cases intro-false`. 이 케이스는 `--initial save-intro-false.json`으로 시작한다. `intro-false.condition-false`, `intro-false.state-unchanged`, `intro-false.no-saves`가 통과여야 한다.
- **증거.** 각 케이스 폴더의 `result.json`, `input.jsonl`, `probe.log`를 확인한다. `probe.log`에는 `armed <mode> target=...` 줄이 있어야 한다.

## Gotchas

- `map-fail`, `closed`, `blocked`는 Runtime 스위치(`FailMapId`, `SetPortalOpen(false)`, `SetBlocked`)로 장애를 **주입**한다. 실제 콘텐츠 결함의 재현으로 보고하지 않는다.
- 취소 probe는 `Dialogue.Cancel`과 `Runtime.Cancel`을 직접 부른다. `X` 키 경로는 사람 입력으로만 증명된다.
- 장애 케이스는 실패 화면 스크린샷을 남기지 않는다. 예외가 났을 때만 `failure.png`가 생긴다. 시각 증거가 필요하면 `play.ps1`로 따로 관찰한다.
