# ADR 0003: 단계4 결합 규약

단계3 전체 검증 통과 후 단계4를 시작한다. 기존 계약에 IEventStagingPort 한 포트를 추가한다. Runner가 전체 임대와 영속 진입 저장을 완료한 다음 Stage(definition, lease)를 호출한다. current는 일상 이동을 순간이동으로 바꾸지 않는다. reserved만 화면을 숨긴 동안 명시적으로 제공된 합류 계획을 적용할 수 있다. stage 실패/취소도 부모의 진입 복구 경로를 따른다.

- 4-1은 Application/Events, Application/Operations를 소유한다. 실제 move/perform과 선택 결과를 기다리고, 새 상태에서 조건 재검사 후 단일 커밋한다. scene은 snapshot.world를 선언 자원에 더해 전체 임대한다. scene 진입 위치 투영 → recovery CaptureEntry → SceneEntry 통지 → SaveLatestSafe의 실제 성공 → staging → script 순서다. 입장 저장 실패는 실행을 시작하지 않는다. 성공 커밋 뒤 저장 실패는 메모리 성공을 취소하지 않고 별도 저장 결과로 드러낸다. room-activity는 전역 진입 롤백 없이 등록 작업을 기다리며 미등록이면 거부한다.
- 4-2는 Persistence와 시험을 소유한다. SafeBoundaryPolicy/SaveCodec/MigrationRegistry/SaveRestorer/SaveService/SceneRecovery를 구현한다. 저장은 기존 IDataCodec/IFileGenerationStore를 주입한다. 원본 전체45필드와 정수 wallet을 유지한다. SceneRecovery는 캡처된 전체 권위 상태와 휘발 actor 상태만 제공한다. Runner가 맵 복원 완료와 상태 교체를 조율한다. 기존 배포 v1이 없으므로 migration은 명시 대표 fixture이며 실제 과거 호환으로 주장하지 않는다.
- 4-3은 Application/Schedules, Application/Reservations를 소유한다. reservations 참가자 operation accept `{eventId,acceptedAt}`, complete `{eventId}`, 소유 /appointments. appointment.id=eventId, 상태는 Offered/Accepted/Ready, acceptedAt은 절대분이다. 성공 완료 커밋만 제거하고 실패는 유지한다. schedule은 현시각 항목 하나만 선택한다. ambient 이동은 공통 MoveService로 실행하며 foreground가 임대한 NPC는 건너뛴다. Stage가 임대된 NPC의 기존 ambient 이동을 취소한 뒤 current/reserved 정책을 적용한다. 전경 장면을 선점하지 않는다.
- 4-4는 Content/Fixtures/FirstConnection만 소유한다. 원본을 보존하는 생성기를 실행해 실제 JSON을 만든다. 두 맵이 플레이 범위이며 원본 전체 카탈로그 참조에 필요한 비활성 정의는 유지한다. 원본12명 메타데이터는 남기고 활성 runner NPC는 최소화한다. A10은 이미 정의된 Mira의 spawn/event/dialogue 추가로 재현한다. raster 생성은 Codex만 담당한다.

같은 단계의 다른 구체 클래스에 의존하지 않으며 테스트용 대역만 별도 소유한다. Bootstrap·공통 등록은 단계5에서 한다. schema/function 원본을 바꾸지 않는다. 시간비용·보상·완료·위치·예약 완료는 기존 계획 프로토콜과 단일 StateStore 커밋을 사용한다.

취소된 장면 뒤의 안전 경계 추적을 다시 열기 위해 ISafeBoundaryRenewal을 Contracts로 옮겼다. Runner는 진입 복원 성공 뒤 Renew(WorldSafe, restored, false)를 호출하며 SceneComplete 성공을 발행하지 않는다. 복구 맵 로드에는 별도의 비정지 watchdog을 두고 자식 취소 종결 후 정리한다. 예약의 자동 시도와 사용자 입력은 서로 다른 owner replay scope를 사용하므로 카운터 숫자가 같아도 재시도를 잘못 중복 처리하지 않는다. 실제 category=festival을 행사200에 연결하며 기본 once completion은 명시 값이 없으면 eventId다.

결합 검토 중 확인한 3-1 보완: 컴파일러가 검증한 비이벤트 호출 칸을 버리지 않도록 ICompiledContentLookup/CompiledColumn을 추가하고 CompiledCatalog에 불변 복사로 보존했다. FindCalls("runner","schedules",scheduleId,"script")를 일정 조립에 사용한다. CompiledCall.InvocationName은 wildcard 사전 서명으로 검증한 실제 숫자/ID 호출 머리를 보존한다. Function.Name은 사전의 `*`를 그대로 유지한다. 기존 ICatalog 구현과 생성자는 호환되며 런타임 문자열 재파싱을 피한다. 원본 정상3종/60오류 및 전체3단계 순수검사를 다시 실행한다.
