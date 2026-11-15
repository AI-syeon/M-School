# 시스템별 현재 실행 명세

## 콘텐츠

Content.Load는 runner-bundle.json과 staging-recipes.json을 읽는다. 런타임은 타입/스키마 컴파일을 하지 않는다. Tests/Core의 Problems가 배포 데이터의 참조·지원 호출·경로·recipe를 사전 확인한다. 새 콘텐츠를 추가하면 이 검사도 확장한다.

## 월드와 이동

Passable은 맵 경계와 blocked 셀을 검사한다. 플레이어는 반경 0.2칸으로 축별 이동한다. FindPath는 포털까지 포함한 4방향 BFS이며 Walk가 속도 예산으로 경로를 따라간다. 이벤트 move는 경로 소진과 destination.radius를 확인한 뒤에만 다음 대사로 넘어간다. NPC 간 충돌·동적 맵 재탐색·timeout·sweep은 없다. 도달 불가 WalkTo는 예외다.

## 상호작용과 약속

interact npc label range가 상호작용 범위를 정한다. 가장 가까운 NPC를 고른 뒤 그 NPC의 Appointments에 남아 있는 이벤트를 표시한다. Accept는 Offered를 Accepted로 바꾸며 재수락은 false다. GameRoot는 수락/시작/수락 후 시작을 제공한다. StartEvent 자체는 예약 존재나 Accepted 여부를 검사하지 않고 진행 이벤트·완료·when을 검사한다. 콘텐츠 작성자는 UI 경로와 Core 진입 가드의 차이를 인지한다.

## 전경 이벤트

Game.Event 하나가 전경 전체를 독점한다. EventRun 생성 시 모든 Actor를 복사한다. 첫 프레임은 Hidden 상태로 recipe.join을 배치하고 반환한다. 다음 프레임에 Hidden=false로 바꾸고 순차 실행한다. say는 확인, choice는 선택한 fork 삽입, move는 도착을 기다린다. 성공 시 set/currency.add → eventId 완료 → 비용 → 약속 제거 순서다.

취소는 Actor 복사본을 되돌리고 대화/Hidden을 닫은 뒤 EndEvent로 NPC의 현시각 일정 경로를 다시 만든다. 약속과 완료 전 상태는 유지한다. 이 복원은 메모리 동작이며 디스크 진입 저장이 아니다. 실행 예외는 GameRoot.Error에 표시되며 자동 CancelEvent/롤백이 보장되지 않는다.

## 시간·일정·정지

평상시 시계와 NPC가 진행한다. 이벤트 중 자연 시간과 평상 일정은 정지한다. 메뉴는 Game.Update 호출을 막지만 일지·초대·수면 확인 패널은 시계를 막지 않는다. Schedule.Run은 (직전분, 현재분] 항목의 경로를 만든다. 로드와 기상은 현재 시각 목적지로 즉시 배치하고 이벤트 종료는 걸어서 복귀한다.

24시에는 종료하지 않는다. 26시가 되면 다음 평상 프레임에서 EndDay(PassedOut)으로 하루를 끝낸다. 침대 수면도 같은 EndDay를 사용한다. 이벤트 중 하루 종료는 거부한다.

## 저장·불러오기

EndDay만 플레이 저장을 쓴다. 완료·수락·취소·게임 종료는 저장하지 않는다. SaveFile은 tmp를 본 파일과 교체하며 이전 파일 하나를 bak로 남긴다. Read는 JSON 파싱 오류 때 bak를 읽지만 스키마/버전/체크섬/내용 불변식을 검증하지 않는다. 권한·디스크 오류는 호출자에게 전파된다. 로드한 Game은 NPC를 일정으로 재구성한다.

## 후속 시스템

방/마법/생활/관계 규칙은 [후속 계약](full-design/02-contracts.md)을 따른다. 현재 없는 명령·상태는 JSON만 추가해 활성화되지 않는다. 구체 Core 구현·Content DTO·GameState 기본값·프레임 연결·사전 검사·UI를 함께 추가해야 한다.
