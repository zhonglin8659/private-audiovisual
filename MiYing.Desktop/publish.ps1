# 密影视频套件 · 发布脚本
# 产物：
#   dist\MiYing.Encryptor\            文件夹版（加密工具）
#   dist\MiYing.Player\               文件夹版（播放器）
#   dist\single\MiYing.Encryptor.exe  单文件版（双击即用，首次启动自动解压）
#   dist\single\MiYing.Player.exe     单文件版（双击即用，首次启动自动解压）
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
New-Item -ItemType Directory -Force (Join-Path $dist 'single') | Out-Null

Write-Host '[1/4] 文件夹版 · 加密工具 ...' -ForegroundColor Cyan
dotnet publish 'src\MiYing.Encryptor\MiYing.Encryptor.csproj' -c Release -r win-x64 --self-contained true -m:1 -nr:false -o (Join-Path $dist 'MiYing.Encryptor')

Write-Host '[2/4] 文件夹版 · 播放器 ...' -ForegroundColor Cyan
dotnet publish 'src\MiYing.Player\MiYing.Player.csproj' -c Release -r win-x64 --self-contained true -m:1 -nr:false -o (Join-Path $dist 'MiYing.Player')

$singleFlags = @(
  '-p:PublishSingleFile=true',
  '-p:IncludeNativeLibrariesForSelfExtract=true',
  '-p:IncludeAllContentForSelfExtract=true',
  '-p:EnableCompressionInSingleFile=true',
  '-p:DebugType=None',
  '-p:DebugSymbols=false'
)

Write-Host '[3/4] 单文件版 · 加密工具 ...' -ForegroundColor Cyan
dotnet publish 'src\MiYing.Encryptor\MiYing.Encryptor.csproj' -c Release -r win-x64 --self-contained true -m:1 -nr:false @singleFlags -o (Join-Path $dist 'single')

Write-Host '[4/4] 单文件版 · 播放器 ...' -ForegroundColor Cyan
dotnet publish 'src\MiYing.Player\MiYing.Player.csproj' -c Release -r win-x64 --self-contained true -m:1 -nr:false @singleFlags -o (Join-Path $dist 'single')

Write-Host ''
Write-Host '发布完成：' -ForegroundColor Green
Write-Host ('  文件夹版：' + (Join-Path $dist 'MiYing.Encryptor\MiYing.Encryptor.exe'))
Write-Host ('  文件夹版：' + (Join-Path $dist 'MiYing.Player\MiYing.Player.exe'))
Write-Host ('  单文件版：' + (Join-Path $dist 'single\MiYing.Encryptor.exe'))
Write-Host ('  单文件版：' + (Join-Path $dist 'single\MiYing.Player.exe'))