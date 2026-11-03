# 시스템별 실행 명세

인터페이스 표기는 C# 구현을 위한 논리 서명이며 실행 코드가 아니다. 모든 비동기 Result/Handle·오류·틱 순서는 [공통 계약](02-architecture.md)을 따른다. Unity 컴포넌트가 여러 곳에서 같은 상태를 변경하지 않게 한다.

## 1. 카탈로그와 검증

입력: UTF-8 JSON 번들, schemaVersion, 함수 사전(등록된 시작 방법·조건·명령·효과 함수). 출력: 불변 Catalog 또는 파일/JSON Pointer/오류 코드 목록. 상태: Unloaded→Parsing→Validating→Ready|Rejected. 처리: JSON 파싱→Schema→호출 문자열 파싱→함수 사전 대조(이름·인자 수·타입)→중복 ID→타입/참조→좌표·문 그래프→가지·핸들 제어흐름→효과 쓰기 충돌→자원 선언 검사→해시 생성. 기본값은 성공한 로드 후에만 정규화한다. JSON Schema의 default는 자동 삽입 기능이 아니며 별도 normalizer가 넣는다.

`LoadBundle(bytes):ValidationResult<Catalog>`, `Resolve<T>(id):T`, `ExplainErrors():Diagnostic[]`. 진단에는 호출 위치(`call`, 예 event.snack#3)와 칸 안 문자 위치(`offset`)가 들어간다. 작성 형식은 [함수 문법](full-design/11-function-syntax.md). 로드 실패 시 기존 Catalog 유지. 실행 중 핫리로드 금지; 시험 모드에서 모든 이벤트 중단·임대 해제·초기 상태로 돌아간 뒤 재로드한다. Catalog는 저장하지 않고 버전·해시만 저장한다.

## 2. 월드·맵·목적지

입력: MapDefinition(크기·장애물·문), named destination, 상태 효과. 출력: WalkabilitySnapshot(revision), 위치, 활성 문. 상태: Unloaded→Loading→Active→Unloading; 로드 실패는 원본 맵 Active 유지. 현재 맵, 목적지 맵, 전환에 필요한 최소 맵의 논리 데이터는 유지한다. 화면 비활성 맵도 NPC 논리 위치를 소유한다. 오브젝트의 Unity 인스턴스 생성/파괴로 영속 상태를 유실하지 않는다.

`GetPosition(actorId)`, `GetDestination(id)`, `GetNavSnapshot(mapId)`, `SetDoorState(id,open,revision)`. 문은 방향성 연결이며 역방향은 별도 문 ID다. 전환은 출발 문 예약→도착 점유 검증→대상 로드→출발 맵에서 제거·목표맵 위치 등록을 한 번에 적용→전환 완료 순서. 로드 실패/도착 막힘이면 출발 문 앞 위치에 남고 실패. 양쪽 맵에 동시에 존재하지 않는다. 전환 중 save는 마지막 안전 저장을 사용한다.

임시 퍼즐 배치·전리품은 room overlay, 영구 열린 문은 StateStore 플래그에 따른 projection이다. 방 초기화는 overlay만 폐기하고 확정 증거·보상 원장을 되돌리지 않는다. 맵 재진입 시 기본 배치→영구 projection→유효 overlay 순서. 카메라 좌표·스프라이트는 영속 상태가 아니다.

## 3. 경로와 이동

입력: actorId, destinationId, ownerToken, timeoutSeconds(기본30), allowPortals(기본true). 출력: MoveHandle와 결과(최종 위치·경유문·재탐색 횟수). 상태: Queued→Planning→Moving↔Replanning→Transitioning→Moving→Arrived; 어느 비종결 상태에서도 Failed/Cancelled 가능.

`RequestMove(MoveRequest):Handle`, `Cancel(handle,reason):Result`, `Query(handle):MoveStatus`. 이벤트 move·NPC 일정·플레이어 자동 이동은 이 요청을 사용한다. 수동 플레이어 이동도 같은 sweep/충돌 실행기를 쓰되 경로 생성만 생략한다. 같은 NPC에 새 요청을 덮어쓰지 말고 먼저 임대 검사한다.

처리: 목적지/제어 토큰 검사→맵 간 방향 문 그래프 Dijkstra(문 비용+내부 예상거리)→각 구간 A*→sweep 이동→목표 도착 확인. 8방향 비용은 직선1/대각√2, 휴리스틱 octile. 동률은 y,x,문ID로 정렬. 대각선 이동은 양 직교 인접칸 모두 통과 가능해야 한다. 논리 맵 간 경로가 있어도 문까지 실제 경로가 없으면 해당 간선을 이번 요청에서 제외하고 다시 탐색한다. 무효 구간을 통과한 것으로 간주하지 않는다.

정적 충돌 변경은 navRevision을 올리고 다음 이동 전에 남은 경로를 검증한다. NPC는 동적 장애물로 sweep/점유 예약한다. 먼저 발급된 요청이 좁은 칸을 예약하고 후발은 대기. 무진행1시뮬초 후 재경로, 요청당 최대3회, 전체 timeout은 계획·대기·맵 로드 포함 시뮬시간(하드 메뉴 정지 제외). 갈 곳 자체가 없으면 NO_ROUTE, 목표 막힘은 TARGET_BLOCKED, 동적 정체 초과는 STALLED. 실패 시 마지막 유효 위치에서 정지한다. 취소는 문 전환 원자 구간 앞/뒤 경계에서 처리하고 반쯤 전환된 위치를 남기지 않는다.

경로·핸들은 임시 상태로 저장하지 않는다. 안전 복원 뒤 일정은 현재 시각에서 재요청, 장면은 진입부터 다시 시작한다. 정상 이동 실패에 자동 순간이동을 넣지 않는다. 예약 장면의 안전 합류만 별도 절차를 사용한다.

## 4. 시간·달력·예약·NPC 일정

입력: simulationDelta, pause tokens, costMinutes, 일정 script. 출력: MinuteCrossed/DayCrossed/AppointmentReady, NPC intent. `Advance(delta)`, `AcquirePause(channels,owner)`, `ReleasePause(token)`, `ScheduleAppointment(eventId,acceptedAt)`, `RecomputeSchedule(actorId,reason)`.

일정은 NPC별 `script` 칸의 `시작분 목적지` 항목이다(예 `420 dest.snack / 1080 home`). 끝은 다음 항목의 시작(마지막은 1440)이며 구간은 반열림 [start,end), 시작분은 0–1439로 증가해야 한다. 예제는 매일 반복, 확장 필터는 weekday/weather/condition으로 추가 명세 후 지원. 같은 NPC의 일정 중복·시작분 역순은 로드 오류. 빈 구간은 home 목적지. 현재 분에 맞는 행 하나를 선택→기존 동일 목표 이동은 유지→다른 목표면 일정 이동 취소·공통 이동 재요청. 다음 일정 경계가 지나면 이전 목적지를 끝까지 방문하지 않고 현재 목표로 갱신한다. event 임대 중에는 일정 의도만 최신으로 갱신한다. 해제 시 현 위치에서 현재 목표로 간다.

예약 상태: Offered→Accepted→Ready→Running→Completed; 취소/실패는 Accepted로 돌아가며 초대 유지. 관계/행사 예약은 날짜 마감 없음. 플레이어가 수락하고 진입 조건을 만족하면 자원 임대 후 fade 또는 비관찰 공간에서 NPC를 준비 지점에 합류시키고 장면 이동을 시작한다. 눈앞의 NPC를 이동시키는 이벤트는 합류 보정 없이 현재 위치부터 이동한다. 두 방식은 콘텐츠의 staging 필드로 명시한다. 수업 종료·대량 시간 가산·로드 직후는 현재 시각 한 번으로 일정을 재계산한다. 하루 경계 작업은 (jobId,absoluteDay) 원장으로 한 번씩 처리한다.

저장: 절대 시각·fraction·Accepted 예약·일별 처리 원장·NPC 위치. 정지 토큰·현재 일정 경로는 미저장. load는 새 토큰만 만들고 과거 pause 토큰을 복원하지 않는다.

## 5. 조건과 효과

입력: 타입 선언 변수(예제 bool/int), 순수 Condition AST(조건 호출 문자열을 로더가 변환; `/`로 이은 목록은 all, `!`는 not), snapshot revision. 출력: bool + 이유 트리. v1 호출 `eq`, `gte`, `any`와 `/`(AND)·`!`(NOT); 빈 목록은 true. `Evaluate(condition,snapshot):ConditionResult`는 무작위·현재 화면 객체·쓰기 작업을 호출하지 않는다. 없는 변수는 false 취급 대신 REF_NOT_FOUND. eq는 선언 타입과 일치, gte는 int만. 모든 진단에 expected/actual을 남긴다.

`ApplyEffects(effects,transactionId,expectedRevision):CommitResult`. 예제 v1 효과는 `set`만, int/bool 타입 검사. 모두 먼저 검증하고 하나라도 불가하면 전체 미적용. 같은 트랜잭션에서 같은 변수 중복 쓰기 금지. event의 effects 커밋에서 효과, event 완료 키, costMinutes, 일회 보상 원장을 한 트랜잭션으로 반영한다. 이미 완료된 일회 키면 ALREADY_COMMITTED 결과로 새 효과·시간 가산 없이 성공 확인한다. 일반 이벤트 재시도와 반복 콘텐츠의 키는 다르다: once는 eventId, daily는 eventId+absoluteDay(확장), repeat는 발급된 영속 회차 ID(확장). 예제는 once만 지원한다.

조건은 행사·관계·퀘스트·상호작용·대화에서 같은 evaluator를 재사용한다. 향후 item.add, relationship.recordExperience, quest.advance 등은 타입·전제·실패·중복 키·저장 계약을 등록한 후 사용한다. 임의의 경로 문자열로 객체를 수정하는 효과나 eval은 허용하지 않는다.

## 6. 상호작용과 가능한 행동

입력: 플레이어 위치·대상·조건 snapshot·입력 수단. 출력: `ActionView{id,label,enabled,reason,eventId}` 배열. `QueryActions(actor,target):ActionView[]`, `ExecuteAction(actionId,revision):Handle`. 대상 거리(타일 단위·기본1.5)·같은 맵·시선/차폐·자원 임대·이벤트 조건을 순서대로 확인. disabled도 이유와 대체 경로를 보여준다. 화면의 enabled 캐시를 신뢰하지 않고 클릭 때 재검증한다. 같은 입력 sequence는 한 번만 실행한다.

처리 상태 Idle→Focused→Requested→Running→Idle. 이벤트가 취소되면 포커스만 돌리고 카메라·입력·NPC 토큰을 회수한다. 일상 대화 중 NPC 일정 경계가 지나도 진행 중 대화는 유지하고 종료 후 재계산한다. 대화 후보는 원본 우선순위, specificity, 안정 ID순으로 선정하며 최근 동일 대사를 피하고 대안이 없으면 기본 인사 허용. 후보와 제외 이유를 기록한다. 예제 schema는 이벤트 연결 행동만 지원하며 가중 대화 선별은 M3 확장 계약이다.

## 7. 이벤트 실행·동시성·중단

입력: eventId·trigger context. 출력: EventHandle·trace·CommitResult. 상태: Eligible→Queued→Acquiring→Staging→Running→Committing→Completed. 조건 불충족은 시작 안 함(reason tree); 실행 중에는 명시 branch에서만 조건 재평가. 취소/명령 실패→Aborting→자식 cancel/종결 확인→오버레이·UI 정리→토큰 해제→진입 안전 상태 복원→Accepted 재등록. commit 성공 후 cancel은 TOO_LATE로 완료를 유지한다.

`TryStart(eventId,context):StartResult`, `CancelEvent(instanceId,reason)`, `ExplainEligibility(eventId):ReasonTree`. scene 자원은 `npc.*`, `space.map.*`, `input.player`, `camera.main`으로 명시. 모든 쓰기 자원을 시작 전에 원자 임대하며 실패하면 아무것도 잡지 않고 대기한다. 공통 우선순위: 해결 연출400 > 관계300 > 행사200 > 일반 상호작용100 > 일정10. 같은 우선순위는 예약 시각·sequence·eventId순. 실행 중 장면은 강제 선점하지 않는다. 생존/자정 귀환은 새 장면이 아니라 안전 중단 요청이며 현재 원자 커밋/전환 종료 뒤 처리한다. 대기30시뮬초마다 RESOURCE_BUSY 진단, 초대 자체 만료 없음. 장소가 겹치지 않아도 대화창 하나·카메라 하나면 동시 임대 불가.

일정은 장면과 달리 선점 가능하다. 높은 우선 장면이 NPC를 요구하면 ControlService는 해당 일정 move를 취소 요청하고 종결 확인 뒤 일정 임대를 해제한다. 장면은 기다리는 동안 다른 자원을 부분 점유하지 않는다. 그 뒤 전체 자원을 다시 원자 획득한다. 실행 중 장면 간 선점 금지 규칙 때문에 평상 일정이 새 장면 시작을 영원히 막지 않도록 구분한다.

v1은 effects를 커밋하는 모든 이벤트(v1 이벤트 전부)에 **암묵적 전역 안전 저장 임대(snapshot.world)**를 추가한다(JSON resources에 작성하지 않음). 따라서 장소가 달라도 영속 효과를 가진 전경 장면은 하나만 Running이다. 동시 후보는 큐에서 보존한다. 병렬은 그 장면 안의 `async=`+`await` 연출과 상태를 쓰지 않는 순수 배경 표시만 허용한다. 방 일별 처리·별도 보상 등 외부 영속 효과는 장면 동안 큐에 두고 완료/복원 후 처리한다. 전체 진입 스냅샷을 복원하면서 다른 이벤트의 완료를 지우는 문제를 막는 제한이다. 향후 독립 영속 이벤트 동시 실행이 필요하면 전체 롤백을 범위별 트랜잭션으로 바꾸고 별도 버전으로 설계해야 한다.

순차 호출은 앞 호출 성공 후 다음 실행. `move`는 기본으로 도착까지 대기한다. `async=핸들`을 주면 작업을 시작해 핸들을 남기고 바로 다음 호출로 가며, `await`가 성공까지 대기한다. 닫히지 않은 핸들·없는 핸들·중복 이름은 로드 오류. 실패/취소 결과는 기본 abort로 전파한다. branch는 순수 조건으로 고른 가지(forks)만 실행. 연이은 `async=` 호출은 같은 snapshot에서 시작하고 `await`가 전부 성공해야 진행(join=all). 한 호출 실패/취소면 나머지 cancel→종결 후 이벤트 실패. v1 join=any와 분리 실행(await 없이 끝내는 async)은 미지원. 동일 actor·카메라를 쓰는 async 충돌은 로드 오류(HANDLE_CONFLICT). 핸들은 가지로 넘기지 않으며 가지 호출은 부모 자원 부분집합만 쓰므로 교착을 막는다.

커밋은 `effects` 칸이 정확히 한 번 한다(script 성공 뒤). 장면 중 영속 효과 금지, 선택 결과는 local(예제 선택별 가지)로 분기하고 마지막 효과를 effects에 모은다. 효과 함수는 명령 사전에 없으므로 가지 안에 효과를 숨기거나 병렬 커밋을 만들 수 없다. checkpoint는 장면 진입·완료만 v1 지원. 중간 PC·대사 글자·경로 재개는 지원하지 않는다. 스킵은 UI/애니메이션만 가속하고 이동 성공·선택·효과를 생략하지 않는다.

## 8. 대화·선택·카메라·애니메이션·입력

입력은 호출 `say 화자 "대사"`, `choice "질문" 가지 "선택문"…`, `camera 목적지 초`, `animate 행위자 클립 초`. 출력은 각 Handle의 완료·실패·취소. 대사는 마지막 글자 표시 후 확인 입력으로 완료; 글자 빨리 표시는 완료와 구분. choice는 enabled 옵션 최소1개, 선택은 한번 확정 후 해당 가지(forks)의 호출목록 순차 실행. 취소 버튼은 전체 event cancel(원본 상태 복원), 뒤로가기 중 보상 지급 없음.

카메라는 논리 목적지를 엔진 좌표로 변환하고 제한 영역 안으로 clamp. 애니메이션 clip은 adapter 등록 키며 미등록이면 로드 실패, 종료 신호 누락이면 duration+허용오차 후 TIMEOUT. duration은 데이터 양수. 완료/실패/중단 모든 경로에서 이벤트가 잡은 입력·카메라 토큰을 finally로 회수한다. 이전 카메라 모드와 입력 map을 저장한 임대 스택으로 되돌리고, 다른 이벤트 토큰을 해제하지 않는다. Presentation 상태는 안전 저장에 포함하지 않는다. 예제는 text만 있고 실제 폰트/초상/애니메이션 파일을 제공하지 않는다.

## 9. 저장·복원·버전

`RequestSave(slot):SaveResult`, `Load(slot):LoadResult`, `Migrate(snapshot,target):MigrationResult`. 상태 Idle→Snapshotting→WritingTemp→Verifying→Replacing→Idle; 실패면 Failed와 이전 정상 파일 유지. Snapshot은 틱 경계의 불변 복사. 동시 요청은 직렬화한다. 저장 파일에는 저장 버전·콘텐츠 버전/해시·세대·시계·위치·선언 상태·완료 키·예약·방 체크포인트가 들어간다. 체크섬은 파일 envelope에서 canonical payload 바이트에 적용하며 예제는 envelope 전 payload다.

진입 절차: 조건/자원 확인·전체 임대→NPC 합류 이전 월드와 예약 포함 안전 스냅샷 확정→scene overlay 생성→staging. snapshot.world는 완료 후 불변 스냅샷 확보 또는 abort 복원까지 유지한다. 취소 정리·복원은 하나의 틱 경계 작업으로 수행하고 그 사이 새 이벤트/일정을 실행하지 않는다. 중간 수동 저장 요청은 마지막 안전 스냅샷을 해당 슬롯에 쓰고 “현재 장면은 시작부터 다시 진행”을 안내한다. 종료 후 로드는 그 스냅샷에서 Accepted 예약을 복원한다. 자동 시작 중복을 막고 플레이어가 다시 시작하게 한다.

완료 절차: 효과·완료·시각을 메모리에서 원자 반영→오버레이와 토큰 정리→완료 후 안전 스냅샷 쓰기→성공 안내. 디스크 실패면 “완료·미저장” 상태로 재시도 UI를 제공하며 이전 정상 파일을 보존한다. 이때 세션 내 원장이 중복 커밋을 막는다. 강제 종료로 이전 저장을 불러오면 완료 전 상태 전체를 복원하므로 보상만 따로 남지 않는다. 보상 지급을 외부 서버나 별도 파일에 분리하지 않는다.

로드 순서: envelope/체크섬→saveVersion→catalog version/hash→연속 마이그레이션→Schema/참조/불변식→서비스 임시 인스턴스 구성→전체 교체→projection/일정 재계산. 마이그레이션 불가한 미래 버전·누락 필수 맵은 SAVE_INCOMPATIBLE, 원본 슬롯 보존. 선택 장식 제거는 명시 migration에서 우편 대체 가능. 기존 hash와 다른데 migration이 없으면 임의 로드 금지. v1→v2가 아직 정의되지 않았으므로 이번 예제는 버전1 동일 카탈로그만 유효하다.

## 10. 인벤토리·관계·퀘스트·생활·전투의 경계

원본에서 필요하므로 없애지 않되 이번 JSON 명령 지원 범위와 분리한다. StateStore의 도메인 모듈로 두고 EventRunner 내부 전용 변수를 늘리지 않는다.

| 모듈 | 입력/출력·상태 전이 | 공통 서비스 재사용·중단/저장 |
|---|---|---|
| 인벤토리/경제 | Give/Consume/Trade 원자 요청→변경 또는 실패. stack={item,qty,provenance,quality}; provenance=normal/grant/loan | 핵심 기록은 item 아닌 증거 집합. grant 판매0, loan 반출/판매불가. 공간 부족 일반 보상은 우편으로 원자 이동. 구매/제작은 비용과 결과 함께 커밋; 취소 전부 미적용. 수량·우편·중복 키 영속 |
| 관계 | RecordExperience(npc,activityType,activityId)→고유 경험 추가·해금 후보 | 첫 유형+8, 새 대화 일+2, 선물 일+2는 조정 데이터. R1/R2/R3는 경험/전 사건 조건. 오래 미접속 감소 없음. 공공 정보 조건에서 관계 금지. 경험·일일 원장·관계 단계 영속 |
| 퀘스트/증거 | undiscovered→discovered→investigating→resolved→epilogue | 단서 수집은 퀘스트 수락과 독립 영속 집합. 수락 시 evaluator로 소급 계산. A–F 다른 사건 참조는 lint 오류, finale만 해결수 집계. 보상은 event commit 원장 공유 |
| 마법/반응 | Cast(template,target,tags)→ReactionResult. projectile/area/target만 우선 | 상태 태그→등록 반응→효과 트랜잭션. 조합6종 명시 표, 미등록은 관찰 피드백. 주문 사용 경험은 contextId 집합 중복 제거. 비용은 전투에서만 집중력 적용 |
| 재배/제작/배치 | 일 경계 성장, 두 성질 recipe 판정, preview→confirm 또는cancel | 물 없으면 성장 정지, 고유 일 처리. 필수 재료 소비 불가. 배치 confirm 효과·undo 이력. 퍼즐 preview는 임시, 방 checkpoint에서 재설정. 전체 상세 데이터 스키마는 M4에서 확장 |
| 전투/방 복구 | encounter Idle→Active→Resolved 또는 Defeated | enemy AI/피해는 Unity 도메인 별도 명세. 확정 효과와 임시 전리품 분리; 패배는 현재 방 overlay 복원, 이전 방/확정 단서 유지. 자정 이후 새 encounter 생성 금지 |

## 11. Unity 적용 책임

순수 C# 도메인 서비스는 MonoBehaviour·Transform·씬 인스턴스 ID에 의존하지 않게 설계한다. Unity 쪽은 WorldView(표시), InputAdapter, CollisionAdapter, MapLoader, DialogueView, CameraAdapter, AnimationAdapter, FileStore로 나눈다. 논리 mapId와 Unity 씬/프리팹 연결은 별도 asset binding으로 둔다. Transform은 도메인 위치의 표시 결과다. 이벤트 명령마다 새 MonoBehaviour를 만들거나 NPC별 스크립트를 복사하지 않는다.

중앙 GameLoop가 공통 틱 순서를 호출한다. Unity의 서로 다른 컴포넌트 갱신 순서에 우연히 의존하지 않는다. 맵 로드/애니메이션 콜백은 도메인 큐로 반환한다. 에셋 없는 상태에서도 임시 도형·텍스트로 인수 시험이 가능해야 한다. Unity 버전·패키지 API 선택은 이번 기획에서 확정하지 않으며 Unity 프로젝트 생성이나 플레이 실행은 수행하지 않았다.
