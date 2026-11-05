# 과설계 정리 리팩토링 (2026-11-03)

## 결정

- 2026-11-03 사용자: "나는 이 게임이 현재 시스템적으로, 코드적으로 너무 과하게 설계되어 있다고 생각한다. 리팩토링이 필요하다."
- 2026-11-03 사용자: "2단계까지 진행". 1단계(죽은 코드 삭제)와 2단계(구조 단순화)를 한다. 3단계(JSON 트리 상태를 타입 있는 게임 상태로 바꾸기, Contracts 축소)는 하지 않는다.

## 지키는 것

- 게임 동작은 바꾸지 않는다. 저장 형식(save v2, 45개 필드), 콘텐츠 스키마(`design/systems/schemas`, `functions.json`), 콘텐츠 JSON은 바꾸지 않는다.
- probe 시스템과 Runtime 장애 주입 스위치(`FailMapId`, `FailDisk` 등)는 검증 스킬이 쓰므로 남긴다.
- 어댑터 인터페이스(ICollisionPort, IMapPort, IInputPort, IFileGenerationStore, IDataCodec)는 Unity 없는 테스트를 위해 남긴다.
- 아직 만들지 않은 시스템용 저장 필드와 함수 정의는 기획 범위라서 건드리지 않는다.

## 기준 검증

- 기준 커밋: `f463071`.
- 순수 도메인 테스트: `Tests/PureDomain`의 ContentIOTests, Stage2Tests, Stage3Tests, Stage4Tests(`-- --real-disk`). 기준 로그는 `Tests/Results/refactor/baseline-*.log`.
- 게임: `verify-magiccampus` 스킬의 probe 13개 케이스와 강제 종료 복원(move, say, choice).
- 단계마다 같은 검사를 다시 돌려 통과 목록과 검사 수를 비교한다. 삭제한 기능만 검사하던 테스트는 함께 삭제하고 그 사실을 적는다.

## 1단계: 죽은 코드 삭제

- 1-H 검수 코드: Stage 1~4 일회성 검수(UnityAdapters/EditorTests, Presentation/Editor, Assets/MagicCampus/Tests, Editor, Scenes/Empty.unity), DialoguePrefabLoader와 프리팹, probe의 리플렉션 접근, Bootstrap asmdef의 안 쓰는 DLL 참조.
- 1-R 런타임·앱: 테스트에서만 쓰는 CancellationScope, ReservationCoordinator, TickDriver/Watchdog, CompletionPump 키 중복 제거, LeaseService 대기열, MoveDebug·이동 추적, 날짜 경계 소비자 장치, 빈 perform 레지스트리, 기본값만 쓰는 옵션 객체.
- 1-D 도메인: 가짜 v1 마이그레이션, AssemblyMarker, NoCompletionReplayPolicy, CatalogCompiler의 죽은 분기, 안 쓰는 결과 코드.

### 1단계 결과

- 1-H: 검수 코드 삭제(-2,610줄). probe가 대화창 내부 필드를 리플렉션으로 읽던 것을 공개 읽기 속성으로 바꿨다. Bootstrap·UnityAdapters asmdef에서 안 쓰는 DLL 참조를 뺐다.
- 1-R: 런타임·앱 죽은 코드 삭제(-1,655줄). 이동 추적만 확인하던 테스트 검사 3개(재경로 기록, 재경로 32회 이하, 포털 통과 기록)가 사라졌다. 도착·시간 제한·실패 코드 검사는 남았다. 숫자가 아닌 domain을 가진 `perform`의 실패 코드가 BAD_ARGUMENT에서 UNKNOWN_FUNCTION으로 바뀌었다. 컴파일된 콘텐츠에서는 생길 수 없는 경우다.
- 1-D: 도메인 죽은 코드 삭제(-126줄). v1 저장 변환은 실제 동작이라 남기고, 쓰이지 않던 ID 치환 장치만 뺐다.
- 검증: 순수 도메인 4종 통과(PASS 40), Unity 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다. 스크린샷 18장 중 8장은 캐릭터 대기 애니메이션 프레임만 다르다(ui-long 육안 비교).

## 2단계: 구조 단순화

