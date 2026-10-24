# Zest Metadata Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A "메타데이터" petal on the Alt (tools) wheel that shows a file's tags, lets the user edit or remove them, remove the location, or remove everything, and saves the result as a new file next to the original — for images, PDFs, audio and video, like the original Lime's `MetadataTools.swift`.

**Architecture:** Core gets a `Zest.Core.Metadata` namespace: one tag model, one edit record, and a reader/writer per family (image, PDF, media) behind `MetadataReader` / `MetadataWriter`. JPEG is rewritten at the segment level so pixels are never re-encoded; other images go through Magick.NET; PDFs through PDFsharp; media through ffprobe/ffmpeg with stream copy. App gets one `MetadataWindow` on the `ZestWindow` frame.

**Tech Stack:** .NET 10, Magick.NET-Q8 15.0.0, PDFsharp 6.2.4 (newly referenced by Core; already pinned for tests), ffmpeg/ffprobe on PATH, xUnit v3.

**Spec:**
- Feature reference: original `LumenJSN/Lime` `Sources/Tools/MetadataTools.swift` (read this session).
- Window design: "메타데이터" section of https://claude.ai/artifact/9WYqLs4XVipoar4BjXQFKX (approved).
- User decision: build it like the original (images, PDF, audio, video; edit, remove one, remove all). Runs after the rename and design plans.

## Global Constraints

- The original file is never modified. Output = `OutputNaming.ToolOutput(source, tag, sameExtension, ctx)` with tag `"edited"`, or `"no metadata"` when "태그 모두 지우기" is on; numbering on collisions is the existing `ReservePath` behavior.
- Runs through `ToolRunner.RunAsync` inside `EngineGates.Image` (images, PDFs) or `EngineGates.Ffmpeg` (media); errors are `ZestException` with a full Korean sentence ending in "다.".
- Formats: images `jpg png webp tiff avif heic` (heic only with `HeicWrite`, avif only with `AvifWrite`; never `bmp`, `svg`), `pdf`, audio and video = `FormatCategory.Audio` / `FormatCategory.Video` (not gif), media only when ffmpeg AND ffprobe exist.
- "태그 모두 지우기" keeps the image's ICC color profile and its EXIF Orientation (when not 1), so photos keep their colors and stay upright. (The original drops orientation; this is a deliberate fix.)
- "위치 지우기" removes every EXIF GPS tag AND the XMP packet (XMP can repeat the location); for media, every tag whose key contains `location` (case-insensitive), e.g. `location`, `com.apple.quicktime.location.ISO6709`; PDFs have no location.
- Editable = text values only: EXIF ASCII tags outside the GPS group, PDF Title/Author/Subject/Keywords/Creator/Producer, and every media tag. Dates in PDFs, pixel size, format and GPS are read-only. Korean text is written as UTF-8.
- One file → the full editor. Two or more files of a supported kind → the same window in batch mode with only two choices, "위치만 지우기" / "태그 모두 지우기".
- Copy: petal label `메타데이터`; icon Segoe Fluent Icons "Tag" ``; window heading `메타데이터`; batch heading `메타데이터 · 파일 {n}개`; primary button `새 파일로 저장`; checkbox `태그 모두 지우기`; banner `이 파일에는 촬영 위치가 들어 있습니다. 공유하기 전에 지우는 것이 좋습니다.` with button `위치 지우기` (toggled: banner `저장할 때 촬영 위치를 뺍니다.`, button `되돌리기`); caption `원본은 그대로 두고 옆에 ‘{stem} (edited).{ext}’를 만듭니다.` (wipe on: `… ‘{stem} (no metadata).{ext}’를 만듭니다. 색 정보(ICC)와 사진 방향은 남깁니다.`); errors `메타데이터를 읽을 수 없습니다.`, `암호로 보호된 PDF는 바꿀 수 없습니다.`, `이 형식은 메타데이터를 바꿀 수 없습니다.`.
- Korean labels for known keys — EXIF: Make 제조사, Model 모델, LensMake 렌즈 제조사, LensModel 렌즈, DateTimeOriginal 촬영 일시, DateTimeDigitized 디지털화 일시, DateTime 수정 일시, Software 소프트웨어, Artist 작성자, Copyright 저작권, ImageDescription 설명, Orientation 방향, GPSLatitude 위도, GPSLongitude 경도, GPSAltitude 고도; PDF: Title 제목, Author 작성자, Subject 주제, Keywords 키워드, Creator 만든 프로그램, Producer 변환 프로그램, CreationDate 만든 날짜, ModDate 수정한 날짜; media: title 제목, artist 아티스트, album 앨범, album_artist 앨범 아티스트, genre 장르, date 연도, track 트랙, composer 작곡가, comment 설명, encoder 인코더, location 위치. Any other key shows as-is. Groups: 파일, 카메라, 촬영, 위치, 설명, 문서, 미디어, 기타.

