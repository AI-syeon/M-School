# 키 아트에서 Unity 타일맵까지

[캠퍼스 키 아트](../../design/screens/01-campus.png)의 석재 안뜰을 재구성했습니다. 왼쪽 도서관, 중앙 계단, 오른쪽 온실이 분수 광장을 둘러싸고, 앞쪽 단풍과 난간 사이로 나침반 문양이 보입니다. 20×14칸 맵에서 카메라가 20×12칸을 담습니다.

## 결과 열기

1. Unity에서 `Assets/MagicCampus/CampusGrid/Generated/CampusCourtyard.unity`를 엽니다.
2. Scene 뷰의 `Courtyard Grid` 아래에서 `Ground` 타일맵과 기물 프리팹 인스턴스를 선택합니다. Play를 누르면 분수와 시계가 움직입니다. Game 뷰의 시각 슬라이더나 **Morning / Noon / Sunset / Night** 버튼으로 원하는 시각을 고르고, **Run**으로 흐름을 다시 시작합니다.
3. 다른 씬에 기물을 배치하려면 `Generated/Props`의 프리팹을 Grid 아래로 끌어 놓고 정수 셀 위치에 맞춥니다. 프리팹은 1칸이 1 Unity 유닛인 Grid를 포함합니다. `Generated/Tiles`의 반복 바닥 타일과 `scenic.campusatlas` 아래의 광장 바닥 타일을 Unity Tile Palette에 추가해 칠할 수 있습니다.

[Unity 시간대별 캡처](review/daylight-times.png), [그림자와 색 보정 전 타일 조립](review/courtyard.png), [분수 루프](review/fountain.apng), [원본과 도트 비교](review/source-vs-grid.png)를 함께 볼 수 있습니다. 분수는 4×3칸 전체를 사용하고, 물받이가 닿는 아래 4×2칸에 2D 충돌이 있습니다. 게시판과 벤치는 아래 2칸이 충돌 범위입니다. 다른 게임 씬의 이동 시스템이 논리 좌표로 충돌을 판정한다면 같은 `blocked` 좌표를 해당 맵 데이터에 반영합니다.

## 같은 결과 재생성

프로젝트 루트에서 PowerShell로 실행합니다. 로컬 `2D-pixel-asset-pipeline`의 가상환경을 사용합니다. 위치가 다르면 `-Pipeline <폴더>`를 지정합니다.

```powershell
.\Art\CampusGrid\rebuild.ps1
```

Unity에서 이름 없는 씬을 열어둔 경우 저장한 다음, **MagicCampus → Art → Rebuild Campus Courtyard**를 실행합니다. 이후 생성된 씬을 엽니다. 생성 씬은 레시피에서 다시 만들기 때문에 손으로 편집할 씬은 별도로 저장합니다. 열린 생성 씬에 저장하지 않은 변경이 있으면 다시 만들기 전에 알려줍니다.

Python 단계는 저장된 PNG를 사용해 32×32 셀 PNG, `scenic.campusatlas`, `layout.json`을 만들고, 비교 이미지·검수용 아틀라스·분수 루프·원본 해시를 `review/`에 남깁니다. Unity 단계는 32 PPU, Point, 압축과 밉맵 없음으로 셀을 가져와 네이티브 Tile, Tilemap, 프리팹과 씬을 생성합니다. `scenic.campusatlas`는 광장 280칸과 건물·정원·학생 조각을 한 텍스처에 담되 각 셀을 개별 Tile로 노출합니다.

## 키 아트 한 장을 제작 레시피로 옮기기

| 순서 | 기록할 결정 | 자동으로 처리하는 일 |
| --- | --- | --- |
| 1. 분해 | 키 아트에서 반복 바닥, 배치할 기물, 움직일 부분을 정합니다. 맵의 셀 수와 대표 기물의 발밑 크기를 먼저 정합니다. | |
| 2. 원본 준비 | 가려지지 않은 바닥은 원본에서 사각형으로 잘라 씁니다. 인물이나 그림자가 겹친 기물은 키 아트와 같은 시점·팔레트의 투명 원본으로 분리해 준비합니다. 생성 원본과 프롬프트는 `source/`에 보관합니다. | 지정한 픽셀 사각형을 잘라내고 목표 크기에서 색과 알파를 정리합니다. |
| 3. 기물과 동작 | `art.json`에서 기물의 가로·세로 칸 수, 충돌 좌표, 프레임 순서와 FPS를 기록합니다. 프레임 전부에서 기준점과 캔버스를 맞춥니다. | 정적 기물은 전체 PNG 한 장으로 내보냅니다. 움직이는 기물은 전체 프레임을 같은 크기의 그리드 시트로 묶습니다. |
| 4. 조립 | `map.json`의 한 글자가 바닥 한 칸입니다. 기물은 프리팹의 좌하단을 배치할 셀로 지정합니다. | 배치 범위와 기물 간 겹침을 검사하고, 지면과 기물 레이어를 조립합니다. |
| 5. 검수 | 바닥은 3×3 이상 반복해 이음새를 보고, 기물은 게임 크기에서 실루엣과 물의 움직임을 확인합니다. | 출력 크기, 바닥의 불투명도, 기물의 투명 여백, 충돌 좌표, 아틀라스와 셀의 픽셀 일치를 검사합니다. |

