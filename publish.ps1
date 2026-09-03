param(
    [switch]$KeepIntermediate
)

$ErrorActionPreference = "Stop"

# 构建产物路径先解析出来，避免中间清理由当前目录变化造成误删。
$root = (Get-Location).Path
$intermediateDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"

# 若应用正在运行，先停止，避免 exe 被文件锁占用。
Stop-Process -Name MyTodo -Force -ErrorAction SilentlyContinue

# 使用仓库本地 NuGet/CLI 缓存，不污染用户全局目录。
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-home"
$env:NUGET_PACKAGES = Join-Path $root ".nuget-packages"
$env:APPDATA = Join-Path $root ".dotnet-home"

dotnet publish MyTodo -c Release -r win-x64 --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $intermediateDir

$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
& $iscc (Join-Path $root "MyTodo.iss")

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出码：$LASTEXITCODE"
}

if (-not $KeepIntermediate -and (Test-Path -LiteralPath $intermediateDir)) {
    Remove-Item -LiteralPath $intermediateDir -Recurse -Force
}

$installer = Get-ChildItem -LiteralPath $distDir -Filter "MyTodo-*-setup.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Write-Host ""
Write-Host "安装包：$($installer.FullName)"
