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
| **聚合 SHA-256** | `f1061043d6b576dae882cad36824ddbc65d6fb2989c69cb6f82022c43bbc0593` |

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
| FunGame.Core | 2.1.0 |
| FunGame.SQLQueryExtension | 2.0.0 |
| Rebex.Elliptic.Ed25519 | 1.2.2 |

## 4. 文件清单

共 366 个文件，完整清单见 `legacy-baseline-files.tsv`（列：`path` `bytes` `lines` `sha256`）。
