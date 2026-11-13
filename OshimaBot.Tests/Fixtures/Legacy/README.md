# Legacy Golden Fixture

行为等价迁移（M0）的样本目录。每个 `.json` 是一个可执行的数值断言 + 一份旧版对照值 + 一条状态契约。

## 双栏设计

```json
"expected": {
  "current": { "expForLevel": [100, 283] },   // 当前实现的实际值，测试直接断言
  "legacy":  { "expForLevel": [100, 283] }    // 旧版值；null = 尚未从旧版提取
}
```

- `current` 由 `dotnet test` 强制校验。**改动数值时必须同步更新**，否则测试失败。
- `legacy` 用 `legacyMatchPolicy.fields` 指定的字段与 `current` 逐字段比较，不是整树 `DeepEquals`：
  旧版通常没有新版新增的字段（如 `maxListings`），整树比较会把「旧版没有」误报成「数值不同」。

## 状态契约

| `status` | 必填字段 | 测试行为 |
|---|---|---|
| `current-locked` | — | 只校验 `current`；旧值待确认，测试报告里列出待办 |
| `legacy-verified` | `legacy` + `legacyMatchPolicy` | 逐字段比较新旧值，任一字段不一致即失败 |
| `intentional-divergence` | `legacy` + `legacyMatchPolicy` + `divergence` | 申报为一致的字段必须一致；申报的差异字段必须**确实**不同，差异被抹平即失败 |

`legacyMatchPolicy`：

| 字段 | 说明 |
|---|---|
| `mode` | `exact`（默认）或 `tolerance`（浮点按绝对误差） |
| `fields` | 参与比较的顶层字段名，不能为空 |
| `tolerance` | `tolerance` 模式下的绝对误差，默认 `1e-6` |

`divergence`：

| 字段 | 说明 |
|---|---|
| `reason` | 旧行为、新行为与为什么这样取舍；不能为空 |
| `decidedBy` / `decidedOn` | 决策人与日期 |
| `fields` | 申报确实不等的字段；**任一侧缺失也算不等**。差异消失后测试会要求升级为 `legacy-verified` |

## 字段

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | 是 | `FIX-<域>-<功能>`，全局唯一 |
| `behaviorId` | 是 | 对应 `migration/MIGRATION_MATRIX.md` 的行为 ID |
| `command` | 是 | 旧版帮助里的命令名 |
| `probe` | 是 | 计算探针，必须是 `LegacyFixtureProbes.KnownProbes` 之一 |
| `status` | 是 | `current-locked` / `legacy-verified` / `intentional-divergence` |
| `input` | 是 | 传给探针的参数，无参数写 `{}` |
| `expected.current` | 是 | 当前实现输出，结构必须与探针返回一致 |
| `expected.legacy` | 见状态表 | 旧版输出 |
| `legacyMatchPolicy` | 见状态表 | 字段级比较策略 |
| `divergence` | 见状态表 | 有意差异登记 |
| `source.file` | 是 | 旧实现位置，`相对路径:行号` |
| `source.method` | 否 | 旧方法名或分支说明 |
| `source.note` | 否 | 已知差异、待确认点 |
| `legacyBaseline` | 是 | 旧版源码聚合指纹，见 `migration/LEGACY_BASELINE.md` |
| `catalogVersion` | 否 | 内容目录版本 |
| `seed` / `now` | 否 | 随机与时间固定的输入 |

## 探针

显式 switch 注册（`OshimaBot.Tests/LegacyFixture.cs`），不做反射扫描：

`levelup` · `break` · `train` · `register` · `gacha` · `forge` · `store` · `market-fee` · `boss` · `signin`

新增探针必须同时改 `KnownProbes` 与 `Run` 的 switch，两处缺一会在测试中失败。

## 新增流程

1. 在矩阵里确认行为 ID 与旧实现位置。
2. 写 fixture，`expected.current` 先填当前实现应得的值（手算或用探针跑一次核对）。
3. 跑 `dotnet test --filter FullyQualifiedName~LegacyFixture`。
4. 旧版值到位后回填 `legacy`，补 `legacyMatchPolicy`，`status` 升级为 `legacy-verified`。
5. 旧版行为有缺陷或不复制时，改 `intentional-divergence`，写 `divergence.reason` 与 `fields`，并同步矩阵。

> 注意：`current-locked` 只证明「当前值没被无意改动」，不证明「与旧版等价」。
