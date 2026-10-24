<p align="center">
  <picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/header-ko-dark.png" /><img alt="Zest: 끌던 파일을, 그 자리에서 바로." src="docs/images/header-ko-light.png" /></picture>
</p>

<p align="center">
  <b>한국어</b> · <a href="README.en.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/Youl-AI/zest/releases/latest"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/badge/release-v1.0.0.svg?logo=lu:Download&amp;variant=default&amp;size=sm&amp;color=65a30d&amp;mode=dark" /><img alt="Release v1.0.0" src="https://shieldcn.dev/badge/release-v1.0.0.svg?logo=lu:Download&amp;variant=default&amp;size=sm&amp;color=65a30d&amp;mode=light" /></picture></a>
  <picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/badge/Windows-10_%7C_11.svg?logo=lu:AppWindow&amp;variant=secondary&amp;size=sm&amp;mode=dark" /><img alt="Windows 10 | 11" src="https://shieldcn.dev/badge/Windows-10_%7C_11.svg?logo=lu:AppWindow&amp;variant=secondary&amp;size=sm&amp;mode=light" /></picture>
  <picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/badge/.NET-10.svg?logo=dotnet&amp;variant=secondary&amp;size=sm&amp;mode=dark" /><img alt=".NET 10" src="https://shieldcn.dev/badge/.NET-10.svg?logo=dotnet&amp;variant=secondary&amp;size=sm&amp;mode=light" /></picture>
  <a href="LICENSE"><picture><source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/badge/License-MIT.svg?logo=lu:Scale&amp;variant=secondary&amp;size=sm&amp;mode=dark" /><img alt="License MIT" src="https://shieldcn.dev/badge/License-MIT.svg?logo=lu:Scale&amp;variant=secondary&amp;size=sm&amp;mode=light" /></picture></a>
</p>

<div align="center">

**Zest**는 탐색기에서 파일을 끌다가 키를 한 번 탭하면 휠을 띄워, 꽃잎에 놓는 것만으로 형식을 바꾸고 도구를 열고 파일을 옮기게 해 주는 Windows 트레이 앱입니다.

</div>

<p align="center">
  <img src="docs/images/demo-ko.webp" width="720" alt="탐색기에서 사진을 끌다가 Shift를 탭하자 커서 자리에 변환 휠이 열리고 가운데에 5.6 MB가 보인다. JPG에 놓으면 진행 카드가 끝나고 JPG 파일이 생긴다. 그 JPG를 Alt 도구 휠의 압축에 놓아 압축 창에서 압축하고, 보고서.pdf는 Ctrl 액션 휠의 선반에 놓아 선반에 올린다." />
  <br />
  <sub>Shift는 변환, Alt는 도구, Ctrl은 액션 휠</sub>
</p>

## 시작하기

1. 탐색기에서 파일을 **누른 채로** 끌기 시작합니다.
2. 버튼을 누른 채로 수정 키를 **한 번 탭**합니다.
   - **Shift**: 변환 휠
   - **Alt**: 도구 휠
   - **Ctrl**: 액션 휠
3. 휠이 뜨면 원하는 꽃잎 위에 놓습니다. 휠 가운데에는 끌고 있는 파일의 크기가 보이고, 꽃잎 위에 올리면 그 꽃잎 이름으로 바뀝니다.
4. 휠 **바깥**이나 **가운데 구멍**에 놓거나 Esc를 누르면 아무 일도 일어나지 않습니다.
5. 변환·도구·메타데이터는 원본을 바꾸지 않고, 결과를 원본 옆에 새 파일로 만듭니다. 원본 파일을 옮기는 것은 **이동** 액션뿐입니다.

키를 탭하지 않고 끌면 평소와 똑같이 동작합니다.

## 기능

<p align="center">
  <picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/wheels-dark.png" /><img alt="세 휠: 가운데에 파일 크기 5.6 MB가 보이는 변환 휠(Shift), 메타데이터가 강조된 도구 휠(Alt), 선반이 강조된 액션 휠(Ctrl)" src="docs/images/wheels-light.png" /></picture>
</p>

**변환 (Shift)**

- 끌고 있는 파일을 꽃잎에 적힌 형식으로 바꿉니다.
- 이미지, 이미지 → PDF, PDF → jpg/png/txt, 오디오·비디오·GIF(ffmpeg), 문서·슬라이드·스프레드시트(Microsoft Office)를 다룹니다.
- PDF → docx는 지원하지 않습니다.

**도구 (Alt)**

