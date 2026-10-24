<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/hero-dark.png">
  <img src="docs/images/hero-light.png" alt="Zest: 파일을 끌다가 키를 한 번 탭하면 그 자리에서 변환, 도구, 액션을 고르는 휠이 뜹니다" width="100%">
</picture>

**한국어** · [English](README.en.md)

탐색기에서 파일을 끌다가 키를 한 번 탭하면 파일 둘레에 원형 메뉴(휠)가 뜨고, 꽃잎에 놓는 것만으로 변환·도구·액션을 실행하는 Windows 트레이 앱입니다.

![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
[![License MIT](https://img.shields.io/badge/License-MIT-green)](LICENSE)

## 세 개의 휠

<table>
  <tr>
    <th width="33%">Shift · 변환 · 레몬</th>
    <th width="33%">Alt · 도구 · 탠저린</th>
    <th width="33%">Ctrl · 액션 · 라임</th>
  </tr>
  <tr>
    <td valign="top">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="docs/images/wheel-convert-dark.png">
        <img src="docs/images/wheel-convert-light.png" alt="변환 휠: PDF, JPG, WEBP, HEIC, TIFF, AVIF, BMP 꽃잎과 가운데의 Shift 변환 표시" width="100%">
      </picture>
      <br>끌고 있는 파일을 꽃잎에 적힌 형식으로 바꿉니다.
    </td>
    <td valign="top">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="docs/images/wheel-tools-dark.png">
        <img src="docs/images/wheel-tools-light.png" alt="도구 휠: 압축, 자르기, PDF 만들기, 글자 복사, 배경 제거, 메타데이터 꽃잎과 가운데의 Alt 도구 표시" width="100%">
      </picture>
      <br>압축, 자르기, PDF 만들기, 글자 복사, 배경 제거, 메타데이터 같은 도구를 엽니다.
    </td>
    <td valign="top">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="docs/images/wheel-actions-dark.png">
        <img src="docs/images/wheel-actions-light.png" alt="액션 휠: 선반, 복사, 이동, 경로 복사, 터미널 꽃잎과 가운데의 Ctrl 액션 표시" width="100%">
      </picture>
      <br>선반에 올리기, 복사, 이동, 경로 복사, 터미널 열기를 실행합니다.
    </td>
  </tr>
</table>

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

- **변환 (Shift)**: 이미지, 이미지 → PDF, PDF → jpg/png/txt, 오디오·비디오·GIF(ffmpeg), 문서·슬라이드·스프레드시트(Microsoft Office). PDF → docx는 지원하지 않습니다.
- **도구 (Alt)**: 압축(프리셋·목표 크기·비율·최대 크기), 자르기(비율 고정, 숫자 입력), PDF 만들기(이미지를 순서대로 한 PDF로), PDF 합치기·나누기, 글자 복사, 배경 제거, 메타데이터.
  - 글자 복사는 Windows의 OCR을 씁니다. 한국어·영어 OCR 언어가 설치되어 있어야 합니다(설정 > 시간 및 언어 > 언어).
  - 배경 제거는 처음 쓸 때 약 973 MB 모델(BiRefNet matting)을 내려받아 `%LOCALAPPDATA%\Zest\models`에 두고, 이미지 처리는 이 PC 안에서만 합니다. CPU에서 이미지 한 장에 7초 안팎 걸립니다.
  - 메타데이터는 파일 하나의 태그를 보여 주고, 값을 고치거나 태그를 지울 수 있습니다. **위치 지우기**는 GPS 정보를 지우고, 사진이면 XMP와 HDR 게인 맵 같은 추가 내장 이미지도 함께 지웁니다. **태그 모두 지우기**는 태그를 모두 지우고, 사진의 색 정보(ICC)와 방향은 남깁니다. 파일 여러 개를 함께 놓으면 **위치만 지우기**와 **태그 모두 지우기** 중에서 고릅니다. 원본은 바뀌지 않고, 결과는 원본 옆에 `이름 (edited).확장자` 또는 `이름 (no metadata).확장자`로 생깁니다.
- **액션 (Ctrl)**: 선반(파일을 잠시 올려 두는 작은 카드), 복사, 이동, 경로 복사, 터미널.

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

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/popups-dark.png">
  <img src="docs/images/popups-light.png" alt="진행 카드 세 장, 압축 창, 파일 여섯 개가 올라간 선반" width="100%">
</picture>

왼쪽부터 작업 진행 카드, 압축 창, 선반입니다. 카드는 진행률과 취소 버튼을 보여 주고, 끝난 카드를 누르면 결과 파일을 탐색기에서 보여 줍니다.

## 설치와 빌드

요구 사항:

- Windows 10 1903 이상 / Windows 11, x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- 오디오·비디오·GIF 변환: `ffmpeg`가 PATH에 있어야 합니다. 오디오·비디오 메타데이터는 ffmpeg와 같은 폴더의 `ffprobe`도 씁니다.
- 문서·슬라이드·스프레드시트 변환: Microsoft Office(Word, PowerPoint, Excel). 정품 인증이 안 된 설치에서도 자동화는 동작하지만, Office가 이를 막기 시작하면 변환이 멈출 수 있습니다.

소스에서 직접 빌드해 실행합니다. 빌드에는 .NET 10 SDK가 필요합니다.

```powershell
./build.ps1
dist\Zest.exe
```

`build.ps1`은 `dist`를 비우고 `dotnet publish src/Zest.App -c Release -r win-x64 --self-contained false -o dist`로 배포한 뒤, 디버그 기호(`.pdb`)와 쓰지 않는 `DirectML.dll`을 지웁니다(약 110 MB). 앱을 실행하면 트레이 아이콘이 생깁니다.

테스트: `dotnet test --project tests/Zest.Core.Tests`

이전 이름인 Lime으로 쓰던 PC라면, Zest를 처음 켤 때 데이터 폴더와 Windows 시작 시 실행 등록이 Zest로 옮겨집니다. 이전 Lime이 실행 중이면 Zest는 시작하지 않으므로, 트레이에서 Lime을 먼저 종료합니다.

개발 문서: [설계 결정 기록](docs/decisions.md), [수동 확인 체크리스트](docs/manual-checklist.md), [전체 테스트 결과](docs/test-results.md)

## 트레이 메뉴

- **휠 켜기**: 체크를 풀면 키·마우스 감지를 완전히 끕니다. 앱을 켤 때마다 켜진 상태로 시작합니다.
- **윈도우 시작 시 실행**
- **라이선스 보기**
- **종료**

## 개인정보

- 키 입력을 기록하거나 저장하지 않습니다. 감지하는 것은 Shift/Ctrl/Alt/Esc와 마우스 왼쪽 버튼, 커서 위치뿐이고, 끌기 중일 때만 휠에 쓰입니다.
- 앱 자체는 텔레메트리를 보내지 않습니다. ONNX Runtime의 텔레메트리도 꺼 둡니다. 배경 제거에 쓰는 Windows ML 구성 요소가 보내는 진단 데이터는 Windows의 진단 데이터 설정(설정 › 개인 정보 및 보안 › 진단 및 피드백)을 따릅니다. 자세한 내용은 [NOTICE.md](NOTICE.md)에 있습니다.
- 네트워크는 배경 제거 모델(과 Windows ML 가속기)을 내려받을 때만 씁니다. 파일 내용은 PC 밖으로 나가지 않습니다.

## 알려진 제한

- 압축 프로그램(반디집 등) 창에서 끌어낸 파일은 Zest가 받지 않습니다. 반디집은 끌어 놓을 때 Shift가 눌려 있으면 압축 파일에서 지우므로, 압축 프로그램에서 끌 때는 Shift를 누르지 마세요.
- 메타데이터에서 EXIF 태그를 고치거나 지우면, 새 파일에서 제조사 노트(maker notes)나 일부 태그가 빠지거나 깨질 수 있습니다. 고치지 않은 EXIF는 그대로 복사합니다.
- PDF의 메타데이터를 고친 새 파일에는 XMP 메타데이터가 들어가지 않습니다.

## 원작과 라이선스

Zest는 LumenJSN의 macOS 앱 [Lime](https://github.com/LumenJSN/Lime)(MIT)을 Windows로 옮긴 앱입니다. 원작 Lime의 MIT 고지와 사용한 라이브러리·모델의 라이선스는 [NOTICE.md](NOTICE.md)에 있습니다(앱의 트레이 메뉴 "라이선스 보기"에서도 볼 수 있습니다).

이 프로젝트는 MIT 라이선스입니다([LICENSE](LICENSE)).
