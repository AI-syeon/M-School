# 3단계 설계: 타입 있는 게임 상태

- 상태: 설계안. 구현은 사용자 승인 뒤에 시작한다(9절 결정 D8).
- 날짜: 2026-11-05. 기준 커밋: master `659cdb3`.
- 지침: `Docs/Refactoring-Stage3-Design-Brief.md`. 근거 조사, 후보 설계, 결정 기록은 이 PC의 `Tests/Results/refactor/`(git 무시)에 있고, 프로토타입 코드는 브랜치 `stage3-proto`에 있다(10절).

## 1. 목표와 결론

사용자 목표(2026-11-03): "나는 이 게임이 현재 시스템적으로, 코드적으로 너무 과하게 설계되어 있다고 생각한다. 리팩토링이 필요하다."

지금은 게임 상태 전체가 JSON 트리(`DataNode`)다. 상태 하나를 바꾸려면 계획(`StatePlan`), 참가자 4개, JSON 경로 소유·겹침 검사, 트리 교체, 그리고 매 커밋 저장 스키마 전체 검사를 거친다. 시계는 이 경로를 멈추지 않은 매 프레임 탄다. "이벤트가 보상을 주면 무엇이 어디에 써지는가"를 답하려면 파일 약 9개, 계층 6개를 열어야 한다. 중복 보상 규칙 하나는 6곳에 흩어져 있다(근거 01, 코드로 확인).

**추천안(A안).** 상태를 불변 `GameState` 하나로 둔다. 상태 변경은 순수 함수 `GameState → Outcome<GameState>`로 표현한다. 저장소는 그 결과로 참조 하나를 바꿔 끼운다. 참가자, 계획, JSON 경로, 커밋마다 하는 스키마 검사가 모두 없어진다. 규칙이 아직 없는 필드는 불투명한 `DataNode` 한 덩어리로 들고 있다가 저장할 때 그대로 돌려놓는다. 검증은 불러오기와 저장 두 경계에서만 한다.

- 보상 질문은 `Transactions/EventRules.cs`와 `State/GameState.cs` 두 파일로 답한다(추정).
- 중복 보상 규칙은 `Progress.IsConsumed` 한 함수가 된다.
- 저장 바이트, 거절 결과 코드, `trace.log`의 `rev=` 번호는 지금과 같다(일부는 실험으로 확인, 10절).

## 2. 지금 구조에서 확인한 사실

지침서 4절을 코드로 다시 확인했다. 대부분 맞고, 다른 점은 다음과 같다.

- Contracts는 8개 파일, 1,352줄, 타입 선언 105개다(지침서는 1,165줄, 125개). 경계를 넘는 것은 38개, Domain·Bootstrap·테스트만 쓰는 것은 65개다(근거 04).
- 저장 경계는 4종이다. world-safe, scene-entry, scene-complete에 room-safe가 더 있다(`Contracts/Ports.cs` 875행).
- `SaveCodec.Seal`은 정규 직렬화를 직접 하지 않는다. 주입된 `JsonDataCodec`(UnityAdapters)이 한다. 저장 때는 스키마를 돌리지 않고, 불러올 때 스키마를 3번 돈다(근거 02).
- 장면 진입 저장과 복구는 `FirstConnectionRuntime`이 아니라 `EventRunner`가 조율한다.
- 중복 보상 검사는 `StateStore.IsCompleted`(비공개) 말고도 5곳에 더 있다. 그중 하나는 목록 또는 객체를 받고, 나머지는 목록만 받는다(근거 01).
- 커밋 결과 캐시(`StateStore.outcomes`)는 매 프레임 시계 커밋마다 항목이 하나씩 쌓이고, 불러오기 때만 비워진다. 코드를 읽어 낸 추론이며 실행해서 재지는 않았다(근거 03).
- 테스트는 8,120줄이다. 내부 구조를 검사하는 것이 720줄(9%), 동작을 검사하지만 준비 단계가 계획 API로 쓰인 것이 563줄, 동작 검사가 1,750줄, 상태 설계와 무관한 것이 1,743줄, 준비 코드·가짜 객체·`Program*.cs` 같은 지원 코드가 3,344줄이다(근거 04, 메서드 단위 판단).

## 3. 비교한 목표 구조

같은 과제문(`Tests/Results/refactor/stage3-arena/task.md`)으로 세 안을 따로 설계했다. A는 Opus high, B와 C는 Sonnet high가 설계했다.

| 기준 | A. 불변 상태 + 순수 함수 | B. 작업 사본 변경 후 교체 | C. 타입 슬라이스 |
|---|---|---|---|
| 상태 변경 방식 | 새 상태를 계산해 참조 교체 | 복제본을 메서드로 고친 뒤 교체 | 슬라이스별 순수 함수, `GameState.Apply`가 조립 |
| 보상 질문에 여는 파일 | 2개 | 4개 | 2개, 의도까지 보면 planner 포함 4개 |
| 잘못된 변경을 막는 곳 | 컴파일 시점(불변 타입) | 일부는 실행 중 검사(`DraftGate`)와 예외 | 컴파일 시점 |
| 보존 필드 | 불투명 `DataNode` | 불투명 `DataNode` | 불투명 `DataNode` |
| 숫자 종류(정수/실수) 보존 | `SaveNumber` | `JsonNum` | `Num` |
| 게임 코드 줄 수 변화(추정) | 약 -1,330(±300) | 약 -1,476 | 약 -1,300 |
| 이 단계에서 돌린 실험 | 없음 | 저장 왕복 17개, 시계 1,260걸음 비교 | 저장 왕복 298개 + 픽스처 6개, 규칙 점검 21개, 틱 비용 |

**A를 고른 이유.** 세 안 중 두 안(A, C)이 따로 설계했는데도 같은 모양에 도착했다. 불변 상태, 순수 함수, 불투명 보존 필드, 숫자 종류 보존, 불러오기·저장 두 경계 검증이다. C는 그 모양을 프로토타입으로 돌려 통과했다. A는 C보다 층이 하나 적다. 이벤트 한 건이 쓰는 모든 것이 `EventRules.Complete` 한 함수에 보인다. 거절 조건 40개를 전부 대응시켰고, 이행 중에는 옛 스키마 검사가 새 함수를 계속 감시하는 단계를 둔다.

