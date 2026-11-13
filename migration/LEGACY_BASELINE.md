# 旧版基线冻结清单

> 基线路径：`C:/Users/mili/milimoe/FunGame.Legacy/OshimaGameModule`
> 用途：行为等价对照的只读参考，禁止在对照过程中升级依赖或修改源码。
> 生成：`python migration/gen_baseline.py`（读取旧版目录，只写本目录下的两个文件）。
> 复核：`migration/legacy-baseline-files.tsv` 逐文件列出路径、大小、行数与 SHA-256。

## 1. 指纹

| 项 | 值 |
|---|---|
| 纳入指纹的文件数 | 366 |
| `.cs` 文件数 | 356 |
| `.cs` 总行数 | 51186 |
| **聚合 SHA-256** | `5ab118ad55dc1483903b27d61d1b3e105ac3d58ff73aa3a2448db171387ad605` |

聚合算法：

```text
entries = []
for path in 按相对路径排序的 {*.cs, *.csproj, *.sln, *.json, *.bat}:
    entries.append(f'{相对路径}:{文件SHA256}\n')
聚合哈希 = SHA256(entries 拼接)
```

跳过目录：`bin` `obj` `.vs` `.git` `node_modules`。任一纳入文件变化都会改变聚合指纹；指纹变化时所有 fixture 的 `legacyBaseline` 必须同步更新，否则说明基线已经漂移。

## 2. 关键行为文件

| 文件 | 行数 | 大小 (KB) | SHA-256（前 16 位） |
|---|---:|---:|---|
| `OshimaServers/Service/FunGameService.cs` | 5541 | 258.0 | `12c7c5d51f974666` |
| `OshimaServers/Service/FunGameOrderList.cs` | 212 | 18.1 | `f60b14b2efb4fa7e` |
| `OshimaWebAPI/Services/RainBOTService.cs` | 3659 | 171.3 | `bbd0992330d61900` |
| `OshimaWebAPI/Controllers/FunGameController.cs` | 9856 | 471.5 | `8650dfc383b8be31` |
| `OshimaWebAPI/Services/CSBettingInputHandler.cs` | 681 | 34.6 | `e83165143fc03025` |
| `OshimaServers/Model/QQBot.cs` | 686 | 22.5 | `da2791245830070e` |
| `OshimaModules/Items/Collectible/幸运物.cs` | 349 | 18.1 | `9658bbcedf4a4b54` |

## 3. 依赖版本

| 包 | 版本 |
|---|---|
| FunGame.Core | 2.1.1 |
| FunGame.SQLQueryExtension | 2.0.0 |
| Rebex.Elliptic.Ed25519 | 1.2.2 |

## 4. 引擎源码（可选复核）

> 源码路径：`C:/Users/mili/milimoe/FunGame.Legacy/FunGame.Core`

| 项 | 值 |
|---|---|
| 版本 | `2.1.1` |
| 提交 | `7ecb11f` |

引擎包本身没有源码可执行，但这份源码与 NuGet 上的同版本一致，可以用来核对 `Character` / `Item` 这类「派生值」的公式（升级后属性、装备加成等）。

### 版本变更记录

| 日期 | 变更 | 原因 |
|---|---|---|
| 2026-11-14 | 旧版五个 `csproj` 的 `FunGame.Core` 从 2.1.0 升到 **2.1.1** | 与本地源码（tag 2.1.1 / `7ecb11f`「补齐核心属性百分比传导」）对齐；旧指纹 `f1061043…` 随之作废，所有 fixture 的 `legacyBaseline` 已同步 |

## 5. 文件清单

共 366 个文件，完整清单见 `legacy-baseline-files.tsv`（列：`path` `bytes` `lines` `sha256`）。