원화가 한 장으로 합쳐져 있으면 영역 선택과 레이어 분리가 제작 단계입니다. 예를 들어 분수 뒤의 가려진 바닥은 별도의 반복 타일로 채웁니다. 이번 샘플의 바닥과 분수는 원화를 참고해 생성한 별도 PNG에서 가져옵니다. [생성 프롬프트](source/prompts.json)에 입력 원화와 작업 조건을 기록했습니다.

### 좌표와 레시피

- `art.json`의 `tiles` 항목은 `id`, `source`, 선택 사항인 `crop: [x, y, w, h]`와 `colors`를 받습니다. `crop`은 **원본 PNG의 좌상단** 기준입니다. 이미 준비된 원본 전체를 쓰려면 생략합니다. `colors`를 지정하면 팔레트 색 수를 제한합니다.
- `props` 항목은 `id`, `size: [가로 칸, 세로 칸]`, `frames`, `blocked`를 받습니다. 각 프레임은 바닥과 같은 소스 형식입니다. `fit: "contain"`은 비율을 유지하고 가운데 아래에 맞춥니다. 정확히 정렬한 프레임을 쓸 때는 모든 프레임을 같은 크기로 준비합니다. 프레임이 여러 개면 `fps`를 지정합니다. `comparisonSource`에는 비교용 원본을 기록할 수 있습니다.
- `blocked`와 `map.json`의 `props[].at`은 **좌하단 셀**이 `[0, 0]`입니다. 맵의 `rows`는 미리보기와 같도록 **위에서 아래**로 씁니다. 그리드 위치는 `gridY = 맵 높이 - 1 - 행 번호`입니다.
- 바닥에 이어지는 무늬가 있다면 `map.json`에 `size`와 `groundImage`를 지정합니다. 이번에는 돌 이음새와 나침반이 이어지는 균일한 조명의 광장 원본을 먼저 도트화하고 280칸으로 분할합니다. 반복 바닥의 경우 `legend`와 `rows`를 사용합니다.
- `art.json`의 `libraries`는 관련 기물을 별도 레시피로 관리하고 아틀라스에 묶습니다. 이번에는 건축, 정원, 학생 라이브러리를 사용합니다.
- `allowVisualOverlap`을 켜면 나뭇가지가 화면 밖으로 나가거나 화단이 분수 뒤에 겹칠 수 있습니다. 실제 충돌 칸의 겹침은 검사합니다. `decorative`는 해당 배치의 충돌을 끄고, `order`는 장면 안에서 기물의 앞뒤 순서를 덮어씁니다.
- 이번 분수는 `fountain.json`에 도트화 크기와 위치, 물줄기 좌표를 기록합니다. `build_fountain.py`가 푸른 물 영역의 하이라이트와 잔물결을 6개 위상으로 만든 후 공통 변환기가 이를 128×96 프레임 6개가 있는 시트로 묶습니다. 8fps에서 한 바퀴는 0.75초입니다. 다른 기물에 프레임을 직접 준비했다면 `frames`에 바로 적습니다.

### 시간대와 빛

빌드할 때 노멀맵을 기본으로 생성합니다. 개별 셀의 노멀은 `Normals/`에 있고, 묶음 노멀인 `Normals/scenic.png`는 `scenic.campusatlas`와 동일한 좌표를 씁니다. 여러 칸짜리 기물은 애니메이션 프레임 전체에서 노멀을 계산한 뒤 32×32로 나눕니다. 특별히 노멀이 필요 없는 레시피는 최상단에 `"normalMaps": false`를 지정할 수 있습니다.

맵 바닥도 전체에서 높이 기울기를 계산해 `GroundNormal.png`를 생성합니다. 기본값은 픽셀 명암에서 높이를 추정합니다. `map.json`의 `groundNormal.strength`로 강도를 조정하고, 별도 높이 이미지는 `groundNormal.height`에 `{"source": "source/height.png"}`로 지정합니다. 안뜰의 바닥 재질은 이 텍스처를 선형 데이터로 읽고 시간대에 따른 해의 방향을 적용합니다.

`build_canopy_mask.py`는 단풍나무 원본의 수관에서 128×128 투명도 마스크를 만듭니다. Unity는 같은 마스크를 여러 나무에서 재사용합니다. `map.json`의 `daylight.canopy`에 수관 너비, 깊이, 높이, 농도와 나무 기준점을 적습니다. `offscreen`은 화면 밖 수관이 드리우는 그림자입니다. 기존 나무 인스턴스를 옮기면 그림자 기준점도 따라갑니다.

