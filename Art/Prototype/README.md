# 최소 연결용 도트 에셋

Codex 제작, 2026-11-02. 모두 **연결 검증용 prototype**이며 출하 아트가 아니다. 기존 아트와 외부 파이프라인 소스는 덮어쓰지 않았다. 유료 생성 API 사용 없음.

## 생성·검증

실행 환경: `C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe`.
`PYTHONPATH`는 위 프로젝트의 `src`, `PYTHONDONTWRITEBYTECODE=1`로 실행한다.

1. `python -m pixel_pipeline.cli Art/Prototype/first-connection.recipe.json --out Art/Prototype/FirstConnection-v1`
2. `python -m pixel_pipeline.cli Art/Prototype/first-connection-compact.recipe.json --out Art/Prototype/FirstConnection-compact-v1`
3. `python Art/Prototype/build_symbols.py`
4. `python Art/Prototype/verify_assets.py`, `python Art/Prototype/verify_assets.py --compact`

전부 파이프라인 가상환경 Python으로 실행한다. 시스템 Python은 사용하지 않는다.

## 산출물

- `FirstConnection-v1`: 16×16 타일 12개, 32×64 캐릭터 20프레임, 7세트. 캐릭터 두 명 각각 idle 4프레임/5fps, walk 6프레임/9fps.
- `FirstConnection-compact-v1`: 동일 타일과 캐릭터를 16×32로 생성한 후보. 16타일과 같은 픽셀 밀도로 인물 높이 2타일을 시험하기 위한 것.
- `FirstConnection-symbols-v1`: 투명한 16×16 문·게시판. `symbols.recipe.json`의 격자 레시피를 해석하고 파이프라인의 `check_image`, `grid_atlas`, `save_json`으로 검사·패키징.
- 각 폴더에 개별 RGBA PNG, atlas, 절대 좌표 manifest 보존. 캐릭터 manifest에는 프레임수/fps 포함.
- `FirstConnection-mira-v1`: 추가 NPC 데이터 검증용 미라의 16×32 idle4/walk6프레임. `first-connection-mira.recipe.json`을 같은 파이프라인으로 생성하고 `verify_assets.py --mira`의 크기/알파/10프레임/atlas 검사 통과. Codex가 확대 미리보기에서 다른 두 의상과 구분되는 녹색 팔레트를 확인했다. `bind_mira.cjs`가 runtime-manifest의 명시 npc.mira 역할을 생성한다. 아직 게임 연결 전이다.
- `asset-qa.json`, `asset-qa-compact.json`: 32프레임씩 크기/알파/수량/해시, 아틀라스 사각형과 개별 PNG 바이트 일치 검사 통과.
- `first-connection-review.png`, `first-connection-compact-review.png`, `FirstConnection-symbols-v1/preview.png`: nearest-neighbor 확대 검수 이미지. 실제 게임 화면 아님.

## 시각 검수와 남은 장벽

Codex가 세 미리보기를 직접 확인했다. 바닥·벽 팔레트, 두 인물의 의상 색, 문틀·손잡이와 게시판 받침대가 구분된다. compact 인물은 16타일에서 2타일 높이 후보로 사용할 수 있다. 기존 금색/청록 stone 타일은 장식용 색 블록이며 실제 문/게시판 표시는 symbols 출력이 대체할 예정이다.

캐릭터는 공통 절차적 실루엣이라 개별 NPC 완성 디자인이 아니다. 방향별 동작과 초상은 이 범위에서 제작하지 않았다. Unity에서 실제 정수배율/가독성/충돌 반경/문 폭/도착 위치를 확인하기 전 런타임 통합 합격을 주장하지 않는다.
