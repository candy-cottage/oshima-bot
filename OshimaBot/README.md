# OshimaBot

基于 **FunGame.Core v3** 的 QQ 机器人 MUD 服务端。由旧版 `OshimaGameModule`（Core v2 / FunGameServer v2）迁移而来，**不再依赖 FunGameServer、MySQL 或旧插件体系**，独立部署。

- 接入：**WebSocket 单模式**（开发与部署一致）；Webhook 按官方规范补齐验签 / ACK，但默认关闭。
- 存储：**SQLite + EF Core 10**，33 张业务表；用户存档、背包、经济流水、模拟统计全部入库，不再有 `saved/*.json`。
- 指令：**66 条**显式注册（零反射扫描），中文动词为主、旧版别名兼容，Markdown 4 段式回复 + 可点击指令 / 自定义按钮。

## 项目结构

| 项目 | 职责 |
|---|---|
| `OshimaBot` | ASP.NET Core WebAPI、QQ 事件管线、消息层、指令框架、玩法仓储、数据层 |
| `OshimaBot.Game` | `FunGameService` 内容目录、模拟器、运行态上下文、统计仓储接口 |
| `OshimaBot.Content` | 18 角色 / 118 技能 / 198 物品的内容定义 |
| `OshimaBot.Tests` | xunit 单测（128 条，含真实 SQLite 端到端） |

目录要点：`Commands/`（显式 `CommandCatalog` + Router）、`Messaging/`（QQ Markdown / 按钮 / 发送）、`Gameplay/`（规则、玩家存档、经济）、`Data/`（DbContext / Migration）、`Security/`（验签、身份、ApiKey）、`Services/Gateway/`（WebSocket 网关）。

> `OshimaBot.slnx` 以相对路径引用仓库外的 `../../FunGame/FunGame.Core/src/FunGame.Core.csproj`；换机器或公开仓库前需改为子模块 / 包引用。

## 快速开始

在解决方案根目录执行：

```powershell
dotnet build OshimaBot.slnx
dotnet test OshimaBot.Tests/OshimaBot.Tests.csproj
dotnet run --project OshimaBot/OshimaBot.csproj --urls http://127.0.0.1:5090
```

开发环境的 OpenAPI 文档位于 `/openapi/v1.json`；`GET /health` 返回模式、网关连接状态与最近错误。端口可用 `--urls` 或环境变量覆盖。

数据库默认 `Data Source=oshimobot.db`（相对 `OshimaBot/` 内容根），启动时自动应用 Migration 并开启 WAL / 外键。需要手动管理迁移时：

```powershell
dotnet tool install dotnet-ef --version 10.0.11 --tool-path .tools
./.tools/dotnet-ef.exe migrations add <Name> --project OshimaBot/OshimaBot.csproj
./.tools/dotnet-ef.exe database update --project OshimaBot/OshimaBot.csproj
```

## 配置

**不要把真实凭据提交进仓库**，用 `dotnet user-secrets` 或环境变量：

```powershell
dotnet user-secrets set "QQBot:Enabled" "true" --project OshimaBot/OshimaBot.csproj
dotnet user-secrets set "QQBot:AppId" "<app-id>" --project OshimaBot/OshimaBot.csproj
dotnet user-secrets set "QQBot:Secret" "<bot-secret>" --project OshimaBot/OshimaBot.csproj
dotnet user-secrets set "Server:ApiKey" "<third-party-api-key>" --project OshimaBot/OshimaBot.csproj
```

| 配置节 | 关键项 | 说明 |
|---|---|---|
| `QQBot` | `Enabled` / `Mode=WebSocket` / `Environment` | WS 仅在 `Enabled=true && Mode=WebSocket` 时启动；`Intents=100663296` = `GROUP_AND_C2C_EVENT \| INTERACTION` |
| | `EnableWebhook=false` | 打开后 `POST /QQBot` 才接受回调，并执行 Ed25519 入站验签 + 时间戳防重放 |
| | `GatewayUrl` / `ShardId` / `GatewayMaxReconnectDelaySeconds` | 留空 `GatewayUrl` 时走 `GET /gateway/bot` 自动发现；支持 Resume 与会话持久化 |
| `Database` | `ConnectionString` / `ApplyMigrationsOnStartup` / `ProcessedEventRetentionHours` | SQLite 单实例；事件幂等记录保留 24 小时 |
| `Server` | `ApiKey` | **仅首次启动引导**：把 SHA-256 写入 `api_keys` 后应从配置移除明文；认证只查库，密钥支持吊销 / 过期 / 分权 |
| | `BindCodeTtlSeconds` / `SuperAdminOpenIds` | 一次性绑定码 300 秒；超管白名单与 `user_roles` 双轨 |
| | `ArchiveSimulationRounds=false` | 默认不生成 `rounds_archive.zip`，复盘时再打开 |
| | `StoreFullSimulationLog=true` | 完整模拟日志写入 `simulation_runs.rounds_json`，重启后可恢复 `/上次的完整日志` |

