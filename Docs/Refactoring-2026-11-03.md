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

## 2단계: 구조 단순화

1단계 검증 뒤에 범위를 확정해 적는다.

## 진행 기록

- 2026-11-03 git 기준점 `f463071` 생성, 기준 테스트 실행 시작.
- 2026-11-03 기준 결과: 순수 도메인 4종 통과, Unity 빌드 오류 0, probe 13개 통과(ui-long·ui-200은 사람 검토 1건 건너뜀), 강제 종료 복원 4건 통과. 요약은 `Tests/Results/refactor/baseline-probe.txt`.
