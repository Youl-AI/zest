# Lime for Windows 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 탐색기에서 파일을 끌다가 Shift/Alt/Ctrl을 탭하면 휠이 뜨고, 꽃잎에 놓으면 변환·도구·액션이 실행되는 윈도우 트레이 앱을 만든다.

**Architecture:** 순수 로직(형식 표, 꽃잎 목록, 출력 이름, 드래그-탭 감지, 휠 기하, 변환·도구 엔진)은 `LimeWin.Core` 라이브러리에 두고 xUnit으로 시험한다. WPF 앱 `LimeWin.App`은 저수준 훅·투명 오버레이·휠 창·진행 카드·도구 창·트레이만 맡고 Core를 호출한다. Office 변환은 전용 STA 스레드 하나에서 COM 늦은 바인딩(`dynamic`)으로 직렬 실행한다.

**Tech Stack:** C# / .NET 10 (`net10.0-windows10.0.19041.0`), WPF, ffmpeg(외부 프로세스), MS Office COM, `Windows.Media.Ocr`. NuGet 패키지는 아래 표의 버전으로 고정한다(2026-10-22 NuGet에서 확인한 최신 안정판).

| 패키지 | 버전 | 용도 / 고른 이유 |
|---|---|---|
| Magick.NET-Q8-AnyCPU | 15.0.0 | 이미지 읽기·쓰기 |
| PDFsharp | 6.5.0 | PDF 만들기·합치기·나누기 (7.0은 프리뷰라 제외) |
| PdfPig (패키지 ID가 `UglyToad.PdfPig`에서 `PdfPig`로 바뀜) | 0.1.16 | PDF 텍스트 추출 |
| PDFtoImage | 5.4.0 | PDF 쪽 렌더링. Chrome과 같은 PDFium 엔진이라 `Windows.Data.Pdf`보다 검증된 렌더러. 스레드 안전하지 않으므로 호출을 직렬화 |
| Markdig / ReverseMarkdown | 1.4.2 / 6.3.0 | Markdown ↔ HTML |
| Microsoft.Windows.AI.MachineLearning | 2.4.89 | 배경 제거 추론(Windows ML). DirectML은 Microsoft가 유지보수 단계로 돌렸고, Windows ML은 RTX용 TensorRT 실행 공급자를 자동으로 받아 준다. 앱에 포함하는(self-contained) 방식이라 Windows App SDK 런타임 설치가 필요 없다 |
| WPF-UI | 4.3.0 | 도구 창·결과 창·선반을 Windows 11 Fluent 스타일(Mica, 둥근 모서리, 시스템 강조색)로. .NET 10 내장 `ThemeMode`는 아직 실험 기능이라 제외 |
| H.NotifyIcon.Wpf | 2.4.1 | 트레이 아이콘 + Windows 11 스타일 WPF 메뉴 (WinForms 메뉴 대신) |
| xunit.v3 / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 4.0.2 / 4.0.1 / 18.11.0 | 시험 |

같은 주 버전 안의 새 패치가 나오면 올려도 되지만 주 버전은 올리지 않는다.

**Spec:** `docs/superpowers/specs/2026-10-22-lime-windows-design.md` (실행자는 계획과 함께 반드시 읽는다)

**원작 참고본:** https://github.com/LumenJSN/Lime 커밋 `db94ab7fd723193d5b0cdcc748fb9524526ea6bc`. 필요하면 저장소 **밖**(예: `%TEMP%\lime-ref`)에 `git clone` 해서 읽는다. 저장소에 커밋하지 않는다.

## Global Constraints

- 원본 파일은 절대 수정·삭제·덮어쓰기 하지 않는다. 결과는 원본과 같은 폴더.
- 변환 결과 `이름.확장자`, 충돌 시 `이름 2.확장자`, `이름 3.확장자`…
- 도구 결과 `이름 (꼬리표).확장자`, 충돌 시 `이름 (꼬리표) 2.확장자`. 꼬리표: `compressed`, `cropped`, `merged`, `pages`, `text`, `no background`.
- 여러 결과는 `이름 (pages)` 폴더에 `이름 01.png` … (자릿수 = 쪽수의 자릿수, 최소 1). 1쪽이면 폴더 없이 `이름.png`.
- 키: Shift = 변환, Alt = 도구, Ctrl = 액션. 정확히 그 키 하나가 새로 눌리는 순간 발동. 드래그 중이 아닐 때는 무시. 훅은 키를 삼키지 않는다(스파이크가 다르게 결론 내지 않는 한).
- 휠이 뜨면 모든 모니터를 덮는 투명 드롭 영역이 함께 뜬다. 꽃잎 외 드롭(가운데 구멍·바깥)은 취소.
- 드래그가 끝나면(드롭·버튼 뗌·Esc) 덮개는 반드시 닫힌다.
- 사용자가 열어 둔 Office 창·문서는 절대 닫지 않는다. 우리가 띄운 인스턴스만, 우리가 열지 않은 문서가 없을 때만 종료.
- 품질 기본값(원작 `ConvertPrefs`): 손실 압축 품질 0.8 (mov는 0.92), 오디오 256 kbps, GIF 15 fps · 최대 폭 960 px, 메타데이터 유지.
- UI 문구는 한국어. 오류 메시지는 `LimeException`(한국어 한 줄)으로 사용자에게 보인다.
- 원작 MIT 고지(`Copyright (c) LumenJSN`, 원작 `LICENSE` 원문)와 사용 라이브러리·모델 라이선스를 `NOTICE.md`와 앱 "라이선스 보기"에 포함.
- 범위 밖(스펙 §2 비목표): 자막, 압축 파일, AirDrop, 오디오·비디오 편집 도구, Pages/EPUB, 설정 UI, 자동 업데이트.
- **미감 (사용자 요구):** 원작 수준의 완성도. 휠은 원작 `Screenshots/wheel-liquid.png`(라이트, 뒤가 비치는 유리)와 `wheel-solid-dark.png`(다크)를 기준 이미지로 삼는다. 모든 창은 WPF-UI Fluent 스타일. 글꼴: 한글 라벨은 Pretendard(OFL, 앱에 포함), 형식 라벨(JPG·PDF)은 Segoe UI Variable Display Semibold. 아이콘: Windows 11 내장 Segoe Fluent Icons. 모션 값은 `emil-design-eng` 기준(300 ms 이하, ease-in 금지, scale(0)에서 시작 금지).
- **시각 확인 루프:** `LimeWin.App.exe --render <폴더>`가 휠(라이트·다크 × 꽃잎 1·4·7·9개 × 강조 있음/없음)·도구 창·진행 카드를 PNG로 그린다(원작의 `--render`와 같은 역할). 시각 작업을 한 작업은 반드시 렌더를 찍어 기준 이미지와 나란히 보고, 최종 판단은 사용자가 한다.

## Review Focus

1. **한글·공백·괄호가 들어간 경로** (`C:\자료\보고서 (최종).pptx`) — ffmpeg 인수, Office `SaveAs`, PdfSharp 모두에서 그대로 동작해야 한다. → Task 10의 매트릭스 시험이 모든 경로를 `한글 파일 (1).<ext>` 이름 픽스처로 돌린다.
2. **쓰기 금지 폴더의 원본**(읽기 전용 폴더, `C:\Program Files` 등) — 부분 파일 없이 명확한 오류. → Task 4 `ConversionRouter_ReadOnlyFolder_ThrowsLimeException_AndLeavesNoFile`.
3. **확장자와 내용이 다른 파일·0바이트·깨진 파일** — 오류 카드, 앱 안 죽음, Office 프로세스 안 남음. → Task 4(이미지), Task 5(PDF), Task 8(Word) 각 `Corrupt` 시험.
4. **중간 취소** — 여러 쪽 PDF·긴 영상 변환 중 취소하면 부분 결과(파일·`(pages)` 폴더)를 지운다. → Task 5 `PdfToPng_Cancelled_RemovesPagesFolder`, Task 7 `Ffmpeg_Cancelled_KillsProcessAndRemovesOutput`.
5. **암호 걸린 PDF/Office 파일** — 대화상자 없이 즉시 실패. → Task 5 `EncryptedPdf_Throws`, Task 8·9 `PasswordProtected_ThrowsWithoutPrompt`.

---

## Phase 0 — 위험 검증

### Task 0: 드래그-휠 스파이크 (버림용)

이 작업은 시험 주도 개발을 하지 않는다. 목적은 스펙 §4.3 위험을 실제로 확인하는 것뿐이다. 코드는 `spike/drag-wheel` 브랜치에만 두고 main에 병합하지 않는다.

**Files:**
- Create: `spikes/DragWheelSpike/` (WPF 앱 하나, 구조 자유)
- Create (main에 커밋): `docs/spike-notes.md`

- [ ] **Step 1:** `git switch -c spike/drag-wheel` 후 `dotnet new wpf -o spikes/DragWheelSpike -f net10.0-windows10.0.19041.0`.
- [ ] **Step 2:** 구현: `SetWindowsHookEx(WH_MOUSE_LL)` + `WH_KEYBOARD_LL`(관찰만, 항상 `CallNextHookEx`). 왼쪽 버튼 누른 채 `GetSystemMetrics(SM_CXDRAG)` 이상 움직인 상태에서 Shift/Ctrl/Alt 단독 키다운 전이가 오면 (a) 가상 화면 전체(`SystemParameters.VirtualScreen*`)를 덮는 `AllowsTransparency=True`, 배경 `#01000000`, `Topmost`, `ShowActivated=False`, `AllowDrop=True` 창을 띄우고 (b) 커서 중심 320×320 원을 그린다. `DragEnter`에서 `DataFormats.FileDrop` 목록을 화면에 표시, `Drop`에서 원 안/밖을 판정해 로그. 버튼 뗌 훅 또는 `Esc`에서 창 닫기.
- [ ] **Step 3:** 실제로 해 보고 `docs/spike-notes.md`에 아래 질문마다 예/아니오와 관찰을 적는다.
  1. 탐색기에서 파일을 끌다 Shift를 탭하면 **마우스를 움직이지 않아도** 덮개가 `DragEnter`를 받는가? 움직여야 받는가?
  2. 원 안에 놓으면 우리 `Drop`이 오고 탐색기는 아무 것도 안 하는가? 원 밖에 놓아도 마찬가지인가?
  3. Shift·Ctrl·Alt 탭이 탐색기 쪽에 부작용을 내는가? (Alt 메뉴 활성화, 놓은 뒤 이동/복사/바로가기 동작, 커서 모양)
  4. Esc로 드래그 취소 시, 그리고 바탕화면·다른 모니터에서 끌기 시작했을 때 덮개가 닫히는가?
  5. 텍스트 선택 드래그·창 이동 드래그 중 키 탭 시 무슨 일이 생기는가? (`FileDrop`이 없으면 휠을 그리지 말아야 함)
  6. 혼합 DPI 다중 모니터에서 덮개 위치·원 위치가 맞는가?
