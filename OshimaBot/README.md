# OshimaBot WebAPI

基于 FunGame.Core v3 与 OshimaBot.Content 的独立 ASP.NET Core 游戏服务器，不依赖 FunGameServer、OshimaServers 或旧 WebAPI 插件。

## 项目结构

- `OshimaBot`：WebAPI、RainBot、QQ Bot 与后台消息队列。
- `OshimaBot.Game`：共享的 `FunGameService`、模拟器和初始化逻辑。
- `OshimaBot.Content`：角色、技能、物品内容包。

## 启动

在解决方案根目录执行：

```powershell
dotnet run --project OshimaBot/OshimaBot.csproj
```

默认地址为 `http://localhost:5090`，开发环境 OpenAPI 文档位于 `/openapi/v1.json`。也可在解决方案目录构建全部项目：

```powershell
dotnet build OshimaBot.slnx
```

## 首轮接口

保留旧版核心路由：

- `GET /FunGame/test`、`/FunGame/testdebug`：混战或团队模拟，支持种子、地图、初始等级等参数。
- `GET /FunGame/last?full=false`：最近模拟日志。
- `GET /FunGame/characterinfo`、`skillinfo`、`skillinfoname`、`iteminfo`、`iteminfoname`。
- `GET /FunGame/stats`、`teamstats`、`winraterank`、`ratingrank`。
- `GET /FunGame/newmagiccard`、`newmagiccardpack`。
- `POST /QQBot`：QQ Webhook；`POST /QQBot/thirdparty`：第三方机器人消息。

新增 `GET /FunGame/status`、`characters`、`skills`、`items` 用于服务状态和目录查询。

## 配置

QQ 外部发送默认关闭。通过用户机密、环境变量或部署配置设置，不要提交真实凭据：

```powershell
dotnet user-secrets set "QQBot:Enabled" "true" --project OshimaBot/OshimaBot.csproj
dotnet user-secrets set "QQBot:AppId" "<app-id>" --project OshimaBot/OshimaBot.csproj
dotnet user-secrets set "QQBot:Secret" "<secret>" --project OshimaBot/OshimaBot.csproj
dotnet user-secrets set "Server:ApiKey" "<third-party-api-key>" --project OshimaBot/OshimaBot.csproj
```

第三方接口使用 `X-API-Key: <key>` 或 `Authorization: Bearer <key>`。未配置 `Server:ApiKey` 时，第三方接口返回 503。

RainBot 首轮支持帮助、角色/技能/物品查询、魔法卡预览、模拟战、统计和排行。存档、库存、交易、社团、活动等依赖旧数据库与 FunGameServer 的模块不在本轮范围内。
