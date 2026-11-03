# 대화창 UI

대사창은 긴 문장도 창 안에서 읽을 수 있게 보여 준다. 창 크기가 640×480이든 960×640이든 깨지지 않아야 한다. 메뉴에서 글자 크기를 2배로 키울 수 있다.

## Sub-features

- `ui-long-text`: 500자 이상 대사가 표시된다.
- `ui-resolution`: 640×480과 960×640 두 해상도에서 대사창이 보인다.
- `ui-text-scale`: 글자 크기 2배에서도 대사창이 보인다.
- `ui-cleanup`: 대사창을 닫으면 창과 입력 잠금이 남지 않는다.

## How to get to it (user POV)

- 이벤트 장면 중 대사가 나올 때마다 대사창이 열린다.
- `Esc` 메뉴의 `글자 크기 1배`·`글자 크기 2배` 버튼으로 크기를 바꾼다.
- 게임 창 가장자리를 끌어 크기를 바꾼다(창 크기 조절 허용).

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.
- 게임 창이 화면에 보여야 한다. 최소화하면 스크린샷이 비거나 시간 초과가 난다.

- **기본 크기.** `scripts\run-probe.ps1 -Cases ui-long -Run <id>`를 실행한다. `ui.text-500+`, `ui.resolution 640x480`, `ui.resolution 960x640`, `ui.cleanup`, `ui.leaks0`가 통과여야 한다.
- **2배 크기.** `-Cases ui-200`. 같은 검사를 글자 크기 2배로 반복한다.
- **사람 판정.** `ui-long-640x480.png`, `ui-long-960x640.png`, `ui-200-640x480.png`, `ui-200-960x640.png`를 열어 직접 본다. 확인할 것은 글자가 창 밖으로 잘리는지, 겹치는지, 스크롤로 끝까지 읽을 수 있는지다. 판단은 `result.json`과 따로 기록한다.

## Gotchas

- 두 케이스 모두 `skipped`에 `ui.human-review`가 남는다. `pass=True`여도 사람 판정 전에는 "UI 품질 통과"라고 쓰지 않는다.
- `ui-200`은 `DialoguePresentation.textScale` 필드를 리플렉션으로 바꾼다. 메뉴의 `글자 크기 2배` 버튼 경로는 probe가 지나지 않는다.
- probe 대사는 `Dialogue.Say`를 직접 호출한 시험 문장이다. 실제 콘텐츠 대사의 줄바꿈이나 이름 표시는 `invitation-event`의 `branch.png`로 본다.