**한계.** A 자신의 코덱(`SaveDocument`, `SaveNumber`)과 `GameStore`는 이번에 실행하지 않았다. 저장 바이트 동일성의 증거는 같은 모양인 C·B의 코덱에서 나왔다. A의 코덱은 단위 2의 왕복 명세가 처음 검사한다. C는 "지금 방식의 축소판"으로 지시받았지만 스스로 참가자를 남기는 변형(타입 있는 `PlannedChange`)을 검토한 뒤 기각했다. 그래서 A와 같은 쪽으로 갔다. 참가자를 남기는 최소 변경안은 C의 기각 사유("레지스트리, 소유 경로, `Prepare`가 남아 읽는 부담의 가장 큰 층이 그대로")로만 비교됐다. 세 안의 비교 판정은 설계 세션 혼자 했다. 다른 계열의 교차 심사 모델이 설정에 없었기 때문이다.

**B를 고르지 않은 이유.** `SafeBoundaryPolicy`와 `SceneRecovery`는 이전 상태를 참조로 붙잡고 있다. 그래서 변경 가능한 상태는 얼려 두는 규율이 필요하다. B는 그 규율을 실행 중 검사와 예외 흐름으로 지킨다. 같은 보장을 A는 타입으로 얻는다.

**다른 안에서 가져온 것.**
- C에서 가져온 것은 세 가지다.
  - 지갑 더하기가 새 값을 nullable로 돌려주게 한다. C의 프로토타입에서 `wallet.TryAdd(x, out wallet)`이 `out`과 `this`가 같은 변수를 가리켜 지갑을 0으로 만든 버그가 실제로 났다.
  - 테스트와 probe에서만 커밋마다 전체 검증을 돌리는 감시 리스너를 둔다.
  - 시계 틱도 리비전을 올리는 근거를 받아들였다. `SafeBoundaryPolicy.MarkSaved`가 리비전으로 "저장됨"을 판단한다.
- B에서는 시계 차이 비교 실험 결과(옛 `ClockParticipant` 대 새 시계, 1,260걸음, 차이 0)를 가져왔다.

## 4. 추천안의 모양

### 4.1 `GameState` 타입 초안

```csharp
// State/GameState.cs (C# 8, netstandard2.1. System.Collections.Immutable은 쓰지 않는다)
public sealed class GameState
{
    public readonly ClockState Clock;              // /clock, /dayJobs, /returnPending
    public readonly WorldState World;              // /player/{mapId,x,y}, /npcPositions
    public readonly Progress Progress;             // /flags, /completed, /rewardLedger, /wallet
    public readonly AppointmentBook Appointments;  // /appointments
    public readonly Knowledge Known;               // /evidence, /learnedSpellIds, /discoveredCaseIds, /caseResolutionOrder (읽기 전용)
    internal readonly PreservedFields Preserved;   // 규칙 없는 최상위 31개 + /player 나머지 6개, 불투명 DataNode
    public GameState With(ClockState clock) => ...; // 부분마다 With 하나
}

public sealed class Progress
{
    public readonly FlagTable Flags;
    public readonly KeySet Completed, RewardLedger;
    public readonly Crowns Wallet;                         // 0..2147483647
    public bool IsConsumed(CompletionKey key) => Completed.Contains(key.Value) || RewardLedger.Contains(key.Value);
    public Outcome<Progress> Claim(CompletionKey key);     // 두 장부에 같은 키. 이미 있으면 CONDITION_FALSE
    public Outcome<Progress> Complete(SaveId id);          // /completed만. 이미 있으면 CONDITION_FALSE
    public Outcome<Progress> Earn(long crowns);            // 넘치면 INVARIANT_VIOLATION
    public Progress Set(string variableId, FlagValue value);
}

public readonly struct Crowns  { public Crowns? Added(long amount); }           // C의 별칭 버그 교훈
public readonly struct SaveNumber { public double Value; public bool Integral;    // 저장 바이트용 JSON 숫자 종류
    public SaveNumber MovedTo(double v) => v == Value ? this : Real(v); }        // 지금의 NumberLike 규칙
public sealed class ClockState { public ClockState Advance(ClockAdvance step); } // 자정마다 boundary.day_dN, returnPending

public sealed class GameStore
{
    public StateSnapshot Current { get; }                                         // (revision, GameState)
    public Outcome<StateSnapshot> Commit(TransactionId txn, Func<GameState, Outcome<GameState>> transition);
    public StateSnapshot Replace(GameState state, TransactionId txn);             // 불러오기, 장면 중단 복원
    public void OnCommitted(Action<StateCommitted> listener);
}
```

전체 스케치는 `stage3-proto` 브랜치의 `Tests/Prototype/Arena/candidate-a/`에 있다. 프로토타입에서 실제로 컴파일되고 돈 `Progress`, `EventRules`는 같은 브랜치의 `Assets/MagicCampus/State/GameState.cs`, `Assets/MagicCampus/Transactions/EventRules.cs`다.

### 4.2 이벤트 커밋 흐름

```csharp
// EventRunner.Finish
var live = d.World.Capture();                                    // ActorWorld의 현재 위치, 지도 범위 검사
var committed = d.Store.Commit(run.Txn, s => d.Rules.Complete(s, run.Def, live.Value));

// EventRules.Complete: 이벤트 한 건이 쓰는 것 전부
//  1. 카탈로그에 이벤트가 있는가 (REF_NOT_FOUND)
//  2. once 키가 이미 쓰였는가 → Progress.IsConsumed (CONDITION_FALSE)
//  3. EventOutcome.Parse: 효과 문자열 → Once(키, 크라운, 분) 또는 Practice(크라운·분 없음)
//  4. flags 설정, complete id 기록, Claim(두 장부), Earn(지갑)
//  5. 시간 비용 → Clock.Advance, 위치 → World.Project
//  6. 예약 행이 있으면 제거
```

`EventRunner.Start`의 once 검사는 `Rules.IsSpent(state, def)` 한 줄이 된다. `RepeatBlocked` 대리자와 미리 계획해 보는 분기는 사라진다.

### 4.3 시계

```csharp
// ClockService.Advance (매 프레임)
store.Commit(tick, s => { step = calendar.Plan(s.Clock.Time, minutes);
                          return Transition.Ok(step.To.Equals(step.From) ? s : s.With(s.Clock.Advance(step))); });
```

계획 객체, 참가자, 스키마 검사가 없다. 자정 경계 기록(`boundary.day_dN`, `returnPending`)은 `ClockState.Advance` 안에 그대로 있다. 커밋으로 남기는 이유는 두 가지다. 하나는 이벤트 시간 비용이 보상과 같은 커밋에 있어야 해서다. 다른 하나는 리비전 번호를 지금과 같게 유지하려는 것이다.

### 4.4 저장 변환

