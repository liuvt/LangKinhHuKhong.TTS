param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'LangKinhHuKhong.TTS\LangKinhHuKhong.TTS.csproj'
$publish = Join-Path $root "Publish\$Runtime"

Write-Host '=== LangKinhHuKhong.TTS Studio v1.2 - ElevenLabs-Style Workstation ===' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'Chua co .NET 9 SDK. Cai tu https://dotnet.microsoft.com/download/dotnet/9.0, sau do chay lai.'
}
$version = & dotnet --version
Write-Host "SDK hien tai: $version" -ForegroundColor Green
$major = [int]($version.Split('.')[0])
if ($major -lt 9) { throw 'Can .NET SDK 9 hoac moi hon.' }

# Stop running instance if locked
Get-Process -Name 'LangKinhHuKhong.TTS' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

# Remove old binary files in Publish\$Runtime while preserving any existing Runtime, Voices, Output content
if (-not (Test-Path $publish)) {
    New-Item -ItemType Directory -Path $publish -Force | Out-Null
} else {
    Get-ChildItem -Path $publish -Force | Where-Object {
        $_.Name -notin @('Runtime', 'Voices', 'Output')
    } | Remove-Item -Recurse -Force
}

$standalone = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish $project -c Release -r $Runtime --self-contained $standalone `
  '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' `
  '-p:EnableCompressionInSingleFile=true' '-p:DebugType=none' '-p:DebugSymbols=false' `
  -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }

$exe = Join-Path $publish 'LangKinhHuKhong.TTS.exe'
if (-not (Test-Path $exe)) { throw "Khong tim thay file: $exe" }

$dnaSource = Join-Path $root 'DNA_LANG_KINH_HU_KHONG_TTS.md'
if (Test-Path $dnaSource) {
    Copy-Item -Path $dnaSource -Destination (Join-Path $publish 'DNA_LANG_KINH_HU_KHONG_TTS.md') -Force
}

# Create the 3 required folders (Runtime, Voices, Output) + subfolders in Publish\$Runtime
$pubRuntime = Join-Path $publish 'Runtime'
$pubVoices  = Join-Path $publish 'Voices'
$pubOutput  = Join-Path $publish 'Output'

foreach ($dir in @(
    $pubRuntime,
    (Join-Path $pubRuntime 'piper'),
    (Join-Path $pubRuntime 'ffmpeg'),
    $pubVoices,
    (Join-Path $pubVoices 'example'),
    $pubOutput
)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

# Copy any existing Runtime or Voices from workspace root or project folder into Publish if present
foreach ($folderName in @('Runtime', 'Voices')) {
    foreach ($candidateRoot in @($root, (Join-Path $root 'LangKinhHuKhong.TTS'))) {
        $src = Join-Path $candidateRoot $folderName
        $dst = Join-Path $publish $folderName
        if ((Test-Path $src) -and ($src -ne $dst)) {
            $items = Get-ChildItem -Path $src -Force -ErrorAction SilentlyContinue
            if ($items) {
                Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force
            }
        }
    }
}

# Update settings.json so the published app points directly to Publish\<Runtime>\Output, Voices, Runtime
$appDataDir = Join-Path $env:LOCALAPPDATA 'LangKinhHuKhong\TTS-Premium-v1.1'
New-Item -ItemType Directory -Path $appDataDir -Force | Out-Null
$settingsPath = Join-Path $appDataDir 'settings.json'
$settingsObj = [ordered]@{
    OutputFolder      = $pubOutput
    VoicesFolder      = $pubVoices
    RuntimeFolder     = $pubRuntime
    LastBaseDirectory = ($publish + '\')
    Speed             = 1.0
    LastVoicePath     = ''
    LastCategory      = '*'
    LastLanguage      = '*'
    ReviewWordLimit   = 50
}
if (Test-Path $settingsPath) {
    try {
        $existing = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($existing.Speed) { $settingsObj.Speed = $existing.Speed }
        if ($existing.ReviewWordLimit) { $settingsObj.ReviewWordLimit = $existing.ReviewWordLimit }
        if ($existing.SampleText) { $settingsObj['SampleText'] = $existing.SampleText }
    } catch { }
}
$jsonText = $settingsObj | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($settingsPath, $jsonText, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host "XONG EXE   : $exe" -ForegroundColor Green
Write-Host " -> Runtime: $pubRuntime" -ForegroundColor Cyan
Write-Host " -> Voices : $pubVoices (kem thu muc example)" -ForegroundColor Cyan
Write-Host " -> Output : $pubOutput" -ForegroundColor Cyan
