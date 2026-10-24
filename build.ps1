# Zest 배포 빌드: dist\Zest.exe 를 만든다.
# 사용법: ./build.ps1   (필요: .NET 10 SDK)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

dotnet publish src/Zest.App -c Release -r win-x64 --self-contained false -o dist -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (종료 코드 $LASTEXITCODE)" }

# 쓰지 않는 파일 정리.
# DirectML.dll: Zest는 DirectML을 요청하지 않는다(docs/decisions.md Task 16). Microsoft는 Windows ML
# self-contained 배포에서 이 파일 제거를 "공식 지원"하지 않는다고 밝히므로, 배포 후 단계에서만 지운다.
Get-ChildItem $dist -Recurse -Include 'DirectML.dll', '*.pdb' | Remove-Item -Force
# PDFsharp 패키지가 함께 넣는 보조 어셈블리: Zest는 문서 정보 편집만 하므로 쓰지 않는다
# (dist에서 이 파일들 없이 PDF 메타데이터 편집·읽기가 되는 것을 확인함, docs/decisions.md F8).
# PdfSharp.WPFonts.dll은 Segoe WP 글꼴 데이터(© Microsoft)라 배포하지 않는다.
Get-ChildItem $dist -Recurse -Include 'PdfSharp.WPFonts.*', 'PdfSharp.Snippets.*', 'PdfSharp.Quality.*', 'PdfSharp.Charting.*', 'PdfSharp.BarCodes.*' |
    Remove-Item -Force

$files = Get-ChildItem $dist -Recurse -File
$mb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "완료: $dist ($($files.Count)개 파일, $mb MB)"
Write-Host "실행: dist\Zest.exe"
