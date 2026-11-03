# ADR 0001: MagicCampus 기반 기술 결정 (Stage 1)

- 상태: 채택 (Task 1-1). 루트 최종 Windows 빌드 검증은 대기 중이다.
- 날짜: 2026-11-02
- 범위: 플랫폼·엔진·어셈블리 경계·픽셀 정책·패키지·저장 필드 소유. 게임플레이 구현은 포함하지 않는다.

## 결정

1. **플랫폼**: Windows 오프라인을 먼저 지원한다. 다른 플랫폼과 온라인 기능은 이후 결정한다.
2. **엔진**: 설치된 Unity 6000.6.3f1을 사용한다. 환경 프로브(Windows64 빌드)는 성공했다(오류 0, exit 0).
3. **언어와 어셈블리**: C# 8, Contracts와 도메인 어셈블리는 netstandard2.1 순수 코드이며 엔진 참조가 없다. Core Contracts 빌드는 오류 0, 경고 0이다. Editor 어셈블리(`MagicCampus.Editor`)는 Editor 전용이며 Contracts만 참조한다.
4. **Bootstrap**: 조립(서비스 생성, 의존 주입, 핸들러 등록, 시작·종료)만 맡는다. 규칙과 상태 전이는 두지 않는다.
5. **픽셀 정책**: 타일 16px, 액터 16x32를 후보로 한다. 네이티브 픽셀 밀도와 nearest 필터, 정수 배율만 쓴다. 고해상도 한글 UI는 월드 픽셀 파이프라인과 분리해 이후 단계에서 정한다. 프로토타입 아트는 제작용(production)이 아니며 교체 대상이다.
6. **빌드 진입점**: `MagicCampus.Editor.EmptyTargetBuild.Build`가 `Assets/MagicCampus/Scenes/Empty.unity`(빈 카메라 씬)를 만들고 `Builds/Stage1/MagicCampus.exe`(Windows64, Development, Mono)를 빌드한 뒤 `Builds/Stage1/build-result.json`(Unity 버전·target·result·errors)을 쓴다. 실패 시 예외를 던지며 Editor를 강제 종료하지 않는다(호출자가 `-quit`을 쓴다).
7. **JSON 스키마 검증**: 패키지 도입과 별개로 Stage 3(콘텐츠), Stage 4(저장)에서 따로 구현한다.
8. **저장**: 원본 save v2 트리 전체를 보존한다. 도메인 구현이 미뤄진 필드도 읽고 쓸 때 값을 그대로 유지한다. 축약 대체 저장은 만들지 않는다.
9. **대여**: 대여품은 임시 scope overlay에서만 유지한다. 영속 상태에는 provenance `normal`/`grant`와 `scopeId=null`만 허용한다.

## 패키지

| 패키지 | 버전 | 용도 |
|---|---|---|
| com.unity.inputsystem | 1.19.0 | 가상 장치 통합 테스트, 컨트롤러 분리(detach) 검증 |
| com.unity.nuget.newtonsoft-json | 3.2.2 | JSON IO 전용 어댑터에서만 사용 |
| 내장 모듈 | tilemap, physics2d, imgui, audio, imageconversion, jsonserialize 1.0.0 | 타일맵, 충돌, 표시, 소리, 이미지, 내장 직렬화 |

- 버전은 공식 문서로 확인하고 실제 루트 프로젝트에서 검증한다. 1.17.0은 설치된 Unity 6000.6에서 폐기 API CS0619로 실패했다. 1.19.0으로 교체 후 가져오기와 Windows 빌드가 오류·경고 0건으로 성공했다. 전체 포트 반영 후 최종 재검증은 별도다.
- Newtonsoft.Json: https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html
- Input System: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/manual/index.html
- 관련 API 보완: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/changelog/CHANGELOG.html
- Contracts와 도메인은 두 패키지를 참조하지 않는다. JSON은 어댑터 경계에서만 다룬다.

## 저장 필드 소유와 기본 정책 (save v2, 최상위 45개 전부)

기본값은 변경하지 않은 `examples/safe-save.json` fixture(전체 기본 프로필)를 따른다. 중첩 필드는 소속 행에 포함한다. `deferred`는 이번 계획(Stage 5까지)에서 도메인 규칙을 구현하지 않고 값만 보존한다는 뜻이다. 일부 소유 Task는 잠정이며 Contracts 동결(Stage 2 전)에서 확정한다.

