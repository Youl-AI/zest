# Lime for Windows

탐색기에서 파일을 끌다가 키를 한 번 탭하면 파일 둘레에 원형 메뉴(휠)가 뜨고, 꽃잎에 놓는 것만으로 변환·도구·액션을 실행하는 트레이 앱입니다. macOS 앱 [Lime](https://github.com/LumenJSN/Lime)(LumenJSN, MIT)을 Windows로 옮겼습니다.

## 쓰는 법

1. 탐색기에서 파일을 **누른 채로** 끌기 시작합니다.
2. 버튼을 누른 채로 수정 키를 **한 번 탭**합니다.
   - **Shift** = 변환 휠
   - **Alt** = 도구 휠
   - **Ctrl** = 액션 휠
3. 휠이 뜨면 원하는 꽃잎 위에 놓습니다.
4. 휠 **바깥**이나 **가운데 구멍**에 놓거나 Esc를 누르면 아무 일도 일어나지 않습니다. 원본은 어떤 경우에도 이동·삭제·덮어쓰기 되지 않고, 결과는 원본 옆에 새 파일로 생깁니다.

키를 탭하지 않고 끌면 평소와 똑같이 동작합니다.

## 기능

- **변환 (Shift)**: 이미지(jpg, png, webp, tiff, avif, bmp, svg, heic), 이미지 → PDF, PDF → jpg/png/txt, 오디오·비디오·GIF(ffmpeg), 문서·슬라이드·스프레드시트(Microsoft Office). PDF → docx는 지원하지 않고, HEIC는 읽기 전용입니다.
- **도구 (Alt)**: 압축(프리셋·목표 크기·비율·최대 크기), 자르기(비율 고정, 숫자 입력), PDF 만들기(이미지를 순서대로 한 PDF로), PDF 합치기·나누기, 글자 복사, 배경 제거.
  - 글자 복사는 Windows의 OCR을 씁니다. 한국어·영어 OCR 언어가 설치되어 있어야 합니다(설정 > 시간 및 언어 > 언어).
  - 배경 제거는 처음 쓸 때 약 973 MB 모델(BiRefNet matting)을 내려받아 `%LOCALAPPDATA%\LimeWin\models`에 두고, 이미지 처리는 이 PC 안에서만 합니다. CPU에서 이미지 한 장에 7초 안팎 걸립니다.
- **액션 (Ctrl)**: 선반(파일을 잠시 올려 두는 작은 카드), 복사, 이동, 경로 복사, 터미널.

변환할 수 없는 조합의 꽃잎은 나타나지 않습니다. Office나 ffmpeg가 없으면 해당 변환이 휠에서 빠집니다.

## 요구 사항

- Windows 10 1903 이상 / Windows 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- 오디오·비디오·GIF 변환: `ffmpeg`가 PATH에 있어야 합니다.
- 문서·슬라이드·스프레드시트 변환: Microsoft Office(Word, PowerPoint, Excel). 정품 인증이 안 된 설치에서도 자동화는 동작하지만, Office가 이를 막기 시작하면 변환이 멈출 수 있습니다.

## 빌드와 실행

.NET 10 SDK가 필요합니다.

```powershell
./build.ps1
dist\LimeWin.App.exe
```

`build.ps1`은 `dist`를 비우고 `dotnet publish src/LimeWin.App -c Release -r win-x64 --self-contained false -o dist`로 배포한 뒤, 디버그 기호(`.pdb`)와 쓰지 않는 `DirectML.dll`을 지웁니다(약 110 MB). 앱을 실행하면 트레이 아이콘이 생깁니다.

테스트: `dotnet test --project tests/LimeWin.Core.Tests`

## 트레이 메뉴

- **휠 켜기**: 체크를 풀면 키·마우스 감지를 완전히 끕니다. 앱을 켤 때마다 켜진 상태로 시작합니다.
- **윈도우 시작 시 실행**
- **라이선스 보기**
- **종료**

## 알려진 제한

- 압축 프로그램(반디집 등) 창에서 끌어낸 파일은 Lime이 받지 않습니다. 반디집은 끌어 놓을 때 Shift가 눌려 있으면 압축 파일에서 지우므로, 압축 프로그램에서 끌 때는 Shift를 누르지 마세요.

## 개인정보

- 키 입력을 기록하거나 저장하지 않습니다. 감지하는 것은 Shift/Ctrl/Alt/Esc와 마우스 왼쪽 버튼, 커서 위치뿐이고, 끌기 중일 때만 휠에 쓰입니다.
- 텔레메트리가 없습니다. ONNX Runtime의 텔레메트리도 꺼 둡니다.
- 네트워크는 배경 제거 모델(과 Windows ML 가속기)을 내려받을 때만 씁니다. 파일 내용은 PC 밖으로 나가지 않습니다.

## 문서

- [설계 결정 기록](docs/decisions.md)
- [수동 확인 체크리스트](docs/manual-checklist.md)
- [전체 테스트 결과](docs/test-results.md)

## 라이선스

MIT ([LICENSE](LICENSE)). 원작 Lime의 MIT 고지와 사용한 라이브러리·모델의 라이선스는 [NOTICE.md](NOTICE.md)에 있습니다(앱의 트레이 메뉴 "라이선스 보기"에서도 볼 수 있습니다).
