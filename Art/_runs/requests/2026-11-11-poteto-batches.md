# 'Pstack poteto mode' 세션의 아트 요청 (2026-11-11)

상태: **진행 중**. 2026-11-11 이 세션의 사용자가 시작을 승인했다("Yes, start with 1"). 그 세션이 근거로 든 사용자 원문은 "아트 리소스도 제대로 만들고... (아트는 Codex 아트 리소스 생성 세션 참고하고 이거 끝나면 결과물 받아서 해줘.)"다. Codex는 한 번에 한 작업만 돌리고, 1번부터 순서대로 진행한다.

| 묶음 | 상태 | 결과 | Codex 사용량 |
|---|---|---|---|
| 1 UI 키트 | 완료(검수함) | `Assets/MagicCampus/Art/UI/` + `manifest.json`, 미리보기 `Art/UI/review/ui-kit-preview.png`. 깔끔하지만 장식이 단순하다. | 48,851토큰, 5분 |
| 2 아이템 | 완료(검수함) | `Assets/MagicCampus/Art/Items/` + `manifest.json`, 미리보기 `Art/Items/review/items-preview.png`. 꿀빵에 꿀이 안 보이고 서리박하의 서리가 약하다. | 33,609토큰, 6.9분 |
| 3 대상 기물 | 완료(검수, 재빌드 바이트 일치 직접 확인) | `Art/CampusGrid/targets.json` → `layout.json` props 12개(맵 배치 없음). 시트 `Art/CampusGrid/review/targets-sheet.png`. 안내판 lit은 등불만 켜지고, 이끼 bloom은 꽃이 2송이뿐이라 차이가 약하다. | 52,506토큰, 8.2분 |
| 4 기숙사 방 | 완료(1차 후 수정 1회. 좌표·연결 검사와 재빌드 바이트 일치 직접 확인) | `Assets/MagicCampus/CampusGrid/Rooms/room-dorm/layout.json`. 재생성은 `Art/CampusGrid/rebuild-rooms.ps1`, 검사는 `check_room.py`. 문은 러너가 벽을 지나가는 모양이라 열린 틈이 뚜렷하지 않고, 키 아트보다 침대와 창이 작다. | 74,458+88,026토큰, 16.4분 |
| 5 휴게실 | 완료(검수, 좌표·연결 검사와 재빌드 바이트 일치 직접 확인) | `Assets/MagicCampus/CampusGrid/Rooms/room-lounge/layout.json`, 벽난로 6프레임 8fps. 문은 기숙사 방과 같은 러너 모양이다. | 60,108토큰, 6.1분 |
| 6 캐릭터 | 완료(검수, 재빌드 바이트 일치 직접 확인) | `Assets/MagicCampus/Art/Characters/{player,npc-serin,npc-mira}/`. 32x64, pivot (16,62), 4방향 × (idle 1 + walk 4, 8fps). NPC의 manifest id는 기존 `npc.serin` / `npc.mira`를 유지했다. | 71,507토큰, 12분 |

전체 Codex 사용량: 429,065토큰, 약 64분(수정 1회 포함).

공통 조건: 32×32 기준, 광원 좌상단, 팔레트는 기존 안뜰에 맞춘다. 묶음이 끝날 때마다 결과 경로와 Codex 사용량을 그 세션에 보낸다. 그 세션의 기록 파일은 `design/게임화/진행.md`다.

## 합의한 형식
- id는 소문자·숫자·하이픈만 쓴다. 상태마다 별도 id를 둔다(예: `target-signboard-dark`, `target-signboard-lit`). `target.signboard` 같은 이름과의 매핑은 게임 쪽이 한다.
- UI와 아이템:
  - 결과: `Assets/MagicCampus/Art/UI/`, `Assets/MagicCampus/Art/Items/`에 PNG와 `manifest.json`
  - 원본: `Art/UI/source`, `Art/Items/source`
- 대상 기물은 `Art/CampusGrid/targets.json` 라이브러리에 등록만 한다. `map.json`에는 배치하지 않는다. 위치는 `runner-bundle.json`이 정한다.
- 장면은 `Assets/MagicCampus/CampusGrid/Rooms/<id>/layout.json`과 셀 PNG로 낸다. 게임이 실행 중에 직접 읽으므로 임포터 확장은 필요 없다. `CampusGridImport.cs`는 이 세션이 고쳐도 된다.

## 우선순위
1. **UI 키트** (06-room·16-journal의 남색+금색 장식 톤, 테두리는 1~2px 픽셀 선으로 Galmuri 11px·9px 글꼴과 어울리게)
   - `ui-panel`: 9-slice, 모서리는 16px 단위
   - `ui-button`: 보통 / 선택 / 비활성
   - `ui-slot`: 보통 / 선택
   - `ui-quickslot`: 숫자 자리 포함
   - `ui-namebox`
   - 9-slice 경계(px)는 JSON으로 남긴다.
2. **아이템 아이콘 9종** (32×32, 투명 배경): lens, rune_chalk, moonbean_seed, frost_mint, crystal_dust, honey_bread, focus_potion, student_card, torn_note
3. **마법 대상 기물 6종 × 2상태** (캔버스와 기준점을 맞춘다)

   | 기물 | 칸 수 | 충돌 | 상태 |
   |---|---|---|---|
   | signboard | 1×2 | [0,0] | 어두움 / 불 밝힘 |
   | pillar | 1×2 | [0,0] | 부서짐 / 수선됨 |
   | seedpot | 1×1 | 있음 | 마른 흙 / 새싹 |
   | moss | 1×1 | 있음 | 이끼 / 꽃 핀 이끼 |
   | plaque | 1×1 | 있음 | 흐림 / 글자 빛남 |
   | rune_stone | 1×1 | 있음 | 꺼짐 / 공명 빛 |
4. **room-dorm** (12×9, 키 아트 06-room)
   - 문: (5,0)
   - 침대: (1,5)~(1,6), 1×2칸. 처음 요청은 캐노피 침대 2×3이었으나 1×2 좌표로 정정됐다.
   - 잠드는 자리: (2,5)
   - 가구: 책장, 책상, 창가, 옷장, 러그
   - 충돌 칸은 레시피에 적는다.
5. **room-lounge** (10×8, 키 아트 12-club 또는 02-classroom 톤)
   - 문: (4,0)
   - 간식 탁자: (4,3)
   - 가구: 소파, 찻상, 벽난로, 게시판

6. **캐릭터 3명**: player, npc-serin, npc-mira
   - 바꾸는 이유: 지금 게임 캐릭터는 `Art/Stage5-v1/characters`의 16px 그림이라 32px 안뜰과 해상도가 맞지 않는다.
   - player: 키 아트 06-room·03-magic의 주인공 비율(은발, 보라 로브, 갈색 가방)
   - 서린·미라: 안뜰 학생(`Art/CampusGrid/students.json`)과 같은 그림체로 하되 서로 구분되게
   - 프레임: 32×48 또는 32×64. 폭은 1칸, 키는 약 1.5~2칸
   - 동작: 4방향(남/북/동/서) 걷기 4프레임 + 서 있기 1프레임
   - 기준점: 발밑 중앙으로 통일
   - 출력 형식: `Art/Stage5-v1/characters`의 runtime-manifest 형식(clip별 rectangles, fps). 다른 형식이면 manifest를 같이 남긴다.
   - 위치: `Assets/MagicCampus/Art/Characters/`, 원본은 `Art/Characters/source`

참고: 안뜰은 게임이 CampusGrid 장면을 그대로 쓴다(`Tests/Results/verify/world-1/spell-lantern/lit.png`).