## Review Focus

- A portrait phone photo (EXIF Orientation 6) with "태그 모두 지우기": the output still displays upright (Task 2 test `RemoveAll_KeepsOrientationAndIcc`).
- Korean typed into 작성자/설명 round-trips exactly when read back (Task 2 test `Edit_KoreanText_RoundTrips`; Task 3 for PDF).
- Location stored only in XMP (some editors write `exif:GPSLatitude` into XMP): "위치 지우기" still removes it (Task 2 test `RemoveLocation_AlsoDropsXmp`).
- An encrypted PDF: a Korean error card, no output file left behind (Task 3 test `Encrypted_ThrowsKoreanError_LeavesNothing`).
- A video whose location sits in a QuickTime key: "위치 지우기" removes it while the streams are copied unchanged (Task 4 test `RemoveLocation_DropsQuickTimeLocation_KeepsStreams`).

---

### Task 1: Catalog, capability and the model

**Files:**
- Modify: `src/Zest.Core/Catalog/Operations.cs` (`ZestTool.Metadata` appended last), `src/Zest.Core/Catalog/WheelCatalog.cs` (rules below, `ToolLabel` arm), `src/Zest.Core/Catalog/FormatCatalog.cs` (`Capabilities` gains `bool Ffprobe = true` as the last parameter; `All` stays all-true), `src/Zest.Core/Convert/CapabilityDetector.cs` (`Ffprobe = ffmpeg is not null && FfmpegLocator.LocateProbe(ffmpeg) is not null`), `src/Zest.App/Wheel/WheelStyle.cs` (`IconFor` arm ``)
- Create: `src/Zest.Core/Metadata/MetadataTag.cs`, `src/Zest.Core/Metadata/MetadataEdit.cs`, `src/Zest.Core/Metadata/MetadataLabels.cs`
- Test: `tests/Zest.Core.Tests/WheelCatalogTests.cs`, `tests/Zest.Core.Tests/MetadataLabelsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MetadataTag(string Id, string Group, string Label, string Value, bool Editable, bool Removable)` — `Id` is `"{family}:{key}"` with family `file|exif|xmp|iptc|pdf|media`.
  - `public sealed record MetadataEdit(IReadOnlyDictionary<string, string> Changed, IReadOnlySet<string> Removed, bool RemoveLocation, bool RemoveAll)` with `static MetadataEdit LocationOnly` and `static MetadataEdit Everything`.
  - `public static class MetadataLabels { (string Group, string Label) For(string family, string key); bool IsLocationKey(string family, string key); }`
  - `public static class MetadataFormats { bool Supports(string format, Capabilities caps); }` in `Zest.Core.Metadata`.

WheelCatalog rules (append, keep existing order): images branch → `Metadata` last when every format passes `MetadataFormats.Supports`; PDF branch → `Metadata` last; new branch after PDF: every format's category is `Audio` or `Video` and `caps.Ffmpeg && caps.Ffprobe` → `[Metadata]`.

