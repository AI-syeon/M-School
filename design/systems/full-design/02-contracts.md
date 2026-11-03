# 전체 도메인을 묶는 실행 계약

## 책임과 권위 상태

콘텐츠 정의 → 검증 → 불변 카탈로그 → 도메인 서비스 → 원자 효과 → 상태 저장소 → Unity 표시 순서다. 다른 서비스는 소유 상태를 직접 쓰지 않는다. Unity Transform·씬 객체 ID·대사창은 저장의 권위가 아니다.

| 소유자 | 소유 상태 | 입력 → 출력 | 저장·경계 |
|---|---|---|---|
| Catalog | ID 정의, 참조, 버전·해시 | UTF-8 JSON → 전부 유효한 불변 목록 | 정의 대신 버전·해시 저장, 오타·중복·참조오류는 번들 전체 거부 |
| World/Movement | 논리 mapId·타일 위치·문·navRevision | Move(actor,destination,token) → 단일종결 Handle | 경로 미저장, 현재 안전 위치만 저장 |
| Clock/Schedule | 절대분·잔여분·일경계 원장·일정 의도 | Tick/Cost/Reserve → 경계 알림·현재 목적지 | 누락 경계 시간순 처리, 경로 대신 현재 일정 재계산 |
| Scene/Control | 장면 큐·임대·명령 위치·임시 상태 | TryStart/Cancel → 완료·실패·초대 유지 | PC·UI·토큰은 미저장, 진입/완료 안전 상태 |
| Ability/Combat | 집중력·HP·적AI·공격권·방 overlay | Cast/Dodge/Encounter → 반응·피해·교전결과 | 현재 방의 미확정 상태는 입장 상태로 복원 |
| Inventory/Economy | 스택·지갑·재고·지급/대여 출처 | Quote/Trade/Consume/Give → 원자 거래 또는 이유 | 비용/소비/산출/우편/원장 함께 커밋 |
| Life | 화분·돌봄·설치·배치 | Water/Grow/Craft/Place → 성장/산출/투영 | 일작업·설치키 영속, preview 미저장 |
| Relationship/Story | 경험집합·관계·증거·노드완료 | Record/Accept/Resolve → 초대·해금·후일담 | 공공정보는 친밀도와 분리, 중복키 영속 |
| Journal/Presentation | 조회모형·읽음·설정 | 상태조회 → 가능한 행동·UI·피드백 | UI 캐시는 미저장, 힌트/읽음/설정은 영속 |

모든 API는 `{status,code,details,revision}` 또는 비동기 Handle을 돌려준다. 성공·실패·취소 종결은 한 번이다. 상태를 바꾸는 입력에는 inputSequence와 transactionId를 부여하며 중복 입력을 무시한다. CONTENT_INVALID, CONDITION_FALSE, RESOURCE_BUSY, NO_ROUTE, TARGET_BLOCKED, MAP_LOAD_FAILED, TIMEOUT, CANCELLED, INSUFFICIENT_ITEMS, INSUFFICIENT_FUNDS, OUT_OF_SCOPE, SAVE_INCOMPATIBLE를 공통 오류로 쓴다. 오류에는 파일/JSON Pointer/대상 ID/기대·실제값/대체 행동을 포함한다.

## 한 틱의 순서와 동시성

1. 입력·비동기 완료를 tick/sequence로 수집한다.
2. 정지 채널을 적용하고 게임분·경계 큐를 갱신한다. 자정 도달 시 새 교전을 금지한다.
3. 후보 조건을 같은 revision에서 평가하고 필요한 NPC·장소·입력·카메라를 한 번에 임대한다.
4. 일정 의도를 현시각으로 계산한다. 장면에 잡힌 NPC는 이동 요청을 보류한다.
5. 이벤트는 즉시명령 최대64개 후 양보한다. 이동·연출·도메인 작업의 실제 성공을 기다린다.
6. 이동 sweep·충돌·적 예고/공격·반응을 처리한다. 조준/명령 입력은 재검증한다.
7. 효과를 sequence순으로 재검증해 원자 커밋한다. 이전 revision과 충돌하면 재평가하고, 덮어쓰지 않는다.
8. 토큰·overlay 정리 → 안전 스냅샷 → 표시 갱신. 완료 통지는 다음 틱에 소비한다.