- [ ] **Step 4:** 판정. 1·2·4가 "예"면 통과 → `docs/spike-notes.md`에 Task 6에서 쓸 결론(움직임 필요 여부, 키 삼키기 필요 여부)을 적고 main에 그 파일만 커밋한다:
  ```bash
  git switch main && git checkout spike/drag-wheel -- docs/spike-notes.md && git add docs/spike-notes.md && git commit -m "docs: drag-wheel spike findings"
  ```
  하나라도 "아니오"면 **여기서 멈추고 사용자에게 돌아간다**(스펙 §4.3). 이후 작업을 진행하지 않는다.

---

## Phase 1 — 필수 경로 (이미지 ↔ 이미지, 이미지 ↔ PDF)

### Task 1: 솔루션 뼈대 + 형식 표 + 출력 이름

**Files:**
- Create: `LimeWin.slnx`, `Directory.Build.props` (`<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<LangVersion>latest</LangVersion>`), `Directory.Packages.props` (중앙 패키지 관리, Tech Stack 표의 버전을 전부 여기에 고정)
- Create: `src/LimeWin.Core/LimeWin.Core.csproj`, `src/LimeWin.Core/Catalog/FormatCatalog.cs`, `src/LimeWin.Core/Output/OutputNaming.cs`, `src/LimeWin.Core/LimeException.cs`
- Create: `tests/LimeWin.Core.Tests/LimeWin.Core.Tests.csproj` (xUnit), `tests/LimeWin.Core.Tests/FormatCatalogTests.cs`, `tests/LimeWin.Core.Tests/OutputNamingTests.cs`
- Create: `.gitignore` (`bin/ obj/ *.user .vs/`), `LICENSE` (MIT, 이 저장소), `NOTICE.md` (원작 MIT 원문 + 아래 작업에서 라이브러리 추가 시마다 갱신), `docs/decisions.md` (구현자 재량 결정 기록)

**Interfaces:**
- Produces:
  - `public sealed class LimeException(string message) : Exception(message)`
  - `public enum FormatCategory { Image, Gif, Audio, Video, Pdf, Document, Slides, Spreadsheet }`
  - `public sealed record Capabilities(bool Office, bool Ffmpeg, bool HeicWrite, bool AvifWrite, bool Ocr, bool BackgroundRemoval) { public static Capabilities All { get; } }`
  - `FormatCatalog.FormatOf(string path) -> string?` — 정규화된 소문자 형식, 폴더·확장자 없음은 null. 별칭: `jpeg/jpe/jfif→jpg`, `tif→tiff`, `heif→heic`, `aif/aifc→aiff`, `markdown/mdown→md`, `htm/xhtml→html`.
  - `FormatCatalog.CategoryOf(string format) -> FormatCategory?`
  - `FormatCatalog.TargetsFor(string format, Capabilities caps) -> IReadOnlyList<string>` (자기 형식·입력 전용 형식 제외, 능력 없으면 해당 대상 제외)
  - `OutputNaming.Stem(string path) -> string`
  - `OutputNaming.UniquePath(string directory, string stem, string ext) -> string`
  - `OutputNaming.ConvertOutput(string source, string targetFormat) -> string`
  - `OutputNaming.ToolOutput(string source, string tag, string ext) -> string`
  - `OutputNaming.CreatePagesFolder(string source) -> string` (고유한 `이름 (pages)` 폴더를 만들고 경로 반환)
  - `OutputNaming.PageFileName(string stem, int index1, int count, string ext) -> string`

형식 표 (스펙 §5.1 그대로, 코드 상수로):
```
Images      = jpg png webp heic tiff avif bmp svg      → ImageTargets = jpg png webp heic tiff avif bmp pdf
Gif         = gif                                       → Video 중 쓰기 가능한 것
Audio       = mp3 m4a wav flac ogg opus aiff wma        → Audio
Video       = mp4 mov mkv webm avi wmv                  → Video + gif mp3
Pdf         = pdf                                       → jpg png docx txt
Documents   = docx doc rtf odt txt md html              → docx odt txt md html pdf
Slides      = ppt pptx odp                              → pdf jpg png (+ pptx: ppt만)
Spreadsheets= xlsx xls ods csv tsv                      → xlsx ods csv tsv pdf
InputOnly   = svg webm doc rtf xls ppt odp
Office 필요: Pdf→docx, Documents 전부(md↔html 제외), Slides 전부, Spreadsheets 전부
Ffmpeg 필요: Gif, Audio, Video 전부
```

- [ ] **Step 1: 실패하는 시험 작성**
```csharp
[Theory]
[InlineData(@"C:\a\photo.JPEG", "jpg")] [InlineData(@"C:\a\x.tif", "tiff")] [InlineData(@"C:\a\x.Heif", "heic")]
[InlineData(@"C:\a\노트.markdown", "md")] [InlineData(@"C:\a\noext", null)]
public void FormatOf_NormalizesAliases(string path, string? expected) => Assert.Equal(expected, FormatCatalog.FormatOf(path));

[Fact] public void TargetsFor_Png_IsImageTargetsWithoutPng() =>
    Assert.Equal(new[] { "jpg", "webp", "heic", "tiff", "avif", "bmp", "pdf" }, FormatCatalog.TargetsFor("png", Capabilities.All));
[Fact] public void TargetsFor_Svg_IsInputOnly_NotATarget() => Assert.DoesNotContain("svg", FormatCatalog.TargetsFor("png", Capabilities.All));
[Fact] public void TargetsFor_Webm_ExcludesWebm_IncludesGifMp3() { var t = FormatCatalog.TargetsFor("webm", Capabilities.All); Assert.DoesNotContain("webm", t); Assert.Contains("gif", t); Assert.Contains("mp3", t); }
[Fact] public void TargetsFor_Pdf_WithoutOffice_DropsDocx() => Assert.Equal(new[] { "jpg", "png", "txt" }, FormatCatalog.TargetsFor("pdf", Capabilities.All with { Office = false }));
[Fact] public void TargetsFor_Png_WithoutHeicWrite_DropsHeic() => Assert.DoesNotContain("heic", FormatCatalog.TargetsFor("png", Capabilities.All with { HeicWrite = false }));
[Fact] public void TargetsFor_Ppt_IncludesPptx_ButPptxDoesNot() { Assert.Contains("pptx", FormatCatalog.TargetsFor("ppt", Capabilities.All)); Assert.DoesNotContain("pptx", FormatCatalog.TargetsFor("pptx", Capabilities.All)); }
[Fact] public void TargetsFor_Md_WithoutOffice_KeepsHtmlOnly() => Assert.Equal(new[] { "html" }, FormatCatalog.TargetsFor("md", Capabilities.All with { Office = false }));
```
```csharp
[Fact] public void UniquePath_AppendsNumberFrom2() { var d = TempDir(); File.WriteAllText(Path.Combine(d, "a.jpg"), ""); Assert.Equal(Path.Combine(d, "a 2.jpg"), OutputNaming.UniquePath(d, "a", "jpg")); }
[Fact] public void ToolOutput_TagThenNumber() { var d = TempDir(); var src = Touch(d, "사진.jpg"); Touch(d, "사진 (compressed).jpg"); Assert.Equal(Path.Combine(d, "사진 (compressed) 2.jpg"), OutputNaming.ToolOutput(src, "compressed", "jpg")); }
[Theory] [InlineData(3, 9, "a 3.png")] [InlineData(3, 12, "a 03.png")] [InlineData(7, 100, "a 007.png")]
public void PageFileName_PadsToCountDigits(int i, int n, string expected) => Assert.Equal(expected, OutputNaming.PageFileName("a", i, n, "png"));
[Fact] public void CreatePagesFolder_IsUnique() { var d = TempDir(); var src = Touch(d, "doc.pdf"); Directory.CreateDirectory(Path.Combine(d, "doc (pages)")); Assert.Equal(Path.Combine(d, "doc (pages) 2"), OutputNaming.CreatePagesFolder(src)); }
```
(`TempDir()`/`Touch()`는 시험 프로젝트의 `TestFiles` 도우미로 둔다.)

- [ ] **Step 2:** `dotnet test tests/LimeWin.Core.Tests` → 컴파일 실패(형식 없음) 확인.
- [ ] **Step 3:** `FormatCatalog`, `OutputNaming`, `Capabilities`, `LimeException` 구현. 대상 순서는 위 표의 순서를 유지한다.
- [ ] **Step 4:** `dotnet test tests/LimeWin.Core.Tests` → 전부 PASS.
- [ ] **Step 5:** 커밋 `feat(core): format catalog and output naming`.

### Task 2: 꽃잎 목록 (WheelCatalog)

**Files:**
- Create: `src/LimeWin.Core/Catalog/Operations.cs`, `src/LimeWin.Core/Catalog/WheelCatalog.cs`
- Test: `tests/LimeWin.Core.Tests/WheelCatalogTests.cs`

**Interfaces:**
- Consumes: `FormatCatalog`, `Capabilities` (Task 1)
- Produces:
  - `public enum WheelMode { Convert, Tools, Actions }`
  - `public enum LimeTool { Compress, Crop, ImagesToPdf, MergePdfs, SplitPdf, CopyText, RemoveBackground }`
  - `public enum DropAction { Shelf, Copy, Move, CopyPath, Terminal }`
  - `public abstract record Operation;` `public sealed record ConvertOp(string Target) : Operation;` `public sealed record ToolOp(LimeTool Tool) : Operation;` `public sealed record ActionOp(DropAction Action) : Operation;`
  - `public sealed record WheelItem(Operation Operation, string Label);` — 라벨: 변환은 대문자 형식(`JPG`), 도구 `압축 / 자르기 / PDF 만들기 / 합치기 / 나누기 / 글자 복사 / 배경 제거`, 액션 `선반 / 복사 / 이동 / 경로 복사 / 터미널`
  - `WheelCatalog.Items(IReadOnlyList<string> paths, WheelMode mode, Capabilities caps) -> IReadOnlyList<WheelItem>`

