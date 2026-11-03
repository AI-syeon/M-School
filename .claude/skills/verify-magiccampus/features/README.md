# MagicCampus 검증 지도

MagicCampus Stage5 빌드의 사용자 기능을 검증하는 유지 문서다. 게임을 구동하기 전에 이 목록을 읽고, 해당 기능 파일을 절차로 쓴다. 기능이 바뀌면 이 지도부터 고친다.

## 기본 전제

- 대상은 `Builds\Stage5-verify\MagicCampus.exe`다. 960×640 창 모드로 뜬다.
- `doctor.ps1 -Build Stage5-verify`가 `exe OK`, `build OK`, `fresh OK`를 보고해야 한다. `STALE`이면 `build.ps1`부터 돌린다.
- 모든 실행은 `--fresh`와 run 폴더 안의 `save\`를 쓴다. 시작 저장은 `save-offered.json`이다. 마당(`map.courtyard`), 1일 07:00, 소지금 200 크라운이고, `event.snack`과 `event.snack_later`가 제안(Offered) 상태다.
- 이 검증 실행이 띄우지 않은 MagicCampus.exe는 구동하지도 종료하지도 않는다.

## 구동 규칙

- 스크립트 경로 `.claude\skills\verify-magiccampus\scripts\`는 아래에서 `scripts\`로 줄여 쓴다.
- 명령은 프로젝트 루트에서 `powershell -ExecutionPolicy Bypass -File scripts\<이름>.ps1 ...` 형태로 그대로 실행한다.
- 케이스마다 새 프로세스와 새 저장 폴더를 쓴다. 이전 케이스의 상태를 이어 쓰지 않는다. 예외는 `run-restart.ps1 -SuccessFrom`처럼 명시한 경우뿐이다.
- probe 경로와 사람 입력 경로를 구분한다. probe는 공개 Runtime 명령과 가상 키보드를 쓴다. "NPC에게 걸어가 E" 같은 물리 입력 경로는 `play.ps1`로 띄운 뒤 사람이 직접 조작해야 증명된다.

## 증명과 건너뜀 보고

- 증거에는 `result.json`(검사 목록), 행동 순서 `input.jsonl`, 결과 상태 `final.json`, 스크린샷 하나 이상이 들어간다.
- 변경 증명에는 두 번째 확인이 필요하다. 저장 세대 파일이 생겼는지, 새 프로세스가 다시 읽은 상태가 맞는지 본다.
- 보고에는 기능 ID, 실행한 케이스, run-id, 종료 코드를 적는다.
- `skipped`에 남은 항목(예: `ui.human-review`)은 이유와 함께 그대로 보고한다. 다른 경로로 대신 통과시키지 않는다.
- 도달하지 못한 경로는 시도한 명령과 충족하지 못한 전제를 적는다.

## 기능 파일 형식

각 파일은 H1 제목과 사용자에게 보이는 동작을 설명하는 한 문단으로 시작한다. 그다음 H2 네 개를 이 순서로 둔다.

1. `Sub-features`: 짧은 ID와 한 줄 설명
2. `How to get to it (user POV)`: 사용자 진입 경로 전부
3. `Driving it with verify-magiccampus scripts`: `Preconditions:`로 시작하고, 행동·명령·관측 결과를 짝지은 항목
4. `Gotchas`: 검증을 망치거나 무효로 만드는 함정

## Features

- [초대 이벤트 진행](./invitation-event.md): 수락, 이벤트 장면, 선택지 분기, 보상, 재실행 차단. 2026-11-03 `skillcheck-01`에서 실제로 구동했다.
- [취소와 실패 복구](./cancel-and-failure.md): 장면 취소, 맵 실패, 닫힌 문, 막힌 칸, 조건 불충족.
- [저장과 복원](./save-and-restore.md): 자동 저장, 디스크 실패 후 재시도, 강제 종료 후 새 프로세스 복원, F5/F9.
- [콘텐츠 검증](./content-validation.md): 기본 카탈로그 컴파일과 잘못된 변형 거부.
- [대화창 UI](./dialogue-ui.md): 500자 이상 대사, 해상도 두 가지, 글자 크기 2배.
