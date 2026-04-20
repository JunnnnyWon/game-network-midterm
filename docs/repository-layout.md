# Repository Layout

윈도우에서 이 저장소를 clone한 뒤 빠르게 이어서 구현하려면, 아래 폴더만 이해하면 됩니다.

## 실제로 작업할 폴더

### `unity_midterm/`

Unity 클라이언트 프로젝트 루트입니다.

- 열어야 하는 Unity 프로젝트 위치
- 핵심 하위 폴더:
  - `Assets/`
  - `Packages/`
  - `ProjectSettings/`

주의:

- 저장소 루트가 아니라 `unity_midterm/` 를 Unity에서 열어야 합니다.

### `src/NetworkSpikeServer/`

외부 C# TCP 서버입니다.

- `Program.cs`: 서버 진입점
- `SpikeServerHost.cs`: 연결, 룸, authoritative 판정
- `MySqlPersistenceService.cs`: MySQL 저장 및 leaderboard 조회
- `NetworkSpikeServer.csproj`: `.NET 10` 서버 프로젝트

### `docker/` 와 `docker-compose.mysql.yml`

MySQL 초기화 및 빠른 로컬 실행용입니다.

- `docker/mysql/init/001-ckgame.sql`
- `docker-compose.mysql.yml`

## 참고용 문서 폴더

### `docs/`

배포와 구조 문서입니다.

- [windows-demo-deployment.md](/Users/junnnny/Desktop/학교/네트워크프로그래밍/중간/docs/windows-demo-deployment.md:1)
- 이 문서

### `design/`

게임 기획 및 UX 문서입니다.

### `production/`

작업 진행 기록과 에픽 문서입니다.

### `prototypes/`

초기 실험 메모와 네트워크 관련 리포트입니다.

## Git에 올리지 않을 로컬 산출물

아래 항목은 clone 이후 로컬에서 다시 생겨도 정상입니다.

- 루트의 `Library/`, `Logs/`, `UserSettings/`
- `unity_midterm/Library/`
- `unity_midterm/UserSettings/`
- `src/NetworkSpikeServer/bin/`
- `src/NetworkSpikeServer/obj/`
- `tmp/`
- `outputs/`
- 루트에 잘못 생성된 `Assets/`, `Packages/`, `ProjectSettings/`

## 가장 빠른 시작 순서

1. 저장소 clone
2. MySQL 또는 Docker MySQL 준비
3. `dotnet run --project .\src\NetworkSpikeServer`
4. Unity Hub에서 `unity_midterm/` 열기
5. `Assets/Scenes/SampleScene.unity` 열기
6. Play 또는 Windows 빌드 실행