규칙 (원작 `WheelCatalog.items` + 스펙 §5.2):
- Convert: 모든 파일의 `TargetsFor` 교집합(첫 파일 순서 유지). 알 수 없는 형식·폴더가 하나라도 있으면 빈 목록.
- Tools: 래스터 이미지(jpg png webp heic tiff avif bmp)만 → `Compress, Crop, ImagesToPdf, CopyText?, RemoveBackground?`; PDF 1개 → `SplitPdf, CopyText?`; PDF 2개 이상 → `MergePdfs, SplitPdf, CopyText?`; 그 외 섞임 → 빈 목록. `?`는 해당 능력(`Ocr`, `BackgroundRemoval`)이 있을 때만. Crop은 이미지 1개일 때만(여러 개면 제외 — `docs/decisions.md`에 기록).
- Actions: 항상 5개, 위 enum 순서. 폴더도 허용.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public void Convert_PngAndJpg_SharesCommonTargets() => Assert.Equal(new[] { "WEBP", "HEIC", "TIFF", "AVIF", "BMP", "PDF" }, Labels(Items(["a.png", "b.jpg"], WheelMode.Convert)));
[Fact] public void Convert_PngAndPdf_HasJpgPngIntersectionOnly() => Assert.Equal(new[] { "JPG" }, Labels(Items(["a.png", "b.pdf"], WheelMode.Convert)));
[Fact] public void Convert_UnknownExtension_Empty() => Assert.Empty(Items(["a.png", "b.xyz"], WheelMode.Convert));
[Fact] public void Tools_OneImage_HasCrop() => Assert.Equal(new[] { "압축", "자르기", "PDF 만들기", "글자 복사", "배경 제거" }, Labels(Items(["a.png"], WheelMode.Tools)));
[Fact] public void Tools_TwoImages_NoCrop() => Assert.DoesNotContain("자르기", Labels(Items(["a.png", "b.png"], WheelMode.Tools)));
[Fact] public void Tools_TwoPdfs_MergeFirst() => Assert.Equal(new[] { "합치기", "나누기", "글자 복사" }, Labels(Items(["a.pdf", "b.pdf"], WheelMode.Tools)));
[Fact] public void Tools_ImageAndPdf_Empty() => Assert.Empty(Items(["a.png", "b.pdf"], WheelMode.Tools));
[Fact] public void Tools_WithoutOcr_HidesCopyText() => Assert.DoesNotContain("글자 복사", Labels(WheelCatalog.Items(["a.png"], WheelMode.Tools, Capabilities.All with { Ocr = false })));
[Fact] public void Actions_AlwaysFive() => Assert.Equal(new[] { "선반", "복사", "이동", "경로 복사", "터미널" }, Labels(Items(["a.xyz"], WheelMode.Actions)));
```
- [ ] **Step 2:** `dotnet test --filter WheelCatalogTests` → FAIL.
- [ ] **Step 3:** 구현.
- [ ] **Step 4:** PASS 확인.
- [ ] **Step 5:** 커밋 `feat(core): wheel catalog`.

### Task 3: 드래그-탭 감지기 + 휠 기하

**Files:**
- Create: `src/LimeWin.Core/Input/DragTapDetector.cs`, `src/LimeWin.Core/Wheel/WheelGeometry.cs`
- Test: `tests/LimeWin.Core.Tests/DragTapDetectorTests.cs`, `tests/LimeWin.Core.Tests/WheelGeometryTests.cs`

**Interfaces:**
- Produces:
  - `[Flags] public enum Modifiers { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }`
  - `public readonly record struct InputSample(bool LeftButton, int X, int Y, Modifiers Mods);`
  - `public sealed class DragTapDetector(int dragThresholdPx) { public WheelMode? Feed(InputSample s); public bool Dragging { get; } public static WheelMode? ModeFor(Modifiers m); }`
  - `public sealed class WheelGeometry { public const double CanvasSide = 320, Inner = 54, Outer = 128, Gap = 5, Hole = 46; public double BearingOf(int index, int count); public int? PetalAt(double dx, double dy, int count); }` — `dx, dy`는 휠 중심 기준 화면 좌표(y 아래로 +). 0번 꽃잎 12시, 시계 방향. 반지름 `< Inner` 또는 `> Outer`면 null. 꽃잎 사이 간격 위도 null. 원작 `Sources/Wheel/WheelGeometry.swift`.

감지 규칙 (원작 `DragGestureMonitor.poll` 이식): 버튼 누른 첫 샘플의 위치 기록 → 그 위치에서 `dragThresholdPx` **초과** 이동하면 `Dragging = true` → `Dragging` 중 `Mods`가 이전 샘플과 다르고, 비어 있지 않고, `ModeFor(Mods)`가 값이 있으면 그 모드 반환. 버튼 떼면 전부 초기화. `ModeFor`: 정확히 `Shift→Convert`, `Alt→Tools`, `Ctrl→Actions`, 그 밖(조합 포함)은 null. 드래그 시작 전부터 누르고 있던 키는 전이가 아니므로 발동하지 않는다.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public void TapDuringDrag_FiresOnce() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.None)); d.Feed(new(true, 10, 0, Modifiers.None)); Assert.Equal(WheelMode.Convert, d.Feed(new(true, 11, 0, Modifiers.Shift))); Assert.Null(d.Feed(new(true, 12, 0, Modifiers.Shift))); }
[Fact] public void TapBeforeThreshold_Ignored() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.None)); Assert.Null(d.Feed(new(true, 2, 0, Modifiers.Shift))); }
[Fact] public void KeyHeldFromStart_DoesNotFire() { var d = new DragTapDetector(4); d.Feed(new(true, 0, 0, Modifiers.Ctrl)); Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Ctrl))); }
[Fact] public void ReleaseAndRetap_SwitchesWheel() { var d = Dragging(); d.Feed(new(true, 20, 0, Modifiers.Shift)); d.Feed(new(true, 20, 0, Modifiers.None)); Assert.Equal(WheelMode.Tools, d.Feed(new(true, 20, 0, Modifiers.Alt))); }
[Fact] public void Combination_Ignored() { var d = Dragging(); Assert.Null(d.Feed(new(true, 20, 0, Modifiers.Shift | Modifiers.Ctrl))); }
[Fact] public void NoButton_NeverFires() { var d = new DragTapDetector(4); Assert.Null(d.Feed(new(false, 50, 0, Modifiers.Shift))); }
[Fact] public void ButtonUp_Resets() { var d = Dragging(); d.Feed(new(false, 20, 0, Modifiers.None)); Assert.False(d.Dragging); }
```
```csharp
[Theory] [InlineData(0, -90, 4, 0)] [InlineData(90, 0, 4, 1)] [InlineData(0, 90, 4, 2)] [InlineData(-90, 0, 4, 3)]
public void PetalAt_CardinalPoints(double dx, double dy, int count, int expected) => Assert.Equal(expected, new WheelGeometry().PetalAt(dx, dy, count));
[Fact] public void PetalAt_Hole_IsNull() => Assert.Null(new WheelGeometry().PetalAt(0, -30, 4));
[Fact] public void PetalAt_Outside_IsNull() => Assert.Null(new WheelGeometry().PetalAt(0, -140, 4));
[Fact] public void PetalAt_Gutter_IsNull() => Assert.Null(new WheelGeometry().PetalAt(Math.Sin(Math.PI / 4) * 90, -Math.Cos(Math.PI / 4) * 90, 4)); // 0번과 1번 경계
[Fact] public void PetalAt_SinglePetal_WholeRing() => Assert.Equal(0, new WheelGeometry().PetalAt(0, 90, 1));
```
- [ ] **Step 2:** FAIL 확인. **Step 3:** 구현. **Step 4:** PASS 확인.
- [ ] **Step 5:** 커밋 `feat(core): drag tap detector and wheel geometry`.

### Task 4: 작업 실행기 + 이미지 변환

**Files:**
- Create: `src/LimeWin.Core/Jobs/OperationContext.cs`, `src/LimeWin.Core/Convert/IConverter.cs`, `src/LimeWin.Core/Convert/ConversionRouter.cs`, `src/LimeWin.Core/Convert/ImageConverter.cs`, `src/LimeWin.Core/Convert/CapabilityProbe.cs`
- Test: `tests/LimeWin.Core.Tests/ImageConverterTests.cs`, `tests/LimeWin.Core.Tests/ConversionRouterTests.cs`, `tests/LimeWin.Core.Tests/Fixtures/FixtureFactory.cs`
- Modify: `NOTICE.md` (Magick.NET Apache-2.0, PDFsharp MIT)

**Interfaces:**
- Consumes: `OutputNaming`, `FormatCatalog`, `LimeException`
- Produces:
  - `public sealed class OperationContext(CancellationToken token, IProgress<double>? progress = null) { public CancellationToken Token { get; } public void Report(double fraction); }`
  - `public interface IConverter { bool Handles(string from, string to); Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx); }` — 반환은 생성된 결과 경로(폴더 결과면 폴더 하나).
  - `public sealed class ConversionRouter(IEnumerable<IConverter> converters) { public Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx); }` — 맞는 변환기가 없으면 `LimeException("이 변환은 지원하지 않습니다.")`. 원본 폴더 쓰기 불가 시(변환기 호출 전 임시 파일 생성 시도로 확인) `LimeException("이 폴더에는 저장할 수 없습니다.")`. 변환기가 예외를 던지면 그 사이 생긴 결과 파일·폴더를 지우고 다시 던진다(`OperationCanceledException` 포함).
  - `public sealed class ImageConverter : IConverter` — from ∈ Images, to ∈ ImageTargets.
  - `public static class CapabilityProbe { public static bool CanWrite(string imageFormat); }` — Magick.NET으로 1×1 이미지를 해당 형식으로 메모리에 써 보고 성공 여부.
  - `FixtureFactory.Image(string dir, string name, int w = 64, int h = 48) -> string` (Magick으로 확장자에 맞게 생성; svg는 텍스트로 작성)

변환 규칙: 손실 형식(jpg webp heic avif) 품질 80, PNG 압축 레벨 6, EXIF 회전 반영(`AutoOrient`), 메타데이터 유지, 투명 → jpg/bmp는 흰 배경으로 합성. svg는 밀도 192 DPI로 래스터화. **이미지 → PDF**: PNG로 디코드(Magick) 후 PDFsharp로 한 쪽, 쪽 크기 = 픽셀 크기(72 DPI 기준 pt), 이미지 꽉 채움.

