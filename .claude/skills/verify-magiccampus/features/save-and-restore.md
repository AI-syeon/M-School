# 하루 끝(취침·기절), 저장과 불러오기

하루는 Core의 `Game.EndDay(DayEnd how)` 한 함수로만 끝난다. 부르는 곳은 두 군데다. 침대에서 `E`를 누르고 확인하면 `Slept`, 시계가 새벽 2시(`Clock.PassOutAt` = 26:00)에 닿으면 `PassedOut`이다. 자정에는 아무 일도 없고 시계는 24:00 이후로 계속 간다(화면에는 00:xx, 01:xx). 어느 쪽이든 다음 날 07:00에 침대 위치에서 일어나고, NPC는 일정 위치에 서고, 게임이 저장된다. 이벤트 중에는 거절된다. 기절에는 벌칙이 없다.

저장은 임시 파일에 쓴 뒤 교체하고 이전 저장 하나를 `.bak`으로 둔다. `F9`는 마지막 저장을 새 게임 객체로 다시 읽는다. 저장이 없으면 시작 상태로 돌아간다.

## Sub-features

- `sleep`: 침대(콘텐츠의 `bed`, 지금은 마당 왼쪽 아래 벤치 칸) 1칸 안에서 `E` → 확인창 → `Enter`/`Y`/`잔다` 버튼. `Esc`/`N`/`X`/`아니요`는 닫는다.
- `pass-out`: 새벽 2시에 같은 함수로 하루가 끝나고 상태줄에 "새벽 2시에 쓰러져 잠들었습니다"가 나온다.
- `day-end-save`: 하루 끝이 날짜 +1, 07:00, 침대 위치, 소지금·완료·약속을 `save.json`에 쓴다.
- `reload-f9`: `F9`가 저장 파일에서 새 `Core.Game`을 만든다. 소지금과 완료가 유지되고, 완료한 이벤트는 다시 시작되지 않는다.

## How to get to it (user POV)

- 마당 왼쪽 아래 벤치 칸으로 걸어가 `E`.
- 새벽 2시까지 깨어 있기(1초 = 게임 1분).
- `F9` 또는 메뉴의 `마지막 저장 불러오기 (F9)`.
- 기본 저장 파일은 `%USERPROFILE%\AppData\LocalLow\MagicCampus\MagicCampus\Stage5\save.json`이다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다.

- **취침, 저장, 다시 읽기.** `scripts\run-probe.ps1 -Cases day-save -Run <id>`. 서린 이벤트를 노트 분기로 마친 뒤 플레이어를 침대 옆에 놓고 `E`, `Enter`를 누른다. 이어서 `F9`. `sleep.prompt`, `sleep.woke-at-bed`, `day-save.file`, `day-save.contents`, `day-save.reloaded-new-game`, `day-save.event-refused`가 통과여야 한다. 스크린샷 `sleep-prompt.png`, `morning.png`.
- **기절.** `scripts\run-probe.ps1 -Cases pass-out -Run <id>`. 시계를 01:58로 맞추고 기다린다. `pass-out.day-ended`, `pass-out.saved`, `pass-out.status`가 통과여야 한다. 스크린샷 `night.png`, `pass-out.png`.
- **저장 증거.** 케이스 폴더의 `save\save.json`을 연다. day-save는 `day: 1`, `minute: 420`, `wallet: 220`, `completed`에 `event.snack`, 플레이어 `x: 0.5, y: 0.5`여야 한다.

## Gotchas

- probe는 침대까지 걷지 않고 플레이어를 침대 옆에 놓는다. 걷기 경로는 다른 케이스(W로 서린에게 걷기)가 증명한다.
- 다시 읽기는 같은 프로세스 안에서 새 `Core.Game`을 만든다. 프로세스를 다시 띄워 읽는 검사는 없다.
- 이벤트 도중 게임을 끄면 그날 시작 전의 마지막 저장에서 다시 시작한다. 이것은 의도된 동작이다.