- [ ] **Step 1: Update and add catalog tests:** `Tools_OneImage_HasCrop` → `["압축","자르기","PDF 만들기","글자 복사","배경 제거","메타데이터"]`; `Tools_TwoPdfs_MergeFirst` → `["합치기","나누기","글자 복사","메타데이터"]`; new `Tools_Audio_MetadataOnly` (`song.mp3`) → `["메타데이터"]`; new `Tools_Video_WithoutFfprobe_Empty` (`clip.mp4`, `Capabilities.All with { Ffprobe = false }`) → empty; new `Tools_Bmp_NoMetadata` (`a.bmp`) → does not contain `"메타데이터"`; new `Tools_HeicWithoutHeicWrite_NoMetadata` → does not contain `"메타데이터"`.
- [ ] **Step 2: Add `MetadataLabelsTests`:** `For("exif","Model")` → `("카메라","모델")`; `For("media","album_artist")` → `("미디어","앨범 아티스트")`; `For("exif","WhiteBalance")` → `("기타","WhiteBalance")`; `IsLocationKey("media","com.apple.quicktime.location.ISO6709")` → true; `IsLocationKey("exif","GPSLatitude")` → true; `IsLocationKey("exif","Model")` → false.
- [ ] **Step 3: Run** `dotnet test --project tests/Zest.Core.Tests --filter-class "Zest.Core.Tests.WheelCatalogTests" --filter-class "Zest.Core.Tests.MetadataLabelsTests"` — Expected: FAIL.
- [ ] **Step 4: Implement** the enum member, capability, catalog rules, model records, labels and `MetadataFormats`.
- [ ] **Step 5: Run the same command** — Expected: all pass. Then the full suite — Expected: 0 failed.
- [ ] **Step 6: Commit** `git commit -m "feat: metadata petal for images, PDFs and media"`

### Task 2: Image tags (JPEG lossless, others through Magick)

**Files:**
- Create: `src/Zest.Core/Metadata/JpegSegments.cs`, `src/Zest.Core/Metadata/ImageMetadata.cs`, `src/Zest.Core/Metadata/MetadataReader.cs`, `src/Zest.Core/Metadata/MetadataWriter.cs`
- Test: `tests/Zest.Core.Tests/JpegSegmentsTests.cs`, `tests/Zest.Core.Tests/ImageMetadataTests.cs`; extend `tests/Zest.Core.Tests/Fixtures/FixtureFactory.cs` with `JpegWithTags(dir, name, IReadOnlyDictionary<ExifTag<string>, string> tags, bool withGps = false, bool withXmpGps = false, ushort orientation = 1, bool withIcc = false)`.

**Interfaces:**
- Produces:
  - `internal static class JpegSegments { byte[] Rewrite(byte[] jpeg, byte[]? exifApp1Payload, bool dropXmp, bool dropIptc); }` — parses markers from SOI up to SOS; drops the old APP1 `Exif\0\0` segment, inserts `exifApp1Payload` (already prefixed with `Exif\0\0`) as APP1 right after SOI/APP0 when not null, drops APP1 XMP (`http://ns.adobe.com/xap/1.0/\0`) when `dropXmp`, drops APP13 when `dropIptc`, keeps every other segment (APP0, APP2 ICC, DQT, …) and copies everything from SOS to the end byte-for-byte. Throws `ZestException("메타데이터를 읽을 수 없습니다.")` on a malformed stream.
  - `public static class MetadataReader { Task<IReadOnlyList<MetadataTag>> ReadAsync(string path, Capabilities caps, CancellationToken token); }` (dispatches by family; this task implements images).
  - `public static class MetadataWriter { Task<string> ApplyAsync(string source, MetadataEdit edit, OperationContext ctx); }` (same dispatch).

Image rules: read EXIF (every value; Ascii values editable unless GPS), IPTC and XMP presence (XMP shown as one read-only row "XMP 데이터 있음", removable), plus `file:` rows 형식 and 크기 (`{w} × {h} · {size}`). Write: build the new EXIF from the source profile with `Changed` applied and `Removed` removed; `RemoveLocation` removes every GPS tag and the XMP; `RemoveAll` builds a profile holding only Orientation (when ≠ 1), drops XMP and IPTC, keeps ICC. JPEG → `JpegSegments.Rewrite`; png/tiff → Magick load without `AutoOrient`, set or remove profiles, write the same format; webp/avif/heic → same at quality 97.

