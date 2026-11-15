# ADR 0004: 현재 Core 기반으로 기획 재정렬

2026-11-06 · 채택. ADR0001~0003의 기술 구조·저장·타일 후보 결정은 현재 Core/Game 코드 및 사용자 32×32 타일 결정으로 대체한다. 과거 검증 결과는 당시 코드에 대한 기록으로 유지한다.

Core는 가변 GameState와 Game의 단일 프레임, EventRun 최대 하나, 구체 World/Schedule/SaveFile 메서드를 쓴다. Newtonsoft.Json과 파일 IO는 Core에서 직접 사용한다. 현재 저장은 수면/02시 EndDay 후 다음날 상태이며 NPC는 로드 시 일정 재배치한다. Contracts/StateStore/Handle/임대/전체 v2 저장/장면 자동 저장/24시 귀환 전제를 재구축하지 않는다.

후속은 Content 정의·GameState 기본값/정규화·구체 Core 규칙·Game 호출·Game 표시를 함께 확장한다. 현 무버전 저장을 호환 입력으로 다룬다. 방 reset은 임시 RoomRun만, 디스크 로드는 마지막 하루 저장이다. 새 기술 기준과 Task별 접점은 design/implementation-tasks/와 design/systems/에 반영했다.

기존 Stage5Art 16px는 32×32 기준과 미일치하며 Task6-6/15-7의 전환 항목이다. 이번 문서 작업에서는 아트나 사용자 수정 씬을 변경하지 않는다.
