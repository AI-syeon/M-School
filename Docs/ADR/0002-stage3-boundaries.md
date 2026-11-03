# ADR 0002: 단계3 독립 구현의 결합 규약

2026-11-02. 단계2 전체 통과 후 발견한 Task1 계약 보완과 단계3의 독립 파일 소유를 확정한다. 등록 함수나 원본 설계를 덮어쓰지 않는다.

- 표준 검증은 JsonSchema.Net 7.2.0(MIT, Draft 2020-12)을 IO 어댑터에서 사용한다. 설치된 Unity 내장 System.Text.Json 8.x로 실제 검증 probe가 통과했다. 7.3/7.4 후보는 JSON9 바이너리 참조 때문에 채택하지 않았다. 게임에는 JsonSchema.Net/JsonPointer.Net/Json.More/Humanizer DLL만 넣고 Unity의 BCL을 중복 배포하지 않는다. 도메인은 DataNode/ISchemaValidationPort만 참조한다.
- `IClockCostPlanner`, `IWorldStateProjector`, `IEventCommitPlanner`, `IPresentationLifetime`는 같은 단계의 구체 클래스 의존 없이 조립하기 위한 추가 포트다. 기존 포트 동작은 바꾸지 않는다. FunctionDef.Owner는 함수 사전의 설명용 서비스 이름이므로 공백을 포함한 원문을 보존한다.

## 고정 내부 계획 형식

게임 저작표가 아닌 등록 참가자 사이의 명령이다. 임의 함수 호출이나 eval이 아니다.

| 참가자 | operation | payload와 소유 |
|---|---|---|
| event-effects | set | `{variable,value}`. 선언된 bool/int flags만. clock 변수 금지 |
| event-effects | complete | `{id}`. 지원 범위 내 고유 완료키 |
| event-effects | currency.add | `{amountCrowns}`. 양의 정수, 공유 completion 원장과 같은 커밋 |
| event-effects | event.complete | `{eventId,completionKey,repeatPolicy}`. once의 completed/rewardLedger를 1회 기록. practice는 고유 원장 추가 안 함 |
| clock | advance | `{minutes}`. frame fraction 또는 마지막 성공 비용. clock/dayJobs/returnPending 소유자가 경계·등록 소비자를 재검증 |
| world | project | `{player:{mapId,x,y},npcPositions:[{npcId,mapId,x,y}]}`. 활성 액터의 위치만 투영. 미활성 NPC와 나머지 player 필드는 보존. 현재 실제 액터 위치와 일치해야 함 |
| reservations | complete | `{eventId}`. appointments.id=eventId인 예약을 성공 커밋에서만 제거. 단계4-3 소유 |

Clock cost planner는 모든 경계 소비자 planned change를 clock change와 함께 반환한다. 경계 키는 `boundary.day_dN`, 소비자 키는 `consumer.<consumerId>.day_dN`이며 기존 v2 dayJobs 문자열 패턴을 지킨다. 미등록 소비자는 실행/완료로 꾸미지 않는다.

Event commit planner는 효과 전체 계획 → once event 원장 → clock cost(수업 등 perform 시간 단일 원천 검증) → world projection → reservation complete를 한 StatePlan으로 만든다. 조건은 Runner가 최신 revision에서 다시 확인한다. unsupported 도메인 효과는 UNREGISTERED로 전체 거부한다. 아직 구현하지 않은 수업/생활/전투를 성공으로 가장하지 않는다.

## 컴파일된 호출 형식

`CompiledCall.Args`는 사전 parameter 이름을 그대로 쓴다. ref/word/text는 String, bool은 Bool, 정수는 Integer, 소수는 Number다. 이름 인자 중 사전 default가 있는 것은 채운다. 생략한 async/선택 else는 key를 만들지 않는다.

- choice.options는 `{fork,label}` Object의 List. await.handles는 String의 List.
- any.alternatives는 원문 String List이며 Alternatives는 각 인용 조건을 컴파일한 호출목록이다.
- branch.condition 원문은 Args에 보존하며 Alternatives[0]은 컴파일된 조건목록이다. Runner는 문자열을 재파싱하지 않는다.
- 함수 사전의 wildcard(kind별 item id/숫자 일정 등)는 원본 nameRule과 메타데이터로 검사한다. 별도 중복 저작표를 만들지 않는다.
- Catalog.FindDefinition: map/destination/variable/schedule/npc는 runner Row를 우선하고 npcMetadata는 full catalog NPC Row다. catalog/root, runner/root는 전체 불변 원본 트리다. 다른 kind는 원본 함수 사전 refKinds/유도 색인을 따른다. catalogHash는 full-catalog 원본 바이트 해시, runner/functions 해시는 실행 증거에 별도 보존한다.

최소 fixture는 full catalog의 12명 정의를 보존하면서 runner에 활성 NPC만 둔다. A10은 추가 NPC spawn Row/event/대사를 JSON으로 넣어 검증한다. 미활성 NPC 저장 필드는 투영에서 삭제하지 않는다.

## 파일 소유

- 3-1: Content/Compilation, Content/Catalog, UnityAdapters/ContentIO, 해당 순수/엔진 검증 자료. IO와 컴파일을 나누어 위임하되 같은 파일은 쓰지 않는다.
- 3-2: World/Movement, World/Navigation, 해당 순수 이동 시험. actor 권위/투영 참가자 포함.
- 3-3: Transactions/Conditions, Transactions/Effects, 해당 순수 시험. 시간·이동 구체 클래스를 수정하지 않는다.
- 3-4: Presentation/Dialogue, Presentation/Cutscene, Presentation/Resources 및 그 범위의 prefab 생성·시험. 에셋 래스터는 Codex 소유.
- 3-5: Time/Clock, Time/Calendar, 해당 순수 시험. 상태 저장소 재구현 없이 포트 주입.

Runtime.Tick 입력은 두 delta 값(시뮬/항상 진행하는 감시 시간)이다. 정상 프레임의 pump는 가장 먼저 이전 완료만 전달한다. 장면 소유 이동/카메라/연출은 Calendar가 정지해도 Scene 채널이 열리면 진행한다. Menu All에서는 연출도 정지하며 UI와 취소 입력은 처리한다.