```csharp
// SaveCodec.Seal(GameState state, SafeBoundary boundary, long generation)
var tree = SaveDocument.Write(state).With("safePoint", ...).With("generation", ...);
validator.Validate(tree);                      // 불러올 때와 같은 검사. 다시 못 읽을 저장은 쓰지 않는다
var bytes = codec.Serialize(tree);

// SaveRestorer.Restore → SaveDocument.Admit(tree, validator): CheckRoot, 스키마 1번, 참조 검사 1번, Read
// 처음 시작 픽스처도 같은 Admit을 거친다
```

`SaveDocument.Read`와 `Write`는 정확히 서로의 역이다. 저장 필드 이름을 아는 곳은 이 파일 하나다.

## 5. 질문 10개에 대한 답

1. **상태의 모양.** 4.1의 다섯 부분이 플레이 중 읽고 쓰는 필드 전부다(최상위 13개 + `/player`의 mapId, x, y). 규칙 없는 필드 처리는 세 방식을 비교했다.

   | | 불투명 `DataNode`(추천) | 타입만, 규칙 없음 | 저장에서 빼기 |
   |---|---|---|---|
   | 저장 바이트 | 그대로 보장 | 숫자 종류를 필드마다 다시 모델링해야 함 | save v2가 깨짐 |
   | 코드 | 약 10줄 | DTO와 코덱 약 700~1,000줄(추정) | 줄어듦 |
   | 읽는 곳 | 없음 | 없음 | 없음 |
   | 동기화 부담 | 없음 | `save.schema.json`의 C# 사본이 생김 | 없음 |
   | 결정 | 설계 | 설계 | 사용자(D2) |

   새 시스템(상점, 인벤토리, 관계)이 규칙을 얻으면 그 필드만 타입 부분으로 올린다. `PreservedFields` 안쪽은 `SaveDocument.cs`만 만지게 소스 검사 명세로 고정한다.

2. **원자성.** 다음 상태 전체를 순수 함수가 계산하고, 참조 대입 한 번으로 바꾼다. 거절이나 예외는 교체 전에 돌아오므로 반쯤 반영된 상태는 생길 수 없다. 같은 이벤트를 두 번 돌리면 두 번째는 `Progress.Claim`에서 거절된다. 4개 필수 거절 조건은 6절 표의 1, 3~5행(중복 보상), 21~22행(지갑 넘침), 15·17행(연습 실행 보상), 8행(이미 완료)이다. 모두 지금과 같은 결과 코드로 거절된다. E2 실험에서 실제 흐름 135건이 옛 코드와 결과 코드, 리비전, 저장 바이트까지 같았다.

3. **검증 위치.** 불러오기(`SaveDocument.Admit`)와 저장(`SaveCodec.Seal`) 두 곳이다. 커밋마다 하던 스키마 검사는 타입이 대신한다.

   | 잘못된 상태 | 지금 | 새 모델 |
   |---|---|---|
   | 타입 필드의 JSON 종류가 틀림, 필드 없음 | 커밋마다 스키마 | 표현 불가(타입 필드) |
   | 지갑 < 0 또는 > 2147483647 | 스키마, 검증기, `SaveTree` | 표현 불가(`Crowns`) |
   | `completed`, `rewardLedger`, `dayJobs` 등의 중복 키 | 스키마 `uniqueItems`, `LedgerDuplicate` | 표현 불가(`KeySet`) |
   | 플래그 값이 bool·정수가 아님, 중복 플래그 id | 읽을 때마다 `FlagTable.TryParse` | 표현 불가. 중복은 불러올 때 거절 |
   | `absoluteMinute` < 0, fraction이 [0,1) 밖 | 읽을 때마다 `ClockStateReader`, 스키마 | 표현 불가(`ClockState` 생성자) |
   | 예약 status가 열거값 밖, 음수 `acceptedAt`, 중복 id | 읽을 때마다 `AppointmentCodec`, 스키마 | 표현 불가(`AppointmentBook`) |
   | 최상위 필드가 정확히 45개가 아님 | 저장 때 `CheckRoot` | 표현 불가(`Write`가 타입 이름 + 보존 이름을 씀) |
   | 저장 목록에 들어가는 id가 패턴과 다름 | 커밋마다 스키마 | 실행 중 검사, 콘텐츠를 읽는 곳(`SaveId.TryParse`) |
   | `classRecords.completed`와 `completed` 불일치 | 커밋마다 검증기 | 실행 중 검사, 저장·불러오기 때 |
   | 아이템 한도, 대여 범위, 가방 용량, 연애 범위, 참조 | 커밋마다 검증기 | 저장·불러오기 때. 게임에서 이 필드를 쓰는 곳이 없음 |
   | 선언된 변수가 모두 `/flags`에 있음 | 커밋마다 검증기 | 저장·불러오기 때. 플래그는 늘기만 함 |

   테스트와 probe에서는 커밋마다 전체 검증을 도는 감시 리스너를 붙인다(C에서 가져옴). 실제 게임의 매 프레임 비용은 없애고, 안전망은 검증 환경에 남긴다.

4. **시계.** 4.3처럼 바꿀 수 있다. 매 프레임 일은 `Calendar.Plan` 한 번, 새 `ClockState` 하나, 참조 교체 한 번이다. C의 프로토타입에서 틱 10만 번에 35ms(틱당 0.35µs, 달력 계산 제외)였다. 옛 경로의 틱 비용은 재지 않았다. 자정 경계 기록은 유지한다. 시계 차이 비교(B 실험)는 1,260걸음, 자정 통과 296번에서 바이트 차이가 0이었다.

5. **월드 위치.** `ActorWorld`가 실시간 위치의 권위로 남는다. `WorldProjection.Capture()`가 `WorldStateProjector`와 `WorldCommitParticipant.Prepare`의 지도 범위 검사를 대신한다. `WorldState.Project`는 순수 병합 함수다. 플레이어 위치를 쓰고, NPC 행을 id로 병합하고, 비활성 행은 유지한다. 숫자 종류는 `SaveNumber.MovedTo`로 유지한다. 안전 위치만 투영하는 규칙은 지금처럼 호출하는 4곳(장면 진입, 이벤트 성공, 초대 수락, 대기 중 재저장)에 둔다. `SafeBoundaryPolicy`는 같은 논리를 타입 스냅샷 위에서 돌린다. 캡처와 커밋이 한 동기 호출 안에서 일어나므로 `ACTOR_MOVED`, `ACTOR_NOT_ACTIVE`는 생길 수 없다.