해결400 > 관계300 > 행사200 > 일반100 > 일정10은 대기 후보의 순서다. 이미 진행 중인 장면을 끊지 않는다. 같은 순위는 예약시각·sequence·ID순이다. 전체 자원을 확보할 수 없으면 하나도 점유하지 않고 초대를 남긴다. 일정 이동은 취소·양도할 수 있으나 다른 전경 장면은 강제 선점하지 않는다. 병렬 연출은 한 명령목록 안의 `async=핸들`과 `await 핸들…`로만 쓴다. await는 join=all이며 한 핸들이 실패하면 나머지를 취소한 뒤 이벤트를 실패시킨다.

## 장면과 방의 저장 경계

**서사 장면**은 snapshot.world를 잡고 진입 스냅샷을 만든다. 중간의 선택·소품·이동은 장면 로컬이다. 성공 시 효과·시간비용·완료·보상원장을 한 커밋으로 기록하고 토큰을 회수한다. 취소/종료는 진입부터 재시작한다. 장면 중 다른 구매·성장·보상 커밋은 큐에 보관한다. 장면의 perform(craft)는 결과 미리보기/재료 예약이며 커밋 전 영속 소비가 없다.

**실시간 방 활동**은 장면 전체를 전역 롤백하지 않는다. 방 입장 체크포인트에 적·퍼즐·임시 전리품 overlay를 둔다. 확정 증거/열린 문/보상은 짧은 전역 트랜잭션으로 확정하고 최신 안전 스냅샷을 만든다. 방 reset은 overlay와 미확정 전리품만 버린다. 따라서 방에서 얻은 증거 때문에 인벤토리 구매·식물 일작업이 취소되는 일이 없다. `perform encounter …`와 `perform puzzle …`은 이 방 작업을 기다린다. 이벤트의 `effects` 칸이 결과 확인과 노드 보상을 한 번 확정하며 교전 중 물리상태 자체를 직렬화하지 않는다.

비장면의 물주기·선물·구매·일괄 제작도 각각 짧은 원자 트랜잭션이다. 전경 장면 중에는 실행을 보류한다. 전경 장면이 아닌 실시간 탐험의 물주기·시간 경과는 계속될 수 있다. 저장 버튼은 항상 가장 최근의 안전 스냅샷을 쓴다. UI는 “현재 장면은 처음부터 / 현재 방은 입장부터”를 구분해 안내한다.

완료키는 콘텐츠 ID가 아니라 **의미 있는 보상의 공유키**다. A3 전투와 장치의 완료키는 둘 다 node.a3다. event.a3_combat/event.a3_device 두 이벤트 ID가 달라도 같은 노드 보상을 두 번 주지 않는다. once 이벤트 원장과 도메인 보상 원장을 함께 검사한다. 반복 연습은 고유완료를 다시 세지 않고, 별도 정해진 반복보상 정책만 허용한다.

## 시계·자정·달력

절대분은 0부터의 정수, 첫날07:00=420, fraction은 [0,1)이다. 1시뮬초=1게임분. day=floor(minute/1440), year=floor(day/80)+1, cycleDay=day%80, season=floor(cycleDay/20), term=floor(cycleDay/40), weekday=day%5. 평일 패턴은 정규수업3/공동실습1/자유1 제안이며 별도 공휴일 콘텐츠는 미정이다.

| 상황 | 달력 | 평상 NPC/AI | 플레이어 | 장면 소유 연출 | 실시간 교전 |
|---|---|---|---|---|---|
| 자유 이동 | 진행 | 진행 | 허용 | 해당 없음 | 진행 |
| 일반 대화 | 정지 | 안전한 평상 이동만 | 잠금 | 진행 | 전투중 대화 시작 불가 |
| 수업·관계 장면 | 정지 | 정지 | 장면 조작만 | 진행 | 시작 불가 |
| 메뉴·수첩·주문 고리 | 정지 | 정지 | UI만 | 정지 | 정지 |
| 퍼즐 배치 | 정지 | 해당 방 정지 | 배치만 | 배치조작 진행 | 정지 |

