param(
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'LangKinhHuKhong.TTS\LangKinhHuKhong.TTS.csproj'
$publish = Join-Path $root "Publish\$Runtime"
Write-Host '=== Lang Kinh Hu Khong TTS Studio v1.1.0 - Premium UI vang do trang + MP3 + SRT ===' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'Chua co .NET 9 SDK. Cai tu https://dotnet.microsoft.com/download/dotnet/9.0, sau do chay lai.'
}
$version = & dotnet --version
Write-Host "SDK hien tai: $version" -ForegroundColor Green
Write-Host "Danh sach SDK cai dat:" -ForegroundColor Cyan
& dotnet --list-sdks
$major = [int]($version.Split('.')[0])
if ($major -lt 9) { throw 'Can .NET SDK 9 hoac moi hon.' }
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
$standalone = if ($FrameworkDependent) { 'false' } else { 'true' }
& dotnet publish $project -c Release -r $Runtime --self-contained $standalone `
  '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' `
  '-p:EnableCompressionInSingleFile=true' '-p:DebugType=none' '-p:DebugSymbols=false' `
  -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }
$exe = Join-Path $publish 'LangKinhHuKhong.TTS.exe'
if (-not (Test-Path $exe)) { throw "Khong tim thay file: $exe" }
Write-Host ''
Write-Host "XONG: $exe" -ForegroundColor Green
Write-Host 'Copy Runtime\piper, Runtime\ffmpeg va Voices (neu chua co) vao thu muc Publish.' -ForegroundColor Yellow
Write-Host 'Co the chon duong dan Runtime/Voices co san o muc Cai dat.'
