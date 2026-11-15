# 전체 시스템 기획 · 현 Core 확장판

2026-11-06. 기술 기준은 [현재 구조](../02-architecture.md), [공통 확장 계약](02-contracts.md), [현 데이터](04-data-contracts.md), [Task 계획](../../implementation-tasks/README.md)이다. [전체 HTML](../../마법학교_전체시스템기획서.html)에서 함께 읽을 수 있다.

도메인별 플레이 규칙은 [마법·수업·전투](domains/domain-01.md), [생활·경제](domains/domain-02.md), [관계·이야기](domains/domain-03.md)에 유지한다. 이 문서들의 과거 DTO 예시는 도메인 의도 설명이며, 구현은 새 Core 연결 규칙을 따른다.

source/, examples/, schemas/, 과거 검증 기록은 이전 설계의 근거 자료다. 현 런타임에서 전체 함수 사전·save v2가 구현/로딩된다는 뜻이 아니다. 새 데이터를 작성할 때 현재 Content DTO와 GameState부터 확인한다. 현 문서 검사는 build-current-plans.cjs로 수행한다. 이전 build-all.cjs/adopt-drafts.cjs는 이전 설계 재생성용이므로 현 문서를 덮어쓰는 용도로 실행하지 않는다.