- 압축: 프리셋, 목표 크기, 비율, 최대 크기
- 자르기: 비율 고정, 숫자 입력
- PDF 만들기(이미지를 순서대로 한 PDF로), PDF 합치기·나누기
- 글자 복사: Windows의 OCR을 씁니다. 한국어·영어 OCR 언어가 설치되어 있어야 합니다(설정 › 시간 및 언어 › 언어).
- 배경 제거: 처음 쓸 때 약 973 MB 모델(BiRefNet matting)을 내려받아 `%LOCALAPPDATA%\Zest\models`에 두고, 이미지 처리는 이 PC 안에서만 합니다. CPU에서 이미지 한 장에 7초 안팎이 걸립니다.
- 메타데이터: 파일 하나의 태그를 보여 주고, 값을 고치거나 태그를 지울 수 있습니다.
  - **위치 지우기**는 GPS 정보를 지웁니다. 사진이면 XMP 정보와 HDR 게인 맵 같은 추가 내장 이미지도, 동영상이면 위치 기록이 담기는 데이터 트랙도 함께 지웁니다.
  - **태그 모두 지우기**는 태그를 전부 지우되, 사진의 색 정보(ICC)와 방향은 남깁니다.
  - 파일 여러 개를 함께 놓으면 **위치만 지우기**와 **태그 모두 지우기** 중에서 고릅니다.
  - 원본은 바뀌지 않고, 결과는 원본 옆에 `이름 (edited).확장자` 또는 `이름 (no metadata).확장자`로 생깁니다.

**액션 (Ctrl)**

- 선반(파일을 잠시 올려 두는 작은 카드), 복사, 이동, 경로 복사, 터미널

변환할 수 없는 조합의 꽃잎은 나타나지 않습니다. Office나 ffmpeg가 없으면 해당 변환이 휠에서 빠집니다.

<details>
<summary>지원 형식</summary>

**변환**

- 이미지: jpg, png, webp, heic, tiff, avif, bmp, svg → jpg, png, webp, heic, tiff, avif, bmp, pdf. svg는 읽기만 합니다. heic와 avif로 저장하려면 이 PC에 해당 인코더가 있어야 합니다.
- GIF → mp4, mov, mkv, avi, wmv
- 오디오: mp3, m4a, wav, flac, ogg, opus, aiff, wma 사이에서 서로 바꿉니다.
- 비디오: mp4, mov, mkv, webm, avi, wmv → 다른 비디오 형식, gif, mp3. webm은 읽기만 합니다.
- PDF → jpg, png, txt
- 문서: docx, doc, rtf, odt, txt, md, html → docx, odt, txt, md, html, pdf. doc와 rtf는 읽기만 합니다. md ↔ html은 Office 없이도 됩니다.
- 슬라이드: ppt, pptx, odp → pdf, jpg, png. ppt는 pptx로도 바꿉니다. ppt와 odp는 읽기만 합니다.
- 스프레드시트: xlsx, xls, ods, csv, tsv → xlsx, ods, csv, tsv, pdf. xls는 읽기만 합니다.

**메타데이터**

- jpg, png, webp, pdf
- avif, heic: 이 PC에 해당 인코더가 있을 때만
- 오디오·비디오: ffmpeg와 ffprobe가 있을 때만. 다시 인코딩하지 않고 태그만 바꿉니다.
- tiff는 지원하지 않습니다.

</details>

## 창과 카드

<p align="center">
  <picture><source media="(prefers-color-scheme: dark)" srcset="docs/images/popups-dark.png" /><img alt="진행 카드 세 장, 압축 창, 파일 여섯 개가 올라간 선반" src="docs/images/popups-light.png" /></picture>
</p>

왼쪽부터 작업 진행 카드, 압축 창, 선반입니다. 카드에는 진행률과 취소 버튼이 있고, 끝난 카드를 누르면 탐색기가 열리며 결과 파일이 선택됩니다.

## 설치와 빌드

### 설치

1. [Releases 페이지](https://github.com/Youl-AI/zest/releases/latest)에서 `Zest-1.0.0-win-x64.zip`을 내려받습니다.
2. 원하는 곳(예: 문서 폴더 안의 새 폴더)에 압축을 풉니다. 따로 설치하는 과정은 없습니다.
3. 압축을 푼 `Zest-1.0.0-win-x64` 폴더에서 `Zest.exe`를 실행하면 트레이 아이콘이 생깁니다.

패키지에 .NET 런타임이 들어 있어 .NET을 따로 설치하지 않아도 됩니다.

내려받은 파일이 맞는지는 PowerShell에서 아래 명령으로 해시를 구해, Releases 페이지의 `SHA256SUMS.txt`에 적힌 값과 비교해 확인합니다. 대소문자는 달라도 됩니다.

```powershell
Get-FileHash .\Zest-1.0.0-win-x64.zip -Algorithm SHA256
```

Zest는 아직 코드 서명이 되어 있지 않아서, 처음 실행할 때 Windows가 "Windows의 PC 보호" 창을 띄울 수 있습니다. 이때 **추가 정보**를 누른 뒤 **실행**을 누릅니다. 이 저장소의 Releases 페이지에서 받은 파일에만 이렇게 하고, 그 전에 SHA256을 먼저 비교하세요.

요구 사항:

- Windows 10 1903 이상 / Windows 11, x64
- 오디오·비디오·GIF 변환: `ffmpeg`가 PATH에 있어야 합니다. 오디오·비디오 메타데이터는 ffmpeg와 같은 폴더의 `ffprobe`도 씁니다.
- 문서·슬라이드·스프레드시트 변환: Microsoft Office(Word, PowerPoint, Excel). 정품 인증이 안 된 설치에서도 자동화는 동작하지만, Office가 이를 막기 시작하면 변환이 멈출 수 있습니다.

### 소스에서 빌드

빌드에는 .NET 10 SDK가 필요합니다.

```powershell
./build.ps1
dist\Zest.exe
```

`build.ps1`은 `dist`를 비우고 `dotnet publish src/Zest.App -c Release -r win-x64 --self-contained false -o dist`로 배포한 뒤, 디버그 기호(`.pdb`)와 쓰지 않는 `DirectML.dll`, PDFsharp 보조 어셈블리를 지웁니다. 결과는 약 110 MB이고, 실행하려면 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)이 있어야 합니다(.NET 10 SDK에 들어 있습니다). 앱을 실행하면 트레이 아이콘이 생깁니다.

