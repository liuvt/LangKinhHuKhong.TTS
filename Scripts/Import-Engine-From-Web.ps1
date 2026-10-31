param(
    [Parameter(Mandatory=$true)][string]$OldProjectFolder,
    [string]$Destination
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $root 'LangKinhHuKhong.TTS' }
if (-not (Test-Path $Destination)) { New-Item -ItemType Directory -Path $Destination -Force | Out-Null }
foreach ($folder in 'Runtime','Voices') {
    $source = Join-Path $OldProjectFolder $folder
    $target = Join-Path $Destination $folder
    if (-not (Test-Path $source)) { Write-Warning "Khong thay thu muc $source"; continue }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
    Write-Host "Copied $folder to $target" -ForegroundColor Green
}
Write-Host 'Hoan tat copy engine/model. Kiem tra quyen su dung model ONNX.' -ForegroundColor Cyan