비용 timeCostMinutes는 시작 전에 보여주고 성공 시 한 번 가산한다. 원본의 현실 평균7분·9분·25분과 게임분 비용을 혼동하지 않는다. 일반 장면 시험값은10게임분, 수업은 정의의60게임분만 한 번 적용하는 제안이다. 취소 시 시간비용을 적용하지 않는다. 가산으로 건너뛴 경계는 순서대로 처리하고 NPC는 현시각 일정 하나만 계산한다.

자정에는 returnPending=true, 새 encounter 시작을 막는다. 메뉴에서 자정이 생기지는 않지만 완료 비용으로 자정을 넘을 수 있다. 교전 중에는 즉시 안전 귀환 또는 현재 교전 종료 후 귀환을 선택한다. 전투를 더 생성하지 않고 현재 교전도 중도 포기 가능하다. 장면은 취소/성공의 원자 경계 후 귀환한다. 귀환 시 현 방 overlay 폐기·확정 증거 보존·다음날07:00 이동·이전날 물주기 성장 정산·새날 원장을 한 번 처리한다. 여러날 비용도 날짜별 같은 원장을 사용한다.

## 명령·조건·효과 확장 사전

작성 형식은 [11-function-syntax.md](#spec-syntax)를 따른다. 한 이벤트는 한 Row이고, 행동 하나는 `함수 인자…` 호출 하나다. 함수 이름·인자·참조 종류·기본값·소유 서비스는 `schemas/functions.json` 한 곳에 있고, 표 전체는 08-field-dictionary.md의 functions 절에 생성된다. 로더는 문자열을 기존과 같은 타입 객체로 바꾸므로 아래 실행·실패·취소·임대 계약은 그대로다.

| 칸 | 호출 예 | 계약 |
|---|---|---|
| trigger | `interact npc.serin "간식 모임 시작" 1.5` / `clock` / `manual` | 이전 interactions 표 대체 |
| when | `completed node.a2 / !completed outcome.a_stopped` | `/`=AND, `!`=부정, `any "…" "…"`=OR. 빈 칸=참 |
| script | `move npc.serin dest.snack / say npc.serin "…" / choice "…" notes "…" tea "…"` | 순서대로 실행. 명령 ID 없음. 위치로 부름(`event.snack#3`) |
| forks | `{"notes":"say npc.serin \"…\""}` | choice·branch가 부르는 같은 Row의 하위 목록(`event.snack:notes#1`) |
| effects | `evidence.add evidence.a3 / complete node.a3 / case.reward case.a` | script 성공 뒤 한 번 원자 커밋. 이전 마지막 commit 명령 |

명령: `move`는 기본으로 도착까지 기다린다(이전 move+await). `async=핸들`을 주면 바로 다음 명령으로 가고, 같은 목록 안에서 `await 핸들…`로 닫는다. 열린 핸들이 있으면 say·choice·branch·perform·목록 끝을 거부한다. 핸들은 가지로 넘기지 않고, 같은 행위자·카메라를 동시에 쓰는 async 두 개는 오류다. `say`, `choice`, `branch`, `wait`, `camera`, `animate`의 실패·취소·임대 계약은 이전과 같다.

`perform <도메인> <정의> [mode=normal|practice]`는 해당 도메인 작업을 시작하고 **그 작업의 성공까지 기다린다**. 결과는 슬롯에 담지 않고 이벤트당 perform 결과로 고정되며 effects 커밋이 읽는다. 실패·취소는 이벤트 abort로 전달한다. 도메인은 class/encounter/puzzle/craft/relationship/project/activity의 닫힌 열거이고, 정의의 참조 종류가 도메인별로 정해진다. puzzle은 방 전체(`room.case_a1`) 또는 방 목표 하나(`room.case_a3:device`)를 받는다. executionMode=scene은 전경 장면, room-activity는 전역 스냅샷 임대 없이 방 컨트롤러로 전달한다. 각 모드의 커밋 경계는 10-review-resolution.md를 따른다. 특정 A3나 NPC 이름을 명령으로 추가하지 않는다.

조건 함수는 eq/gte/completed/discovered/evidence/learned/caseCountGte/any다. 이전 all은 `/`, not은 `!`로 쓴다. 빈 목록=참, `any`의 대안은 2개 이상이다. 없는 정의는 데이터 오류다. completed는 도메인 완료집합을, discovered는 사건 발견집합을, evidence는 증거집합을 읽는다. gte는 선언 int만, eq·set은 선언 타입과 일치한다. 최종 조건은 A–F의 해결수6만 확인한다.

| 효과 호출 | 의미·실패·저장 |
|---|---|
| `set 변수 값` | 선언 bool/int만, 읽기전용 시계 쓰기 금지 |
| `complete ID` | 고유 완료집합 추가, 이미 있으면 보상도 NO_OP |
| `evidence.add ID` | 수락 전 수집 가능, 영속집합 중복 제거 |
| `item.grant 아이템 수량 출처(normal 또는 grant) [quality=standard]` | 가방초과 우편, loan은 대여 서비스 전용 |
| `experience.record NPC 활동 유형 [participationOnly=false]` | 고유 활동 기록, 첫 유형 점수만, 참여 NPC별 적용 |
| `project.install 프로젝트` | 실제 작업 성공·소비계획 확인 후 영구 투영키 추가 |
| `spell.learn 주문` | 학습조건 확인 후 영구 주문집합 추가 |
| `currency.add 크라운` | 양의 정수, 공유 completionKey 원장과 같은 커밋에서만 |
| `case.reward 사건` | 사건 정의의 rewardCrowns 지급. 금액 리터럴 없음, 같은 effects에 3번째 노드 complete 필수 |

효과는 신뢰할 수 있는 호출이라도 도메인의 전제조건을 재검증한다. effects에 complete만 넣어 수업을 건너뛰게 하지 않는다. perform 결과·참여자·준비된 재료 예약과 일치해야 한다. 정상례는 runner-bundle.json, 부정례는 validate.cjs다. 새 함수를 추가하려면 함수 사전·C# 구현·정상례·부정례를 함께 추가한다.

## 저장 파일·버전·Unity 경계

SaveService는 틱 경계 불변복사 → 임시파일 쓰기 → 체크섬 확인 → 이전 정상본 보존 → 교체 순서다. 디스크 실패 시 메모리 완료는 유지하되 “미저장” 표시와 재시도를 제공한다. 종료 후 이전 정상본은 모든 상태가 함께 되돌아가므로 보상만 남지 않는다. 로드는 체크섬·버전·카탈로그해시·마이그레이션·스키마·참조·불변식 순으로 검사한 뒤 전체 교체한다. 예제 payload의 체크섬 envelope와 실제 파일 교체는 구현 단계에서 작성한다.

v1 기반 저장 → v2 전체 저장은 기존 변수·완료·위치를 유지하고 새 도메인 상태를 초기화하는 명시적 migration이 필요하다. 카탈로그 ID 치환표와 보상키 변환도 필요하며 예제 v2를 v1에 억지로 로드하지 않는다. 실제 마이그레이션 코드는 미구현이다.

순수 C# 도메인은 Unity 참조 없이 정의/상태/판정/전이를 소유한다. Unity의 InputAdapter, CollisionAdapter, MapLoader, WorldView, DialogueView, CameraAdapter, AudioAdapter, AssetResolver, FileStore는 표시·입력·물리 관측·파일을 맡는다. 맵/프리팹 주소는 asset binding이다. 바인딩은 따로 작성하지 않고 assetKey 칸에서 생성하며 주소 기본값은 `PLACEHOLDER/<key>`, 실제 주소만 카탈로그 `assetAddresses`에 쓴다. 미등록 필수 에셋은 시작 오류, 개발용 placeholder 허용은 명시 설정이다. Unity 버전·Input/Tilemap/저장 패키지 API는 채택 전이다. 설치된 편집기 흔적을 프로젝트 버전 확정으로 취급하지 않는다.