6. **저장 경계.** 변환은 `SaveDocument` 한 곳이다. 바이트 비교 방법은 세 가지다.
   - **왕복 명세.** `Serialize(Write(Read(t))) == Serialize(t)`를 정상 저장 파일, 픽스처, 로컬 probe 저장에 대해 확인한다. 이 명세는 계속 남긴다.
   - **이행 중 차이 비교.** 같은 상태에서 옛·새 쓰기를 돌려 직렬화 바이트를 비교한다.
   - **게임 안 비교.** 마지막에 probe의 `initial.json`, `final.json`, `entry.json`을 기준 실행과 바이트로 비교한다.

   주의할 점이 있다. Unity가 쓴 저장 중 17자리 double이 들어간 4개는 .NET 테스트에서 같은 바이트가 나오지 않는다. Mono와 .NET의 `ToString("R")` 차이다(추론). 따라서 Unity 저장의 바이트 비교는 게임 안 probe와 `run-restart.ps1`로 한다. 코덱은 직렬화기를 바꾸지 않으므로 이 차이를 새로 만들지 않는다.

7. **Contracts 축소.** 상태 장치 타입 15개를 지운다. `StateSnapshot`(DataNode판), `PlannedChange`, `StateReplacement`, `StatePlan`, `StateCommitted`, `IStateParticipant`, `IStateReader`, `IStateStore`, `IStateInvariant`, `ChangeSet`, `OperationResult`, `EffectContext`, `IWorldStateProjector`, `IClockCostPlanner`, `IEventCommitPlanner`가 그것이다. 남는 타입 중 두 멤버도 함께 지운다. `ICalendar.FromSnapshot(StateSnapshot)`과 `EventCompletion.Operations`(`OperationResult` 목록, 읽는 곳 없음)다. 이 둘을 빠뜨리면 Contracts가 컴파일되지 않는다(12절 F3에서 빌드로 확인).
   - 스냅샷이 시그니처에 들어간 인터페이스 6개(`IConditionEvaluator`, `ConditionReport`, `ISafeBoundaryPolicy`, `SafeSnapshot`, `ISafeBoundaryRenewal`, `ISceneRecovery`)는 단위 5에서 Domain으로 옮긴다. Contracts는 Domain을 참조할 수 없어서, 옮기지 않으면 빌드가 실패한다.
   - 나머지 비경계 타입 44개는 단위 6에서 Domain 폴더로 옮긴다. 네임스페이스는 스크립트로 바꿔 다시 돌릴 수 있게 한다.
   - asmdef 참조는 바뀌지 않는다. UnityAdapters와 Presentation은 원래 이 타입을 안 쓴다. Unity 빌드가 이것을 증명한다.
   - 새 파일이 기존 폴더(`State/`, `Transactions/`, `Persistence/`, `World/Movement/`)에 들어가므로 루트 csproj의 glob도 바뀌지 않는다. `ContentIOTests`는 Contracts만 참조하는 경계 증명으로 남긴다.

8. **테스트.**
   - 내부 구조 검사(720줄)는 그 장치와 함께 지운다. `StateSpecs`의 소유·겹침·레지스트리·포인터 검사, `ClockParticipantSpecs`, `EventRunnerSpecs`의 호출 순서 기록이 여기에 든다.
   - 계획 API로 준비하는 동작 검사(563줄)는 단언을 유지하고 준비 단계를 `SaveDocument.Read(fixture)` + `GameStore.Commit`으로 바꾼다. 예를 들면 `EffectsSpecs.cs:543`(연습은 아무것도 얻지 않음), `:338`(거절 시 상태 불변), `StateSpecs.cs:306`, `ContentValidationSpecs.cs:65`다.
   - 예외가 하나 있다. 같은 거래 id를 다시 커밋하면 첫 결과를 돌려준다는 단언 4개는 유지할 수 없다. `EffectsSpecs`의 "replayed transaction returns the first result", `ContentValidationSpecs`의 "transaction replay no second state replacement", `StateSpecs`의 `SameTransactionReplay`, `ReplaceAllSuccessAndFailure`의 replay 부분이다. 재실행 캐시를 지우면 이 넷은 "다시 내면 거절되고 상태와 장부·지갑은 그대로"를 단언하게 바꾼다. 플레이어가 보는 약속(중복 보상 없음)은 같다.
   - 동작 검사(1,750줄)는 대부분 그대로 둔다. `EventRunnerSpecs`의 커밋 횟수 세기는 리비전 차이로 바꾼다.
   - 새로 넣는 것은 저장 왕복 명세, 이행 중 차이 비교 명세(단위 4c에서 지움), 보존 필드 소스 검사, 전이 명세(보상, 연습, 2147483647 넘침, 중복, 자정, 위치 1.5→2.0→1.5)다.

9. **probe 증거.** 런타임에 `DataNode StateTree() => SaveDocument.Write(Store.Current.State)`를 둔다. probe는 `Rt.Store.Current.Root` 대신 이것을 부른다(`FirstConnectionProbe.cs` 74, 192, 199, 237, 267, 293, 795행). `Dump`는 바꾸지 않으므로 `initial.json`, `final.json`, `entry.json`은 같은 45개 필드 글이다. 참고로 `Dump`는 지금도 정수값 실수를 `0`으로 써서 저장 파일(`0.0`)과 다르다(54개 파일에서 확인). 증거 형식은 원래 `Dump` 형식이므로 바뀌지 않는다. 리비전은 지금처럼 상태가 바뀐 커밋마다(틱 포함) +1, 교체마다 +1이다. 그래서 `rev=`, `rrev=`, `checkpoint.json`의 값이 같다. 게임 안 확인은 단위 4a의 E3이다.

10. **단계 나누기.** 7절이다. 옛·새 경로가 함께 사는 기간은 단위 3~4c다. 옛 계획 장치는 마지막 생산자가 옮겨 가는 4c에서 한꺼번에 지운다. 단위 5에서 다리(`TypedState()`)와 트리 기반 저장소를 지운다.

## 6. 거절 대응표

U는 표현 불가, T는 타입 불변식, R은 실행 중 검사(함수 이름)다. 줄 번호는 `659cdb3` 기준이다.

