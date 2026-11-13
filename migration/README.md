# migration/

旧版行为等价迁移的**可验证资产**。这里的文件入库，`docs/` 下的策划资料不入库。

## 文件

| 文件 | 是否生成 | 作用 |
|---|---|---|
| `matrix.tsv` | 人工维护 | **唯一数据源**：163 条旧版唯一入口的映射、等级、状态、夹具引用 |
| `matrix_external.tsv` | 人工维护 | 矩阵外功能（旧版帮助没有条目：模拟、绑定、服务器状态等） |
| `MIGRATION_MATRIX.md` | `gen_matrix.py` 生成 | 给人看的矩阵；不要手工改 |
| `DIVERGENCES.md` | 人工维护 | 有意差异与范围排除决策表；矩阵用 `DIV-0xx` 引用 |
| `SIMPLIFICATIONS.md` | `gen_simplifications.py` 生成 | 47 行「简化替代」的归类与逐行差异 |
| `LEGACY_BASELINE.md` | `gen_baseline.py` 生成 | 基线指纹、关键文件、依赖版本 |
| `legacy-baseline-files.tsv` | `gen_baseline.py` 生成 | 366 个文件的逐项哈希，用于复核指纹 |
| `gen_matrix.py` | 脚本 | 渲染矩阵 + 校验（`--check` 只校验） |
| `gen_baseline.py` | 脚本 | 重算基线指纹（只读旧版目录） |
| `gen_simplifications.py` | 脚本 | 从 `matrix.tsv` 汇总简化替代清单 |

## 常用命令

```powershell
# 校验矩阵（CI 用的就是这个）
python migration/gen_matrix.py --check

# 改完 matrix.tsv 后重新渲染
python migration/gen_matrix.py

# 重算旧版基线指纹
python migration/gen_baseline.py

# 重新汇总「简化替代」清单（matrix.tsv 改过就跑）
python migration/gen_simplifications.py
```

旧版目录默认是 `C:/Users/mili/milimoe/FunGame.Legacy/OshimaGameModule`，
引擎源码默认是 `C:/Users/mili/milimoe/FunGame.Legacy/FunGame.Core`（只用于核对派生值公式，
基线指纹不包含它）。其他机器上分别设置 `OSHIMA_LEGACY_ROOT` / `OSHIMA_CORE_SOURCE` 即可。找不到旧版目录时，`gen_matrix.py`
会跳过命令集合与顺序比对并给出警告，其余结构校验照常执行（CI 依赖这个降级行为）。

## 校验规则

`gen_matrix.py --check` 会拒绝以下情况，任何一条都让 CI 失败：

1. 行数与旧版唯一入口数不一致，或出现重复/多余/缺失命令。
2. 行顺序偏离旧版帮助顺序（顺序变了，历史记录里的行为 ID 就对不上）。
3. 行为 ID 不是 `XXX-000`，或前缀与首个帮助分类不符。
4. 状态不在枚举内；`等价`/`增强`/`简化替代`/`部分缺失` 没给 L0–L3 等级；
   `缺失`/`scope-excluded` 却给了等级。
5. `Fixture` 列直接写 `FIX-xxx` 但夹具不存在 —— 未建成的必须写 `待建 FIX-xxx`。
6. 差异列引用了 `DIVERGENCES.md` 里不存在的 `DIV-xxx`。

## 与 Fixture 的关系

`OshimaBot.Tests/Fixtures/Legacy/*.json` 每个样本都记 `behaviorId` 指回这里的行，
状态契约（`current-locked` / `legacy-verified` / `intentional-divergence`）见
`OshimaBot.Tests/Fixtures/Legacy/README.md`。

矩阵的 `Fixture` 列有三种取值：

- 指向真实 `.json`：有对照样本，`LegacyFixtureTests` 会跑它；
- `待建 FIX-…`：声明了等价等级但还没有样本（**当前为 0**，`gen_matrix.py` 会把条数打进输出）；
- `—`：这一行没有数值可锁，行为由单元测试覆盖，具体的测试类与方法写在「差异 / 备注」列。

`—` 不是「没验证」，而是「验证方式不是数值夹具」。改动这些行时请顺手把备注里的测试名保持有效。