- [ ] **Step 1: 실패하는 시험**
```csharp
public static TheoryData<string, string> Routes => /* Images × ImageTargets 중 자기 자신·svg 대상 제외, CapabilityProbe가 거짓인 대상 제외 */;
[Theory, MemberData(nameof(Routes))]
public async Task Converts_AndOutputDecodes(string from, string to) {
    var src = FixtureFactory.Image(Dir, $"그림 (1).{from}");
    var before = Sha256(src);
    var outs = await Router.ConvertAsync(src, to, Ctx);
    Assert.Equal(Path.Combine(Dir, $"그림 (1).{to}"), Assert.Single(outs));
    if (to == "pdf") Assert.Equal(1, PdfReader.Open(outs[0], PdfDocumentOpenMode.Import).PageCount);
    else { using var m = new MagickImage(outs[0]); Assert.Equal((64u, 48u), (m.Width, m.Height)); }
    Assert.Equal(before, Sha256(src));
}
[Fact] public async Task Png_WithAlpha_ToJpg_HasWhiteBackground() { /* 투명 PNG → jpg 좌상단 픽셀이 흰색(±3) */ }
[Fact] public async Task CorruptJpg_ThrowsLimeException_AndLeavesNoFile() { var src = Path.Combine(Dir, "깨짐.jpg"); File.WriteAllBytes(src, [1, 2, 3]); await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "png", Ctx)); Assert.False(File.Exists(Path.Combine(Dir, "깨짐.png"))); }
[Fact] public async Task MislabeledPngAsJpg_StillConverts() { /* PNG 내용을 .jpg로 저장 → webp 변환 성공 */ }
[Fact] public async Task ConversionRouter_ReadOnlyFolder_ThrowsLimeException_AndLeavesNoFile() { /* 폴더 ACL에 쓰기 거부(Everyone Deny WriteData) 설정 후 png→jpg; finally에서 ACL 복구 */ }
[Fact] public async Task ConversionRouter_NoConverter_ThrowsLimeException() { /* new ConversionRouter([]) */ }
```
- [ ] **Step 2:** FAIL 확인.
- [ ] **Step 3:** 구현. HEIC/AVIF 쓰기 불가로 판명되면 `docs/decisions.md`에 기록.
- [ ] **Step 4:** `dotnet test --filter "ImageConverterTests|ConversionRouterTests"` → PASS.
- [ ] **Step 5:** 커밋 `feat(core): job context, router, image conversion`.

### Task 5: PDF → 이미지 / 텍스트

**Files:**
- Create: `src/LimeWin.Core/Convert/PdfConverter.cs`, `src/LimeWin.Core/Pdf/PdfRenderer.cs`, `src/LimeWin.Core/Pdf/PdfText.cs`
- Test: `tests/LimeWin.Core.Tests/PdfConverterTests.cs`; `FixtureFactory.Pdf(string dir, string name, int pages, bool withText = true, string? userPassword = null) -> string` 추가 (PDFsharp로 각 쪽에 "Page N 페이지" 텍스트)
- Modify: `NOTICE.md` (PdfPig Apache-2.0, PDFtoImage MIT, PDFium BSD-3-Clause)

**Interfaces:**
- Consumes: `IConverter`, `OperationContext`, `OutputNaming`
- Produces:
  - `public static class PdfRenderer { public static Task<IReadOnlyList<string>> RenderPagesAsync(string pdf, string outDirOrFile, string ext, int dpi, int maxEdgePx, OperationContext ctx); public static Task<int> PageCountAsync(string pdf); public static Task<byte[]> RenderPagePngAsync(string pdf, int index0, int dpi, int maxEdgePx, CancellationToken ct); }` — PDFtoImage(PDFium) 사용. PDFium은 스레드 안전하지 않으므로 모든 호출을 정적 `SemaphoreSlim(1)`로 직렬화. 암호 PDF는 `LimeException("암호로 보호된 PDF는 열 수 없습니다.")`.
  - `public static class PdfText { public static IReadOnlyList<string> PageTexts(string pdf); }` — PdfPig, 쪽마다 텍스트(단어 사이 공백, 줄 순서 유지).
  - `public sealed class PdfConverter : IConverter` — pdf→jpg/png/txt. (pdf→docx는 Task 8)

규칙: 200 DPI, 긴 변 최대 4096 px, jpg 품질 80(`docs/decisions.md`에 기록 — 원작은 경로마다 150/200 DPI). 여러 쪽은 `CreatePagesFolder` + `PageFileName`, 1쪽은 `ConvertOutput`. 쪽마다 `ctx.Report((i+1)/(double)n)`, 쪽마다 취소 확인. txt는 쪽 사이 빈 줄, UTF-8(BOM 없음). 텍스트 층이 없는 PDF의 txt는 빈 문자열 대신 `LimeException("이 PDF에는 글자 정보가 없습니다. 도구 휠의 '글자 복사'를 쓰세요.")`.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public async Task ThreePagePdf_ToPng_MakesPagesFolder() { var src = FixtureFactory.Pdf(Dir, "보고서.pdf", 3); var outs = await Router.ConvertAsync(src, "png", Ctx); var folder = Assert.Single(outs); Assert.Equal(Path.Combine(Dir, "보고서 (pages)"), folder); Assert.Equal(new[] { "보고서 1.png", "보고서 2.png", "보고서 3.png" }, Directory.GetFiles(folder).Select(Path.GetFileName).Order()); }
[Fact] public async Task OnePagePdf_ToJpg_SingleFile_At200Dpi() { var src = FixtureFactory.Pdf(Dir, "a.pdf", 1); var o = Assert.Single(await Router.ConvertAsync(src, "jpg", Ctx)); Assert.Equal(Path.Combine(Dir, "a.jpg"), o); using var m = new MagickImage(o); Assert.InRange((int)m.Width, 1650, 1660); /* A4 8.27in × 200 */ }
[Fact] public async Task Pdf_ToTxt_HasPageTextInOrder() { /* "Page 1 페이지" 가 "Page 2 페이지" 앞 */ }
[Fact] public async Task EncryptedPdf_Throws() { var src = FixtureFactory.Pdf(Dir, "e.pdf", 1, userPassword: "pw"); await Assert.ThrowsAsync<LimeException>(() => Router.ConvertAsync(src, "png", Ctx)); }
[Fact] public async Task CorruptPdf_Throws() { /* 0바이트 .pdf → LimeException */ }
[Fact] public async Task PdfToPng_Cancelled_RemovesPagesFolder() { /* 30쪽, 2쪽째 Report에서 cts.Cancel() → OperationCanceledException, Dir 안에 "(pages)" 폴더 없음 */ }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS.
- [ ] **Step 5:** 커밋 `feat(core): pdf to image and text`.

### Task 6: 앱 셸 — 트레이, 훅, 오버레이, 휠, 진행 카드 (변환 휠 연결)

이 작업이 끝나면 **필수 범위가 실제로 동작**한다. `docs/spike-notes.md`의 결론을 따른다.

**Files:**
- Create: `src/LimeWin.App/LimeWin.App.csproj` (`<UseWPF>true</UseWPF>`, `<OutputType>WinExe</OutputType>`, 앱 매니페스트에 `PerMonitorV2` DPI 인식, WPF-UI·H.NotifyIcon.Wpf 참조, `Assets/Fonts/Pretendard-*.otf` 리소스 포함), `App.xaml(.cs)` (WPF-UI 테마 사전, 시스템 라이트/다크 따라감)
- Create: `src/LimeWin.App/Wheel/BackdropCapture.cs` (유리 효과용 화면 캡처), `src/LimeWin.App/Wheel/WheelStyle.cs` (색·반지름·모션 토큰 한 곳), `src/LimeWin.App/Render/RenderHarness.cs` (`--render`)
- Create: `src/LimeWin.App/Input/LowLevelHooks.cs` (P/Invoke, 관찰만), `src/LimeWin.App/Input/DragSession.cs`
- Create: `src/LimeWin.App/Wheel/OverlayWindow.xaml(.cs)`, `src/LimeWin.App/Wheel/WheelView.xaml(.cs)`, `src/LimeWin.App/Wheel/WheelController.cs`
- Create: `src/LimeWin.App/Jobs/JobRunner.cs`, `src/LimeWin.App/Jobs/ProgressCard.xaml(.cs)`, `src/LimeWin.App/Jobs/ProgressCardHost.cs`
- Create: `src/LimeWin.App/Tray/TrayIcon.cs`, `src/LimeWin.App/Tray/LoginItem.cs`, `src/LimeWin.App/Tray/LicenseWindow.xaml(.cs)`
- Create: `src/LimeWin.App/Services.cs` (Capabilities 계산, ConversionRouter 구성)
- Create: `docs/manual-checklist.md`
- Modify: `NOTICE.md` (WPF-UI MIT, H.NotifyIcon MIT, Pretendard SIL OFL 1.1)

**Interfaces:**
- Consumes: `DragTapDetector`, `WheelGeometry`, `WheelCatalog`, `ConversionRouter`, `OperationContext`, `Capabilities`, `CapabilityProbe`
- Produces:
  - `WheelController.Show(WheelMode mode, Point screenPx)` / `SwitchMode(WheelMode)` / `Close()`; 이벤트 `ItemDropped(WheelItem item, IReadOnlyList<string> files, Point screenPx)`.
  - `JobRunner.Run(string title, Func<OperationContext, Task<IReadOnlyList<string>>> work)` — 진행 카드 생성, 백그라운드 실행, 완료 시 카드 클릭으로 `explorer.exe /select,"<첫 결과>"`, 실패 시 `LimeException.Message`(그 외 예외는 "알 수 없는 오류: <메시지>"), 4초 뒤 자동 닫힘(실패 카드는 클릭으로 닫음). 이후 작업(Task 13·14·15·16)이 이 메서드로 작업을 띄운다.
  - `Services.Capabilities` (시작 시 1회 계산: Office = 세 ProgID 모두 등록, Ffmpeg = PATH에 `ffmpeg.exe`, HeicWrite/AvifWrite = `CapabilityProbe`, Ocr = Task 15 전까지 false, BackgroundRemoval = Task 16 전까지 false).