`Server:ApiKey` 首次启动会为第三方通道懒建一个独立内部用户；每个 ApiKey 一个账号，需要多人共用时显式指定 `api_keys.user_id`。

## HTTP 接口

| 路由 | 说明 |
|---|---|
| `GET /`、`GET /health` | 服务信息与健康检查 |
| `POST /QQBot` | Webhook 回调（默认 404；开启后验签 + op13 验证 + op12 ACK + 事件分发） |
| `POST /QQBot/thirdparty` | 第三方消息（JSON body），需 `X-API-Key` 或 `Authorization: Bearer` |
| `POST /QQBot/thirdpartytest?msg=...&openID=...` | 联调用的简化入口，同样需 ApiKey |
| `POST /QQBot/bind` | 用 6 位一次性绑定码兑换外部身份，需 ApiKey |

第三方消息自报的 `openid` 只用于回显，身份一律以 ApiKey 的 `ClaimsPrincipal` 为准。

## 指令概览

语法 `/动词 [目标] [参数]`，`/` 可省略；`@编号` 按列表顺序稳定映射，`x2` 表示数量，`p2` 翻页。`/帮助` 由注册表自动生成（11 页）。

| 批次 | 代表指令 |
|---|---|
| 系统 / 查询 | `/帮助`、`/战斗帮助`、`/取消`、`/绑定`、`/查角色`、`/查技能`、`/查物品`、`/上次的日志` |
| 战斗模拟 | `/FunGame模拟`、`/混战模拟`、`/FunGame团队模拟`、`/查个人评分`、`/查团队胜率` |
| P1 存档 | `/状态`、`/观察`、`/签到`、`/每日任务`、`/领奖 @1`、`/抽卡`、`/十连抽卡`、`/背包`、`/使用 @1`、`/分解 @1`、`/装备 @1`、`/我的角色`、`/设主战 @1`、`/升级 @1`、`/突破 @1`、`/练级 @1`、`/小队` |
| P2 经济 | `/商店`、`/购买 @1`、`/市场`、`/上架 @1 200`、`/购买市场 @1`、`/报价列表`、`/报价 @1 300`、`/锻造 @1`、`/秘境 精英`、`/决斗`、`/Boss`、`/排行榜 战绩` |

所有货币变动与物品变动在**同一事务**完成并写 `ledger_entries`；市场 / 报价成交时买卖双方各记一笔，可对账。市场上架物品会进入托管状态，成交前不出现在任何玩家背包。

## 数据与持久化

- 首访懒建档：`users` / `user_identities` / `user_roles` + `player_states`；外部 OpenID 与内部 `users.id` 解耦，支持换绑。
- **旧版 MySQL 存档不导入**（数据已丢失、OpenID→uid 不可重建）；所有旅人从今天重新开始。
- 模拟统计与日志存 `simulation_statistics` / `simulation_runs`，启动时 `LoadAsync` 恢复，重启后 `/上次的日志` 与评分排行仍在；`rounds_archive.zip` 默认不再生成。
- 备份：停服后直接复制 `oshimobot.db`（含 WAL 时一并复制 `-wal` / `-shm`），或用 `VACUUM INTO` 生成一致性快照。

## 范围

玩法交付 **P0（查询 / 模拟）→ P1（存档 / 养成）→ P2（经济 / 战斗）**。P3（社团、赛马、赛事预测、探索地图、私信）按计划不在本轮范围，相关表未建。完整迁移计划与运维说明见 `docs/`（本地资料，不纳入版本控制）。
