# Notices

## Original project: Lime

Zest is a Windows port of the macOS app Lime (https://github.com/LumenJSN/Lime),
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
- PDFsharp 6.2.4 — MIT, © 2026 empira (edits PDF document info; also used by tests to build fixtures). 6.5.0+ is AGPL-3.0-only and must not be used.
- PDFtoImage 5.4.0 — MIT (renders PDF pages)
- PDFium (bblanchon.PDFium.Win32 152.0.7961 binaries, pulled in by PDFtoImage) — BSD-3-Clause (upstream PDFium, https://pdfium.googlesource.com/pdfium/+/main/LICENSE). The binary package's NuGet metadata states Apache-2.0 (for the bblanchon/pdfium-binaries build).
- SkiaSharp 4.150.1 — MIT (transitive via PDFtoImage; encodes rendered pages)
- WPF-UI 4.3.0 (with WPF-UI.Abstractions 4.3.0) — MIT (Fluent theme and controls for cards and windows)
- H.NotifyIcon.Wpf 2.4.1 (with H.NotifyIcon 2.4.1) — MIT (notification-area icon and menu)
- Markdig 1.4.2 — BSD-2-Clause (Markdown to HTML)
- ReverseMarkdown 6.3.0 — MIT (HTML to Markdown)
- AngleSharp 1.5.2 — MIT (transitive via ReverseMarkdown; parses HTML)
- Pretendard 1.3.9 (Pretendard-SemiBold.otf, embedded) — SIL Open Font License 1.1, full text below (Korean petal labels)
- Microsoft.Windows.AI.MachineLearning 2.4.89 (Windows ML, self-contained) — Microsoft Software License Terms for the "Microsoft Windows ML Runtime" (the package's license.txt; its binaries are distributable code). Runs background removal. Zest switches off ONNX Runtime's own telemetry (`DisableTelemetryEvents`) when it first creates the runtime; any diagnostic data the Windows ML components themselves send is governed by the Windows diagnostic-data settings (Settings › Privacy & security › Diagnostics & feedback).
- ONNX Runtime 1.27.1 (onnxruntime.dll and the managed Microsoft.ML.OnnxRuntime API, shipped inside the Windows ML package) — MIT, Copyright (c) Microsoft Corporation
- BiRefNet matting checkpoint (BiRefNet-matting-epoch_100.onnx by ZhengPeng7, from the BiRefNet v1 release; not shipped, downloaded on first use of 배경 제거) — MIT, Copyright (c) 2024 ZhengPeng
- Foreground colour estimation (`ForegroundEstimator`): a C# re-implementation of the blur-fusion estimator as used in BiRefNet's `refine_foreground` (image_proc.py, MIT, Copyright (c) 2024 ZhengPeng), which credits Photoroom's fast-foreground-estimation (that repository declares no license). The algorithm was re-implemented from its description; no code was copied.

### Pretendard license

```
Copyright (c) 2021, Kil Hyung-jin (https://github.com/orioncactus/pretendard),
with Reserved Font Name Pretendard.

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://scripts.sil.org/OFL


-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded, 
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```
