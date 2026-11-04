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
- 하지 않는 것: 테스트 프로젝트 4개 통합(가치가 낮음), DTO 생성자 검증 제거(테스트가 예외를 검사함, 효과 대비 변경이 큼).

## 진행 기록

- 2026-11-03 git 기준점 `f463071` 생성, 기준 테스트 실행 시작.
- 2026-11-03 기준 결과: 순수 도메인 4종 통과, Unity 빌드 오류 0, probe 13개 통과(ui-long·ui-200은 사람 검토 1건 건너뜀), 강제 종료 복원 4건 통과. 요약은 `Tests/Results/refactor/baseline-probe.txt`.
