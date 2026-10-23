# 전체 테스트 결과

- 날짜: 2026-10-23
- 명령: `dotnet test --project tests/LimeWin.Core.Tests` (Office, Ffmpeg, Matrix 포함 전체, 배경 제거는 실제 모델로 CPU 추론)
- 환경: Windows 11 Education 10.0.26300, .NET 10, Microsoft Office 설치됨(자동화 가능), ffmpeg가 PATH에 있음
- 끝난 뒤 남은 Word/Excel/PowerPoint/ffmpeg 프로세스: 없음

## 요약 (전체)

| 합계 | 성공 | 실패 | 건너뜀 | 시간 |
|---|---|---|---|---|
| 791 | 779 | 0 | 12 | 3분 56초 |

## 분류별 (같은 테스트가 여러 분류에 걸칠 수 있어 합이 전체와 같지 않다)

| 분류 | 합계 | 성공 | 실패 | 건너뜀 |
|---|---|---|---|---|
| `Category=Office` | 162 | 159 | 0 | 3 |
| `Category=Ffmpeg` | 202 | 202 | 0 | 0 |
| `Category=Matrix` (변환 경로 전체) | 220 | 213 | 0 | 7 |

## 건너뛴 12개와 이유

| 테스트 | 이유 |
|---|---|
| `WordConverterTests.MacroDoc_Refused`, `ExcelConverterTests.MacroWorkbook_Refused`, `PowerPointConverterTests.MacroPresentation_Refused` | 이 PC에서 VBA 프로젝트 접근이 꺼져 있어 매크로 픽스처 파일을 만들 수 없음 (매크로 파일 거부 로직 자체는 다른 테스트가 확인) |
| `ImageCompressorTests.Preset_Heic_UsesQuality`, `TargetSize_Heic_SearchesQuality` | 이 Magick.NET 빌드에는 HEIC 인코더가 없음 |
| `ConversionMatrixTests.Plain` heic → jpg / webp / png / pdf / bmp / tiff / avif (7개) | HEIC를 쓸 수 없어 원본 픽스처를 만들 수 없음 (HEIC는 읽기 전용) |