| # | 지금 거절하는 곳 | 새 모델 | 종류 |
|---|---|---|---|
| 1 | once 이벤트가 이미 소비됨, 시작 시(`EventRunner.cs:161-164`, `StateCompletionLedger`) | `EventRules.IsSpent` → `Progress.IsConsumed`, CONDITION_FALSE | R |
| 2 | 같은 검사, 미리 계획(`EventRunner.cs:168-169`) | 1에 합침(분기 삭제) | R |
| 3 | 계획 시 키 소비됨(`EventCommitPlanner.cs:58-61`) | `EventRules.Complete` 첫 검사 | R |
| 4 | 저장소의 `plan.Completion` 재확인(`StateStore.cs:106-107`) | 3과 같은 검사, 진단 `COMPLETION_ALREADY_RECORDED` 유지 | R |
| 5 | 참가자 검사(`EventEffectsParticipant.cs:222-223`) | `Progress.Claim` | R |
| 6 | 완료된 키의 예약 수락(`ReservationParticipant.cs:90-91`) | `ReservationService.Accept` → `IsConsumed` | R |
| 7 | 후보 목록에서 완료 제외(`ReservationService.cs:58`) | 같은 함수로 거름 | R |
| 8 | 이미 완료된 `complete(id)`(`EffectPlanner.cs:112-113`, 참가자 `:177`) | `Progress.Complete`, CONDITION_FALSE | R |
| 9 | 이벤트 키로 `complete(id)`(`EffectPlanner.cs:107-108`) | `EventOutcome.Parse`, CONTENT_INVALID | R |
| 10 | 한 계획에 `event.complete` 두 개(참가자 `:88, :208`) | `EventOutcome`에 claim은 하나뿐 | U |
| 11 | claim 키·정책이 카탈로그와 다름(`:204, :206`) | claim을 카탈로그 이벤트에서 만듦(`CompiledEvent.OnceKey`) | U |
| 12 | 카탈로그에 없는 이벤트(참가자 `:201`, planner `:43`, 예약 `:74`) | `EventRules.Complete`, `Accept`, REF_NOT_FOUND | R |
| 13 | 계획·문맥 completion 불일치(`:217`, planner `:50-51`) | 계획·문맥이 없음 | U |
| 14 | `/rewardLedger` 중복(`SaveStateValidator.cs:67-69`) | `KeySet` | T |
| 15 | 연습 실행의 `currency.add`(`EffectPlanner.cs:129-130`, 이벤트 전체 실패) | `EventOutcome.Parse`, TRANSACTION_MISMATCH. 이후 Practice에는 크라운 자리가 없음 | R, U |
| 16 | 연습 `event.complete`에 계획 completion(`:213`) | 계획 completion이 없음 | U |
| 17 | once `event.complete` 없는 `currency.add`(`:185-186`) | 크라운은 `EventOutcome.Once`에만 있음 | U |
| 18 | 연습 시간 비용 0 강제(planner `:196`) | Practice에는 분 자리가 없음 | U |
| 19 | 금액이 정수가 아님(`EffectPlanner.cs:125-126`) | `EventOutcome.Parse`, ARG_TYPE | R |
| 20 | 금액 ≤ 0 또는 > int.MaxValue(`:127-128`) | `EventOutcome.Parse`, INVARIANT_VIOLATION | R |
| 21 | 지갑 넘침(`:135-136`) | `Progress.Earn` → `Crowns.Added`, INVARIANT_VIOLATION | R, T |
| 22 | 합계 넘침(참가자 `:189-190`) | 21과 같음(Parse에서 합산, 한 번 더함) | R, T |
| 23 | `/wallet`이 음이 아닌 정수가 아님(`SaveTree.cs:72-74`) | `Crowns`. 디스크 입력은 불러오기 경계 | T |
| 24 | 커밋 뒤 지갑 음수·최대 초과(검증기 `:64-65`, 스키마) | `Crowns` | T |
| 25 | 거래 id 없음(`StateStore.cs:82-83`) | `GameStore.Commit`, TRANSACTION_MISMATCH | R |
| 26 | 커밋 중 다시 커밋(`:84-85`) | `GameStore.Commit`의 busy 표시, INVARIANT_VIOLATION | R |
| 27 | 같은 거래 id 재실행은 첫 결과 반환(`:86`) | 없앰. 거절이 아니라 중복 제거이고, 게임 코드에는 같은 id를 다시 내는 곳이 없음(모든 id에 순번이 붙음). 반복 이벤트는 3에서 거절. 이 동작을 단언하는 명세 4개는 "다시 내면 거절되고 상태·보상은 그대로"로 바꾼다(8절, 12절 F1) | 삭제 |
| 28 | 리비전 불일치(`:103-104`, 참가자 `:33`, `EventRunner.cs:785-789`) | 전이는 `Commit` 안에서 현재 상태로 돔 | U |
| 29 | 참가자 미등록·예외·null, 잘못된 포인터, `SLICE_NOT_OWNED`, 겹침, 쓸 수 없는 경로, 레지스트리 시작 검사 | 참가자·경로가 없음. 전이가 예외를 던지면 STORE_EXCEPTION | U, R |
| 30 | 커밋 시 불변식·스키마(`StateStore.cs:162-163`) | 5절 3번 표 | T, R |
| 31 | 월드 연산·데이터 오류, 비활성 액터, `ACTOR_MOVED`(`WorldCommitParticipant.cs:58-137`) | 캡처와 커밋을 한 호출에서 함 | U |
| 32 | 위치가 지도 밖(`:101-102`) | `WorldProjection.Capture`, POSITION_INVALID | R |
| 33 | 시계를 못 읽음, 잘못된 연산·데이터(`ClockParticipant.cs:49-76`) | 타입 `ClockState` | U |
| 34 | 시간(분)이 잘못됨(달력 예외) | `Calendar.TryCheckMinutes`, CONTENT_INVALID | R |
| 35 | 예약 행을 못 읽음, 잘못된 `acceptedAt` | 타입 `AppointmentBook`, 불러오기 경계 | U |
| 36 | 예약 완료의 completion 불일치(`ReservationParticipant.cs:104`) | 예약 제거가 같은 `Complete` 안에서 일어남 | U |
| 37 | `event.complete` 없는 예약 완료(연습 이벤트 + 예약 행, `:108`) | `EventRules.Complete`, TRANSACTION_MISMATCH(유지, 결정 D4) | R |
| 38 | 정의가 카탈로그와 다름, 시계·월드 planner가 남의 변경을 씀(planner `:44-45, :200-234`) | 계획이 없음. 규칙이 카탈로그를 직접 읽음 | U |
| 39 | 등록 안 된 효과(`item.grant`, `spell.learn`), 부정·대안 효과, `set` 인자·타입·미선언·범위 | `EventOutcome.Parse`, 같은 코드 | R |
| 40 | staged 비용 검사(planner `:110-177`) | 도달 불가 경로와 함께 삭제(단위 1) | 삭제 |

여러 결함이 겹친 입력은 첫 결과 코드가 다를 수 있다. 예를 들어 "키 소비됨 + 지갑 넘침"은 지금 INVARIANT_VIOLATION이지만 새 모델에서는 CONDITION_FALSE다. 실제 planner가 만들 수 없는 수제 계획에서만 생긴다(E2, 10절).