동작:
- 훅 콜백은 `InputSample`만 만들어 `Dispatcher.BeginInvoke`로 넘기고 즉시 `CallNextHookEx` 반환.
- `DragTapDetector(GetSystemMetrics(SM_CXDRAG))`가 모드를 반환하면: 덮개(가상 화면 전체, 배경 `#01000000`, `Topmost`, `ShowActivated=False`, `AllowDrop`)를 띄우고 커서 위치에 휠 자리를 잡는다. **휠은 `DragEnter`에서 `FileDrop` 목록을 받은 뒤에만 그린다.** 목록이 없으면 투명한 채로 두었다가 드래그 끝에 닫는다.
- 휠 위 `DragOver`에서 `PetalAt`으로 강조 꽃잎 갱신, `DragDropEffects.Copy` 반환(꽃잎 위) / `None`(그 외). `Drop`: 꽃잎이면 `ItemDropped`, 아니면 아무것도 안 함. 어느 경우든 덮개 닫기.
- 닫기 안전장치: 버튼 뗌 훅, `Esc` 훅, 그리고 100 ms 타이머로 `GetAsyncKeyState(VK_LBUTTON)`이 떼어져 있으면 닫기.
- 공통 꽃잎이 없으면 휠 가운데에 "할 수 있는 작업 없음"만 표시.
- **창 구조:** 덮개(전체 화면, 정적, 드롭만 받음)와 휠(320×320 작은 창, 그림·애니메이션)은 **별도 창**이다. 큰 투명 창을 매 프레임 다시 그리지 않기 위해서다. 휠 창은 `IsHitTestVisible=False`로 두고 드롭·좌표 판정은 덮개가 한다.
- **유리 효과 (원작 Liquid/Frosted 재현):** 휠을 띄우기 직전, 휠 자리 320×320 화면 영역을 `BitBlt`(**`CAPTUREBLT` 없이** — 탐색기의 끌기 이미지 같은 레이어드 창이 찍히지 않게)로 캡처해 `BlurEffect`(반경 24, `KernelType=Gaussian`, `RenderingBias=Quality`)를 건 이미지를 휠 판 모양으로 잘라 깔고, 그 위에 색 막(라이트 `#B3F2F2F2` / 다크 `#B31C1C1C`)과 0.5 px 안쪽 하이라이트 선(`#40FFFFFF`)을 얹는다. 꽃잎은 막보다 한 단계 밝게(라이트 `#CCFFFFFF` / 다크 `#CC3A3A3A`). 다크·라이트는 `HKCU\...\Themes\Personalize\AppsUseLightTheme`. 모든 값은 `WheelStyle.cs` 한 곳에.
- **강조:** 꽃잎 색 라임 `#8BC34A`(원작 스크린샷 기준; 다크에서는 `#6FA83A`), 글자 흰색, 바깥쪽으로 5 px 떠오름(원작 `liftDistance`). 가운데 구멍에 강조된 꽃잎 이름(원작과 같음). 크기 변화 없음.
- **모션 (`emil-design-eng` 기준 — 키로 하루 수십 번 띄우는 요소):**

  | 장면 | 값 | 이유 |
  |---|---|---|
  | 휠 등장 | **애니메이션 없음, 즉시 표시** | 키보드로 띄우는 동작은 애니메이션하지 않는다(Raycast 원칙). 지연은 곧 둔함 |
  | 꽃잎 등장 순차(stagger) | 없음 | 자주 보는 요소에 장식 금지 |
  | 꽃잎 강조 | 색: 100 ms `ease`, 5 px 떠오름: 140 ms `cubic-bezier(0.23, 1, 0.32, 1)`, 되돌아감도 같은 값(중간에 끊겨도 현재 위치에서 이어지도록 `From` 없이 시작) | 드롭 대상 표시라는 목적이 있음 |
  | 휠 바꾸기(다른 키 탭) | 즉시 교체, 크로스페이드 없음 | 키보드 동작 |
  | 꽃잎에 드롭 | 즉시 닫힘, 진행 카드가 피드백을 맡음 | 시스템 응답은 빠르게 |
  | 취소(구멍·바깥·Esc) | 불투명도 1→0, 100 ms `cubic-bezier(0.23, 1, 0.32, 1)` | 사라지는 게 "취소됨"으로 읽히게 |
  | 진행 카드 | 등장: 아래 8 px→0 + 불투명도, 200 ms 같은 곡선 / 퇴장 150 ms | 가끔 보는 요소의 표준 값 |
  | 모션 줄이기 | `SystemParameters.ClientAreaAnimation == false`면 떠오름·이동을 빼고 색·불투명도만 | 접근성 |

  `scale(0)`에서 시작 금지, ease-in 곡선 금지, 300 ms 초과 금지.
- **글꼴·아이콘:** 형식 라벨 Segoe UI Variable Display Semibold 15 px 대문자, 한글 라벨 Pretendard SemiBold 12 px, 도구·액션 아이콘 Segoe Fluent Icons 20 px(꽃잎 라벨 위). 글자 렌더링 `TextOptions.TextFormattingMode=Display`, `TextRenderingMode=ClearType` 대신 `Grayscale`(투명 창에서 ClearType 번짐 방지).
- **진행 카드·트레이 메뉴·도구 창**은 WPF-UI 컨트롤과 Mica 배경, 둥근 모서리 8 px, 시스템 강조색을 쓴다.
- 이 작업에선 `ConvertOp`만 연결: 파일마다 `JobRunner.Run($"{파일명} → {대상}", ctx => router.ConvertAsync(...))`. `ToolOp`/`ActionOp`은 "준비 중" 카드.
- 트레이(H.NotifyIcon) 메뉴: `휠 켜기`(체크, 끄면 훅 해제), `윈도우 시작 시 실행`(체크, `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LimeWin`), `라이선스 보기`(`NOTICE.md` 내용), `종료`. 중복 실행 방지(이름 있는 `Mutex`). 트레이 아이콘은 라임 조각 모양 단색 아이콘(라이트/다크 작업표시줄용 2종, 16·20·24·32 px).
- `--render <폴더>`: 실제 창 대신 같은 `WheelView`를 `RenderTargetBitmap`(2× 배율)으로 그려 `wheel-{light|dark}-{n}petals-{hl|plain}.png`, `progress-card-{running|done|failed}.png`로 저장. 유리 배경 자리에는 원작 스크린샷처럼 문서 목록·초록 그라데이션 고정 배경을 깐다.

- [ ] **Step 1:** 구현.
- [ ] **Step 2:** `dotnet build LimeWin.slnx -c Release` 경고 0 확인, `dotnet test` 전부 PASS.
- [ ] **Step 2a (시각 확인):** `src/LimeWin.App/bin/Release/.../LimeWin.App.exe --render artifacts/render`를 실행하고 렌더 PNG를 원작 `Screenshots/wheel-liquid.png`, `wheel-solid-dark.png`와 나란히 놓고 본다(이미지를 직접 읽어 비교). 꽃잎 간격·둥근 모서리·라벨 정렬·강조 위치가 원작과 어긋나면 `WheelStyle.cs`를 고쳐 다시 렌더. 그다음 **사용자에게 렌더 이미지를 보여 주고 "예쁘다"는 확인을 받는다.** 확인 전에는 다음 작업으로 넘어가지 않는다. (`artifacts/`는 `.gitignore`)
- [ ] **Step 3:** `docs/manual-checklist.md` 작성 — 스파이크 질문 1~6 + 다음 항목: PNG 끌고 Shift → JPG 꽃잎 → 결과 생김, 이미지 3장 → PDF, 3쪽 PDF → PNG 폴더, 바깥 드롭 취소 후 원본 위치 그대로, 휠 없이 평소 끌기 이동·복사 정상, 앱 강제 종료(작업 관리자) 후 평소 끌기 정상, 드래그 아닐 때 Shift/Ctrl/Alt 타이핑 정상, 휠 켜기 해제 시 휠 안 뜸.
- [ ] **Step 4:** 앱을 실행해 체크리스트 Phase 1 항목을 **사용자와 함께** 확인하고 결과를 체크리스트에 기록.
- [ ] **Step 5:** 커밋 `feat(app): tray, hooks, overlay wheel, convert wheel`.

---

## Phase 2 — 나머지 변환

### Task 7: ffmpeg 변환 (오디오·비디오·GIF)

**Files:**
- Create: `src/LimeWin.Core/Convert/FfmpegConverter.cs`, `src/LimeWin.Core/Convert/FfmpegArgs.cs`
- Test: `tests/LimeWin.Core.Tests/FfmpegConverterTests.cs` (`[Trait("Category", "Ffmpeg")]`); `FixtureFactory.Media(dir, name, seconds = 2)` 추가 (`ffmpeg -f lavfi -i testsrc=size=320x240:rate=25 -f lavfi -i sine=frequency=440` 로 생성)

**Interfaces:**
- Produces:
  - `public static class FfmpegArgs { public static IReadOnlyList<string> For(string source, string sourceFormat, string target, string output); }` — 순수 함수, 인수 목록(따옴표 없이 원소별; `ProcessStartInfo.ArgumentList`로 전달).
  - `public sealed class FfmpegConverter(string ffmpegPath) : IConverter`

인수 표 (공통 앞부분 `-hide_banner -nostdin -y -i <src>`, 메타데이터 유지 `-map_metadata 0`):
```
mp3  : -vn -c:a libmp3lame -b:a 256k        m4a : -vn -c:a aac -b:a 256k
wav  : -vn -c:a pcm_s16le                   aiff: -vn -c:a pcm_s16be
flac : -vn -c:a flac -compression_level 6   ogg : -vn -c:a libvorbis -q:a 6 (libvorbis 없으면 decisions에 기록 후 ogg 대상 제외)
opus : -vn -c:a libopus -b:a 192k           wma : -vn -c:a wmav2 -b:a 192k
mp4  : -c:v libx264 -crf 20 -preset medium -pix_fmt yuv420p -c:a aac -b:a 256k -movflags +faststart
mov  : -c:v libx264 -crf 17 -preset medium -pix_fmt yuv420p -c:a aac -b:a 256k   (원작: mov는 높은 품질)
mkv  : -c:v libx264 -crf 20 -preset medium -c:a aac -b:a 256k
avi  : -c:v mpeg4 -q:v 3 -c:a libmp3lame -b:a 256k
wmv  : -c:v wmv2 -q:v 3 -c:a wmav2 -b:a 192k
gif  : -filter_complex "[0:v:0]fps=15,scale='min(960,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a" -an
```
진행률: `-progress pipe:1`의 `out_time_us`를 `ffprobe`로 얻은 길이로 나눠 `Report`. 취소 시 프로세스 트리 종료 후 결과 삭제. 종료 코드 ≠ 0이면 stderr 마지막 줄을 담아 `LimeException("변환 실패: …")`.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public void Args_Mp4_HasFaststartAndYuv420p() { var a = FfmpegArgs.For("in.mov", "mov", "mp4", "out.mp4"); Assert.Contains("+faststart", a); Assert.Contains("yuv420p", a); Assert.Equal("out.mp4", a[^1]); }
[Fact] public void Args_Gif_Uses15FpsAnd960Cap() => Assert.Contains(FfmpegArgs.For("a.mp4", "mp4", "gif", "a.gif"), x => x.Contains("fps=15") && x.Contains("min(960,iw)"));
[Fact] public void Args_KoreanPath_PassedVerbatim() => Assert.Equal(@"C:\자료\영상 (1).mp4", FfmpegArgs.For(@"C:\자료\영상 (1).mp4", "mp4", "mp3", "o.mp3")[FfmpegArgs.For(@"C:\자료\영상 (1).mp4", "mp4", "mp3", "o.mp3").ToList().IndexOf("-i") + 1]);
[Theory, Trait("Category", "Ffmpeg"), MemberData(nameof(AudioVideoRoutes))]
public async Task Converts_AndFfprobeReadsResult(string from, string to) { /* 결과를 ffprobe로 열어 스트림 존재 확인, 원본 해시 불변 */ }
[Fact, Trait("Category", "Ffmpeg")] public async Task Ffmpeg_Cancelled_KillsProcessAndRemovesOutput() { /* 30초 영상 → mkv, 진행 0.1 넘으면 취소 → 결과 없음, ffmpeg 프로세스 남지 않음 */ }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** `dotnet test --filter "FullyQualifiedName~Ffmpeg"` PASS.
- [ ] **Step 5:** `Services`에 `FfmpegConverter` 등록, 커밋 `feat(core): ffmpeg audio/video/gif conversion`.

