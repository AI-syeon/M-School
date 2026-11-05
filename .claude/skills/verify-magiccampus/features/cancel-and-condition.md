# 취소와 조건 불충족

이벤트 장면을 취소하면 보상과 시간 변화 없이 장면 시작 때의 위치·맵으로 돌아간다. 수락한 약속은 그대로 남는다. 도입 조건이 맞지 않는 이벤트는 시작되지 않고 상태도 바뀌지 않는다.

## Sub-features

- `cancel-scene`: 선택지에서 `Esc`(또는 `X`/`Backspace`)를 누르면 장면이 끝나고 모든 인물이 시작 위치로 돌아간다. 수락 상태는 유지된다.
- `cancel-repeat`: 끝난 장면을 다시 취소해도 아무 일도 없다.
- `condition-false`: `story.intro_done`이 거짓이면 `event.snack`이 시작되지 않고 상태와 저장 파일이 그대로다.

## How to get to it (user POV)

- 장면 중 `Esc`, `X`, `Backspace`로 취소한다. 선택지 창에 "취소 닫기"가 표시된다.
- 조건 불충족은 도입을 마치지 않은 상태(`start-intro-false.json`)에서 초대를 시작할 때 생긴다. 상태줄에 "지금은 시작할 수 없는 약속입니다."가 나온다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.

- **취소.** `scripts\run-probe.ps1 -Cases cancel -Run <id>`. `cancel.event-ended`, `cancel.no-effects`, `cancel.accepted-kept`, `cancel.actors-restored`, `cancel.repeat-is-noop`가 통과여야 한다. `choose.png`(취소 직전 선택지)와 `cancel.png`(마당으로 돌아온 화면)를 연다.
- **조건 불충족.** `-Cases intro-false`. 스크립트가 `--initial start-intro-false.json`을 붙인다. `intro-false.refused`, `intro-false.state-unchanged`, `intro-false.no-save`가 통과여야 한다.

## Gotchas

- probe는 `Esc` 키로 취소한다. 마우스 경로는 없다.
- intro-false 검사는 `Game.StartEvent`를 직접 부른다. 초대 창의 버튼도 같은 메서드를 부르지만 상태줄 문구는 사람 입력으로만 확인된다.
