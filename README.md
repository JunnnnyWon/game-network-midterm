# Battery Rush Arena

게임네트워크기초 중간 과제를 위한 Unity 클라이언트 + 외부 C# 서버 + MySQL 구조의 멀티플레이 게임 프로젝트입니다.

이 저장소는 윈도우에서 빠르게 내려받아 이어서 구현하거나 발표할 수 있도록 다음 기준으로 정리되어 있습니다.

- Unity 프로젝트는 저장소 루트가 아니라 `My project/` 입니다.
- 서버는 `src/NetworkSpikeServer/` 에 있습니다.
- DB 초기화 스크립트는 `docker/mysql/init/` 에 있습니다.
- 로컬 캐시와 발표 산출물은 Git 추적 대상에서 제외합니다.
- Codex 템플릿 자산은 별도 저장소 [Codex-Game-Studios](https://github.com/JunnnnyWon/Codex-Game-Studios) 로 분리되었습니다.

중요:

- Unity Hub에서는 저장소 루트가 아니라 `My project/` 폴더를 여세요.
- 윈도우에서는 가능하면 `C:\dev\battery-rush-arena` 같은 짧은 ASCII 경로에 clone 하는 것을 권장합니다.

## 빠른 시작

### 필수 환경

- Windows 10/11
- Git for Windows
- Unity Hub + Unity `6000.3.10f1`
- Unity `Windows Build Support`
- `.NET 10 SDK`
- MySQL 8.x 또는 Docker Desktop

### 가장 빠른 실행 순서

1. MySQL 실행
2. 서버 실행
3. Unity에서 `My project/` 열기
4. `Assets/Scenes/SampleScene.unity` 열기
5. Play 또는 Windows 빌드 실행

처음 Unity를 열 때는 패키지 복원 때문에 인터넷 연결과 Git 사용 가능 상태가 필요할 수 있습니다.

## 프로젝트 구조

```text
My project/                 Unity 클라이언트 프로젝트
src/NetworkSpikeServer/     외부 C# 서버
docker/mysql/init/          MySQL 초기 스키마
docker-compose.mysql.yml    Docker 기반 MySQL 실행 설정
docs/                       배포/구조/아키텍처 문서
design/                     게임 기획 문서
production/                 구현 진행 문서
prototypes/                 초기 실험 및 리포트
```

구조 설명은 [docs/repository-layout.md](/Users/junnnny/Desktop/학교/네트워크프로그래밍/중간/docs/repository-layout.md:1), 윈도우 발표 배포는 [docs/windows-demo-deployment.md](/Users/junnnny/Desktop/학교/네트워크프로그래밍/중간/docs/windows-demo-deployment.md:1) 에 정리되어 있습니다.

## 핵심 구성

### Unity 클라이언트

- 경로: `My project/`
- 주요 스크립트: `My project/Assets/Scripts/NetworkSpike/`
- 역할:
  - 플레이어 입력 처리
  - 룸 생성/참가 UI
  - 서버 스냅샷 수신 및 화면 반영
  - 결과/리더보드 표시

### 외부 C# 서버

- 경로: `src/NetworkSpikeServer/`
- 진입점: `src/NetworkSpikeServer/Program.cs`
- 역할:
  - TCP 연결 수락
  - 룸 상태 관리
  - authoritative 이동/점수/상태 판정
  - 경기 종료 후 MySQL 저장

### MySQL

- Compose 설정: `docker-compose.mysql.yml`
- 초기 스키마: `docker/mysql/init/001-ckgame.sql`
- 역할:
  - 경기 결과 저장
  - 플레이어 승/무/패 및 최고 점수 집계
  - 리더보드 조회

## 윈도우 실행 방법

### 방법 A. 로컬 MySQL 사용

이미 윈도우에 MySQL이 설치되어 있으면 Docker 없이도 실행할 수 있습니다.

1. MySQL 서버 실행
2. `ckgame` 스키마 생성
3. 서버 실행
4. Unity 실행

초기 스키마 적용:

```powershell
mysql -u root -p < .\docker\mysql\init\001-ckgame.sql
```

서버 실행 전에 환경변수로 DB 계정을 맞출 수 있습니다.

```powershell
$env:MYSQL_HOST="127.0.0.1"
$env:MYSQL_PORT="3306"
$env:MYSQL_DATABASE="ckgame"
$env:MYSQL_USER="root"
$env:MYSQL_PASSWORD="비밀번호"
dotnet run --project .\src\NetworkSpikeServer
```

### 방법 B. Docker MySQL 사용

```powershell
docker compose -f docker-compose.mysql.yml up -d
docker compose -f docker-compose.mysql.yml ps
dotnet run --project .\src\NetworkSpikeServer
```

`docker compose ps` 에서 MySQL 컨테이너가 `healthy` 가 된 뒤 서버를 실행하는 것을 권장합니다.

### Unity 실행

Unity Hub에서 이 저장소를 연 뒤, 반드시 `My project/` 폴더를 프로젝트로 여세요.

- Unity 버전: `6000.3.10f1`
- 시작 씬: `Assets/Scenes/SampleScene.unity`

## 기본 접속값

- 게임 서버: `127.0.0.1:7777`
- MySQL: `127.0.0.1:3306`
- 데이터베이스: `ckgame`
- 기본 계정: `ckgame_user`
- 기본 비밀번호: `ckgame_pass`

## 발표용 추천 동선

1. 서버와 MySQL을 같은 윈도우 PC에서 실행
2. Unity 에디터로 한 명 접속
3. Windows 빌드 exe 또는 두 번째 실행본으로 한 명 더 접속
4. 첫 번째 클라이언트에서 방 생성
5. 두 번째 클라이언트에서 룸 코드 입력 후 참가
6. 두 명 모두 Ready
7. 경기 종료 후 결과와 leaderboard 확인

## 네트워크 흐름

1. Unity가 TCP로 서버에 접속합니다.
2. 서버가 룸 생성, 참가, ready, 경기 시작을 처리합니다.
3. 경기 중 클라이언트는 입력만 보내고 서버가 판정을 담당합니다.
4. 서버가 현재 상태를 스냅샷으로 다시 클라이언트에 보냅니다.
5. 경기 종료 후 서버가 MySQL에 결과를 저장하고 리더보드를 반환합니다.

## 비고

- Unity 클라이언트는 MySQL에 직접 접속하지 않습니다.
- DB 접근은 서버에서만 수행합니다.
- 현재 기본 설정은 단일 PC 발표에 가장 잘 맞습니다.
- 여러 PC 데모를 하려면 서버 host와 클라이언트 host를 실제 LAN IP 기준으로 바꿔야 합니다.