- 2-A 효과·콘텐츠: 효과를 세 번 검증하는 흐름을 한 번으로 줄이고, 쓰지 않는 카탈로그 실행 중 교체 경로와 구현이 하나뿐인 콘텐츠 인터페이스를 없앤다.
- 2-B 상태·저장: 같은 경로 안의 중복 검증과 체크섬 재계산을 없애고, 참가자 레지스트리와 구현이 하나뿐인 저장 인터페이스를 단순화한다. 커밋마다 하는 상태 불변식 검사는 동작이므로 남긴다.
- 2-C 런타임·앱: 틱 인터페이스의 중복 인자, 의미 없어진 계약 필드, 항상 빈 값, 구현이 하나뿐인 인터페이스를 정리한다.
- 2-V 화면: 자체 JSON 리더를 기존 코덱으로 바꾸고 FirstConnectionRuntime의 안 쓰는 공개 멤버를 정리한다. 2-A~C를 합친 뒤에 한다.
- 2-B 결과(-165줄): 저장 복원의 체크섬 재계산, 도달할 수 없는 검사, 항상 null인 invariants 인자, 참가자 레지스트리 래퍼, 완료 재처리 정책 인터페이스, ISaveCodec·ISaveRestorer·IStatePlan을 없앴다. SaveFields.CheckRoot는 스키마와 다른 오류 코드를 내므로 남겼다.
- 2-C 결과(-157줄): 틱을 값 하나로 줄이고 IRuntimeTick·ITickClock·IEventRunner·ICompletionPump·IReservationService·IScheduleService와 중복 ID 검사(RuntimeGuard, IdGuard)를 없앴다. FirstConnectionRuntime의 안 쓰는 공개 멤버를 비공개로 바꿨다. ICompletionLedger는 테스트 가짜가 있어 남겼다. 게임 종료 정리(Dispose) 중 NPC가 최대 0.3초 거리를 더 걸을 수 있으나 저장·커밋이 없는 시점이다.
- 2-B·2-C 합친 뒤 검증: 순수 도메인 4종 통과, Unity 빌드 오류 0, probe 13개와 복원 4건이 기준과 같다.
- 이벤트 진행 틱 비교: 첫 기준 실행은 success-notes fork.enter가 123틱이었고 리팩토링 후는 122틱이었다. 기준 커밋 f463071을 다시 빌드해 두 번 돌리니 122틱이 나와 첫 기준 실행의 흔들림으로 판단했다. 마지막 refused 단계는 같은 빌드에서도 1틱씩 흔들린다.
- 2-A 결과(-200줄): 효과 검증을 EffectPlanner 한 곳으로 모으고, 저장 반영(EventEffectsParticipant)은 타입 있는 EffectChange를 그대로 적용한다. 효과 핸들러 플러그인, 카탈로그 실행 중 교체(TryReplace), ICallParser·ICatalogCompiler·IFunctionDictionary(Loader)·IEffectHandler·IEffectPlanner를 없앴다. 연습 실행의 돈 지급 거절은 같은 오류 코드(TRANSACTION_MISMATCH)로 남고 메시지 문구만 바뀌었다. 에이전트가 커밋 직전에 중단돼 코디네이터가 diff를 검토하고 커밋했다.
- 2-A 합친 뒤 검증(2026-11-05): 순수 도메인 4종 통과, Unity 빌드 오류 0, probe 13개와 복원 4건이 기준과 같다. 이벤트 틱은 123/152/160으로 첫 기준 실행과 같다.
- 테스트 실행 환경: Windows 앱 제어가 결정적 빌드의 일부 테스트 DLL을 해시로 차단해, run-all.ps1을 비결정적 빌드(-p:Deterministic=false)로 바꿨다.
- 2-V 결과(-149줄): 아트 매니페스트를 자체 JsonReader 대신 프로젝트 JSON 코덱(JsonDataCodec)으로 읽는다. 실제 매니페스트 3종과 변형 130건을 옛·새 경로로 읽어 모두 같았다. 문법이 틀린 JSON(중복 키, 016, \q)은 이제 거부된다. 실제 매니페스트에는 없다.
- 최종 검증(2026-11-05, 26b7a2c): 순수 도메인 4종 통과, Unity 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다. 월드·대화창 스크린샷은 캐릭터 대기 애니메이션 프레임만 다르다(moved, ui-200 육안 비교).
- 총 변화: 게임 C# 19,747줄 → 16,285줄(-3,462줄, 111 → 93개 파일), asmdef 9 → 6개, 테스트 C# 7,881 → 7,304줄.
- 하지 않는 것: 테스트 프로젝트 4개 통합(가치가 낮음), DTO 생성자 검증 제거(테스트가 예외를 검사함, 효과 대비 변경이 큼).