## 7. 구현 순서

검사 명령은 프로젝트 루트에서 PowerShell 5.1로 돌린다.

- **T**: `powershell -ExecutionPolicy Bypass -File Tests\PureDomain\run-all.ps1 -Tag s3-uN`. 기대 결과는 4종 모두 PASS, FAIL 0이다. 스위트별 PASS 수는 명세를 지우거나 더하면 바뀌며, 단위 기록에 적는다.
- **B**: `powershell -ExecutionPolicy Bypass -File .claude\skills\verify-magiccampus\scripts\build.ps1 -Build Stage5-verify`. 기대 결과는 오류 0, 경고 0이다.
- **P**: `run-probe.ps1 -Cases success-notes,success-tea,moved,a10,cancel,closed,blocked,map-fail,disk-fail,intro-false,validation,ui-long,ui-200 -Build Stage5-verify -Run s3-uN`. 기대 결과는 모두 pass다. ui-long과 ui-200은 사람 검토 1건씩 건너뛴다.
- **R**: `run-restart.ps1 -Phases move,say,choice -SuccessFrom s3-uN -Build Stage5-verify -Run s3-uN-r`. 기대 결과는 4건 pass(checks 11, 11, 11, 52)다.
- 이벤트 틱(`fork.enter`)은 같은 커밋을 두 번 돌려 흔들림 범위 안인지 본다(success-notes 122~123, a10 151~152).

| 단위 | 바꾸는 것 | 검사 | 이 단위 뒤에 남는 옛 경로 |
|---|---|---|---|
| 1 | 먼저 덜어내기. staged 비용 분기, `OperationResult`와 그것을 담는 `EventCompletion.Operations`, `EffectContext.Staged`, 미리 계획 분기, `SaveSanitizer`(스키마상 no-op)를 지운다. 같은 식 5벌(`Completion ?? new CompletionKey(Id)`)을 `CompiledEvent.OnceKey` 하나로 모은다. 같은 단위에서 명세 3개를 고친다. (가) `RepeatBlocked`를 필수로 바꾸고 `EventRunnerSpecs`의 Rig가 완료 장부 검사를 연결한다. `OnceGuardRefusesBeforeLeasing`(288-291행)의 "대여 전에 거절" 단언은 유지한다. (나) `PersistenceSpecs.SanitizerChecks`(208행)의 대여품 제거 검사는 지운다. 대여품은 커밋 때 스키마가 막아 저장까지 갈 수 없다. (다) `EffectsSpecs.cs:543`은 staged 입력 없이 "연습은 보상도 시간 비용도 없다"를 단언하게 바꾼다. | T, B, P, R | 전부 |
| 2 | `GameState.cs`, `SaveDocument.cs`(Read, Write, Admit), 저장 왕복 명세, 새 전이 명세를 추가한다. 전이 명세에는 `rewardLedger`에만 있는 키의 재보상 거절과 지갑 정확히 2147483647/한 칸 넘침을 넣는다. 이 두 규칙은 지금 planner와 커밋 스키마만 지키고 있어서, 그 장치를 지우는 4c·5보다 먼저 들어와야 한다(12절 F4). 호출처는 안 바꾼다. | T(새 명세 통과), B | 전부 |
| 3 | 읽는 쪽을 옮긴다. 스냅샷마다 캐시되는 `snapshot.TypedState()` 다리를 둔다. 조건 평가기, 완료 검사, 달력 읽기, 예약 행, `BuildActors`, HUD, probe의 `Wallet`·`Count`가 타입 부분을 읽는다. | T, B, P, R | 다리, 모든 쓰는 쪽 |
| 4a | 혼합 저장소. `StateStore.Commit(txn, 전이 함수)`가 `SaveDocument.Write(next)`를 쓰고 옛 커밋 불변식을 계속 돌린다. 그래서 새 전이가 모두 스키마 감시를 받는다. 시계 틱을 `ClockState.Advance`로 옮긴다. | T, B, P, R, E3(`rev=` 동일) | `ClockParticipant`(이벤트 시간 비용이 아직 씀) |
| 4b | `WorldProjection` + `WorldState.Project`를 장면 진입, 초대 수락(`ReservationService.Accept`와 함께), 재저장에 적용한다. | T, B, P, R | 월드·예약 참가자(이벤트가 아직 씀) |
| 4c | 이벤트를 `EventRules.Complete`로 옮긴다. 계획을 만드는 곳이 더 없으므로 같은 단위에서 참가자 전부, 레지스트리, `StatePlan` 계열, `StateTree`, `JsonPointer`, planner, `SaveTree`, `AppointmentCodec`, `StateCompletionLedger`를 지운다. 내부 명세를 지우고 계획 API 명세는 준비 단계를 바꾼다. 차이 비교 명세는 마지막으로 돌리고 지운다. | T, B, P, R | 다리, 트리 기반 저장소 |
| 5 | 뒤집기. `GameStore`가 `StateSnapshot(revision, GameState)`를 든다. 커밋 불변식과 `IStateInvariant`를 지우고 `Seal`이 검증한다. `SaveRestorer`는 `Admit`으로 `GameState`를 돌려준다. 불러오기와 장면 복원은 `Replace`다. probe는 `SaveDocument.Write`로 덤프한다. 테스트·probe 전용 감시 리스너를 붙인다. 다리와 Contracts의 상태 타입을 지운다. | T, B, P, R, E3 전체(`initial/final/entry.json` 바이트 동일, `rev=` 동일) | 없음 |
| 6 | Contracts 축소. 남은 비경계 타입 44개를 Domain 폴더로 옮긴다(네임스페이스 변경 스크립트). | B(asmdef 경계), T, P 중 success-notes | 없음 |

E2 프로토타입은 단위 4c의 규칙 부분을 미리 해 본 것이다. 4c의 기대 결과는 E2에서 이미 한 번 관찰했다.

## 8. 줄 수와 읽는 부담

**측정한 것.**
- 게임 C#은 `wc -l` 기준 18,037줄이다(빈 줄 포함. 진행 기록의 16,285줄과 세는 방식이 다르다).
- 지울 파일 17개는 1,959줄이다. 상태 관련 JSON 등장은 592곳 중 약 233곳이다.
- 보상 질문은 지금 파일 약 9개, 계층 6개를 거친다(근거 01).

**추정한 것.** 줄 수 추정은 A안 기준이다.

