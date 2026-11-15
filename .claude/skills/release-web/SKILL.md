---
name: release-web
description: MagicCampus를 WebGL로 빌드해 GitHub Release(AI-syeon/M-School)로 올리고, 최신 릴리즈를 GitHub Pages(https://ai-syeon.github.io/M-School/)에서 웹으로 플레이할 수 있게 배포한다. "릴리즈해줘", "웹에 올려줘", "웹 버전 갱신" 같은 요청에 쓴다.
---

# 웹 릴리즈

## 구조
- 저장소: https://github.com/AI-syeon/M-School (원격 `origin`, 브랜치 `main`, 공개).
- Unity 빌드는 이 PC에서 한다(GitHub 서버에는 Unity 라이선스가 없다).
- `scripts/release-web.ps1`이 WebGL 빌드 → zip → 태그 `vX.Y.Z` → GitHub Release(초안 → zip 첨부 → 공개)를 한다.
- 릴리즈가 공개되면 `.github/workflows/pages.yml`이 최신 릴리즈의 `MagicCampus-web-*.zip`을 받아 Pages에 배포한다.
- 플레이 주소: https://ai-syeon.github.io/M-School/

## 절차
1. 릴리즈할 변경을 커밋하고 `git push origin main` 한다(스크립트는 미커밋·미push 상태면 멈춘다). 커밋·push는 사용자가 요청할 때만 한다.
2. Unity 에디터가 이 프로젝트를 열고 있으면 닫는다(배치 빌드와 같이 못 쓴다).
3. 실행: `powershell -ExecutionPolicy Bypass -File .claude/skills/release-web/scripts/release-web.ps1` (버전 지정: `-Version v0.2.0`, 설명: `-Notes "..."`, 이미 빌드했으면 `-SkipBuild`). 버전을 안 주면 마지막 태그의 patch +1(첫 릴리즈 v0.1.0).
4. https://github.com/AI-syeon/M-School/actions 에서 "Deploy latest release to Pages"가 성공했는지 확인하고, 플레이 주소를 브라우저로 열어 게임이 시작되는지 본다.

## 주의
- 인증은 Git Credential Manager에 저장된 GitHub 로그인(첫 `git push` 때 로그인)을 쓴다. 토큰을 출력하거나 파일에 남기지 않는다.
- Pages는 Content-Encoding을 안 붙이므로 WebGL은 Gzip + decompression fallback으로 빌드한다(`GameBuild.BuildWeb`).
- 배포 워크플로만 다시 돌리려면 Actions 탭에서 workflow_dispatch로 실행한다(최신 릴리즈를 다시 배포).
