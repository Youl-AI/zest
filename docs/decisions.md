# Implementation decisions

Decisions made at the implementer's discretion while building Lime for Windows; later tasks append here.

- Task 1: xunit.v3 runs on Microsoft.Testing.Platform via `global.json`, so the test command is `dotnet test --project tests/LimeWin.Core.Tests` (the positional form fails on SDK 10).
- Task 1: Gif targets are the writable video formats; Video targets add gif+mp3.
- Task 1: pdf->txt is not Office-gated (PdfPig handles it).
- Task 1: `OutputNaming.UniquePath` treats an existing directory with that name as taken.
- Task 2: `FormatOf` returns null for an existing directory (so a folder named `x.png` is not treated as an image); a folder among the dragged paths empties Convert and Tools, Actions stay at 5.
- Task 2: Crop appears only when exactly one image is dragged (multi-image drops omit it).
- Task 2: Convert offers the intersection of every file's targets in the first file's order; any unknown format or folder yields an empty list. Tools: raster images only (jpg png webp heic tiff avif bmp; svg excluded), or PDFs only; mixed yields empty.
- Task 3: Drag threshold is per-axis (`|dx| > px || |dy| > px`, strictly greater), following the Windows SM_CXDRAG convention, not Euclidean distance.
- Task 3: `WheelGeometry.PetalAt` treats the Inner and Outer radii as inclusive; gutters are constant-width strips (angular half-gutter = asin(Gap/2/r)); `BearingOf` returns the petal's centre in radians.
- Task 4: SVG rasterization stays at 192 DPI (density 192, 2x), so `FixtureFactory.Image` writes SVG sources at half size (w/2 x h/2 user units) and every source in the Routes theory decodes to 64x48.
- Task 4: CapabilityProbe on this machine with Magick.NET 15.0.0 (Q8-AnyCPU): HEIC write = false, AVIF write = true. The Routes theory skips heic as target and as source (the fixture cannot be written); avif is fully covered.
- Task 4: ConversionRouter checks the source folder is writable by creating/deleting a temp file before calling the converter, and after any converter exception deletes directory entries that did not exist before the call (files and folders), then rethrows.
- Task 4: ImageConverter decodes from bytes (content decides the decoder, so a PNG named .jpg works); Magick exceptions map to `LimeException("이미지를 읽을 수 없습니다.")`. Alpha is flattened to white for jpg and bmp only. PNG uses define png:compression-level=6.
- Task 4: PDFsharp 6.5.0 turned out to be AGPL-3.0-only (license key required at runtime; 6.2.4 is the last MIT release). Pin lowered to 6.2.4 and referenced only by the test project; image->PDF is written with PdfPig's `PdfDocumentBuilder` (page = pixel size in pt, `AddPng` fills the page). PDF tests verify with PdfPig.
- Task 4: Enabled `CentralPackageTransitivePinningEnabled` and pinned transitive System.Security.Cryptography.Pkcs to 10.0.13 (8.0.1 and 10.0.0 carry NU1902/NU1903 advisories); build is warning-free.
- Task 4 fix: failure cleanup no longer diffs the folder. Converters call `ctx.Track(path)` for each output file/folder BEFORE creating it (`OperationContext.Created`, thread-safe); on any exception the router deletes only tracked paths (folders recursively), so files other processes create meanwhile are never touched. ImageConverter maps decode failures to "이미지를 읽을 수 없습니다.", encode failures to "이미지를 저장하지 못했습니다.", and source IO/permission errors to "원본 파일을 열 수 없습니다.".