## 진행 기록

- 2026-11-03 git 기준점 `f463071` 생성, 기준 테스트 실행 시작.
- 2026-11-03 기준 결과: 순수 도메인 4종 통과, Unity 빌드 오류 0, probe 13개 통과(ui-long·ui-200은 사람 검토 1건 건너뜀), 강제 종료 복원 4건 통과. 요약은 `Tests/Results/refactor/baseline-probe.txt`.
- 2026-11-05 3단계 설계 완료: 불변 GameState + 순수 전이 + 참조 교체(A안) 추천, 저장 바이트·거절 코드 유지. 프로토타입에서 이벤트 효과를 새 규칙으로 바꿔도 순수 도메인 4종 통과, 실제 흐름 135건 옛 코드와 동일. 문서 `Docs/Refactoring-Stage3-Design.md`, 코드 브랜치 `stage3-proto`. 구현은 사용자 결정 D1~D9 뒤에 시작한다.
- 2026-11-05 사용자가 D1~D9를 추천대로 확정("추천대로 확정"). 3단계 구현은 설계 문서 7절 단위 1부터 별도 세션에서 시작한다.

## 3단계: 타입 있는 게임 상태

- 설계: `Docs/Refactoring-Stage3-Design.md`(A안, 결정 D1~D9). 단위마다 커밋 하나.
- 3-1 결과(`82542aa`, -232줄): staged 비용 분기, 미리 계획 분기, `OperationResult`·`EventCompletion.Operations`·`EffectContext.Staged`, `SaveSanitizer`를 지웠다. `Completion ?? new CompletionKey(Id)` 5벌을 `CompiledEvent.OnceKey`로 모았다. staged 분기만 검사하던 명세 2개를 지우고 비-staged 단언은 `EventCostIsTheOnlyTimeSource`로 옮겼다. 검증: 순수 도메인 4종 통과(10/5/16/9), 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다(`s3-u1-p2`, `s3-u1-r`). 첫 probe 실행에서 success-tea·moved·a10이 한 번 실패했고, 같은 실행 파일로 다시 돌려 통과했다. 실패한 실행의 player.log에만 Direct3D 프레임 통계 경고가 있었다(환경 흔들림으로 추정).
- 3-2 결과(`a58f840`, +863줄, 호출처 변경 없음): `State/GameState.cs`, `Persistence/SaveDocument.cs`(Read·Write·Admit), 명세 `GameStateTransitions`·`SaveDocumentRoundTrip`을 더했다. 왕복 명세는 픽스처 12개와 저장 파일 369개(git에 있는 것 17개, 나머지는 이 PC의 git 무시 폴더)에서 바이트가 같다. 규칙을 일부러 깨는 변이 11개를 넣어 각각 명세가 실패함을 확인했다(`rewardLedger`만 있는 키 재보상, 지갑 2147483647 한 칸 넘침 포함). 검증: 순수 도메인 4종 통과(10/5/16/11), 빌드 오류 0·경고 0. 연습 이벤트가 아무것도 얻지 않는 규칙은 4c의 `EventRules`에서 따로 명세한다.
- 3-3 결과(`33eb206`, +230/-167줄): 스냅샷마다 캐시되는 `TypedState()` 다리(`Persistence/TypedSnapshot.cs`)를 두고, 조건 평가기, 완료 장부, 달력·시계 읽기, 예약 후보, `BuildActors`, HUD, probe의 `Wallet`·`Count`가 타입 상태를 읽는다. 쓰는 쪽(참가자·planner)은 4단위까지 트리에 남는다. 읽을 수 없는 스냅샷은 필드별이 아니라 한 번에 거절된다(테스트에서만 생기는 경우). 완료 장부가 `rewardLedger`를 안 보면 4종이 모두 통과하던 빈틈을 명세 2개로 막았고, 그 변이를 넣으면 Stage4가 실패함을 확인했다. 검증: 순수 도메인 4종 통과(10/5/16/11), 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다(`s3-u3`, `s3-u3-r`).
- 3-4a 결과(`dd899f7`, +193/-110줄): `StateStore.Commit(txn, 전이 함수)`를 더했다. 타입 상태로 전이를 돌리고 `SaveDocument.Write` 결과를 옛 커밋 불변식으로 계속 검사한다. 같은 상태를 돌려주면 리비전이 그대로다. 매 프레임 시계 틱은 `ClockState.Advance`로 옮겼다. 옛 틱과 새 틱을 15개 상태에서 22,500걸음(자정 1,807번, 정수→실수 13번) 돌려 저장 바이트·리비전·결과가 모두 같았다(`ClockParitySpecs`, 4c에서 지움). 틱이 이제 현재 상태 위에서 돌므로 생길 수 없는 REVISION_CONFLICT 명세 `ConflictPreservesTime`을 지웠다. 검증: 순수 도메인 4종 통과(10/5/16/11), 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다(`s3-u4a`, `s3-u4a-r`). E3: probe 13개·복원 7개 흔적의 `rev=` 순서가 단위 3 실행과 같다. probe는 시계를 멈춰 두므로 실제 틱 경로는 일반 플레이 실행(`s3-u4a-play`)에서 리비전 206→653, 오류 0으로 확인했다.
- 3-4b 결과(`08a460a`, +544/-43줄, 그중 게임 코드 +116/-28): `WorldProjection.Capture`/`Project`와 `ReservationService.Accept(GameState, id)`를 더하고 장면 진입 저장, 초대 수락, 대기 중 재저장을 타입 커밋으로 옮겼다. 이벤트 성공은 4c까지 참가자 경로에 남는다. 옛 경로와 새 경로를 18개 상태에서 2,862걸음(거절 567건 포함) 돌려 저장 바이트·리비전·결과 코드·진단 문구가 모두 같았다(`WorldProjectionParity`, 4c에서 지움). 거절된 초대 수락도 거래 순번을 하나 쓴다(trace의 id 꼬리만 달라짐, 저장·리비전·결과 같음). 검증: 순수 도메인 4종 통과(10/5/16/12), 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다(`s3-u4b`, `s3-u4b-r`). probe 증거 `initial.json`·`final.json` 26개가 단위 4a 실행과 바이트까지 같다.
- 3-4c 결과(`d305e86`, 70개 파일 +671/-3,696줄, 게임 코드 +272/-2,164): 이벤트 완료를 `Transactions/EventRules.cs`(`IsSpent`, `Complete`, `EventOutcome.Parse`)로 옮기고, 참가자 4개, 레지스트리, `StatePlan` 계열, `StateTree`, `JsonPointer`, planner, `SaveTree`, `AppointmentCodec`, `StateCompletionLedger`, `WorldStateProjector`, 옛 `ClockState`·`FlagTable`, Contracts 상태 장치 타입 10개를 지웠다. 지우기 전 옛·새 이벤트 경로를 70개 시작 상태에서 5,980걸음 돌려 결과 코드·리비전·저장 바이트 차이 0. 진단 문구만 다른 경우 414건(수제 합산 넘침 이벤트, 미등록 효과 목록), 지도 밖 위치와 다른 결함이 겹친 입력의 첫 코드가 다른 경우 137건(설계 6절이 받아들인 차이). 6절 R행 전부를 명세로 덮었다. 설계와 다른 점: 연습 이벤트에 completionKey가 있으면 지금처럼 TRANSACTION_MISMATCH로 거절(콘텐츠로 표현 가능해 결과 코드 유지), 위치 캡처를 커밋 전이 안에서 함, `RepeatBlocked` 연결점 유지. 검증: 순수 도메인 4종 통과(10/5/16/11), 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다(`s3-u4c`, `s3-u4c-r`). probe 증거 26개와 `rev=` 순서가 단위 4b와 같다. 게임 C# 18,568 → 16,722줄.
- 3-5 결과(`dd4b476`, 43개 파일 +711/-836줄): `GameStore`가 `StateSnapshot(revision, GameState)`를 든다. 커밋 불변식, `IStateInvariant`, 같은 거래 id 재실행 캐시, `TypedState()` 다리, Contracts의 DataNode판 상태 타입을 지웠다. `SaveCodec.Seal`이 쓴 트리를 검증한 뒤 직렬화하고, `SaveRestorer`는 `SaveDocument.Admit`으로 `GameState`를 돌려준다(스키마 1번). 불러오기·장면 복원은 `Replace`. 스냅샷을 쓰는 인터페이스 6개를 Domain으로 옮겼다. 결정 D9대로 일반 플레이에는 커밋마다 검증이 없고, 테스트 리그 4곳과 probe에는 감시 리스너(`CommitValidation.Watch`)가 붙는다. 재실행 단언 5개는 "다시 내면 거절되고 상태·장부·지갑은 그대로"로 바꿨고, 커밋 불변식 자체를 검사하던 명세는 지웠다. `PreservedFields` 소스 검사 명세를 더했다. 설계와 다른 점: `Replace`가 `Outcome`을 돌려줌(거래 id·재진입 거절 유지), 경계 검증 포트 `ISaveValidator`를 둠. 검증: 순수 도메인 4종 통과(10/5/16/12), 빌드 오류 0·경고 0, probe 13개와 복원 4건이 기준과 검사 수까지 같다(`s3-u5`, `s3-u5-r`). E3: probe `initial.json`·`final.json` 26개, 복원 `entry.json`·`initial.json`, `rev=`·`rrev=` 순서, checkpoint 리비전이 단위 4c와 같다(복원 뒤 `final.json`의 `clock.fraction`만 다름, 알려진 예외). 일반 플레이 70초에서 리비전 1→4134, 오류 0.
- 3-6 결과(`5844add`, 75개 파일 +1,020/-534줄): Contracts 타입 84개를 다시 분류해 경계를 넘는 40개만 남기고 44개를 Domain 폴더 12곳으로 옮겼다(네임스페이스 변경). 지운 타입은 없다. 분류·이동은 `Tools/ContractsReduction/`의 스크립트로 하며, 다시 돌리면 "옮길 것 없음"이 나온다. Contracts는 1,148 → 616줄. asmdef와 csproj는 바뀌지 않았다. 검증: 빌드 오류 0·경고 0(경계 증명), 순수 도메인 4종 통과(10/5/16/12), probe success-notes 통과, `initial.json`·`final.json`이 단위 5와 바이트까지 같다.