| 폴더 | 지금 | 단위 5 뒤 | 변화 |
|---|---|---|---|
| Contracts | 1,352 | 약 1,205 | 약 -147 |
| State | 409 | 약 500 | 약 +91 |
| Transactions | 1,368 | 약 710 | 약 -658 |
| Time | 506 | 약 205 | 약 -301 |
| World | 1,696 | 약 1,440 | 약 -256 |
| Application | 2,328 | 약 2,080 | 약 -248 |
| Persistence | 864 | 약 1,120 | 약 +256 |
| Content, Bootstrap | 5,812 | 약 5,750 | 약 -62 |
| Runtime, Presentation, UnityAdapters | 3,702 | 3,702 | 0 |
| 게임 코드 합계 | 18,037 | 약 16,710 | 약 -1,330(±300) |
| Tests/PureDomain | 8,120 | 약 7,240 | 약 -880 |

**읽는 부담(추정).** 보상 질문에 여는 파일은 `EventRules.cs`, `GameState.cs` 둘이다. 상태가 바뀌는 길은 `GameStore.Commit` 하나다. 숨은 규칙은 다음 것들이 사라진다.
- 경로 소유
- 참가자 등록 순서
- 같은 기준 상태 위에서 따로 도는 `Prepare`
- 문자열 상수로 다른 참가자의 데이터를 읽는 결합(`ReservationParticipant.HasEventCompletion`)

## 9. 사용자에게 가져갈 결정

| # | 결정 | 선택지 | 추천과 이유 |
|---|---|---|---|
| D1 | 저장 파일 형식(save v2, 45개 필드)을 바이트 수준으로 유지할지 | 유지 / 변경 | **유지.** 추천안은 형식 변경이 필요 없다. ADR-0001 결정 8과도 맞는다. |
| D2 | 아직 없는 시스템(전투, 상점, 생활 등)의 저장 필드 | 불투명 보존 / 타입만 / 저장에서 빼기 | **불투명 보존.** 약 10줄로 바이트가 보장되고, 시스템이 생길 때 그 필드만 타입으로 올린다. 빼면 형식과 ADR이 바뀐다. |
| D3 | 숫자 표기 정규화(위치와 `fraction`을 항상 실수로 씀) | 하지 않음 / 함 | **하지 않음.** 하면 `SaveNumber`가 사라지지만 이동 뒤 첫 저장부터 바이트가 달라진다(D1과 충돌). |
| D4 | 연습 이벤트에 예약 행이 있으면 수락은 되는데 완료는 언제나 거절되는 동작 | 지금처럼 유지 / 나중에 고침 | **이번엔 유지.** 같은 동작을 지키는 리팩토링이므로 고치는 것은 별도 결정이다. 버그로 보인다(코드 읽기 추론). |
| D5 | v1 저장 변환(`SaveMigration`, 실제 v1 저장 없음) | 남김 / 나중에 지움 | **이번엔 남김.** 3단계 범위 밖이다. |
| D6 | 콘텐츠 스키마(`design/systems/schemas`, `functions.json`) 변경 | 안 바꿈 / 바꿈 | **안 바꿈.** 추천안은 콘텐츠 스키마를 건드리지 않는다. |
| D7 | `dayJobs`, `returnPending`(쓰기만 하고 읽는 곳 없음) | 유지 / 지움 | **유지.** 지침서가 유지하라고 했고, 비용이 거의 없다. |
| D8 | 구현 시작 승인 | 승인 / 보류 | 7절 단위 1부터 별도 세션에서 진행하기를 추천한다. |
| D9 | 실제 게임에서 커밋마다 하던 전체 검증을 없애는 것. `classRecords` 일치 규칙은 커밋 때가 아니라 저장·불러오기 때 거절된다 | 없앰(테스트·probe는 유지) / 게임에서도 유지 | **없앰.** 이 검사가 막던 상태는 대부분 타입이 표현 불가로 만든다(5절 3번 표). `classRecords` 규칙은 지금 콘텐츠로는 생기지 않는다. 픽스처 6개의 기록이 모두 `completed: true`다(직접 확인). 안전망을 낮추는 결정이라 사용자에게 올린다. |

**사용자 결정(2026-11-05).** D1~D9를 모두 추천대로 확정했다. 원문: "추천대로 확정". 확정 결과는 다음과 같다.
- D1 저장 형식 유지. D2 미구현 시스템 필드는 불투명 보존. D3 숫자 표기 정규화 안 함.
- D4 연습 이벤트 예약 행 동작 유지. D5 `SaveMigration` 남김. D6 콘텐츠 스키마 안 바꿈. D7 `dayJobs`, `returnPending` 유지.
- D8 구현 승인. 7절 단위 1부터 별도 세션에서 진행한다.
- D9 게임 안 커밋마다 하던 전체 검증을 없앤다. 테스트·probe의 전체 검증은 유지한다.

설계 쪽에서 정한 것(되돌릴 수 있음)은 다음과 같다.
- 같은 거래 id 재실행 캐시를 없앤다. 게임 코드에서 거래 id를 만드는 7곳은 모두 순번이나 실행별 고유 id를 붙인다(직접 grep 확인). 그래서 같은 id를 다시 내는 곳이 없다. 매 프레임 쌓이는 문제(측정하지 않은 추론)도 함께 사라진다. 이 동작을 단언하는 명세 4개는 바뀐다(8절).
- 여러 결함이 겹친 입력의 첫 결과 코드가 바뀌는 것을 받아들인다.

## 10. 프로토타입 결과

프로토타입 코드는 브랜치 `stage3-proto`에 있고 master에 합치지 않는다.