### Task 8: Office 호스트 + Word 변환 (문서, PDF→DOCX, Markdown)

**Files:**
- Create: `src/LimeWin.Core/Office/OfficeHost.cs`, `src/LimeWin.Core/Office/OfficeProcesses.cs`, `src/LimeWin.Core/Office/WordConverter.cs`, `src/LimeWin.Core/Convert/MarkdownBridge.cs`
- Test: `tests/LimeWin.Core.Tests/WordConverterTests.cs` (`[Trait("Category", "Office")]`), `tests/LimeWin.Core.Tests/MarkdownBridgeTests.cs`
- Modify: `NOTICE.md` (Markdig BSD-2-Clause, ReverseMarkdown MIT)

**Interfaces:**
- Produces:
  - `public sealed class OfficeHost : IDisposable { public OfficeHost(string progId, string processName); public Task<T> RunAsync<T>(Func<dynamic, T> work, TimeSpan timeout, CancellationToken ct); }` — 전용 STA 스레드 하나, 작업 직렬 실행. 첫 작업 때 앱 확보: 해당 프로세스가 이미 있으면 `attachedToUser = true`. 앱 생성은 `Activator.CreateInstance(Type.GetTypeFromProgID(progId))`. 생성 전후 프로세스 ID 비교로 우리가 띄운 PID 기록. 생성 직후 `DisplayAlerts = 0`(Excel은 `false`), `AutomationSecurity = 3`, Word/Excel은 `Visible = false`, `ScreenUpdating = false`. 시간 초과 시 우리가 띄운 PID만 종료하고 `LimeException("Office 응답이 없어 중단했습니다.")`; `attachedToUser`면 종료하지 않는다. 각 작업 후 COM 개체 `Marshal.FinalReleaseComObject`. **유휴 30초 후** 정리: 우리가 띄웠고, 열린 문서 수 = 0일 때만 `Quit()`; 문서가 남아 있으면 연결만 끊는다(스펙 §7).
  - `public sealed class WordConverter(OfficeHost word) : IConverter` — from ∈ {docx doc rtf odt txt html, pdf(→docx만), md}, to ∈ {docx odt txt md html pdf}.
  - `public static class MarkdownBridge { public static string ToHtml(string markdown); public static string FromHtml(string html); }` — Markdig(고급 확장) / ReverseMarkdown(`GithubFlavored = true`, 알 수 없는 태그 통과).

Word 호출 (상수는 `OfficeHost.cs` 옆에 이름 붙여 정의):
- 열기: `Documents.Open(FileName: src, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, PasswordDocument: "\u0001lime", Visible: false, NoEncodingDialog: true)` — 틀린 암호를 일부러 넘겨 암호 문서는 대화상자 없이 COM 예외 → `LimeException("암호로 보호된 문서는 열 수 없습니다.")`.
- 매크로: 열린 뒤 `doc.HasVBProject`가 참이면 닫고 `LimeException("매크로가 들어 있는 문서는 변환하지 않습니다.")`.
- 저장 `SaveAs2(FileName, FileFormat, Encoding)`: docx=16, pdf=17, odt=23, html=10(필터링된 HTML), txt=7(`wdFormatUnicodeText`) 저장 후 UTF-16→UTF-8(BOM 없음)로 다시 쓰기.
- md 입력: `MarkdownBridge.ToHtml` → 임시 `.html` → Word로 열어 대상 저장(대상이 html이면 Word 없이 바로 씀). md 출력: Word로 필터링된 HTML 임시 저장 → `MarkdownBridge.FromHtml`. html→md와 md→html은 Word 없이 처리.
- 닫기: `doc.Close(SaveChanges: 0)`.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public void Markdown_RoundTrip_KeepsHeadingListBoldItalic() { var md = "# 제목\n\n- 하나\n- **둘**\n\n*기울임*"; var back = MarkdownBridge.FromHtml(MarkdownBridge.ToHtml(md)); Assert.Contains("# 제목", back); Assert.Contains("**둘**", back); Assert.Matches(@"[*_]기울임[*_]", back); }
[Theory, Trait("Category", "Office"), MemberData(nameof(WordRoutes))]
public async Task Converts_AndResultOpens(string from, string to) { /* 픽스처: Word로 "안녕 Hello" 문서를 각 형식으로 만들어 둠(FixtureFactory.Document). 결과: pdf는 PdfPig 텍스트에 "안녕", docx는 ZIP 안 word/document.xml에 "안녕", txt/md/html은 파일 텍스트에 "안녕". 원본 해시 불변. */ }
[Fact, Trait("Category", "Office")] public async Task Pdf_ToDocx_ContainsText() { }
[Fact, Trait("Category", "Office")] public async Task PasswordProtected_ThrowsWithoutPrompt() { /* Word로 암호 docx 생성 → LimeException, 30초 안에 끝남 */ }
[Fact, Trait("Category", "Office")] public async Task MacroDoc_Refused() { /* .docm을 .doc 바이너리로 저장한 픽스처(HasVBProject) → LimeException */ }
[Fact, Trait("Category", "Office")] public async Task CorruptDocx_Throws_AndNoWordLeftRunning() { /* 0바이트 docx → LimeException; host Dispose 후 우리가 띄운 WINWORD PID 없음 */ }
[Fact, Trait("Category", "Office")] public async Task UserOpenDocument_IsNeverClosed() { /* 시험이 먼저 Word를 보이게 띄워 문서 하나 연 상태에서 변환 → 끝난 뒤 그 문서가 여전히 열려 있음 */ }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** `dotnet test --filter "Category=Office|FullyQualifiedName~Markdown"` PASS, 작업 관리자에 남은 WINWORD 없음 확인.
- [ ] **Step 5:** `Services`에 등록, 커밋 `feat(core): office host and word conversion`.

### Task 9: PowerPoint · Excel 변환

**Files:**
- Create: `src/LimeWin.Core/Office/PowerPointConverter.cs`, `src/LimeWin.Core/Office/ExcelConverter.cs`
- Test: `tests/LimeWin.Core.Tests/PowerPointConverterTests.cs`, `tests/LimeWin.Core.Tests/ExcelConverterTests.cs` (둘 다 `Category=Office`)

**Interfaces:**
- Consumes: `OfficeHost` (Task 8), `OutputNaming`
- Produces: `public sealed class PowerPointConverter(OfficeHost ppt) : IConverter`, `public sealed class ExcelConverter(OfficeHost excel) : IConverter`

PowerPoint (스펙 §7 결정 사항):
- `Visible`을 건드리지 않는다. 열기: `Presentations.Open(src + "::\u0001lime::", ReadOnly: -1, Untitled: 0, WithWindow: 0)` — `파일::암호::` 문법으로 암호 파일을 대화상자 없이 실패시킨다. **Step 1 첫 시험으로 암호 없는 파일에 이 문법이 통하는지 확인**하고, 안 통하면 `Presentations.Open` 전에 OOXML 암호화 여부(CFB `EncryptionInfo` 스트림 존재)를 직접 검사하는 방식으로 바꾸고 `docs/decisions.md`에 기록.
- 매크로: `pres.HasVBProject` → 거부.
- pdf: `pres.SaveAs(out, 32)`. pptx(ppt만): `SaveAs(out, 24)`. jpg/png: 슬라이드마다 `slide.Export(path, "JPG"|"PNG", 1920, 1920 * 높이/폭)`, 여러 장이면 `(pages)` 폴더.
- 우리가 연 프레젠테이션만 `Close()`. 앱 종료는 `OfficeHost` 규칙(우리가 띄웠고 열린 프레젠테이션 0개).

Excel:
- csv/tsv 입력: `Workbooks.OpenText(src, Origin: 65001, DataType: 1, Tab: tsv, Comma: csv, Local: false)`. 그 외: `Workbooks.Open(src, UpdateLinks: 0, ReadOnly: true, Password: "\u0001lime", IgnoreReadOnlyRecommended: true, AddToMru: false)`.
- 매크로: `wb.HasVBProject` → 거부.
- 저장: xlsx=51, ods=60, csv=62(`xlCSVUTF8`, 첫 시트), tsv=42(`xlUnicodeText`, 첫 시트) 후 UTF-16→UTF-8 다시 쓰기, pdf=`wb.ExportAsFixedFormat(0, out)`.
- `wb.Close(SaveChanges: false)`.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact, Trait("Category", "Office")] public async Task PasswordSyntax_WorksOnPlainFile() { /* 암호 없는 pptx를 "path::\u0001lime::"로 열어 성공 */ }
[Theory, Trait("Category", "Office"), MemberData(nameof(SlideRoutes))] public async Task Slides_Convert(string from, string to) { /* 3장 픽스처 → pdf 3쪽 / png·jpg (pages) 폴더 3개, 각 1920px 폭 / ppt→pptx 열림 */ }
[Theory, Trait("Category", "Office"), MemberData(nameof(SheetRoutes))] public async Task Sheets_Convert(string from, string to) { /* 셀 A1="이름", B2=42 픽스처 → 결과를 다시 읽어 같은 값; csv/tsv는 UTF-8 BOM 없이 "이름" 포함 */ }
[Fact, Trait("Category", "Office")] public async Task PasswordProtected_ThrowsWithoutPrompt() { /* pptx, xlsx 각각 */ }
[Fact, Trait("Category", "Office")] public async Task UserPowerPointOpen_IsNotQuit() { /* 시험이 PowerPoint를 띄워 프레젠테이션 하나 열고 → 변환 → host Dispose → 그 프레젠테이션 여전히 열림, POWERPNT 살아 있음 */ }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS + 남은 Office 프로세스 없음 확인.
- [ ] **Step 5:** `Services`에 등록, 커밋 `feat(core): powerpoint and excel conversion`.

### Task 10: 변환 경로 매트릭스 점검

원작 `Tests/ConversionMatrixChecks.swift`에 해당. 형식 표의 **모든** (원본→대상) 쌍을 한 번씩 실제로 돌린다.

**Files:**
- Create: `tests/LimeWin.Core.Tests/ConversionMatrixTests.cs`
- Modify: `tests/LimeWin.Core.Tests/Fixtures/FixtureFactory.cs` — `For(string dir, string format) -> string` (형식별 픽스처를 `한글 파일 (1).<ext>` 이름으로 생성; Office 형식은 Office로 생성, 미디어는 ffmpeg로)

