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
- probe 경로를 구분해 보고한다. 키 입력(W 걷기, E, F, C, Enter, 방향키, Esc, F9)은 가상 키보드로 실제 입력 경로를 지난다. 초대는 E를 누르면 대화창 선택지로 열린다. 초대마다 라벨 한 줄(눌러서 수락하고 바로 시작)과 마지막 "다음에"뿐이고, 약속만 잡기는 J 수첩에 있다. 일반 케이스는 선택지 대신 같은 공개 메서드 `GameRoot.AcceptAndStart`를 부르고, `ui-tour`는 Enter로 선택지를 고른다. 대사는 한 글자씩 나타나므로 probe는 다 나온 뒤 Enter를 누른다. 마우스 클릭 경로는 `play.ps1`로 띄운 뒤 사람이 조작해야 증명된다.

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
- [주문 시전(영창·빠른 시전)](./spellcasting.md): Q·1-4·Enter 영창, 대상 태그 반응, 입력 문맥, 빠른 시전 해금·슬롯·저장.
- [지도와 가방](./map-and-inventory.md): M 전체 지도(플레이어 위치·방향, 가 본 구역), I 가방 24칸·필수품·우편 보관함, 저장.
- [가구 배치](./furniture-arrangement.md): 기숙사 방에서 F로 가구 집기·옮기기(놓을 수 있는 자리 초록/없는 자리 빨강)·놓기·보관·꺼내기, 새 자리의 충돌, 지도 칸 색, 잠자기 저장과 F9 불러오기 뒤 같은 배치(`furniture` 케이스), 화분·책을 책상 윗면·상자 뚜껑에 올리기(층 규칙, `stacking` 케이스).
- [NPC 루틴](./npc-routine.md): NPC가 일과표(`routines.json`)대로 정해진 시각에 걸어가 도착해서 행동(읽기·먹기·필기·이야기) 애니메이션을 재생, 이벤트 뒤 복귀, 취소 시 복원(`routine`, `routine-mira` 케이스).
- 게임 화면 UI 둘러보기(`ui-tour`, 시작 `start-offered.json`): 평소 HUD(날짜·시계·소지금 카드, 의뢰 추적, 1일차 키 안내) → 안뜰 대상 6개의 기본 스프라이트(`01b-targets-default.png`) → 안내판 조준(이름표·태그, 조준 외곽선) → Q 영창 입력(룬 칩, 주문 이름) → 등불 시전으로 첫 의뢰 완료 알림(`05-toast.png`는 반응 효과 도중, `05b-signboard-lit.png`는 켜진 안내판과 은은한 빛) → J·K·I·M 수첩 탭(창이 열린 동안 시계 멈춤 검사) → 서린 앞 E 프롬프트와 초대 선택지(둘이면 `10-`, 하나면 `10b-`) → Enter로 이벤트 시작, 대사(타자 효과 중·완료) → 침대 E 프롬프트, 잠자기 확인 → 하루 결산 카드 → 아침 → 메뉴와 조작법 페이지. 검사 이름은 `tour.*`, 스크린샷은 `01-hud.png`부터 `15-menu-controls.png`까지다.
