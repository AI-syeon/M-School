# MagicCampus 검증 지도

MagicCampus Stage5 빌드의 사용자 기능을 검증하는 유지 문서다. 게임을 구동하기 전에 이 목록을 읽고, 해당 기능 파일을 절차로 쓴다. 기능이 바뀌면 이 지도부터 고친다.

## 기본 전제

- 대상은 `Builds\Stage5-verify\MagicCampus.exe`다. 960×640 창 모드로 뜬다.
- `doctor.ps1 -Build Stage5-verify`가 `exe OK`, `build OK`, `fresh OK`를 보고해야 한다. `STALE`이면 `build.ps1`부터 돌린다.
- 모든 실행은 `--fresh`와 run 폴더 안의 `save\`를 쓴다. 시작 상태는 `start-offered.json`이다. 마당(`map.courtyard`), 1일 07:00, 소지금 200 크라운이고, `event.snack`과 `event.snack_later`가 제안(Offered) 상태다.
- 이 검증 실행이 띄우지 않은 MagicCampus.exe는 구동하지도 종료하지도 않는다.

## 구동 규칙

- 스크립트 경로 `.claude\skills\verify-magiccampus\scripts\`는 아래에서 `scripts\`로 줄여 쓴다.
- 케이스마다 새 프로세스와 새 저장 폴더를 쓴다.
- probe 경로를 구분해 보고한다. 키 입력(W 걷기, E, Enter, 방향키, Esc, F9)은 가상 키보드로 실제 입력 경로를 지난다. 초대 창의 `수락 후 시작`은 마우스 클릭 대신 같은 공개 메서드를 부른다. 마우스 클릭 경로는 `play.ps1`로 띄운 뒤 사람이 조작해야 증명된다.

## 증명 보고

- 증거에는 `result.json`(검사 목록과 `steps`), 결과 상태 `final.json`, 스크린샷 하나 이상이 들어간다.
- 보고에는 기능 ID, 실행한 케이스, run-id, 종료 코드를 적는다.
- 도달하지 못한 경로는 시도한 명령과 충족하지 못한 전제를 적는다.

## 기능 파일 형식

각 파일은 H1 제목과 사용자에게 보이는 동작을 설명하는 한 문단으로 시작한다. 그다음 H2 네 개를 이 순서로 둔다: `Sub-features`, `How to get to it (user POV)`, `Driving it with verify-magiccampus scripts`(`Preconditions:`로 시작), `Gotchas`.

## Features

- [초대 이벤트 진행](./invitation-event.md): 걸어가서 E, 수락, 이벤트 장면, 선택지 분기, 보상, 재실행 거절.
- [취소와 조건 불충족](./cancel-and-condition.md): 장면 취소 후 복원, 도입 조건이 거짓인 이벤트 거절.
- [하루 끝(취침·기절), 저장과 불러오기](./save-and-restore.md): 침대 취침, 새벽 2시 기절, 하루 넘김 저장, F9로 새 게임 객체에 다시 읽기.
- [대화창 UI](./dialogue-ui.md): 500자 이상 대사, 해상도 두 가지, 글자 크기 2배, 넘겨 보기.
