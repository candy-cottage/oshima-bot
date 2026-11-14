# 运维手册：备份与还原

> 目标：能在 10 分钟内把数据库恢复到某个已知好状态，并且**演练过一次**。
> 数据库是 SQLite（默认 `oshimobot.db`，启动时开 WAL）。

## 一、备份

### 1. 服务运行中（推荐）

管理员在群里发：

```
/备份数据库
```

实现是 `VACUUM INTO`（`BackupService`），不是复制文件——WAL 模式下直接 `cp` 可能拿到只写了一半的页。
`VACUUM INTO` 是事务性读，服务跑着也能得到一致快照。

- 目录：`Database:BackupDirectory`，默认程序目录下的 `backups/`
- 文件名：`oshimobot-yyyyMMdd-HHmmss-fff.db`（带毫秒，连打两次不会撞名）
- 保留：最近 **10** 份，超了自动删最旧的
- 回复里会给出文件名、目录、大小和当前保留份数

### 2. 服务没起来 / 不能用指令

```bash
# 有 sqlite3 命令行时
sqlite3 oshimobot.db "VACUUM INTO 'backups/manual-$(date +%Y%m%d-%H%M%S).db'"
```

没有 `sqlite3` 时：**先停服务**，再把 `oshimobot.db`、`oshimobot.db-wal`、`oshimobot.db-shm`
三个文件一起复制走。只拷主文件会丢最近的事务。

## 二、还原

还原必须在服务停止时做，否则会被正在跑的连接覆盖。

```powershell
# 1) 停服务（按你的部署方式：systemd / 计划任务 / 窗口）
# 2) 备份"还原前"的状态，万一还原错了还能切回来
Copy-Item oshimobot.db "backups/before-restore-$(Get-Date -Format yyyyMMdd-HHmmss).db"
# 3) 清掉 WAL 边车文件（它们属于旧库，留着会让新库读到不一致的页）
Remove-Item oshimobot.db-wal, oshimobot.db-shm -ErrorAction SilentlyContinue
# 4) 覆盖
Copy-Item backups/oshimobot-20261114-220000-000.db oshimobot.db
# 5) 启动服务
```

`ops/restore-db.ps1` 把 2–4 步包好了，用法：

```powershell
pwsh ops/restore-db.ps1 -Backup backups/oshimobot-20261114-220000-000.db -Database oshimobot.db
```

脚本会先检查服务是否还在跑（同名进程存在就拒绝执行），并把当前库复制成
`before-restore-*.db` 再覆盖。

## 三、还原后的抽查清单

| 项 | 怎么做 | 期望 |
|---|---|---|
| 库能打开 | 启动日志出现「数据库迁移完成」 | 无异常 |
| 玩家数据在 | 该玩家发 `/状态` | 金币/角色数与备份时刻一致 |
| 账本连续 | 查 `LedgerEntries` 最新一条的 `BalanceAfter` | 与钱包余额相等 |
| 挂单不丢 | `/市场` | 备份时刻的在售挂单都在 |
| 幂等表没炸 | 正常发一条指令 | 有回复，不报「重复事件」 |

## 四、演练记录（每次上线前做一次）

| 日期 | 备份文件 | 还原耗时 | 抽查结果 | 执行人 |
|---|---|---|---|---|
| 待填 | | | | |

> 没有演练过的备份方案等于没有备份。第一次演练建议在测试库上做：
> 造 3 个玩家 + 1 笔成交 → 备份 → 再改点数据 → 还原 → 核对余额与挂单。
