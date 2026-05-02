# ComCross

> 문서 안내: 이 문서는 AI가 생성했거나 번역을 보조했을 수 있습니다.
> 이해와 탐색을 돕기 위한 참고 문서입니다. 영어 README 또는 공식 명세와
> 충돌하는 경우 영어 원문을 기준으로 삼으십시오.

ComCross는 시리얼, TCP, UDP 워크플로를 위한 크로스 플랫폼 임베디드 통신
도구입니다.

## 상태

ComCross는 아직 안정 호환성 기간에 들어가지 않았습니다. 아키텍처 경계,
런타임 디렉터리, 플러그인 배치, 패키징 계약을 바로잡기 위한 breaking change는
허용됩니다. 단, 이러한 변경은 문서화되어야 합니다.

## 주요 기능

- 시리얼, TCP, UDP 버스 어댑터를 격리된 플러그인으로 제공.
- 세션과 워크로드를 영속 descriptor로 관리.
- 제한된 frame attributes를 지원하는 검색 가능한 RX/TX 메시지 스트림.
- 플러그인 설정 페이지와 플러그인이 생성하는 UI 상태.
- Shell은 영어와 중국어 간체 로컬라이제이션을 내장.

## 개발 빠른 시작

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## 중요 사항

- 향후 사용자용 진입점은 `ComCross.Startup`으로 계획되어 있습니다.
- Windows 공개 릴리스 초기에는 자체 서명 테스트 인증서를 사용할 수 있습니다.
  이 인증서는 Windows에서 기본적으로 신뢰되지 않습니다.
- 운영체제 경고를 우회하기 전에 릴리스 체크섬과 서명을 검증해야 합니다.
- 공식 플러그인 패키지 서명은 Windows 코드 서명 및 릴리스 산출물 서명과
  분리됩니다.

## 참고

- 루트 README: [../../README.md](../../README.md)
- 명세 색인: [../specs/00-Index.md](../specs/00-Index.md)
- Startup, 인스턴스 ID, 서명 설계:
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
