<#
.SYNOPSIS
    用一份备份文件还原 OshimaBot 的 SQLite 数据库。

.DESCRIPTION
    步骤：确认服务已停 → 把当前库另存为 before-restore-*.db → 删掉 -wal/-shm → 覆盖。
    WAL 边车文件属于旧库，不删会读到不一致的页。

.PARAMETER Backup
    要还原的备份文件（/备份数据库 生成的那种，或手工 VACUUM INTO 出来的）。

.PARAMETER Database
    目标数据库文件，默认当前目录的 oshimobot.db。

.PARAMETER ProcessName
    服务进程名，默认 OshimaBot。存在同名进程时脚本会拒绝执行。

.EXAMPLE
    pwsh ops/restore-db.ps1 -Backup backups/oshimobot-20261114-220000-000.db
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Backup,
    [string]$Database = "oshimobot.db",
    [string]$ProcessName = "OshimaBot"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Backup)) {
    throw "找不到备份文件：$Backup"
}
$backupItem = Get-Item -LiteralPath $Backup
if ($backupItem.Length -eq 0) {
    throw "备份文件是空的：$Backup"
}

$running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if ($running) {
    throw "服务还在运行（PID $($running.Id -join ', ')）。先停服务再还原，否则会被正在跑的连接覆盖。"
}

$target = if (Test-Path -LiteralPath $Database) { Resolve-Path -LiteralPath $Database } else { $null }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$directory = if ($target) { Split-Path -Parent $target } else { (Get-Location).Path }
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}

if ($target) {
    $keep = Join-Path $directory "before-restore-$stamp.db"
    Copy-Item -LiteralPath $target -Destination $keep
    Write-Host "当前库已另存：$keep"
}

foreach ($sidecar in @("$Database-wal", "$Database-shm")) {
    if (Test-Path -LiteralPath $sidecar) {
        Remove-Item -LiteralPath $sidecar -Force
        Write-Host "已删除边车文件：$sidecar"
    }
}

Copy-Item -LiteralPath $Backup -Destination $Database -Force
Write-Host "已还原：$Backup → $Database"
Write-Host ""
Write-Host "接下来：启动服务 → 按 ops/README.md 的抽查清单核对（余额、账本、挂单、幂等）。"