### 3단계 결과

- 게임 C#(ThirdParty 제외) 18,037 → 16,695줄(-1,342, 설계 추정 -1,330±300). Contracts 1,352 → 616, Transactions 1,368 → 670, Time 506 → 291, State 409 → 542, Persistence 864 → 1,174. 테스트 C# 8,120 → 7,768줄.
- 상태가 바뀌는 길은 `GameStore.Commit(txn, GameState → Outcome<GameState>)` 하나다. 이벤트 한 건이 쓰는 것은 `Transactions/EventRules.cs`의 `Complete` 한 함수에 있다. 중복 보상 규칙은 `Progress.IsConsumed` 하나다. 검증은 불러오기(`SaveDocument.Admit`)와 저장(`SaveCodec.Seal`) 두 곳이다.
- 단위마다 저장 바이트, `rev=` 순서, 결과 코드가 앞 단위와 같았다(위 단위별 기록). 지킨 사용자 결정: D1 저장 형식 유지, D2 불투명 보존 필드, D3 숫자 표기 정규화 안 함, D4 연습 이벤트 예약 행 동작 유지, D5 `SaveMigration` 남김, D6 콘텐츠 스키마 안 바꿈, D7 `dayJobs`·`returnPending` 유지, D9 게임 안 커밋마다 하던 검증 제거(테스트·probe는 유지).
- 확인하지 않은 것: 사람이 직접 걸어가 E 키로 초대를 받는 플레이, ui-long·ui-200 스크린샷의 사람 검토, 매 프레임 비용 측정(일반 플레이 70초의 리비전이 4a 653 → 5 4134로 늘어난 것은 프레임이 늘었다는 추론).
- 최종 검증(2026-11-05, `5844add` 빌드): 순수 도메인 4종 통과(10/5/16/12), probe 13개 통과·검사 수 기준과 같음(`s3-final`, `initial.json`·`final.json` 26개와 `rev=` 순서가 단위 5와 같음), 복원 4건 통과(11/11/11/52, `s3-final-r`).
