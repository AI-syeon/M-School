# 3단계 설계 지침: 타입 있는 게임 상태로 바꾸기

다음 세션이 `/pstack:poteto-mode`로 받아 **설계**를 하는 지침이다. 이 세션의 산출물은 설계 문서와 판정용 프로토타입이다. 전체 구현은 사용자가 설계안을 고른 뒤에 시작한다.

## 1. 목표

- 사용자 목표: "나는 이 게임이 현재 시스템적으로, 코드적으로 너무 과하게 설계되어 있다고 생각한다. 리팩토링이 필요하다." (2026-11-03)
- 1·2단계(2026-11-03~05)에서 게임 코드가 19,747줄에서 16,285줄로 줄었다. 남은 과설계의 뿌리는 상태 모델이다. 지금은 게임 상태 전체를 JSON 트리(`DataNode`)로 들고 다닌다. 상태를 바꿀 때는 참가자(participant)가 JSON 경로 단위로 소유 범위를 선언하고, 계획(StatePlan)을 거쳐 커밋한다.
- 3단계 목표는 이 구조를 **타입 있는 게임 상태**로 바꾸는 설계를 만드는 것이다. 이렇게 하면 읽는 사람이 따라가야 할 계층과 숨은 규칙이 줄어든다. Contracts 어셈블리도 실제로 경계를 넘는 타입만 남도록 줄인다.
- 성공 기준은 줄 수가 아니라 **읽는 부담 감소**다. 예를 들어 "이벤트가 보상을 주면 무엇이 어디에 써지는가"를 파일 한두 개로 따라갈 수 있어야 한다.

## 2. 사용자 결정

| 날짜 | 원문 | 뜻 |
|---|---|---|
| 2026-11-03 | "2단계까지 진행" | 1·2단계는 끝났다. 3단계는 아직 승인되지 않았다. |
| 2026-11-05 | "이걸 설계하기 위한 지침 작성해줘. poteto mode로 다음 세션에 넘길거야." | 다음 세션은 3단계를 **설계**한다. |
| 2026-11-03 | 프로젝트는 32×32 타일 기준(AGENTS.md) | 상태 설계와 직접 관련은 없다. 좌표 단위를 바꾸지 않는다. |

**사용자가 정할 것.** 설계 세션은 아래 항목마다 근거와 선택지, 추천을 가져오고 직접 정하지 않는다.
- 저장 파일 형식(save v2, 최상위 45개 필드)을 바이트 수준으로 유지할지.
  - ADR-0001 결정 8 "원본 save v2 트리 전체 보존"을 바꾸는 것은 큰 방향 변경이다.
- 아직 만들지 않은 시스템(전투, 상점, 생활 등)의 저장 필드를 계속 보존할지.
  - 이것은 기획 범위 결정이다.
- 콘텐츠 스키마(`design/systems/schemas`, `functions.json`)를 바꿀지. 기본값은 바꾸지 않는 것이다.

## 3. 덜어내지 않을 것 (2026-11-03 합의)

- **이벤트 실행기**: `Application/Events/EventRunner.cs`(약 1,000줄)는 스토리를 돌리는 해석기다. 상태 접근 방식만 바꾸고 해석 로직은 유지한다.
- **실제 게임 동작**: 길찾기(`World/Navigation`), NPC 일정(`Application/Schedules`), 일시정지 관리(`Runtime/Pause`)가 여기에 속한다.
- **검증 probe**: `Bootstrap/FirstConnection/FirstConnectionProbe.cs`와 Runtime의 장애 주입 스위치(`FailMapId`, `FailDisk`, 포털 닫기, 칸 막기). `verify-magiccampus` 스킬이 이것으로 게임을 검증한다.
- **어댑터 인터페이스**: `ICollisionPort`, `IMapPort`, `IInputPort`, `IFileGenerationStore`, `IDataCodec`. Unity 없이 `Tests/PureDomain`을 돌리게 해 주는 장치다.

## 4. 현재 구조 (2026-11-05, master `419bd9d` 기준 측정)

### 4.1 상태와 쓰기

- `State/StateStore.cs`가 `StateSnapshot(revision, DataNode root)`를 들고 있다.
- `Commit(StatePlan)`의 흐름은 이렇다.
  1. 계획 안의 `PlannedChange`를 참가자별로 나눈다.
  2. 각 참가자의 `Prepare`가 `StateReplacement`(JSON Pointer 경로 → 새 값)를 돌려준다.
  3. 경로 소유와 겹침을 검사한다.
  4. `StateTree.TryReplace`로 새 트리를 만들고, 상태 불변식(스키마)으로 검사한 뒤 교체한다.