- [ ] **Step 1:** 시험 작성 — `MemberData`는 `FormatCatalog`의 모든 입력 형식 × `TargetsFor(_, Services와 같은 방식으로 계산한 Capabilities)`. 각 경우: 결과 존재, 크기 > 0, 원본 SHA-256 불변, 시험 후 남은 `WINWORD/POWERPNT/EXCEL/ffmpeg` 중 우리가 띄운 PID 없음. 경로 수를 출력(`ITestOutputHelper`).
- [ ] **Step 2:** `dotnet test --filter ConversionMatrixTests` 실행. 실패 경로가 있으면 해당 변환기를 고친다(새 경로 시험은 해당 Task 파일에 추가).
- [ ] **Step 3:** 전부 PASS + 경로 수를 `docs/decisions.md`에 기록.
- [ ] **Step 4:** 커밋 `test: full conversion route matrix`.

---

## Phase 3 — 도구 휠, 액션 휠

### Task 11: PDF 도구 (PDF 만들기, 합치기, 나누기)

**Files:**
- Create: `src/LimeWin.Core/Tools/PdfTools.cs`
- Test: `tests/LimeWin.Core.Tests/PdfToolsTests.cs`

**Interfaces:**
- Produces:
  - `PdfTools.ImagesToPdfAsync(IReadOnlyList<string> imagesInOrder, OperationContext ctx) -> Task<string>` — 결과 `ToolOutput(첫 이미지, "merged", "pdf")`. 각 이미지 한 쪽, Task 4의 이미지→PDF 규칙.
  - `PdfTools.MergeAsync(IReadOnlyList<string> pdfsInOrder, OperationContext ctx) -> Task<string>` — `ToolOutput(첫 PDF, "merged", "pdf")`.
  - `PdfTools.SplitAsync(string pdf, OperationContext ctx) -> Task<string>` — `CreatePagesFolder` + `PageFileName(stem, i, n, "pdf")`, 폴더 경로 반환.
  - 암호 PDF는 `LimeException`. 실패·취소 시 부분 결과 삭제.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public async Task ImagesToPdf_OrderPreserved() { /* 빨강·초록·파랑 이미지 순서 → 3쪽, 각 쪽 렌더 가운데 색 순서 일치 */ }
[Fact] public async Task Merge_PageCountIsSum_NamedMerged() { var a = FixtureFactory.Pdf(Dir, "가.pdf", 2); var b = FixtureFactory.Pdf(Dir, "나.pdf", 3); var o = await PdfTools.MergeAsync([a, b], Ctx); Assert.Equal(Path.Combine(Dir, "가 (merged).pdf"), o); Assert.Equal(5, PageCount(o)); }
[Fact] public async Task Split_TwelvePages_ZeroPadded() { var o = await PdfTools.SplitAsync(FixtureFactory.Pdf(Dir, "s.pdf", 12), Ctx); Assert.Equal("s 01.pdf", Path.GetFileName(Directory.GetFiles(o).Order().First())); Assert.Equal(12, Directory.GetFiles(o).Length); }
[Fact] public async Task Merge_EncryptedInput_Throws() { }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현(PDFsharp `PdfReader.Open(..., Import)`). **Step 4:** PASS.
- [ ] **Step 5:** 커밋 `feat(core): pdf create/merge/split tools`.

### Task 12: 이미지 도구 (압축, 자르기)

**Files:**
- Create: `src/LimeWin.Core/Tools/ImageCompressor.cs`, `src/LimeWin.Core/Tools/ImageCropper.cs`
- Test: `tests/LimeWin.Core.Tests/ImageCompressorTests.cs`, `tests/LimeWin.Core.Tests/ImageCropperTests.cs`

**Interfaces:**
- Produces:
  - `public enum SizeGoal { Preset, FileSize, Percentage }` `public enum SquashLevel { Gentle, Hard }` `public enum ResizeCap { Original = 0, Px2560 = 2560, Px1920 = 1920, Px1280 = 1280 }`
  - `public sealed record CompressOptions(SizeGoal Goal = SizeGoal.Preset, SquashLevel Level = SquashLevel.Gentle, double ShrinkPercent = 50, long? TargetBytes = null, ResizeCap Cap = ResizeCap.Original) { public long? TargetFor(long originalBytes); }`
  - `ImageCompressor.CompressAsync(string source, CompressOptions o, OperationContext ctx) -> Task<string>` — 결과 `ToolOutput(src, "compressed", 같은 형식)`.
  - `public readonly record struct PixelRect(int X, int Y, int Width, int Height);`
  - `ImageCropper.CropAsync(string source, PixelRect rect, OperationContext ctx) -> Task<string>` — `ToolOutput(src, "cropped", 같은 형식)`, 범위를 이미지 안으로 자름, 폭·높이 < 1이면 `LimeException`.

압축 알고리즘 (원작 `ImageTools.swift` 30–80행 그대로 이식):
- 먼저 `Cap`이면 긴 변을 그 값으로 축소.
- 프리셋: 품질 Gentle 0.72 / Hard 0.48. PNG는 색 수 양자화(품질 > 0.6 → 256색, > 0.35 → 128색, 그 외 64색). TIFF는 LZW. 결과가 원본 이상이고 `Cap == Original`이면 `LimeException("더 이상 작게 만들 수 없습니다.")`.
- 목표 크기(`FileSize`·`Percentage`): 목표 < 1000 바이트면 `LimeException("목표 크기는 1KB 이상이어야 합니다.")`, 목표 ≥ 원본이면 `LimeException("목표가 지금 파일보다 작아야 합니다.")`. 최대 8회: 손실 형식이면 품질 0.05–0.95 사이 이분 탐색 7회로 목표 이하 중 최고 품질을 찾으면 끝; 무손실이면 품질 0.3 한 번. 못 맞추면 메타데이터를 먼저 빼고 다시, 그다음 긴 변 ×0.8 축소(긴 변 < 32면 중단). 끝내 실패하면 `LimeException($"{가장 작은 크기}까지 줄였지만 더 작게는 안 됩니다.")`.
- `Percentage`: 목표 = 원본 × (1 − clamp(ShrinkPercent, 1, 99)/100).

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public async Task Preset_Gentle_JpgGetsSmaller_NamedCompressed() { /* 2000×1500 노이즈 jpg(품질 98) → "(compressed).jpg", 크기 < 원본 */ }
[Fact] public async Task TargetSize_HitsTarget() { /* 목표 200_000 → 결과 ≤ 200_000 */ }
[Fact] public async Task Percentage50_RoughlyHalvesOrLess() { }
[Fact] public async Task TargetBelow1KB_Throws() { }
[Fact] public async Task Cap1280_LongEdgeIs1280() { }
[Fact] public async Task AlreadyTinyPng_PresetThrows() { /* 1×1 png */ }
[Fact] public async Task Crop_ClampsToBounds_AndKeepsFormat() { /* 100×80 webp, rect(50,40,100,100) → 50×40 webp "(cropped)" */ }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS.
- [ ] **Step 5:** 커밋 `feat(core): image compress and crop`.

### Task 13: 도구 창과 도구 휠 연결

**Files:**
- Create: `src/LimeWin.App/Tools/CompressOptionsWindow.xaml(.cs)`, `src/LimeWin.App/Tools/CropWindow.xaml(.cs)`, `src/LimeWin.App/Tools/OrderWindow.xaml(.cs)`, `src/LimeWin.App/Tools/ToolDispatcher.cs`
- Modify: `src/LimeWin.App/Wheel/WheelController.cs` 이벤트 처리부 (ToolOp → `ToolDispatcher`)

**Interfaces:**
- Consumes: `ImageCompressor`, `ImageCropper`, `PdfTools`, `JobRunner.Run` (Task 6, 11, 12)
- Produces: `ToolDispatcher.Handle(LimeTool tool, IReadOnlyList<string> files)` — Task 15·16이 `CopyText`·`RemoveBackground` 분기를 채운다.

창:
- **압축 옵션:** 라디오 `프리셋(약하게/세게)` / `목표 크기 [숫자][KB|MB]` / `비율 [슬라이더 1–99 %]`, 콤보 `최대 크기: 원본 / 2560 / 1920 / 1280 px`, `압축` 버튼. 여러 파일이면 같은 옵션으로 각각 `JobRunner.Run`.
- **자르기:** 이미지 미리보기 위 드래그 사각형(모서리·변 핸들, 안쪽 드래그로 이동), 비율 콤보 `자유 / 1:1 / 4:3 / 3:2 / 16:9 / 9:16`, 숫자 입력 `X Y 폭 높이`(원본 픽셀, 사각형과 양방향 연동), `자르기` 버튼. 화면 좌표 ↔ 원본 픽셀 변환은 DPI·축소 배율 고려.
- **순서 창 (PDF 만들기·합치기):** 파일 목록, 드래그로 순서 바꾸기, ↑↓ 버튼, 이미지면 썸네일, `만들기`/`합치기` 버튼.
- 나누기는 창 없이 바로 실행.
- 창들은 앞으로 가져오기(`Activate`), Esc로 닫기.
- 모든 창: WPF-UI `FluentWindow` + Mica, 기본 버튼 강조색, 간격 8의 배수, 미리보기 영역 둥근 모서리 8 px. 창 열기·닫기는 Windows 기본 애니메이션에 맡기고 자체 애니메이션을 넣지 않는다. 눌리는 버튼은 WPF-UI 기본 눌림 효과 사용.
- `--render`에 `compress-options.png`, `crop.png`(샘플 이미지 포함), `order.png` 추가 → 렌더를 보고 정렬·여백을 다듬은 뒤 사용자 확인.

- [ ] **Step 1:** 구현.
- [ ] **Step 2:** `dotnet build` 경고 0, `dotnet test` PASS.
- [ ] **Step 3:** `docs/manual-checklist.md`에 도구 항목 추가(이미지 1장 Alt → 5개 꽃잎, 압축 세 방식, 자르기 비율·숫자, 이미지 3장 순서 바꿔 PDF, PDF 2개 합치기, 12쪽 나누기)하고 사용자와 확인.
- [ ] **Step 4:** 커밋 `feat(app): tools wheel and tool windows`.

### Task 14: 액션 휠 + 선반

