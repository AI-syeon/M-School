# 대화창 UI

대사창은 긴 문장도 창 안에서 읽을 수 있게 보여 준다. 넘치는 문장은 ▼ 표시가 나오고 위·아래 방향키나 마우스 휠로 넘겨 본다. 창 크기가 640×480이든 960×640이든 깨지지 않아야 한다. 메뉴에서 글자 크기를 2배로 키우면 HUD와 대사창이 함께 커진다.

## Sub-features

- `ui-long-text`: 500자 이상 대사가 표시된다.
- `ui-resolution`: 640×480과 960×640 두 해상도에서 대사창이 보인다.
- `ui-text-scale`: 글자 크기 2배에서도 대사창이 보인다.
- `ui-paging`: 아래 방향키로 넘친 문장을 넘겨 본다.
- `ui-close`: Enter로 대사를 닫으면 창이 사라진다.

## How to get to it (user POV)

- 이벤트 장면 중 대사가 나올 때마다 대사창이 열린다.
- `Esc` 메뉴의 `글자 크기 1배`·`글자 크기 2배` 버튼으로 크기를 바꾼다.
- 게임 창 가장자리를 끌어 크기를 바꾼다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.
- 게임 창이 화면에 보여야 한다.

- **기본 크기.** `scripts\run-probe.ps1 -Cases ui-long -Run <id>`. probe가 640자 시험 대사 하나짜리 이벤트를 콘텐츠에 더해 실제 이벤트 경로로 띄운다. `ui.started`, `ui.resolution 640x480`, `ui.resolution 960x640`, `ui.paged`, `ui.closed`가 통과여야 한다.
- **2배 크기.** `-Cases ui-200`. 같은 검사를 글자 크기 2배로 반복한다.
- **사람 판정.** `<case>-640x480.png`, `<case>-960x640.png`, `<case>-paged.png`를 열어 직접 본다. 글자가 창 밖으로 잘리는지, 겹치는지, 넘긴 뒤 ▲가 보이는지 확인하고 판단을 `result.json`과 따로 적는다.

## Gotchas

- `pass=True`여도 사람 판정 전에는 "UI 품질 통과"라고 쓰지 않는다.
- `ui-200`은 `GameRoot.TextScale`을 바로 바꾼다. 메뉴 버튼 경로는 probe가 지나지 않는다.
