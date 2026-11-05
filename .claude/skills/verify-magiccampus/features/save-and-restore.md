# 하루 마치기 저장과 불러오기

게임은 하루를 마칠 때만 저장한다(메뉴의 `하루 마치기` 또는 자정). 다음 날 07:00으로 넘어가고, NPC는 일정 위치에 선다. 저장은 임시 파일에 쓴 뒤 교체하고 이전 저장 하나를 `.bak`으로 둔다. `F9`는 마지막 저장을 새 게임 객체로 다시 읽는다. 저장이 없으면 시작 상태로 돌아간다.

## Sub-features

- `day-end-save`: `하루 마치기`가 날짜 +1, 07:00, 소지금·완료·약속을 `save.json`에 쓴다.
- `reload-f9`: `F9`가 저장 파일에서 새 `Core.Game`을 만든다. 소지금과 완료가 유지되고, 완료한 이벤트는 다시 시작되지 않는다.
- `midnight`: 자정이 되면 같은 방식으로 다음 날이 되고 저장된다(`Tests/Core`가 검사한다. probe 케이스는 없다).

## How to get to it (user POV)

- `Esc` → `하루 마치기 (저장)`. 이벤트 중에는 비활성이다.
- `F9` 또는 메뉴의 `마지막 저장 불러오기 (F9)`.
- 기본 저장 파일은 `%USERPROFILE%\AppData\LocalLow\MagicCampus\MagicCampus\Stage5\save.json`이다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.

- **하루 저장과 다시 읽기.** `scripts\run-probe.ps1 -Cases day-save -Run <id>`. 서린 이벤트를 노트 분기로 마친 뒤 `GameRoot.EndDay`(메뉴 버튼과 같은 메서드)를 부르고 `F9`를 누른다. `day-save.file`, `day-save.contents`, `day-save.reloaded-new-game`, `day-save.event-refused`가 통과여야 한다.
- **저장 증거.** 케이스 폴더의 `save\save.json`을 연다. `day: 1`, `minute: 420`, `wallet: 220`, `completed`에 `event.snack`이 있어야 한다.

## Gotchas

- 다시 읽기는 같은 프로세스 안에서 새 `Core.Game`을 만든다. 프로세스를 다시 띄워 읽는 검사는 없다. 필요하면 `play.ps1`로 같은 `--save-dir`을 주고 `--fresh` 없이 띄운다(스크립트 지원 없음).
- 이벤트 도중 게임을 끄면 그날 시작 전의 마지막 저장에서 다시 시작한다. 이것은 의도된 동작이다.
