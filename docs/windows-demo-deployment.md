# Windows 발표용 배포 가이드

이 문서는 현재 저장소 기준으로 가장 안전한 발표 시나리오를 정리합니다.

권장 발표 구성은 다음과 같습니다.

- 발표용 Windows PC 1대
- Docker Desktop으로 MySQL 실행
- 로컬에서 외부 C# 서버 실행
- 같은 PC에서 Unity Windows 빌드 2개 실행

현재 기본 설정은 서버와 클라이언트 모두 `127.0.0.1:7777`을 사용하므로, 코드 수정 없이 가장 안정적인 방식은 단일 PC 시연입니다.

## 1. 현재 프로젝트 기준 배포 대상

### Unity 클라이언트

- 경로: `unity_midterm/`
- Unity 버전: `6000.3.10f1`
- 기본 빌드 씬: `Assets/Scenes/SampleScene.unity`
- 네트워크 기본값: `127.0.0.1:7777`

### 외부 C# 서버

- 경로: `src/NetworkSpikeServer/`
- 진입점: `src/NetworkSpikeServer/Program.cs`
- 대상 런타임: `.NET 10`
- DB 드라이버: `MySqlConnector 2.4.0`

### MySQL

- Compose 파일: `docker-compose.mysql.yml`
- 스키마 파일: `docker/mysql/init/001-ckgame.sql`
- 기본 DB 접속값:
  - host: `127.0.0.1`
  - port: `3306`
  - database: `ckgame`
  - user: `ckgame_user`
  - password: `ckgame_pass`

## 2. 발표 전 준비물

- Windows 10 또는 11
- Git for Windows
- Unity Hub + Unity `6000.3.10f1`
- Unity `Windows Build Support`
- Docker Desktop
- .NET 10 SDK 또는 Runtime

발표장에서 최대한 흔들리지 않게 하려면 서버는 `publish` 결과물로 준비하고, DB는 Docker Desktop으로 미리 이미지를 받아 두는 것이 좋습니다.
가능하면 프로젝트 경로도 `C:\dev\battery-rush-arena` 처럼 짧고 ASCII 위주로 맞추는 편이 안전합니다.

## 3. 가장 안전한 발표 시나리오

### 권장 시나리오: 한 PC에서 전부 실행

1. Docker Desktop 실행
2. MySQL 컨테이너 실행
3. 외부 서버 실행
4. Unity Windows 빌드 실행
5. 같은 빌드를 한 번 더 실행해서 두 명 접속
6. 한 명이 방 생성, 다른 한 명이 방 참가
7. Ready 후 경기 시작
8. 경기 종료 뒤 결과와 leaderboard 확인

이 방식이 안전한 이유:

- 서버 기본 바인딩이 `127.0.0.1`
- 클라이언트 기본 접속 대상도 `127.0.0.1`
- 네트워크 문제를 발표장에서 최소화할 수 있음

## 4. Windows에서 DB 실행

PowerShell에서 프로젝트 루트 기준:

```powershell
docker compose -f docker-compose.mysql.yml up -d
docker compose -f docker-compose.mysql.yml ps
```

`ckgame-mysql` 이 `healthy` 로 보인 뒤 서버를 실행하는 것을 권장합니다.

정리:

```powershell
docker compose -f docker-compose.mysql.yml down
```

확인 포인트:

- 컨테이너 이름: `ckgame-mysql`
- 포트: `3306`
- 최초 실행 시 `docker/mysql/init/001-ckgame.sql`이 자동 적용됨

## 5. Windows에서 서버 실행

### 방법 A. 발표용 권장: publish 후 exe 실행

프로젝트 루트에서:

```powershell
dotnet publish .\src\NetworkSpikeServer\NetworkSpikeServer.csproj -c Release -r win-x64 --self-contained false
```

생성 위치:

```text
src\NetworkSpikeServer\bin\Release\net10.0\win-x64\publish\
```

실행:

```powershell
.\src\NetworkSpikeServer\bin\Release\net10.0\win-x64\publish\NetworkSpikeServer.exe
```

### 방법 B. 개발용 간단 실행

```powershell
dotnet run --project .\src\NetworkSpikeServer
```

### 환경변수로 DB 값을 바꾸고 싶을 때

기본값을 그대로 써도 되지만, 필요하면 PowerShell에서 아래처럼 지정할 수 있습니다.

```powershell
$env:MYSQL_HOST="127.0.0.1"
$env:MYSQL_PORT="3306"
$env:MYSQL_DATABASE="ckgame"
$env:MYSQL_USER="ckgame_user"
$env:MYSQL_PASSWORD="ckgame_pass"
.\src\NetworkSpikeServer\bin\Release\net10.0\win-x64\publish\NetworkSpikeServer.exe
```

