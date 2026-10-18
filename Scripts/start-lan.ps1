param([int]$Port = 4110)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\LangKinhHuKhong.TTS.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw 'Chua cai .NET 9 SDK. Cai SDK tu https://dotnet.microsoft.com/download/dotnet/9.0'
}
Write-Host "Lăng Kính Hư Không TTS - http://localhost:$Port" -ForegroundColor Cyan
Write-Host "Các máy LAN: http://<IP-server>:$Port (cần mở firewall)" -ForegroundColor Yellow
dotnet run --project $project --urls "http://0.0.0.0:$Port"