| 필드 | 규칙 소유 | 구현 Task | 기본/보존 정책 |
|---|---|---|---|
| saveVersion, contentVersion, generation, safePoint, catalogHash | Persistence | 4-2 (catalogHash 입력은 3-1) | saveVersion=2 상수. 나머지는 fixture 값, 저장 시 갱신, 로드 시 버전·해시 검증. safePoint는 world-safe/scene-entry/scene-complete/room-safe. catalogHash는 64자리 소문자 hex |
| clock (absoluteMinute, fraction) | Clock | 3-5 | fixture 값. absoluteMinute 0 이상, fraction은 0 이상 1 미만 |
| player.name, pronounKey, appearanceId | 신규 프로필 | 4-4 | fixture 값. appearanceId는 ID 패턴 |
| player.hp, focus | State/Effects | 3-3 (상태 2-2) | fixture 값. 0~100 |
| player.mapId, x, y | World/Movement | 3-2 | fixture 값. 안전 위치만 영속 |
| player.quickSlots | Inventory (잠정) | 3-3 | 길이 4, ID 또는 null |
| wallet, stacks (itemId, quantity, provenance, quality, scopeId), mail (id, attachments, kind, createdAt, reservationId, state) | Inventory/Economy | 3-3 (mail invite/reminder는 4-3) | fixture 값. provenance는 normal/grant만, scopeId=null 고정, 수량 1~999, 지갑 0 이상. 대여는 임시 overlay |
| completed, evidence, rewardLedger, preAcceptedEvidenceIds | Story/Event | 3-3 커밋, 4-1 | fixture 값. 완료키 공유, 같은 completionKey 재시도는 재보상 없음 |
| learnedSpellIds, spellContexts (spellId, contextIds), spellApplicationSelections (spellId, variantKey a/b), discoveredRecipeIds | Ability (잠정) | 3-3 | fixture 값. 마법 규칙 자체는 deferred, 값 보존 |
| relations (npcId, affinity, greeted, activityKeys, conflict, lastTalkDay, lastGiftDay, expression, r1BaselineActivityKeys, stage, rewardedActivityTypes, r2ActivityTypesSinceR1, activityRecords), invitedRelationIds, acceptedActivityIds, trackedActivityIds (최대 3), companion (npcId, usedDays) | Relationship/Schedule | 4-1 (relations), 4-3 (초대·수락·추적·동행) | fixture 값. affinity 0~100, stage 0~3 |
| appointments (id, status, acceptedAt) | Schedule/Reservation | 4-3 | fixture 값. status는 Offered/Accepted/Ready |
| plots, creatureState, installedProjects, placements | Life/Placement/Project | 3-3 (표현), 2-2 (상태 검증) | fixture 값. provenance normal/grant, plots의 scopeId=null. 생활 규칙은 deferred, 값 보존 |
| roomCheckpoints, defeats, classRecords, dayJobs, shopState | Room/Combat, Class, Economy | deferred (후속 단계) | fixture 값을 트리 그대로 보존 |
| settings (textScale, flash, shake, damageMultiplier, telegraphMultiplier, aimAssist, textSpeed, particleDensity, holdToToggle, masterVolume, subtitles, aimAssistStrength, preset) | Settings | 3-4 | fixture 값. 배율 0~3, aimAssistStrength 0~1, preset은 basic/comfort/challenge/custom |
| discoveredCaseIds, questStates (caseId, status, activeNodeId, resolved), caseResolutionOrder (최대 6), truthReadKeys, finalState (status, choice, archiveGeneration), flags (id, value), returnPending | Story | 4-1 | fixture 값. flags는 boolean 또는 integer, finalState.status는 locked/ready/running/done |
| journal (entryId, read, hintLevel), dialogueHistory (npcId, recentDialogueIds, lastSpokenDay) | Journal/Dialogue | 3-4 | fixture 값. hintLevel 0~3 |
| discoveredSafePointIds | Persistence | 4-2 | fixture 값 |
| npcPositions (npcId, mapId, x, y) | World/Movement | 3-2 | fixture 값 |

## Task 소유권

| Stage | Task |
|---|---|
| 2 | 2-1 Parsing, 2-2 State/TransactionsCore, 2-3 RuntimeHandleControlPauseTick, 2-4 UnityAdaptersInputMapsCollision, 2-5 UnityAdaptersStorage |
| 3 | 3-1 ContentCompilationCatalog, 3-2 WorldMovementNavigation, 3-3 TransactionConditionsEffects, 3-4 Presentation, 3-5 Time |
| 4 | 4-1 ApplicationEventsOperations, 4-2 Persistence, 4-3 ApplicationSchedulesReservations, 4-4 ContentFixturesFirstConnection |
| 5 | 5-1 BootstrapFirstConnection 및 통합 |

Contracts는 Stage 2 전에 동결한다. 이후 발견된 결함은 Task 1로 되돌린다. Stage 6 이후는 다루지 않는다.

## 게이트

