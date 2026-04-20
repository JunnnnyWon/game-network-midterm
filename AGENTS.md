# 게임네트워크기초중간 작업 안내

이 저장소는 네트워크 프로그래밍 중간 프로젝트 전용 저장소입니다.

## 실제 작업 위치

- Unity 클라이언트: `unity_midterm/`
- 외부 C# 서버: `src/NetworkSpikeServer/`
- MySQL 초기 스키마: `docker/mysql/init/001-ckgame.sql`

## 작업 원칙

- Unity는 저장소 루트가 아니라 `unity_midterm/`를 엽니다.
- 게임 로직과 DB 접근은 서버 중심 구조를 유지합니다.
- Unity 클라이언트는 입력과 화면 표시를 담당하고, MySQL에 직접 연결하지 않습니다.
- 윈도우 실행 절차는 `README.md`와 `docs/windows-demo-deployment.md`를 우선 기준으로 봅니다.

## Git 주의사항

- `Library/`, `Logs/`, `UserSettings/`, `tmp/`, `outputs/` 같은 로컬 산출물은 추적하지 않습니다.
- 루트에 잘못 생성된 `Assets/`, `Packages/`, `ProjectSettings/`는 이 저장소의 실제 프로젝트가 아닙니다.
- 실제 Unity 프로젝트 자산은 `unity_midterm/Assets`, `unity_midterm/Packages`, `unity_midterm/ProjectSettings` 아래에 있습니다.