## 6. Unity 클라이언트 Windows 빌드

Unity에서 `unity_midterm`를 연 뒤:

1. `File > Build Profiles` 또는 `File > Build Settings`
2. 플랫폼을 `Windows, Mac, Linux`로 선택
3. 타깃을 `Windows`로 전환
4. `Assets/Scenes/SampleScene.unity`가 포함되어 있는지 확인
5. `Build`로 폴더 생성

발표용 추천:

- `Build\BatteryRushArena\BatteryRushArena.exe` 같은 식으로 별도 폴더에 저장
- 빌드 폴더째 복사해서 준비

처음 Unity를 열 때는 패키지 복원 때문에 인터넷 연결과 Git 사용 가능 상태가 필요할 수 있습니다.

## 7. 발표 당일 실행 순서

### 순서

1. Docker Desktop 실행
2. PowerShell에서 MySQL 실행
3. 다른 PowerShell에서 서버 실행
4. 클라이언트 exe 2개 실행
5. 첫 번째 클라이언트에서 방 생성
6. 두 번째 클라이언트에서 룸 코드 입력 후 참가
7. 두 명 모두 Ready
8. 경기 진행
9. 종료 후 결과 화면에서 persistence 상태와 leaderboard 확인

### 발표 직전 체크

- Docker Desktop이 켜져 있는지
- `3306` 포트가 비어 있는지
- `7777` 포트가 비어 있는지
- 서버 콘솔이 종료되지 않았는지
- 클라이언트가 모두 같은 PC에서 실행 중인지

## 8. 발표 때 강조하면 좋은 기술 포인트

### 1. 서버 authoritative 구조

- Unity 클라이언트는 입력만 전송
- 서버가 이동, 점수, 배터리 획득, 상태 이상, 승패를 판정
- 클라이언트는 서버 스냅샷을 렌더링

### 2. 룸 기반 멀티플레이

- 한 명이 방 생성
- 다른 한 명이 룸 코드로 참가
- 두 명이 Ready 상태가 되면 경기 시작

### 3. 결과 저장 분리

- Unity는 DB에 직접 연결하지 않음
- 서버만 MySQL에 접근
- 경기 종료 후 서버가 `match_results`, `player_stats`를 갱신

### 4. 발표에서 보여주기 좋은 화면

- pre-match 방 생성/참가 UI
- active 상태에서 두 플레이어 위치 동기화
- 경기 종료 후 결과 패널
- persistence 상태 문구
- leaderboard 목록

## 9. 여러 PC로 발표하고 싶을 때 주의점

현재 기본 설정은 다중 PC 발표에 바로 맞지 않습니다.

이유:

- 서버 기본 host가 `127.0.0.1`
- 클라이언트 기본 접속 대상도 `127.0.0.1`

즉, 코드 수정 없이 다른 PC에서 접속하면 각 PC가 자기 자신을 바라보게 됩니다.

여러 PC 데모를 하려면 최소한 다음이 필요합니다.

- 서버 바인딩 주소를 실제 LAN IP 또는 `0.0.0.0`으로 변경
- 클라이언트 기본 host를 발표용 서버 PC IP로 변경
- Windows 방화벽에서 서버 포트 `7777` 허용

여러 PC 발표가 꼭 필요하지 않다면, 이번 발표는 단일 PC 시연이 가장 안전합니다.

## 10. 문제 발생 시 빠른 복구

### 클라이언트가 접속되지 않을 때

- 서버 콘솔이 떠 있는지 확인
- 서버가 `127.0.0.1:7777`에 떠 있는지 확인
- 클라이언트를 서버보다 먼저 실행했다면 다시 접속 시도

### leaderboard가 비어 있을 때

- MySQL 컨테이너가 정상 실행 중인지 확인
- 서버 실행 전에 Docker가 올라왔는지 확인
- 경기 종료까지 완료했는지 확인

### 같은 빌드를 두 번 실행하고 싶을 때

- 현재 프로젝트 설정상 single-instance 강제는 켜져 있지 않음
- 따라서 같은 exe를 두 번 띄우는 단일 PC 데모가 적합함

## 11. 발표용 한 줄 요약

이 프로젝트는 Unity 클라이언트가 입력만 보내고, 외부 C# 서버가 게임 상태와 승패를 authoritative 하게 판정한 뒤, 경기 결과를 MySQL에 저장하고 leaderboard를 다시 클라이언트에 보여주는 구조입니다.