- Stage 1 전체 게이트: 모든 포트의 컴파일과 루트 프로젝트의 최종 Windows 빌드가 모두 통과해야 한다. 현재 루트 최종 빌드는 대기 중이다.
- Stage 2~5는 아직 구현되지 않았다.
- 같은 Stage의 Task는 서로의 미완성 구현을 호출하지 않는다. 공통 포트 변경은 Task 1 또는 단독 통합 Task에서만 한다.

## 근거와 상태

- 환경 프로브: Unity 6000.6.3f1, Windows64 빌드 성공, 오류 0, exit 0.
- Core Contracts: netstandard2.1, 오류 0, 경고 0, Unity 참조 없음.
- 생성 이력: 최초 원격 요청은 DNS 실패, 이어진 긴 생성 2회는 파일 없이 취소됐다. 작은 요청은 sonnet(실제 모델 claude-sonnet-5-5)으로 성공했다.
- 이 문서와 빌드 코드는 루트 프로젝트에서 아직 컴파일·실행되지 않았다.

## 영향

- 도메인 단위 테스트는 엔진 없이 실행할 수 있다.
- 패키지 도입 범위가 어댑터로 한정되어 교체 비용이 낮다.
- 저장 트리 전체 보존으로 미구현 도메인이 추가될 때 migration 없이 값이 이어진다.
- 프로토타입 아트와 UI는 이후 교체를 전제로 하므로 에셋 경로에 의존하는 규칙을 만들지 않는다.

## Codex 계약 검토 보완 (단계2 전 확정)

- Position 좌표는 기존 JSON의 7.5/2.5를 보존하는 finite double이다. 정수 좌표 초안은 수정했다.
- DataNode는 원본 save v2 전체 트리를 보존한다. 숫자는 integer/number를 유지하며 스키마 number는 둘을 허용, integer는 수학적으로 정수인 유한 number도 JSON Schema 의미에 맞춰 검사한다. 파생 직렬화 순서와 원본 파일 해시를 혼동하지 않는다.
- ContentBundle.SourceHash는 원본 UTF-8 바이트의 SHA-256이다. catalogHash는 기존 계약대로 full-catalog 원본 바이트 해시다. runner/functions 원본 해시는 별도 실행 증거에 보존한다. 컴파일러 입력은 catalog/runner/functions/각 스키마를 명시적으로 받는다.
- StateReplacement.Slices와 OwnedSlices는 canonical JSON Pointer 경로다. 배열은 단위 교체하며 인덱스 쓰기는 금지한다. 상위/하위 경로 소유 충돌을 거부한다. player의 위치와 HP 소유자를 분리할 수 있도록 top-level-only 초안을 수정했다.
- 최소 연결 참가자 소유: world는 /player/mapId,/player/x,/player/y,/npcPositions; clock은 /clock,/dayJobs,/returnPending; event-effects는 /flags,/completed,/rewardLedger,/wallet; reservations는 /appointments,/acceptedActivityIds,/invitedRelationIds. 나머지 필드는 해당 도메인이 구현될 때까지 원본 값을 읽고 보존하며 이번 단계에서 수정하지 않는다. 참가자가 자기 소유 밖 경로를 반환하면 전체 거부한다.
- transactionId 재전송 캐시는 실행 중 유지하고 전체 로드 교체에서 초기화한다. 프로세스 간 중복 보상은 영속 completed/rewardLedger의 공유 completionKey로 차단한다. 임의 transactionId를 기존 보상 ID 배열에 집어넣지 않는다.
- dayJobs에 기록할 달력 경계 키는 boundary.day_dN 형식이다. 숫자만인 ID 구간을 금지하는 기존 스키마를 지킨다. 경계와 각 소비자 job을 같은 시간 커밋에서 기록한다. 이후 일 소비자는 자신의 안정된 접두어 키를 쓴다.
- ReadOnlyMemory<byte>의 backing array가 외부 API로 노출되지 않도록 SaveEnvelope/FileGeneration은 생성 시와 조회 시 모두 복사한다.
- 함수 사전의 전체 원본 정의는 검증기에 그대로 전달한다. ParamDef가 표현하지 않는 사전 메타데이터(가변 인자 그룹/상한/교차 참조)는 원본 DataNode를 기준으로 검증하며 임의 허용하지 않는다. 일반 숫자·bool·인자 문법과 실제 함수별 의미를 분리한다.
- 런타임 Handle 생성/제어, completion pump, actor 위치 권위, 맵 크기, 상태 불변식, 장면 진입 복원, JSON IO 포트는 Coordination.cs에서 확정한다. Stage2 어댑터는 같은 단계의 Runtime 구체 클래스에 의존하지 않고 이 포트를 주입받는다.
- 전체 저장 기본 프로필은 fixture 전체 값을 명시 복사해 구성한다. 이후 최소 fixture에 필요한 NPC/변수/해시만 선언과 함께 조정하며 미구현 도메인 필드를 누락시키지 않는다.
