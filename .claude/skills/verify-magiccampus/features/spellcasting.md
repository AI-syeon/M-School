# 주문 시전(영창·빠른 시전)

배운 주문을 `Q`로 영창을 열어 `1`-`4` 룬을 차례로 누르고 `Enter`로 확정해 시전한다. 바라보는 쪽 3칸 안의 대상(안내판 등)에 주문을 쓰면 대상 태그에 따라 보호, 등록 조합 6종, 단일 반응, 무반응 관찰 순으로 결과가 정해지고 HUD 한 줄에 표시된다. 빠른 시전 해금 주문을 자기 자신에게 성공시키면 슬롯 4개가 열리고, 주문 수첩(`K`)에서 슬롯에 배운 주문을 넣으면 자유 조작 중 숫자 키 한 번으로 같은 규칙이 실행된다. 룬 입력·대상 태그·마지막 결과는 세션 데이터이고, 배운 주문·해금·슬롯·상황 기록은 하루를 마칠 때 저장된다.

## Sub-features

- `incantation`: `Q` 열기·닫기, `1`-`4` 룬 추가(누른 프레임에만), `Enter` 확정. 잘못된 순서는 입력 줄을 비우고 영창을 연 채로 둔다. 룬 둘 또는 룬+`Enter` 동시 입력은 그 프레임을 무시하고 "차례로 눌러 주세요"를 띄운다. `Esc`/`X`/`Backspace`/`Q`는 영창만 취소한다.
- `input-context`: 프레임당 입력 문맥 하나(F9 > 메뉴 > 대화·이벤트 > 패널 > 자유). 대화 중 숫자는 선택지에만 쓴다. 메뉴·패널·이벤트·포털·F9·하루 종료·창 포커스 상실이 영창을 비운다.
- `target-reaction`: 대상 해석(같은 맵, 사거리 3칸, 정면, 0.6 정면각 + 0.4 거리), 콘텐츠 `reactions` 표(Protect > Combo > React, 파일 순서). 거부된 시전은 상태·해금·기록을 바꾸지 않는다.
- `quick-cast`: 해금 전 숫자 키는 아무 일도 하지 않는다. 해금 후 빈 슬롯은 안내 문구를 띄우고, 찬 슬롯은 영창과 같은 `TryCastSpell`을 부른다.
- `spellbook`: `K` 또는 HUD 버튼 `주문 수첩(K)`. 배운 주문의 룬 배열과 사용 조건, 슬롯 넣기·비우기(해금 후, 이벤트 밖).
- `placeholder-targets`: 마당의 어두운 안내판 등은 색 사각형과 이름·태그 글자로 그린다(어두움은 검은 사각형, 밝음은 노란 사각형). 정식 그림은 없다.

## How to get to it (user POV)

- 시작 상태 `start-spells.json`으로 실행한다: `MagicCampus.exe --fresh --initial start-spells.json`. 기본 시작 상태(`start-offered.json`)에는 배운 주문이 없다.
- 위쪽으로 걸어 안내판을 바라본 뒤 `Q` → `1` → `2` → `Enter`.
- `Q` → `3` → `2` → `1` → `Enter`는 빠른 시전 해금 주문이다. 이어서 `K`로 슬롯을 정하고 숫자 키를 누른다.

## Driving it with verify-magiccampus scripts

Preconditions:

- `scripts\doctor.ps1 -Build Stage5-verify`의 종료 코드가 0이다. `run-probe.ps1`이 이 세 케이스에 `--initial start-spells.json`을 붙인다.

- **등불 영창.** `scripts\run-probe.ps1 -Cases spell-lantern -Run <id>`. 안내판을 바라보고 `Q 1 2 Enter`로 켠다. `lantern.lit`, `lantern.single-cast`, `lantern.wrong-order-clears`, `lantern.two-runes-at-once-ignored`, `lantern.rune-and-enter-at-once-ignored`, `lantern.hold-is-one-rune`, `lantern.cancel-*`이 통과여야 한다. 스크린샷 `before`, `incantation-open`, `simultaneous-ignored`, `wrong-order`, `runes-1-2`, `lit`.
- **입력 문맥.** `scripts\run-probe.ps1 -Cases spell-context -Run <id>`. 이벤트 중 숫자는 선택지만 고르고 시전하지 않는다. 이벤트 시작, 메뉴, 주문 수첩, 포커스 상실, 포털, F9, 하루 종료가 영창을 비운다. `context.*`가 모두 통과여야 한다.
- **빠른 시전.** `scripts\run-probe.ps1 -Cases spell-quick -Run <id>`. 해금 전 숫자 키 무반응 → 해금 주문 → `GameRoot.AssignQuickSlot`(주문 수첩 버튼과 같은 공개 메서드)으로 슬롯 1에 등불 → `1`로 시전 → 취침 저장 → 저장 파일로 새 `Core.Game` → `F9` 뒤에도 해금·슬롯 유지. `quick.*`가 통과여야 한다. 스크린샷 `unlocked`, `spellbook`, `spellbook-assigned`, `quick-cast`, `quick-after-load`.

## Gotchas

- 주문 수첩의 슬롯 버튼은 마우스 클릭이다. probe는 같은 공개 메서드를 불러 대신하므로 클릭 경로 자체는 사람이 확인해야 한다.
- 대상 태그는 저장하지 않는다. 하루 종료·불러오기·`Game.ResetMagicTargets()`(방 재시도의 자리 표시)에서 처음 태그로 돌아간다.
- 사용 조건(스탯·레벨·소지품)을 읽는 시스템은 아직 없다. 게임이 조건 제공자를 연결하지 않아 조건이 있는 출시 주문은 `Tests/Core`가 거부한다. 조건 판정은 단위 시험의 시험용 제공자로만 증명된다.
- 영창 중에도 이동과 세계 시간은 흐른다. 이벤트 중에는 세계 시간과 대상 태그의 제한 시간이 멈춘다.
