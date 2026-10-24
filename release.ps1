# Zest 릴리스 패키지: artifacts\release\Zest-<버전>-win-x64.zip 과 SHA256SUMS.txt 를 만든다.
# 사용법: ./release.ps1   (필요: .NET 10 SDK)
# build.ps1과 달리 self-contained로 배포하므로, 받는 사람은 .NET을 따로 설치하지 않아도 된다.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# 버전은 src/Zest.App/Zest.App.csproj의 <Version>에서 읽는다(여기에 다시 적지 않는다).
$version = (dotnet msbuild src/Zest.App -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) { throw "프로젝트에서 버전을 읽지 못했습니다 (종료 코드 $LASTEXITCODE)" }

$name = "Zest-$version-win-x64"
$release = Join-Path $PSScriptRoot 'artifacts\release'
$folder = Join-Path $release $name
$zip = Join-Path $release "$name.zip"
$sums = Join-Path $release 'SHA256SUMS.txt'

# 이전 결과물만 지운다(같은 폴더의 RELEASE_NOTES.md 등은 그대로 둔다).
New-Item -ItemType Directory -Force $release | Out-Null
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
foreach ($old in $zip, $sums) { if (Test-Path $old) { Remove-Item $old -Force } }

# Windows ML(Microsoft.Windows.AI.MachineLearning)은 원래 앱 폴더에 함께 들어가는 방식이라
# Windows App SDK 런타임이나 WindowsAppSDKSelfContained 설정 없이 onnxruntime.dll 등이 폴더에 들어간다.
dotnet publish src/Zest.App -c Release -r win-x64 --self-contained true -o $folder -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (종료 코드 $LASTEXITCODE)" }

# 쓰지 않는 파일 정리(build.ps1과 같은 이유).
# DirectML.dll: Zest는 DirectML을 요청하지 않는다(docs/decisions.md Task 16). Microsoft는 Windows ML
# self-contained 배포에서 이 파일 제거를 "공식 지원"하지 않는다고 밝히므로, 배포 후 단계에서만 지운다.
Get-ChildItem $folder -Recurse -Include 'DirectML.dll', '*.pdb' | Remove-Item -Force
# PDFsharp 패키지가 함께 넣는 보조 어셈블리: Zest는 문서 정보 편집만 하므로 쓰지 않는다
# (docs/decisions.md F8). PdfSharp.WPFonts.dll은 Segoe WP 글꼴 데이터(© Microsoft)라 배포하지 않는다.
Get-ChildItem $folder -Recurse -Include 'PdfSharp.WPFonts.*', 'PdfSharp.Snippets.*', 'PdfSharp.Quality.*', 'PdfSharp.Charting.*', 'PdfSharp.BarCodes.*' |
    Remove-Item -Force

# 배경 제거에 필요한 Windows ML 파일이 빠졌으면 여기서 멈춘다(앱은 onnxruntime.dll이 없으면 배경 제거를 숨긴다).
foreach ($required in 'Zest.exe', 'onnxruntime.dll', 'Microsoft.Windows.AI.MachineLearning.dll', 'Microsoft.ML.OnnxRuntime.dll') {
    if (-not (Test-Path (Join-Path $folder $required))) { throw "패키지에 $required 이(가) 없습니다" }
}

# zip 안에는 $name 폴더가 들어가고, 그 안에 Zest.exe가 있다.
# ZipFile.CreateFromDirectory는 Windows PowerShell 5.1에서 항목 경로에 '\'를 써서(zip 규격은 '/')
# 일부 압축 프로그램이 잘못 푸므로, 항목을 하나씩 '/' 경로로 넣는다.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $folder -Recurse -File) {
        $entry = $name + '/' + $file.FullName.Substring($folder.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $archive.Dispose() }

# SHA256SUMS.txt: "<소문자 해시>  <파일 이름>" (sha256sum 형식, BOM 없음, LF).
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText($sums, "$hash  $name.zip`n", (New-Object System.Text.UTF8Encoding $false))

$files = Get-ChildItem $folder -Recurse -File
$folderMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "완료: $folder ($($files.Count)개 파일, $folderMb MB)"
Write-Host "zip: $zip ($zipMb MB)"
Write-Host "SHA256: $hash"
