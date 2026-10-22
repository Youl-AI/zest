# Implementation decisions

Decisions made at the implementer's discretion while building Lime for Windows; later tasks append here.

- Task 1: xunit.v3 runs on Microsoft.Testing.Platform via `global.json`, so the test command is `dotnet test --project tests/LimeWin.Core.Tests` (the positional form fails on SDK 10).
- Task 1: Gif targets are the writable video formats; Video targets add gif+mp3.
- Task 1: pdf->txt is not Office-gated (PdfPig handles it).
- Task 1: `OutputNaming.UniquePath` treats an existing directory with that name as taken.
- Task 2: `FormatOf` returns null for an existing directory (so a folder named `x.png` is not treated as an image); a folder among the dragged paths empties Convert and Tools, Actions stay at 5.
- Task 2: Crop appears only when exactly one image is dragged (multi-image drops omit it).
- Task 2: Convert offers the intersection of every file's targets in the first file's order; any unknown format or folder yields an empty list. Tools: raster images only (jpg png webp heic tiff avif bmp; svg excluded), or PDFs only; mixed yields empty.