`CampusDaylight`가 하나의 시각에서 해의 방향과 고도, 빛의 온기와 세기, 등불의 세기를 계산합니다. 그림자는 해의 반대 방향으로 투영되며 아침과 저녁에 길어지고 정오에 짧아집니다. 해가 지면 나뭇잎 그림자가 사라집니다. 바닥 위에는 나뭇잎 그림자, 그 위에는 기물 밑에 붙는 고정 그림자가 따로 그려집니다. `shadows`는 고정 그림자, `glows`는 밤에 밝아지는 등불과 창문 좌표입니다. 카메라의 `CampusCourtyardLook`이 같은 시각 값으로 화면의 온기와 밝기, 빛 번짐을 조정합니다. Unity의 Built-in Render Pipeline을 사용합니다.

안뜰의 미리보기 시계는 기본값으로 96초에 하루를 돌립니다. 본편에서 쓰려면 시계의 분 값을 `CampusDaylight.SetGameMinute(int)`에 넘깁니다. 예를 들어 17:00은 1020분이고, 자정을 넘긴 분 값은 24시간 기준으로 순환합니다. 호출하면 미리보기 자동 진행이 멈춥니다.

독립 레시피를 시험할 때는 `build_grid.py --art ... --map ... --unity-out ... --review-out ...`를 가상환경 Python에서 실행해 별도 폴더에 출력할 수 있습니다. `rebuild.ps1 -Art ... -Map ...`은 프로젝트의 Unity 입력을 갱신합니다. 기본 아트 레시피에서 분수 프레임과 나뭇잎 마스크도 다시 생성합니다.

### Unity 스프라이트 시트

기물 PNG는 `Sprites/`에, 같은 크기의 노멀 PNG는 `Normals/Sprites/`에 저장합니다. 정적 기물은 Unity에서 **Single**, 애니메이션 기물은 **Multiple**로 가져옵니다. 다시 자를 때는 **Sprite Editor → Grid by Cell Size**에서 기물 전체의 프레임 크기를 입력합니다. 3×4칸 가구의 프레임 크기는 96×128px입니다. 여백 없이 왼쪽 위에서 오른쪽으로 진행하는 순서를 사용합니다. 레시피에서 `columns`로 열 수를 지정할 수 있습니다.

`layout.json`의 `rectangles`는 시트에서 실제 프레임이 있는 픽셀 영역을, `blocked`는 맵에서 충돌하는 32px 칸을 지정합니다. 분수의 프리팹과 반복 애니메이션은 Unity 메뉴 **MagicCampus → Art → Rebuild Campus Courtyard**에서 재생성할 수 있습니다. 캐릭터는 `Art/Characters/build_characters.py`가 32×64 프레임 시트와 노멀 시트를 생성합니다.

## Rooms

프로젝트 루트에서 `powershell -ExecutionPolicy Bypass -File .\Art\CampusGrid\rebuild-rooms.ps1`을 실행합니다. `-Pipeline`으로 로컬 파이프라인 경로를 지정할 수 있으며, `-Art`와 `-Map`으로 방 하나의 레시피를 지정할 수 있습니다. 기본 실행은 모든 `room-*-map.json`을 빌드합니다. 준비 스크립트가 git에서 제외된 `prepared/`를 원본으로부터 재생성합니다.

각 방의 결과는 `Assets/MagicCampus/CampusGrid/Rooms/room-<id>/`, 검수 이미지는 `Art/CampusGrid/review/room-<id>/`에 저장됩니다. 지정된 파이프라인 Python으로 `Art/CampusGrid/check_room.py dorm`을 실행하면 기숙사의 고정 충돌 조건과 연결성을 검사합니다.

게임은 `layout.json`의 `props[].blocked`에 있는 기물 로컬 좌하단 기준 셀에 `placements[].x/y`를 더해 충돌 셀을 구합니다. `placements[].decorative`가 참인 배치는 충돌에서 제외합니다. 벽도 `blocked` 셀을 가진 기물입니다.

## 검증 결과

- 같은 PNG와 레시피로 두 번 생성해 Unity 입력과 검수 이미지가 바이트 단위로 일치하는지 확인했습니다. 32×32 조각을 다시 합친 분수 6프레임도 도트화된 전체 프레임과 일치했습니다.
- Unity 6000.6.3f1의 별도 검증 프로젝트에서 생성 씬을 다시 열고 분수 6프레임의 동기 재생을 확인했습니다. 08:00, 12:00, 17:00, 22:00를 실제 렌더링해 나뭇잎 그림자의 위치와 크기가 달라지고 밤에는 사라지는 것을 확인했습니다. 나무 하나를 옮겼을 때 투영된 그림자도 이동했습니다. [Unity 시간대별 캡처](review/daylight-times.png)