| 실험 | 무엇을 | 관찰 | 지지 |
|---|---|---|---|
| 저장 바이트 기준선 (`Tests/Prototype/SaveBytes`, 커밋 `977a016`) | 저장 302개를 해독해 다시 직렬화·봉인 | .NET이 만든 216개는 모두 같은 바이트다. 해독 불가 4개는 일부러 만든 58바이트 고장 파일이다. Unity가 만든 82개 중 4개는 17자리 double 표기 차이로 다르다. `clock.fraction`의 약 20%는 정수값인 실수(`0.0`)라 숫자 종류를 지켜야 한다. | 숫자 종류 보존(`SaveNumber`) 필요. Unity 저장 비교는 게임 안에서 |
| 타입 상태 왕복 (C 프로토타입, 브랜치 `Tests/Prototype/Arena/candidate-c/proto`) | `FromTree → ToTree → Serialize` | 정상 저장 298개와 픽스처 6개에서 바이트 차이 0. 규칙 점검 21개 통과. 틱 10만 번에 35ms. 298개 중 git에 있는 것은 17개이고, 나머지는 이 PC의 git 무시 폴더에 있다. 브랜치만으로 돌리면 17개 + 픽스처 6개를 본다(둘 다 직접 다시 돌려 ALL PASS 확인). C는 `classRecords` 규칙을 커밋 때 검사하므로, 그 규칙에 한해서는 A의 근거가 되지 않는다 | A·C 모양 |
| 시계 차이 비교 (B 프로토타입) | 옛 `ClockParticipant` 대 새 시계 | 1,260걸음, 자정 통과 296번, 정수→실수 전환 포함, 바이트 차이 0 | 4.3 |
| 이벤트 효과를 새 규칙으로 (E2, 커밋 `e1555ab`) | `EventEffectsParticipant`의 규칙을 타입 `Progress`/`EventRules`로 교체하고 기존 명세 전체 + 옛/새 비교 | 4종 PASS 10/5/16/9, FAIL 0. 명세 수정 0건. 일부러 장부 쓰기를 빼면 명세 2개가 실패해 명세가 새 규칙을 실제로 검사함을 확인. 실제 흐름 135건 차이 0. 수제 계획 31건 중 7건 차이(planner가 만들 수 없는 입력: 0·음수 금액, 겹친 결함의 첫 코드, 순서, 손상된 기준 트리) | A |

확인하지 않은 것은 다음과 같다.
- 게임 안 리비전·증거 동일성(E3)은 아직 돌리지 않았다. 단위 4a의 검사로 남긴다.
- 월드 투영과 예약의 새 규칙은 C 프로토타입의 규칙 점검으로만 확인했다. 옛 참가자와 직접 비교하지는 않았다.
- 매 프레임 비용은 새 경로만 쟀고, 옛 경로는 재지 않았다.

## 11. 위험과 되돌리기

- **여러 결함 입력의 결과 코드 변화.** 한 가지 결함만 있는 입력의 코드는 같다(E2). 진단 문자열을 단언하는 명세가 있으면 그때 확인한다.
- **혼합 기간 비용.** 단위 3~4에서 커밋마다 트리를 읽고 쓴다. 지금의 매 프레임 스키마 검사와 같은 자리에 있는 임시 비용이고, 단위 5에서 사라진다.
- **보존 필드에 손대는 새 코드.** 새 시스템이 `PreservedFields`를 직접 고치면 검증 없이 저장될 수 있다. 소스 검사 명세가 막고, 저장 때 검증이 마지막 안전망이다.
- **`classRecords` 규칙의 시점 변경.** 커밋 때가 아니라 저장 때 거절된다. 픽스처 6개의 기록이 모두 `completed: true`라 지금 콘텐츠로는 생기지 않는다(직접 확인). 결정 D9다.
- **되돌리기.** 각 단위는 커밋 하나이고 저장 바이트가 바뀌지 않는다. 어느 단위든 `git revert` 하나로 되돌릴 수 있고 저장 파일에는 영향이 없다.

## 12. 반대 검토(interrogate) 결과

Opus high와 Sonnet high 검토자가 각자 worktree에서 설계의 주장 7개를 깨 보았다. 실행으로 재현된 것만 남겼다. 아래 넷은 설계 세션이 직접 다시 돌려 같은 출력을 확인했고, 위 절들에 반영했다. F4는 `IsConsumed` 변이만 직접 재현했다. 지갑 한도 변이는 Sonnet 검토자의 실행 보고다.

| # | 지적 | 재현 | 반영 |
|---|---|---|---|
| F1 | 재실행 캐시를 지우면 명세 4개가 실패한다. 계획 API 명세의 단언을 "그대로 유지"한다는 8절과 맞지 않는다(두 검토자 중 Opus) | 캐시 한 줄 삭제 패치 → Stage2 FAIL 1, Stage3 FAIL 2 | 6절 27행, 8절: 단언 4개를 "다시 내면 거절, 상태 불변"으로 바꾼다고 명시 |
| F2 | 단위 1만 적용하면 통과하지 않는다. `EventRunnerSpecs` Rig가 미리 계획 분기에 기대고, `SanitizerChecks`가 대여품 제거를 단언한다(두 검토자 모두) | 패치 → Stage4 FAIL 2(NullReferenceException, "loan stacks stripped") | 7절 단위 1에 명세 3개 수정을 넣음 |
| F3 | Contracts 축소 목록이 모자라 컴파일되지 않는다. `ICalendar.FromSnapshot`, `EventCompletion.Operations`(Sonnet) | 목록대로 지운 Contracts 사본 빌드 → CS0246 3건 | 5절 7번, 7절 단위 1 |
| F4 | 지금 명세로는 잡히지 않는 규칙이 둘 있다. `IsConsumed`가 `rewardLedger`를 안 봐도, 지갑 한도가 1 어긋나도 4종이 모두 통과한다. 지금은 planner와 커밋 스키마가 대신 지키지만 그 장치는 4c·5에서 사라진다(Sonnet) | `IsConsumed` 변이 → 4종 PASS, FAIL 0 | 7절 단위 2에 두 전이 명세를 먼저 넣음 |

그 밖에 보관한 B·C 프로토타입이 브랜치에서 빌드되지 않던 경로 오류도 지적받았다. 경로를 고쳐 `stage3-proto` `0c22d84`에 커밋했고 다시 돌려 통과했다. 두 검토자 모두 저장 바이트(주장 1), 리비전(주장 3), 상태를 읽고 쓰는 곳 목록(주장 6)에서는 깨지는 것을 찾지 못했다. 커밋마다 하는 검증을 뺀 채 E2를 다시 돌려도 실제 흐름 135건이 같았다(Opus). 다만 리비전과 읽는 곳 목록은 코드 읽기와 grep으로 본 것이고, 게임 안 확인(E3)은 아직이다.

## 13. 근거 파일

- 근거 조사: `Tests/Results/refactor/stage3-grounding/01-commit-pipeline.md`, `02-save-load.md`, `03-state-readers.md`, `04-contracts-tests.md`
- 후보 설계와 과제문: `Tests/Results/refactor/stage3-arena/`(브랜치 `stage3-proto`의 `Tests/Prototype/Arena/`에 사본)
- 결정 기록: `Tests/Results/refactor/stage3-design-decisions.tsv`
- 프로토타입: 브랜치 `stage3-proto`의 `Tests/Prototype/SaveBytes`, `Tests/Prototype/EventParity`, `Tests/Prototype/Arena`
- 반대 검토 재현 파일: 브랜치 `stage3-proto`의 `Tests/Prototype/Review/opus`, `Tests/Prototype/Review/sonnet`(커밋 `d1a97f2`)