- [ ] **Step 1: Write `JpegSegmentsTests`:** `Rewrite_KeepsScanDataByteForByte` (bytes from SOS to end equal the source's); `Rewrite_DropsXmp_KeepsIcc` (output has no XMP APP1, still has the APP2 ICC segment); `Rewrite_Malformed_Throws` (`[0xFF,0xD8,0x00]` → `ZestException`).
- [ ] **Step 2: Write `ImageMetadataTests`** (fixtures via `JpegWithTags` / `FixtureFactory.Image`):
  - `Read_Jpeg_ListsCameraAndLocation` → contains `exif:Model` "Pixel 9" in group 카메라 editable, and GPS rows in group 위치 read-only and removable.
  - `Edit_KoreanText_RoundTrips` → `Changed = {"exif:Artist": "홍길동"}`; reading the output gives "홍길동"; output name ends `" (edited).jpg"`; source bytes unchanged.
  - `Remove_OneTag` → `Removed = {"exif:Model"}`; output has no Model, keeps Make.
  - `RemoveLocation_AlsoDropsXmp` → fixture with EXIF GPS and an XMP packet containing `exif:GPSLatitude`; output has no GPS tags and no XMP.
  - `RemoveAll_KeepsOrientationAndIcc` → fixture orientation 6 + ICC + Make/Model; output name ends `" (no metadata).jpg"`; output EXIF has only Orientation = 6; ICC present; Make/Model gone.
  - `Jpeg_PixelsUnchanged` → decoded pixel hash of output equals source (Magick `GetPixels().ToByteArray`).
  - `Png_Edit_RoundTrips` → png with EXIF Artist; edit to "김"; read back "김".
  - `SameNameExists_GetsNumbered` → an existing `photo (edited).jpg` → output `photo (edited) 2.jpg`.
- [ ] **Step 3: Run** `--filter-class "Zest.Core.Tests.JpegSegmentsTests" --filter-class "Zest.Core.Tests.ImageMetadataTests"` — Expected: FAIL.
- [ ] **Step 4: Implement `JpegSegments`, `ImageMetadata`, and the image path of `MetadataReader` / `MetadataWriter`.** Build the APP1 payload from Magick's `ExifProfile.ToByteArray()`; if Magick returns it without the `Exif\0\0` prefix, add it (the round-trip tests decide).
- [ ] **Step 5: Run the same command** — Expected: all pass.
- [ ] **Step 6: Commit** `git commit -m "feat: read and rewrite image tags; JPEG without re-encoding"`

### Task 3: PDF document info

**Files:**
- Modify: `src/Zest.Core/Zest.Core.csproj` (`<PackageReference Include="PDFsharp" />`), `NOTICE.md` (PDFsharp 6.2.4 — MIT, with its notice, in the libraries section)
- Create: `src/Zest.Core/Metadata/PdfMetadata.cs`
- Test: `tests/Zest.Core.Tests/PdfMetadataTests.cs`

**Interfaces:**
- Consumes: `MetadataReader` / `MetadataWriter` dispatch (Task 2).
- Produces: the `pdf:` family — keys `Title, Author, Subject, Keywords, Creator, Producer` (editable), `CreationDate, ModDate` (read-only, shown as `yyyy-MM-dd HH:mm`).

Rules: open with PDFsharp in modify mode from a copy of the bytes; write the edited Info to the output; `RemoveAll` clears all eight entries and removes the catalog's `/Metadata` stream; Producer in the output must equal the user's value (or be absent after removal) even though PDFsharp stamps its own by default — override it explicitly before saving. Encrypted → `ZestException("암호로 보호된 PDF는 바꿀 수 없습니다.")`; unreadable → `"PDF를 읽을 수 없습니다."`.

- [ ] **Step 1: Write the tests** (fixtures via `FixtureFactory.Pdf` with Info set through PDFsharp in the fixture):
  - `Read_ListsTitleAndAuthor` → `pdf:Title` "제주 일정" editable; `pdf:CreationDate` read-only.
  - `Edit_Author_RoundTrips_AndProducerIsOurs` → `Changed = {"pdf:Author": "김제주", "pdf:Producer": "Zest"}` → read back both exactly; page count unchanged; name ends `" (edited).pdf"`.
  - `RemoveAll_ClearsInfo` → no Title/Author/Subject/Keywords/Creator/Producer in the output; name ends `" (no metadata).pdf"`.
  - `Encrypted_ThrowsKoreanError_LeavesNothing` → fixture with `userPassword: "x"` → `ZestException` with the exact message; no `* (edited).pdf` left in the folder.
- [ ] **Step 2: Run** `--filter-class "Zest.Core.Tests.PdfMetadataTests"` — Expected: FAIL.
- [ ] **Step 3: Implement `PdfMetadata`** and wire it into the dispatch.
- [ ] **Step 4: Run the same command** — Expected: 4 passed.
- [ ] **Step 5: Commit** `git commit -m "feat: edit and remove PDF document info"`

### Task 4: Audio and video tags

**Files:**
- Create: `src/Zest.Core/Convert/FfmpegProcess.cs` (the process runner moved out of `FfmpegConverter.RunAsync` and its ffprobe call), `src/Zest.Core/Metadata/MediaMetadata.cs`
- Modify: `src/Zest.Core/Convert/FfmpegConverter.cs` (use `FfmpegProcess`; behavior unchanged)
- Test: `tests/Zest.Core.Tests/MediaMetadataTests.cs` (`[Trait("Category","Ffmpeg")]`, `[Collection(ProcessCollection.Name)]`); extend `FixtureFactory` with `TaggedMedia(dir, name, IReadOnlyDictionary<string,string> tags, int seconds = 2)` (runs ffmpeg with `-metadata k=v` and, for mp4/mov, `-movflags +use_metadata_tags`).

**Interfaces:**
- Produces: `internal static class FfmpegProcess { Task RunAsync(string ffmpegPath, IReadOnlyList<string> args, double? duration, OperationContext ctx); Task<string?> CaptureAsync(string exePath, IReadOnlyList<string> args, CancellationToken token); }` — same KillOnExitJob, cancel-kill, stderr tail and Korean error as today.
- Produces: the `media:` family. Read = `ffprobe -v error -show_entries format_tags -of json <src>` → every key/value, editable.

Write args (exact): `-hide_banner -nostdin -y -i <src> -map 0 -c copy`, then for `RemoveAll`: `-map_metadata -1 -map_chapters -1 -fflags +bitexact -flags:v +bitexact -flags:a +bitexact`; otherwise `-map_metadata 0` plus one `-metadata key=value` per changed key and `-metadata key=` per removed key (and per location key when `RemoveLocation`); for mp4, mov, m4a add `-movflags +faststart+use_metadata_tags`; then the output path.

- [ ] **Step 1: Write the tests:**
  - `Read_ListsTitleAndArtist` → `media:title` "바다", `media:artist` "제스트" from `TaggedMedia("a.mp3", {title, artist})`.
  - `Edit_Title_RoundTrips` → change title to "파도" → read back "파도"; name ends `" (edited).mp3"`.
  - `RemoveLocation_DropsQuickTimeLocation_KeepsStreams` → `TaggedMedia("clip.mov", {"com.apple.quicktime.location.ISO6709": "+33.4996+126.5312/", "title": "x"})`; output tags have no key containing "location", still have title; ffprobe stream types equal the source's.
  - `RemoveAll_LeavesNoTags` → output `format_tags` empty except keys ffmpeg always writes (`major_brand`, `minor_version`, `compatible_brands`).
- [ ] **Step 2: Run** `--filter-class "Zest.Core.Tests.MediaMetadataTests"` — Expected: FAIL.
- [ ] **Step 3: Extract `FfmpegProcess`**, then run `--filter-class "Zest.Core.Tests.FfmpegConverterTests"` — Expected: still all pass (refactor only).
- [ ] **Step 4: Implement `MediaMetadata`** and wire it in. Run `--filter-class "Zest.Core.Tests.MediaMetadataTests"` — Expected: 4 passed.
- [ ] **Step 5: Commit** `git commit -m "feat: edit and remove audio and video tags without re-encoding"`

### Task 5: The metadata window

**Files:**
- Create: `src/Zest.App/Tools/MetadataWindow.xaml`, `src/Zest.App/Tools/MetadataWindow.xaml.cs`
- Modify: `src/Zest.App/Tools/ToolDispatcher.cs` (`case ZestTool.Metadata`), `src/Zest.App/Render/ToolWindowRenders.cs` and `RenderHarness.cs` (`metadata{,-dark}.png`, `metadata-batch{,-dark}.png`), `docs/manual-checklist.md`

**Interfaces:**
- Consumes: `MetadataReader.ReadAsync`, `MetadataWriter.ApplyAsync`, `MetadataEdit`, `ZestWindow` and the styles from the design plan.
- Produces: `public MetadataWindow(IReadOnlyList<string> files, IReadOnlyList<MetadataTag>? tags, Action<MetadataEdit> save)` — `tags` null = batch mode.

Layout (single file), per the popup page: width 460; the location banner on top only when any tag is a location tag; a scrolling list (max 420 px) of groups in the order 파일, 카메라, 촬영, 위치, 설명, 문서, 미디어, 기타, each row = label (96 px, text-2) + value (editable `Zest.Field`, or read-only text) + a 28 px remove × for removable rows (toggles a strike-through and adds the id to `Removed`); the caption above the footer; footer left = `태그 모두 지우기` checkbox (dims the list), right = 취소, 새 파일로 저장. Batch: heading `메타데이터 · 파일 {n}개`, a two-segment choice 위치만 지우기 / 태그 모두 지우기, the caption, footer right = 취소, 새 파일로 저장. Dispatcher: one file → read tags on a background task (failure → failed card titled `메타데이터` with the error) → `Present(new MetadataWindow(...))`; save → one job per file, `JobKind.Tool`, title `메타데이터`, detail = file name.

- [ ] **Step 1: Build the window and the dispatcher case.**
- [ ] **Step 2: Render** — `./build.ps1; dist\Zest.exe --render "$env:TEMP\zest-render"` — Expected: `metadata*.png` match the popup page section; buttons right-aligned.
- [ ] **Step 3: Add manual checks** to `docs/manual-checklist.md`, table "### 메타데이터 (2026-10-24)":
  - `| 70 | 휴대폰 사진 → Alt → 메타데이터 → 위치 지우기 → 새 파일로 저장 | ‘이름 (edited).jpg’가 생기고, 속성 › 자세히에 GPS가 없음. 원본은 그대로 | |`
  - `| 71 | 세로 사진 → 태그 모두 지우기 | ‘이름 (no metadata).jpg’가 똑바로 보이고 색이 그대로 | |`
  - `| 72 | 작성자에 한글 입력 후 저장 | 다시 메타데이터 창을 열면 같은 한글이 보임 | |`
  - `| 73 | 사진 5장 → 메타데이터 → 위치만 지우기 | 카드 5장, 결과 5개, 각 결과에 GPS 없음 | |`
  - `| 74 | mp3 → 메타데이터 → 제목 바꾸기 | 탐색기 속성의 제목이 바뀐 새 파일, 재생 시간과 음질 그대로 | |`
  - `| 75 | 암호 걸린 PDF → 메타데이터 | 카드에 "암호로 보호된 PDF는 바꿀 수 없습니다." | |`
- [ ] **Step 4: Full tests** `dotnet test --project tests/Zest.Core.Tests` — Expected: 0 failed.
- [ ] **Step 5: Commit** `git commit -m "feat: metadata window with single-file editor and batch cleanup"`
