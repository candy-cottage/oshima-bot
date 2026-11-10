# Legacy Golden Fixture

行为等价迁移（M0）的样本目录。每个 `.json` 是一个可执行的数值断言 + 一份待回填的旧版期望值。

## 双栏设计

```json
"expected": {
  "current": { "expForLevel": [100, 283] },   // 当前实现的实际值，测试直接断言
  "legacy":  null                              // 旧版值，null = 尚未从旧版提取
}
```

- `current` 由 `dotnet test` 强制校验。**改动数值时必须同步更新**，否则测试失败。
- `legacy` 为 `null` 时只做结构校验；回填后应改 `status` 为 `legacy-verified`，
  或在不一致时改 `intentional-divergence` 并在矩阵里写明原因。
- 本组测试全绿 **只代表「当前值没被无意改动」**，不代表「已与旧版等价」。

## 字段

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | 是 | `FIX-<域>-<功能>`，全局唯一 |
| `behaviorId` | 是 | 对应 `docs/MIGRATION_MATRIX.md` 的行为 ID |
| `command` | 是 | 旧版帮助里的命令名 |
| `probe` | 是 | 计算探针，必须是 `LegacyFixtureProbes.KnownProbes` 之一 |
| `status` | 是 | `current-locked` / `legacy-verified` / `intentional-divergence` |
| `input` | 是 | 传给探针的参数，无参数写 `{}` |
| `expected.current` | 是 | 当前实现输出，结构必须与探针返回一致 |
| `expected.legacy` | 否 | 旧版输出；`null` 表示待补 |
| `source.file` | 是 | 旧实现位置，`相对路径:行号` |
| `source.method` | 否 | 旧方法名或分支说明 |
| `source.note` | 否 | 已知差异、待确认点 |
| `legacyBaseline` | 是 | 旧版源码聚合指纹，见 `docs/2026-11-10/LEGACY_BASELINE.md` |
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
4. 旧版值到位后回填 `legacy`，把 `status` 升级为 `legacy-verified`。
5. 旧版行为有缺陷且决定不复制时，改 `intentional-divergence` 并写进矩阵的差异列。
