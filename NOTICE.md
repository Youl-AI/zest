# Notices

## Original project: Lime

Lime for Windows is a port of the macOS app Lime (https://github.com/LumenJSN/Lime),
used under the following MIT license.

```
MIT License

Copyright (c) 2026 Lumen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Third-party

Libraries are listed here as later tasks add them (name, version, license).

- Magick.NET-Q8-AnyCPU 15.0.0 — Apache-2.0 (Magick.NET); bundles ImageMagick under the ImageMagick License
- PdfPig 0.1.16 — Apache-2.0 (writes image-to-PDF output, extracts PDF text)
- PDFsharp 6.2.4 — MIT; test-only dependency (not shipped). 6.5.0+ is AGPL-3.0-only and must not be used.
- PDFtoImage 5.4.0 — MIT (renders PDF pages)
- PDFium (bblanchon.PDFium.Win32 152.0.7961 binaries, pulled in by PDFtoImage) — Apache-2.0 per the NuGet package metadata (upstream PDFium is BSD-3-Clause/Apache-2.0 licensed)
- SkiaSharp 4.150.1 — MIT (transitive via PDFtoImage; encodes rendered pages)