- `Transactions/Core/StateParticipantRegistry.cs`가 참가자 등록과 경로 겹침 거부를 맡는다.
- 쓰기 참가자 4개와 소유 경로는 다음과 같다.

| 참가자 | 파일 | 소유 경로 |
|---|---|---|
| world | `World/Movement/WorldCommitParticipant.cs` | `/player/mapId`, `/player/x`, `/player/y`, `/npcPositions` |
| clock | `Time/Clock/ClockParticipant.cs` | `/clock`, `/dayJobs`, `/returnPending` |
| event-effects | `Transactions/Effects/EventEffectsParticipant.cs` | `/flags`, `/completed`, `/rewardLedger`, `/wallet` |
| reservations | `Application/Reservations/ReservationParticipant.cs` | `/appointments` (`ReservationRules.cs`의 `AppointmentCodec.Pointer`). ADR-0001은 `/acceptedActivityIds`, `/invitedRelationIds`도 예약 소유로 적었지만 코드는 `/appointments`만 쓴다. |

- 나머지 약 30개 최상위 필드는 읽고 보존만 한다(ADR-0001 표 참고).
- 시계는 매 프레임 계획을 만들어 커밋한다(`ClockService`, `FirstConnectionRuntime.Step`).
- 이벤트 한 번의 결과는 `EventCommitPlanner`가 한 계획으로 만든다. 그 안에 효과, 시간 비용, 위치 투영이 함께 들어간다(`Transactions/Effects/EventCommitPlanner.cs`).

### 4.2 상태를 JSON으로 직접 읽는 곳

`DataNode`, `SaveTree.`, `StateTree.`, `JsonPointer`, `.Root`의 등장 횟수 상위는 다음과 같다(2026-11-05 측정).
- Contracts `Ports.cs` 55
- `Persistence/SaveCodec.cs` 32
- `WorldCommitParticipant.cs` 30
- `FirstConnectionVisuals.cs` 28. 대부분 아트 매니페스트이며 상태가 아니다.
- `Transactions/Conditions/VariableDeclaration.cs` 25
- `FirstConnectionProbe.cs` 24
- `Persistence/SaveMigration.cs` 23
- `FirstConnectionRuntime.cs` 22
- `WorldStateProjector.cs` 17
- `ConditionEvaluator.cs` 16
- `ClockParticipant.cs` 16

설계 세션은 이 목록을 **상태 읽기**와 **콘텐츠·저장 파일 읽기**로 다시 나눠야 한다. `DataNode`는 콘텐츠 컴파일과 저장 코덱에서는 정당한 경계 타입이다.

### 4.3 저장

- **쓰기.** `Persistence/SaveService` → `SaveCodec.Seal`을 거친다. Seal은 필드 검사(`SaveFields.CheckRoot`), 정규 직렬화, SHA-256을 맡는다. 그 결과를 `IFileGenerationStore`가 세대 파일로 쓴다.
- **읽기.** `SaveCodec.Decode` → `SaveRestorer` 순서로 진행한다. SaveRestorer는 v1→v2 변환(`SaveMigration`), 스키마 검사, `SaveStateValidator`(카탈로그 해시, 참조)를 맡는다. 마지막에 `StateStore.ReplaceAll`로 상태를 교체한다.
- **안전 경계.** `SafeBoundaryPolicy`가 어떤 스냅샷을 저장해도 되는지 정한다(world-safe, scene-entry, scene-complete). 장면 진입 저장과 복구는 `SceneRecovery`와 `FirstConnectionRuntime`이 조율한다.
- **중복 보상 방지.** 영속 `completed`와 `rewardLedger`의 공유 completionKey가 막는다(`StateStore.IsCompleted`).

### 4.4 어셈블리

| asmdef | 참조 |
|---|---|
| MagicCampus.Contracts | 없음 |
| MagicCampus.Domain | Contracts |
| MagicCampus.UnityAdapters | Contracts, InputSystem |
| MagicCampus.Presentation | Contracts, InputSystem |
| MagicCampus.Bootstrap | Contracts, Domain, UnityAdapters, Presentation, InputSystem |
| MagicCampus.Bootstrap.Editor | Bootstrap |