**Files:**
- Create: `src/LimeWin.Core/Actions/FileMover.cs`, `src/LimeWin.App/Actions/ActionRunner.cs`, `src/LimeWin.App/Shelf/ShelfWindow.xaml(.cs)`, `src/LimeWin.App/Shelf/ShelfManager.cs`
- Test: `tests/LimeWin.Core.Tests/FileMoverTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MoveResult(IReadOnlyList<string> Moved, IReadOnlyList<string> Failures);`
  - `FileMover.Move(IReadOnlyList<string> paths, string destinationDir) -> MoveResult` — 원작 `OperationCoordinator.move`: 자기 자신·자기 하위로 이동은 실패 목록(`"‘이름’은 자기 안으로 옮길 수 없습니다."`), 이미 그 폴더에 있으면 건너뜀, 이름 충돌은 `UniquePath`. 파일·폴더 모두.
  - `ActionRunner.Run(DropAction action, IReadOnlyList<string> files, Point screenPx)`

액션:
- 복사: `Clipboard.SetFileDropList` + `Preferred DropEffect = Copy`(탐색기 Ctrl+V 붙여넣기 가능).
- 이동: `Microsoft.Win32.OpenFolderDialog`(제목 `"‘이름’을 옮길 폴더"` 또는 `"항목 N개를 옮길 폴더"`) → `FileMover.Move` → 실패 있으면 카드로 표시.
- 경로 복사: 전체 경로를 `\r\n`으로 이어 텍스트 클립보드.
- 터미널: 첫 항목이 폴더면 그 폴더, 아니면 상위 폴더. `wt.exe -d "<폴더>"`, 없으면 `powershell.exe -NoExit -Command "Set-Location -LiteralPath '<폴더>'"` (작은따옴표 이스케이프).
- 선반: 드롭 위치에 작은 카드(제목줄 + 파일 목록, 아이콘·이름). 제목줄 드래그로 이동, 닫기 ×. 목록에서 여러 개 선택해 바깥으로 끌어내기(`DragDrop.DoDragDrop`, `FileDrop`, Copy|Move 허용), 파일을 끌어 넣으면 추가(중복 무시). 존재하지 않게 된 파일은 흐리게. 선반 여러 개 가능(`ShelfManager`).

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public void Move_NameClash_GetsNumber() { /* dest에 a.txt 있음 → a 2.txt 로 이동, 원래 위치에 없음 */ }
[Fact] public void Move_IntoItself_Fails() { var dir = Directory.CreateDirectory(Path.Combine(Dir, "폴더")).FullName; var r = FileMover.Move([dir], Path.Combine(dir, "안")); Assert.Single(r.Failures); Assert.True(Directory.Exists(dir)); }
[Fact] public void Move_AlreadyThere_Skipped() { }
```
- [ ] **Step 2:** FAIL. **Step 3:** `FileMover` 구현 → PASS. 이어서 `ActionRunner`, 선반 구현.
- [ ] **Step 4:** 체크리스트에 액션 항목 추가(복사 후 탐색기 붙여넣기, 이동 충돌, 경로 복사, 터미널 위치, 선반 넣기·꺼내기·여러 개)하고 사용자와 확인.
- [ ] **Step 5:** 커밋 `feat: actions wheel and shelf`.

---

## Phase 4 — 품질 조건부 기능

### Task 15: 글자 복사 (OCR)

**Files:**
- Create: `src/LimeWin.Core/Tools/OcrService.cs`, `src/LimeWin.App/Tools/TextResultWindow.xaml(.cs)`
- Test: `tests/LimeWin.Core.Tests/OcrServiceTests.cs`
- Modify: `src/LimeWin.App/Tools/ToolDispatcher.cs` (CopyText), `src/LimeWin.App/Services.cs` (`Ocr = OcrService.IsAvailable`)

**Interfaces:**
- Consumes: `PdfText.PageTexts`, `PdfRenderer.RenderPagePngAsync` (Task 5)
- Produces:
  - `OcrService.IsAvailable -> bool` (`OcrEngine.TryCreateFromUserProfileLanguages() != null`)
  - `OcrService.InstalledLanguages -> IReadOnlyList<string>`
  - `OcrService.RecognizeAsync(string path, OperationContext ctx) -> Task<string>` — 이미지: 바로 OCR. PDF: 쪽마다 `PdfText` 결과의 공백 제외 글자 수가 20자 이상이면 그대로, 아니면 300 DPI 렌더 후 OCR. 쪽 사이 빈 줄. 이미지가 `OcrEngine.MaxImageDimension`을 넘으면 비율 유지 축소.
  - 엔진: 설치된 언어마다 엔진을 만들어 `ko`를 우선 쓴다. 한국어·영어가 모두 설치돼 있으면 `ko` 엔진 하나로 충분한지 Step 4에서 판단하고 기록.

결과 창: 텍스트 상자(편집 가능), `복사`, `텍스트로 저장`(`ToolOutput(src, "text", "txt")`), 닫기. 인식 즉시 클립보드에도 복사. 여러 파일이면 파일 제목을 구분선으로 넣어 한 창에.

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public async Task Image_KoreanText_Recognized() { /* Magick으로 "맑은 고딕" 48pt "안녕하세요 2026" 렌더 → 결과에 "안녕하세요" 와 "2026" */ }
[Fact] public async Task Pdf_WithTextLayer_UsesLayerNotOcr() { /* 텍스트 층 PDF → 결과가 PdfText와 동일 */ }
[Fact] public async Task ScannedPdf_FallsBackToOcr() { /* 이미지만 있는 PDF(ImagesToPdf로 생성) → 글자 인식됨 */ }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현. **Step 4:** PASS.
- [ ] **Step 5 (품질 관문):** 사용자에게 실제 샘플(한글 스크린샷, 한영 혼합 문서, 스캔 PDF)을 받아 결과 창으로 보여 준다. 영어 언어팩이 없어 영어가 깨지면 설치 방법(설정 › 시간 및 언어 › 언어 › 영어 추가)을 안내하고 다시 확인. 사용자가 "쓸 만하다"고 하면 계속, 아니면 **멈추고** `Ocr`을 false로 두고 대안(PaddleOCR 등)을 사용자와 논의. 결과를 `docs/decisions.md`에 기록.
- [ ] **Step 6:** 커밋 `feat: copy text (ocr)`.

### Task 16: 배경 제거

**Files:**
- Create: `src/LimeWin.Core/Tools/BackgroundRemover.cs`, `src/LimeWin.Core/Tools/ModelStore.cs`, `src/LimeWin.App/Tools/ModelDownloadWindow.xaml(.cs)`
- Test: `tests/LimeWin.Core.Tests/BackgroundRemoverTests.cs`, `tests/LimeWin.Core.Tests/ModelStoreTests.cs`
- Modify: `ToolDispatcher.cs` (RemoveBackground), `Services.cs` (`BackgroundRemoval = true` — 모델이 없으면 첫 사용 때 내려받기 창), `NOTICE.md` (BiRefNet MIT, ONNX Runtime MIT, Windows ML 라이선스)

**Interfaces:**
- Produces:
  - `ModelStore.ModelPath -> string` (`%LOCALAPPDATA%\LimeWin\models\BiRefNet-general-epoch_244.onnx`)
  - `ModelStore.IsReady -> bool` (파일 존재 + SHA-256 일치)
  - `ModelStore.DownloadAsync(IProgress<double> progress, CancellationToken ct) -> Task` — `https://github.com/ZhengPeng7/BiRefNet/releases/download/v1/BiRefNet-general-epoch_244.onnx` (972,666,916 바이트). 임시 파일로 받아 크기·SHA-256 확인 후 이름 변경. **SHA-256 값은 구현자가 처음 받은 파일에서 계산해 상수로 고정**하고 `docs/decisions.md`에 기록.
  - `BackgroundRemover.RemoveAsync(string source, OperationContext ctx) -> Task<string>` — `ToolOutput(src, "no background", "png")`.

추론: Windows ML(`Microsoft.Windows.AI.MachineLearning`, self-contained). 시작 시 `ExecutionProviderCatalog.GetDefault().EnsureAndRegisterCertifiedAsync()`로 이 PC에 맞는 실행 공급자(RTX → NvTensorRtRtx)를 받아 등록하고, 세션 옵션은 GPU 우선(`ExecutionProviderDevicePolicy.PREFER_GPU` 또는 문서상 동등한 API — 2026-10 기준 Windows ML 문서 "Accelerate AI models"의 C# 예제를 따르고 `docs/decisions.md`에 기록), 실패 시 CPU. 실행 공급자 내려받기도 모델 내려받기 창에서 함께 진행 표시. 입력 1024×1024 RGB, `(x/255 − mean)/std`, mean `[0.485, 0.456, 0.406]`, std `[0.229, 0.224, 0.225]`, NCHW float32. 출력 마지막 텐서에 시그모이드 → 원본 크기로 쌍선형 확대 → 알파 채널. 세션은 한 번 만들어 재사용(첫 로드 느림 → 진행 카드 "모델 불러오는 중").

- [ ] **Step 1: 실패하는 시험**
```csharp
[Fact] public void ModelStore_WrongHash_NotReady() { /* 같은 이름의 가짜 파일 → IsReady false */ }
[Fact] public async Task Remove_SolidBackgroundCircle_CornersTransparentCenterOpaque() { Assert.SkipUnless(ModelStore.IsReady, "모델 없음"); /* 흰 배경 위 빨간 원 512px → 모서리 알파 < 20, 중심 알파 > 235, 이름 "(no background).png" */ }
```
- [ ] **Step 2:** FAIL. **Step 3:** 구현, 모델 내려받기. **Step 4:** PASS (모델 받은 뒤).
- [ ] **Step 5 (품질 관문):** 사용자 샘플(인물·머리카락, 동물 털, 물건)로 결과를 보여 주고 처리 시간을 잰다. 사용자가 "맥 수준"이라 하면 계속. 아니면 **멈추고** `BiRefNet_HR-general` 또는 `matting` 모델을 사용자와 논의. 결과를 `docs/decisions.md`에 기록.
- [ ] **Step 6:** 커밋 `feat: background removal`.

---

## Phase 5 — 마무리

### Task 17: 전체 확인과 문서

**Files:**
- Create: `README.md` (설치·실행 방법, 키 배치, 요구 사항: Windows 11, .NET 10 런타임, ffmpeg, MS Office, 원작 링크와 MIT 고지), `build.ps1` (`dotnet publish src/LimeWin.App -c Release -r win-x64 --self-contained false -o dist`)
- Modify: `docs/manual-checklist.md`

- [ ] **Step 1:** `dotnet test` 전체(`Office`, `Ffmpeg` 포함) PASS 출력 저장.
- [ ] **Step 2:** `./build.ps1` 후 `dist\LimeWin.App.exe` 실행, `docs/manual-checklist.md` 전 항목을 사용자와 함께 통과.
- [ ] **Step 3:** 스펙 §2 반목표를 하나씩 직접 확인(원본 불변, 평소 드래그, 평소 키, Office 창·프로세스, UI 멈춤 없음)하고 체크리스트에 기록.
- [ ] **Step 4:** 커밋 `docs: readme, build script, final checklist`.
