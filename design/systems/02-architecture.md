# 전체 구조와 공통 실행 계약

## 경계와 의존성

콘텐츠 JSON → 형식/참조 검증 → 불변 ContentCatalog → 조건/효과/월드 서비스 → 이벤트·일정·상호작용 → 엔진 어댑터(충돌·맵 로드·표시·입력·디스크). 상위 서비스는 엔진 노드 경로나 화면 객체를 저장하지 않는다. 엔진은 화면을 그리고 물리 충돌을 보고하지만 사건 완료 여부를 소유하지 않는다.

| 권위 소유자 | 상태 | 다른 시스템의 접근 |
|---|---|---|
| ContentCatalog | ID별 불변 정의·버전·해시 | 조회만, 플레이 중 교체 금지 |
| WorldState | NPC/플레이어 논리 위치·맵, 문/충돌 버전, 방 상태 | 이동 서비스만 위치 변경, 효과로 월드 플래그 변경 |
| ClockService | 절대 게임분·정지 토큰·미처리 경계 큐 | 예약은 구독, 이벤트는 명시적 비용 요청 |
| StateStore | 타입 선언된 변수·집합·물품·관계·퀘스트·완료 원장 | EffectService의 원자 트랜잭션으로 변경 |
| ControlService | NPC·장소·입력·카메라 임대 | 토큰 소유자만 명령 가능 |
| MoveService | 이동 핸들·경로·재시도·종결 결과 | 일정/이벤트/플레이어가 같은 API 사용 |
| EventRunner | 인스턴스·프로그램 위치·분기·자식 핸들·장면 로컬 | 다른 이벤트는 내부값 접근 금지 |
| Presentation | 현재 대사·선택지·카메라·애니메이션 | 이벤트 호출(핸들)로 완료/취소 통지 |
| SaveService | 마지막 안전 스냅샷·세대·백업 | 일관된 상태만 직렬화 |

## 식별자와 데이터 버전

정의 ID는 `종류.이름` 소문자 ASCII(예: npc.serin, map.courtyard)로 번들 전체에서 유일하다. 목적지는 `dest.*`, 문은 `portal.*`다. 명령에는 ID를 주지 않고 호출 위치(`event.snack#3`, 가지는 `event.snack:notes#1`)로 부른다. 기존 N01 등 원본 표기와 ID 대응은 NPC의 source 필드에 둔다. 번들 schemaVersion은 정수2(1은 이전 객체 형식이며 읽지 않는다), contentVersion은 명시 버전 문자열이다. 저장은 saveVersion·contentVersion·catalogHash를 기록한다. JSON 객체 순서는 의미가 없다. 실행 순서는 호출 칸 안의 `/` 순서가 정하고, 호출 칸의 문법은 [함수 문법](full-design/11-function-syntax.md)를 따른다.

정의 ID는 출시 후 재사용하지 않는다. 이름을 바꿔도 ID는 유지한다. 삭제/이동 시 마이그레이션 테이블로 치환한다. 필수 참조 누락·중복·미등록 함수·잘못된 인자·오타 필드는 번들 전체 로드를 막는다. 개발 중 유효한 이전 번들은 유지하고 오류 번들을 부분 적용하지 않는다.

## 좌표와 이동 계약

`Position={mapId,x,y}`는 타일 단위, 원점은 맵 왼쪽 위 모서리, +x 오른쪽/+y 아래, 실수 허용이다. 타일(x,y)의 중심은 (x+0.5,y+0.5). 정수 타일 인덱스와 렌더 픽셀을 섞지 않는다. 경로 노드는 타일 중심, 충돌은 반경을 포함한 연속 원/캡슐로 어댑터가 sweep한다. footprint를 통과 가능한 공간으로 팽창시킨 경로망을 사용해야 하며 단순 점 A*만으로 충돌을 보증하지 않는다. 예제는 단일 반경0.25·완전 타일 장애물이다.

목적지는 mapId·x·y·radius·facing이며 radius는 도착 허용 거리(예제0.2), 충돌 반경과 다르다. 도착은 같은 맵에서 목표 거리 이내이고 sweep상 정지 가능할 때만 성공한다. 문 통과와 도착은 별도 기록이다. 가까운 다른 칸으로 끝내고 성공 처리하지 않는다. 상호작용 목적지의 사용 대기는 목적지 점유로 처리하고 다음 NPC는 예약 대기한다.

모든 비동기 작업은 `Handle{id,owner,status}`를 즉시 반환한다. 상태는 Pending→Running→Succeeded|Failed|Cancelled이며 종결은 정확히 한 번이다. `Result={status,code,details}`. 동일 cancel을 반복해도 추가 변경이 없다. 완료 콜백은 즉시 재진입하지 않고 다음 처리 단계 큐에 넣는다. 주인은 존재하지 않는 핸들을 기다릴 수 없다. wait timeout은 자식 cancel을 요청한 후 자식 종결을 받고 자신의 실패를 반환한다.