- Contracts는 8개 파일, 1,165줄이다.
- 1단계 조사(2026-11-03) 때는 Contracts 타입 125개 중 UnityAdapters·Presentation이 쓰는 것이 38개였다. 지금 숫자는 다시 재야 한다.
- 루트의 `MagicCampus.Contracts.csproj`, `MagicCampus.Domain.csproj`와 `Tests/PureDomain/*.csproj` 4개가 같은 소스를 Unity 없이 컴파일한다.

### 4.5 폴더별 크기 (줄)

| 폴더 | 줄 수 |
|---|---|
| Contracts | 1,165 |
| State | 357 |
| Persistence | 789 |
| Transactions | 1,254 |
| Application | 2,089 |
| World | 1,526 |
| Time | 433 |
| Runtime | 394 |
| Content | 2,060 |
| Bootstrap | 3,275 |
| Presentation | 1,135 |
| UnityAdapters | 1,808 |

## 5. 설계가 답해야 할 질문

1. **상태의 모양.** 플레이 중 실제로 읽고 쓰는 필드를 담는 `GameState` 타입을 정한다. 아직 규칙이 없는 필드(보존만 하는 약 30개)는 세 가지 방식을 비교한다.
   - 불투명한 `DataNode`로 들고 있다가 저장할 때 그대로 돌려놓는다.
   - 타입은 만들되 규칙 없이 둔다.
   - 저장에서 뺀다. 이것은 사용자 결정이다.
2. **원자성.** "이벤트 결과는 전부 반영되거나 전혀 반영되지 않는다"를 무엇으로 보장할지 정한다. 후보는 불변 레코드 + 순수 함수 `Apply(state, outcome) → state`, 복사 후 변경 뒤 교체, 지금의 계획·참가자 방식 축소판이다. 무엇이든 지금의 거절 조건은 그대로 거절해야 한다.
   - 중복 보상
   - 지갑 넘침
   - 연습 실행 보상
   - 이미 완료된 completion
3. **검증 위치.** 스키마와 참조 검사를 로드·저장 경계 한 곳으로 모을 수 있는지 정한다. 매 커밋마다 스키마를 돌리는 지금의 불변식 검사를 타입이 대신할 수 있는지도 본다. 대신한다면 어떤 잘못된 상태가 타입으로 표현 불가능해지고, 어떤 것은 여전히 런타임 검사가 필요한지 표로 낸다.
4. **시계.** 매 프레임 계획을 만들어 커밋하는 구조를 단순한 시간 필드 갱신으로 바꿀 수 있는지 정한다. 자정 경계 기록(`/dayJobs`의 `boundary.day_dN`, `/returnPending`)은 유지한다.
5. **월드 위치.** `ActorWorld`가 실시간 위치의 권위다. 안전한 위치만 상태에 투영하는 규칙(`WorldStateProjector`, `SafeBoundaryPolicy`)을 새 모델에서 어디에 둘지 정한다.
6. **저장 경계.** 타입 상태와 save v2 JSON 사이의 변환을 한 곳(코덱)에 둔다. 저장 바이트가 지금과 같은지 비교하는 방법을 정한다. 같은 상태를 넣으면 같은 `gen-*.mcgen` 내용이 나와야 한다. 단, 사용자가 형식 변경을 승인하면 예외다.
7. **Contracts 축소.** UnityAdapters·Presentation과 경계를 넘는 타입만 Contracts에 남기고 나머지는 Domain으로 옮긴다. asmdef 참조와 루트 csproj 목록에 미치는 영향을 적는다.
8. **테스트.** `Tests/PureDomain` 명세(약 7,300줄) 중 참가자, 계획, 경로 소유 같은 내부 구조를 검사하는 것과 플레이어가 관찰하는 동작을 검사하는 것을 나눈다. 동작 검사를 새 모델로 옮기는 계획을 세운다.
9. **probe 증거.** probe와 `result.json`, `initial.json`, `final.json`, `trace.log`는 상태를 JSON으로 읽고 쓴다. 증거 형식을 그대로 유지하는 방법을 정한다.
10. **단계 나누기.** 한 번에 바꾸지 않는다. 각 단위가 끝날 때 테스트와 probe로 검증할 수 있는 순서를 정한다. 그 사이에 옛·새 경로가 함께 사는 기간이 필요하면 언제 지울지 적는다.

