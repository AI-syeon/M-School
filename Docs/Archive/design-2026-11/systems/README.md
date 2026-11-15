# MagicCampus 현재 시스템 기획

2026-11-06 · 기반 교체 반영판. 현재 코드가 구현 사실의 기준이고, 이 문서군은 현 구조와 후속 확장을 설명한다.

1. [구조](02-architecture.md): Core/Game, 단일 이벤트와 프레임, 하루 저장.
2. [시스템 동작](03-systems.md): 이동·대화·시간·초대·실패 경계.
3. [실제 데이터](04-data-authoring.md): 지원 문법과 DTO, 현재 저장 형식.
4. [Task1–5](../implementation-tasks/01-기반과최소연결.md), [Task6–9](../implementation-tasks/02-게임시스템.md), [Task10–18](../implementation-tasks/03-통합과콘텐츠검증.md).
5. [인수 기준](../implementation-tasks/인수기준.md), [검증 결과](07-validation-results.md).
6. [후속 전체 시스템](full-design/README.md).

[HTML 시스템 기획서](시스템_기획서.html)와 [프로그래머 구현계획서](../프로그래머_구현계획서.html)는 현 Markdown에서 생성한다.

기존 원본 기획의 콘텐츠 수량·플레이 의도는 유지한다. 옛 포트/스키마/함수 사전/저장 예제와 과거 검증은 현 런타임 계약이 아니다. 이전 문서판은 [보관본](../archive/2026-11-02/systems/README.md), 원문·모델 응답·정적 예제는 조사 이력으로 남긴다. 현재 런타임이 읽는 데이터는 Assets/StreamingAssets/FirstConnection 아래이며 빌드 입력은 Content/Fixtures/FirstConnection/generated다.