`./release.ps1`은 Releases 페이지에 올리는 zip을 만듭니다. .NET 런타임을 함께 넣어(self-contained) `artifacts\release\Zest-<버전>-win-x64`에 배포하고, 위와 같은 파일을 지운 뒤, 이 폴더를 `Zest-<버전>-win-x64.zip`으로 묶고 `SHA256SUMS.txt`를 씁니다. 버전은 `src/Zest.App/Zest.App.csproj`의 `<Version>`에서 읽습니다.

테스트: `dotnet test --project tests/Zest.Core.Tests`

README 이미지는 `dist\Zest.exe --render-readme docs/images`로 다시 그립니다. 데모 영상은 `dist\Zest.exe --render-demo docs/images artifacts/demo`로 만듭니다. `docs/images`에 webp가, `artifacts/demo`에 1080p mp4가 생기며, ffmpeg가 필요합니다.

개발 문서: [설계 결정 기록](docs/decisions.md), [수동 확인 체크리스트](docs/manual-checklist.md), [전체 테스트 결과](docs/test-results.md)

## 트레이 메뉴

- **휠 켜기**: 체크를 풀면 키·마우스 감지를 완전히 끕니다. 앱을 켤 때마다 켜진 상태로 시작합니다.
- **윈도우 시작 시 실행**
- **라이선스 보기**
- **종료**

## 개인정보

- 키 입력을 기록하거나 저장하지 않습니다. 감지하는 것은 Shift/Ctrl/Alt/Esc와 마우스 왼쪽 버튼, 커서 위치뿐이고, 끌기 중일 때만 휠에 쓰입니다.
- 앱 자체는 텔레메트리를 보내지 않습니다. ONNX Runtime의 텔레메트리도 꺼 둡니다. 배경 제거에 쓰는 Windows ML 구성 요소가 보내는 진단 데이터는 Windows의 진단 데이터 설정(설정 › 개인 정보 및 보안 › 진단 및 피드백)을 따릅니다. 자세한 내용은 [NOTICE.md](NOTICE.md)에 있습니다.
- 네트워크는 배경 제거 모델과 Windows ML 가속기를 내려받을 때만 씁니다. 파일 내용은 PC 밖으로 나가지 않습니다.

## 알려진 제한

- 압축 프로그램(반디집 등) 창에서 끌어낸 파일은 Zest가 받지 않습니다. 반디집은 끌어 놓을 때 Shift가 눌려 있으면 압축 파일에서 지우므로, 압축 프로그램에서 끌 때는 Shift를 누르지 마세요.
- 메타데이터에서 EXIF 태그를 고치거나 지우면, 새 파일에서 제조사 노트(maker notes)나 일부 태그가 빠지거나 깨질 수 있습니다. 고치지 않은 EXIF는 그대로 복사합니다.
- EXIF 태그를 고치거나 지우면 바꾼 값이 다른 곳에 남지 않도록 XMP와 추가 내장 이미지(HDR 게인 맵 등)도 새 파일에서 빠집니다.
- 메타데이터를 고치면 jpg가 아닌 이미지는 다시 인코딩됩니다(webp·avif·heic는 품질 97). 움직이는 webp는 첫 프레임만 남습니다.
- 동영상의 **위치 지우기**는 자막 트랙 안에 든 위치 기록(DJI 드론 영상 등)은 지우지 못합니다.
- PDF의 메타데이터를 고친 새 파일에는 XMP 메타데이터가 들어가지 않습니다.

## 라이선스

MIT 라이선스입니다([LICENSE](LICENSE)). 사용한 라이브러리와 모델의 라이선스는 [NOTICE.md](NOTICE.md)에 있습니다(트레이 메뉴의 "라이선스 보기"에서도 볼 수 있습니다).
