# Stage 5 이미지 생성 원본 → 로컬 도트 자산 v1

2026-11-03. **6.1 Sol Medium 서브 에이전트**가 built-in imagegen으로 학생 방향 시트와 캠퍼스 타일 시트를 먼저 생성한 뒤, 로컬 `2D-pixel-asset-pipeline` 가상환경과 `pixel_pipeline.cli`의 `check_image`, `grid_atlas`, `save_json`으로 도트화·검사·패키징했다. API 키/인증정보를 읽지 않았다. `Art/Prototype`은 보존했다.

## 파일

- `source/students-original.png`: imagegen 생성 원본, 4열(남/서/동/북) × 3행(player/Serin/Mira), 실제 RGBA 투명 배경.
- `source/academy-tiles-original.png`: imagegen 생성 원본, 4×4 타일 16종.
- `source/prompts.json`: 원본 생성 프롬프트와 도구/출처 기록.
- `characters/{player,npc.serin,npc.mira}/`: 32×64 셀 40개/명의 `sprite-sheet-alpha.png`(320×256), 같은 크기의 `normals/atlas.png`, 원본 비교용 정지 방향 포즈 4개, `manifest.json`, 확대 `review.png`, 남향 `walk-south.gif`.
- `tiles/`: 개별 16×16 RGBA PNG 16종, `atlas.png`(64×64), 절대 좌표 `manifest.json`.
- `comparison-students.png`, `comparison-tiles.png`: 원본/도트 결과 나란히 비교.
- `character-review.png`, `tile-review.png`: 최근접 확대 검수.
- `maps/map.courtyard.png`(128×96), `maps/map.lounge.png`(96×80): 기존 fixture 크기/장애물/포털에 맞춘 선택적 정적 배경. `-review.png`는 4배 확대.
- `runtime-manifest.json`: 기존 Prototype 소비 방식과 같은 `schema`, `tiles`, `actors`, `actors.*.animations` 및 방향별 `directionAnimations` 제공.
- `qa.json`: 원본 해시, 수량/크기/알파/atlas 일치/시각 검수 기록.

## 런타임 연결

모든 `image` 경로는 이 폴더 기준이다. 16 pixels/unit, point filter, mipmap 없음, 정수 확대를 사용한다. atlas 사각형은 **좌상단 원점 절대 픽셀 좌표**다. Unity 좌하단 원점으로 읽을 때 `unityY = sheetHeight - y - h`. 캐릭터 pivot은 `[0.5, 0.0625]`(아래에서 2픽셀)다.

기존 소비자는 `actors[actorId].animations.idle/walk`로 남향 idle4(5fps), walk6(9fps)를 그대로 읽을 수 있다. 방향 지원 소비자는 `actors[actorId].directionAnimations["south"|"west"|"east"|"north"]["idle"|"walk"].rectangles`를 읽는다. 방향 행 순서는 남/서/동/북이며 각 행 앞 4칸 idle, 뒤 6칸 walk다. 좌표/프레임수/fps/loop는 추정하지 않고 manifest 값을 사용한다.

기본 호환 타일 키는 `floor.courtyard`, `floor.lounge`, `wall.school`, `door`, `notice`. 추가 키는 포장길/짙은 잔디/지붕/나무/울타리/벤치/분수/창문/꽃밭/흙길/양피지다.

`maps` 배경은 선택적이다. 각 배경에 장애물과 문이 포함되어 있으므로 사용 시 같은 문/장애물 타일을 겹쳐 그리지 않는다. 캐릭터와 게시판은 별도다. 월드 y는 위로 증가하고 PNG y는 아래로 증가한다(`imageY = mapHeight - 1 - worldY`). 실제 충돌과 이동/도착/이벤트 판정은 기존 fixture 데이터를 사용한다.

## 재생성 및 검증

PowerShell에서 프로젝트 루트를 작업 폴더로 사용한다.

```powershell
$env:PYTHONPATH='C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/src'
$env:PYTHONDONTWRITEBYTECODE='1'
& C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe Art/Stage5-v1/build_assets.py
```

생성 후 16타일/120프레임/알파0·255/절대 atlas 사각형/시트 슬라이스와 입력 프레임 바이트 일치 검사가 통과했다. 확대 이미지에서 학생 셋의 색 정체성, 남서동북 실루엣, 문/게시판/환경 오브젝트 가독성을 직접 확인했다. 이것은 자산 검수이며 Unity 실제 화면 검증 완료를 뜻하지 않는다.

## 품질 범위와 한계

원본은 새 imagegen 생성물이며 도트 결과는 이를 nearest-neighbor 축소하고 포즈별 최대24색으로 정리한 것이다. idle/walk는 새 원본의 방향별 정지 포즈를 **상체 1픽셀 bob + 분리 다리 이동**으로 로컬 리깅한 간단한 루프다. 프레임별 손그림/생성형 걷기 동작은 아니며 profile 다리 움직임은 단순하다. 16픽셀 타일은 작은 오브젝트 디테일이 축약되고 일부 잔디/포장 패턴 이음이 보일 수 있다. 나무/문/분수도 모두 1셀 크기 장식이며 실제 건물 외관 세트나 대형 캠퍼스 맵 디자인을 대체하지 않는다. 선택적 맵 배경은 현재 작은 fixture에 맞춘 것으로 공간을 임의 확장하지 않았다.