## 6. 진행 방법 (poteto)

- 첫 메시지에서 목표, 문제, 이 세션의 산출물(설계 문서 + 판정 프로토타입)을 사용자에게 말한다.
- 플레이북은 **figure-it-out**이 맞다. 여러 시스템에 걸친 큰 구조 작업이기 때문이다. 플레이북의 단계를 할 일 목록 맨 앞에 그대로 옮긴다.
- 순서는 다음과 같다.
  1. **how** 스킬로 지금 상태 흐름의 계약을 확인한다. 4절의 사실을 출발점으로 쓰되, 코드로 다시 확인한다.
  2. **architect** 스킬로 서로 다른 목표 구조 2~3개를 나란히 설계한다. 각 안에 `GameState` 타입 초안, 이벤트 커밋 흐름, 저장 변환, 줄 수와 읽는 부담 추정, 위험을 담는다.
  3. 실험으로 가를 수 있는 질문은 **Prototype** 플레이북으로 확인한다. 예를 들면 "타입 상태 → save v2 JSON 변환이 지금과 같은 바이트를 내는가", "이벤트 효과 한 줄기를 새 모델로 옮겼을 때 기존 명세가 통과하는가"가 있다. 사용자에게 묻기 전에 먼저 돌려 본다.
  4. 결론이 갈리면 **interrogate** 스킬로 반대 검토를 받는다.
  5. 설계 문서를 쓰고, 2절의 사용자 결정 항목을 선택지와 추천과 함께 사용자에게 가져간다.
- 서브에이전트에 맡길 때는 모델과 effort를 명시한다(사용자 공용 지침). 판단이 필요한 설계와 조사는 Sonnet high, 크고 독립적인 설계 한 안은 Opus medium이다. 같은 파일에 쓰는 사람은 한 명이다. 겹치면 worktree로 분리한다.
- 프로토타입 코드는 별도 브랜치나 worktree에 두고 master에 합치지 않는다. 설계 문서에 결과만 옮긴다.

## 7. 검증 수단과 기준 결과

모든 명령은 프로젝트 루트에서 PowerShell 5.1로 실행한다.

| 무엇 | 명령 | 2026-11-05 기준 결과 |
|---|---|---|
| 순수 도메인 테스트 | `powershell -ExecutionPolicy Bypass -File Tests\PureDomain\run-all.ps1 -Tag <이름>` | ContentIOTests 10, Stage2Tests 5, Stage3Tests 16, Stage4Tests 9 PASS, FAIL 0 |
| Unity 빌드 | `powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\build.ps1 -Build Stage5-verify` | 오류 0, 경고 0 |
| 게임 probe 13개 | `run-probe.ps1 -Cases success-notes,success-tea,moved,a10,cancel,closed,blocked,map-fail,disk-fail,intro-false,validation,ui-long,ui-200 -Build Stage5-verify -Run <새 run-id>` | `Tests/Results/refactor/final-probe.txt`(git 무시, 이 PC에만 있음). 모두 pass, ui-long·ui-200은 사람 검토 1건 건너뜀 |
| 강제 종료 복원 | `run-restart.ps1 -Phases move,say,choice -SuccessFrom <probe run-id> -Build Stage5-verify -Run <새 run-id>` | 4건 pass(checks 11, 11, 11, 52) |

알아 둘 점은 다음과 같다.
- `run-all.ps1`은 스위트마다 빌드한 뒤 DLL을 직접 실행한다. `dotnet run`을 연달아 쓰면 공유 `obj/` 때문에 다른 스위트가 실행될 수 있다.
- 이 PC의 Windows 앱 제어가 결정적 빌드의 일부 테스트 DLL을 해시로 차단한 적이 있다. 그래서 스크립트는 `-p:Deterministic=false`로 빌드한다.
- 이벤트 진행 틱(`trace.log`의 `tick=`)은 같은 빌드에서도 마지막 단계가 1틱씩 흔들린다. 새 빌드의 첫 실행은 1틱 늦을 수 있다. 비교할 때는 같은 커밋을 두 번 돌려 흔들림 범위를 먼저 잰다.
  - 기준값은 success-notes fork.enter 122~123, a10 fork.enter 151~152다.