표준 오류: DATA_INVALID, REF_NOT_FOUND, CONDITION_FALSE, RESOURCE_BUSY, NO_ROUTE, TARGET_BLOCKED, STALLED, PORTAL_CLOSED, MAP_LOAD_FAILED, TIMEOUT, CANCELLED, EFFECT_REJECTED, SAVE_INCOMPATIBLE. 취소는 실패와 구분하며 효과는 실행하지 않는다. 실패의 기본 정책은 장면 abort→마지막 안전 상태 복구→초대 유지·원인 표시다. 명시적 branch 이외에는 실패를 성공처럼 계속 진행하지 않는다.

## 틱 처리 순서

권장 고정 시뮬레이션 60Hz. 실제 프레임과 분리한다. 한 틱 안의 순서는 다음과 같다.

1. 입력·전 틱 완료 신호·비동기 맵 로드 결과를 큐에 수집(안정 정렬: tick, 발급 sequence).
2. 정지 정책을 적용하고 ClockService 게임분 누적. 경계 알림은 시간순·ID순으로 큐잉.
3. 대기 장면 후보 조건을 같은 StateStore revision에서 판정하고 자원 전체 임대. 이미 실행 중인 장면을 강제 선점하지 않는다.
4. 일정 재계산으로 이동 의도 생성. 이벤트 임대된 NPC는 보류한다.
5. EventRunner는 인스턴스별 최대64개 즉시 호출 처리, 대기하는 호출(도착 대기 `move`, `await`, `say` 등)에서는 양보. 한도 초과는 다음 틱으로 양보하고 연속600틱 무대기 루프면 DATA_INVALID 중단.
6. MoveService 경로 요청 처리·sweep 이동·문 전환·도착. 위치는 이 단계에서만 갱신.
7. 효과 트랜잭션을 발급 sequence순 검증/커밋. 충돌한 revision은 조건 재검증 후 실패 또는 재평가하며 임의 덮어쓰기 금지.
8. 종결 알림 큐잉, 해제 자원/오버레이 정리, 안전 저장 요청 스냅샷, 표시 갱신. 이번 틱 발생 신호는 다음 틱에 소비.

효과 변경이 이번 틱 앞 단계의 조건을 소급해서 바꾸지 않는다. 조건을 판정한 revision과 효과 커밋 revision을 로그에 남긴다. 동시 경합이 게임 프레임 속도에 따라 바뀌지 않게 안정 정렬한다.

## 시간과 정지

ClockService 권위 값은 `absoluteMinute>=0`. 첫날07:00=420. minuteOfDay=absoluteMinute%1440, absoluteDay=floor(/1440), 연도=floor(day/80)+1, 계절=floor((day%80)/20), 학기=floor((day%80)/40), 5일 주기=day%5. 부동 누적은 시뮬초 잔여량으로 보관하고 1초당1게임분을 기본 비율로 한다. 저장에는 정수 absoluteMinute와 0≤fraction<1만 기록한다.

정지 토큰은 세 채널로 나눈다: calendar(게임 시계), ambient(NPC 일정 이동·일반 AI), player(플레이어 조작). 대화는 calendar+player, 정식 수업/관계 장면은 calendar+ambient+player, 메뉴/전투 주문 고리는 세 채널+worldSimulation을 멈춘다. worldSimulation 중에도 UI와 취소/로드는 반응한다. 이벤트 소유 move·camera·animation은 calendar/ambient 정지 중 계속 시뮬레이션된다. 수업 장면 속 NPC도 event 소유 이동이면 허용한다. 대화 중 장면 이동을 기다려도 시계 정지와 교착하지 않는다.

실시간 wait는 pause 가능한 simulationSeconds이고 calendar와 독립이다. gameMinutes wait는 calendar에 의존하므로 calendar 정지 장면에서는 로드 오류로 금지한다(v1 `wait`는 simulationSeconds만 지원). costMinutes는 화면에 사전 표시하고 **성공 커밋(effects 적용) 때 한 번** 가산한다. 실패·취소 시 미가산. 가산으로 건너뛴 수업/행사는 경계별로 평가해 지난 회차/복습·재예약으로 표시하며 놓친 NPC 경로를 전부 재생하지 않는다.

## 확장 경계

이 프레임워크의 재사용 단위는 ID/카탈로그, 조건/효과, 맵/이동, 시계/예약, 명령/임대, 저장/진단이다. 게임별 adapter와 상태 모듈(전투·재배 등)은 교체한다. 전투 알고리즘, 모든 미니게임, 렌더러, 편집기, 에셋 파이프라인을 범용 엔진으로 새로 만드는 범위가 아니다. 각 함수는 지원 버전·인자 형식·핸들 결과·취소·저장 정책을 함수 사전에 등록해야 새 JSON에서 사용할 수 있다.
