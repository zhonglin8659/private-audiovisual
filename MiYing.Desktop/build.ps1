# 密影视频套件 · 构建脚本
# 说明：本机 .NET SDK 缺少部分 Workload 解析器目录，解决方案级并行构建会静默失败，
#       因此统一使用 -m:1 -nr:false（单节点构建、禁用 MSBuild 节点复用）。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host '== 构建解决方案 ==' -ForegroundColor Cyan
dotnet build MiYingVideo.sln -c Debug -m:1 -nr:false

Write-Host '== 运行单元测试 ==' -ForegroundColor Cyan
dotnet test 'tests\MiYing.Core.Tests\MiYing.Core.Tests.csproj' -c Debug --no-build -m:1 -nr:false

Write-Host '构建与测试完成。' -ForegroundColor Green