- 스크린샷은 캐릭터 대기 애니메이션 프레임 때문에 바이트가 달라질 수 있다. 차이가 나면 PNG를 직접 열어 비교한다.
- Unity 빌드와 probe는 한 번에 한 작업만 돌린다. 빌드는 `Assets/MagicCampus/Scenes/FirstConnection.unity`를 다시 만든다. 이 파일이 수정됨으로 보이는 것은 정상이며 되돌리지 않는다.
- Unity 편집기가 프로젝트를 열고 있으면 빌드가 거부된다. 편집기는 대신 닫지 말고 사용자에게 묻는다.
- probe가 지나지 않는 경로(WASD 이동, E 상호작용, 초대 창 클릭, F5/F9)는 사람이 `play.ps1`로 플레이해야 확인된다.

## 8. 작업 환경 주의

- 셸은 Windows PowerShell 5.1이다. `&&`를 쓰지 않는다. PATH에 node와 python이 없다. dotnet은 `C:\Program Files\dotnet\dotnet.exe`에 있다.
- git은 로컬 저장소다(2026-11-03 생성, 원격 없음).
  - 커밋은 경로를 명시해서 하고 `git add -A`는 쓰지 않는다.
  - 커밋 작성자는 `-c user.name=syeon -c user.email=syeon.dev@gmail.com`이다.
  - push와 원격 작업은 하지 않는다.
- `.gitattributes`의 `* -text`로 체크아웃이 바이트 그대로 유지된다. 콘텐츠 해시(catalogHash)가 원본 바이트에 의존하므로 이 설정을 바꾸지 않는다.
- git 무시 대상: `Builds/`, `Library/`, `design/implementation-tasks/execution/`, `Tests/Results/verify/`, `Tests/Results/refactor/`, `.claude/worktrees/`.
- worktree에서 테스트할 때 필요한 픽스처는 모두 git에 있다. ContentValidation 음성 픽스처는 `Tests/PureDomain/ContentValidation/negative-fixtures.json`이다.

## 9. 산출물과 완료 기준

설계 세션이 끝날 때 다음이 있어야 한다.

1. `Docs/Refactoring-Stage3-Design.md` 설계 문서. 다음 내용을 담는다.
   - 5절 질문 10개 각각의 답 또는 사용자 결정 요청
   - 비교한 목표 구조 2~3개와 추천안, 추천 근거
   - 추천안의 `GameState` 타입 초안(코드 스케치)과 이벤트 커밋, 저장 변환의 호출 흐름
   - 지금 거절되는 잘못된 입력과 상태 목록, 그리고 새 모델에서 각각이 어디서 거절되는지 대응표
   - 단위별 구현 순서. 각 단위의 검증 명령과 기대 결과를 적는다.
   - 줄 수와 읽는 부담 추정. 측정한 것과 추정한 것을 구분한다.
   - 위험과 되돌리는 방법
2. 프로토타입 결과. 무엇을 실험했고, 무엇을 관찰했고, 어느 안을 지지하는지 적는다. 코드는 별도 브랜치에 남긴다.
3. 사용자에게 가져갈 결정 목록. 저장 형식 유지 여부, 보존 필드 처리, 구현 시작 승인이 들어간다.
4. `Docs/Refactoring-2026-11-03.md` 진행 기록에 3단계 설계 결과를 한 줄로 남긴다.

설계 세션에서는 master의 게임 코드를 바꾸지 않는다. 구현은 사용자가 안을 고른 뒤 별도 세션에서 한다.

## 10. 참고 파일

- 진행 기록과 1·2단계 결과: `Docs/Refactoring-2026-11-03.md`
- 최초 기반 결정과 저장 필드 소유표: `Docs/ADR/0001-foundation.md`
- 단계 3·4 결합 규약: `Docs/ADR/0002-stage3-boundaries.md`, `Docs/ADR/0003-stage4-boundaries.md`
- 원래 설계 문서: `design/systems/02-architecture.md`, `design/systems/03-systems.md`, `design/implementation-tasks/구현구조.md`
- 저장 스키마: `Content/Fixtures/FirstConnection/generated/base/schemas/`(빌드 때 `Assets/StreamingAssets/FirstConnection`으로 복사)
- 검증 스킬: `.claude/skills/verify-magiccampus/SKILL.md`와 `features/`
- 조립 지점: `Assets/MagicCampus/Bootstrap/FirstConnection/FirstConnectionRuntime.cs`
