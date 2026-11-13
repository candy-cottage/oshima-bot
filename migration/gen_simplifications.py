"""把矩阵里 status=简化替代 的行汇总成 SIMPLIFICATIONS.md。

数据源是 `matrix.tsv`（与 MIGRATION_MATRIX.md 同源），所以矩阵改了这里重跑即可，
不要手工编辑生成的文档。
"""

from __future__ import annotations

import collections
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "matrix.tsv"
OUT = HERE / "SIMPLIFICATIONS.md"
STATUS = "简化替代"

# 人工归纳的差异模式。机械统计只能给出「哪几行」，归因还是得读代码后写。
PATTERNS: list[tuple[str, str, str]] = [
    ("A. 角色不再有持久状态",
     "CHR-006 / CHR-007 / CHR-008 / CHR-026",
     "旧版角色有生命、能量（EP）与复活次数，练级会回血、结算会复活；新版角色每场战斗满状态进场，"
     "没有持久 HP/EP，因此「状态页」「练级预览」里的这些字段整体消失。"),
    ("B. 操作对象从「角色序号」收敛到「主战 / 编成」",
     "CHR-002 / CHR-009 / CHR-022~027 / ITM-004",
     "旧版几乎所有角色操作都要先传角色序号（装备、看明细、改小队），新版把养成集中在主战角色上："
     "`/装备 @编号` 只装主战，小队用「位置号」而不是角色序号。槽位本身已经对齐 Core 的 6 个。"),
    ("C. 多入口合并成一个列表",
     "CHR-001 / CHR-004 / CHR-015 / ITM-001~003 / ITM-005~007 / SHO-028",
     "旧版按用途拆成多条命令（简略/详细/分类/按类型/单品详情/搜索2…），新版统一 `/背包`、`/我的角色`、"
     "`/市场` 一条入口 + 分页 + `@编号` 引用。信息没丢，但「一条命令直达某个视图」的捷径没有了。"),
    ("D. 物品指令的参数形态",
     "ITM-013 / ITM-017 / ITM-018",
     "旧版按「名称 + 次数 + 角色序号」批量操作，新版按 `@编号 [x数量]`；「强制分解」这种绕过保护的入口被移除，"
     "必须先 `/解锁`。"),
    ("E. 经济与战斗口径",
     "BAT-007 / PLY-011~017 / SHO-025 / SHO-029",
     "秘境奖励的**量级**已按旧版「每名出战角色各掷一次」对齐，差异只剩掉落品类（新版统一产出「<类型>秘境掉落」）"
     "与经验归属（新版记主战，旧版均分 + 溢出转经验书）；"
     "市场多了 5 单上限与双边 ledger；排行榜的默认榜从「养成榜」变成「金币榜」。"),
    ("G. 房间的「准备」位",
     "PLY-021 / PLY-023",
     "旧版房间有 ready 状态，全员准备才开局；新版由房主直接开局（赛马房任何人可开），没有准备位。"
     "**2026-11-14 已定：不补**，房间是一次性组队场景，准备位只是多一步点击。"),
]


def load() -> list[dict[str, str]]:
    lines = SOURCE.read_text(encoding="utf-8").splitlines()
    header = lines[0].split("\t")
    return [dict(zip(header, line.split("\t"))) for line in lines[1:] if line.strip()]


def main() -> None:
    rows = [r for r in load() if r["status"] == STATUS]
    by_group = collections.Counter(r["groups"] for r in rows)

    out: list[str] = []
    out.append("# 简化替代清单")
    out.append("")
    out.append("> 本文件由 `python migration/gen_simplifications.py` 从 `matrix.tsv` 生成，**不要手工编辑**。")
    out.append("")
    out.append(
        f"`简化替代` 的含义：**功能已经实现、能正常玩**，但与旧版的行为不完全一致，且这个不一致是被接受的取舍"
        f"（不是漏做，也不是 bug）。当前共 **{len(rows)} 行**。"
    )
    out.append("")
    out.append("判据：如果某行的差异其实是「旧版没有这个功能」，它应该标 `增强`；")
    out.append("如果是「旧版有这个功能、新版完全没有入口」，标 `缺失`；只有「做了、但做法不同」才落在这里。")
    out.append("")

    out.append("## 1. 按分类")
    out.append("")
    out.append("| 分类 | 行数 |")
    out.append("|---|---:|")
    for group, count in sorted(by_group.items(), key=lambda kv: (-kv[1], kv[0])):
        out.append(f"| {group} | {count} |")
    out.append("")

    out.append("## 2. 差异模式（人工归纳）")
    out.append("")
    out.append(f"这 {len(rows)} 行不是 {len(rows)} 个孤立差异，绝大多数能归到下面几类；同类之间改法一致，改一处就能一起推进。")
    out.append("")
    out.append("| 模式 | 涉及行 | 说明 |")
    out.append("|---|---|---|")
    for name, ids, note in PATTERNS:
        out.append(f"| {name} | {ids} | {note} |")
    out.append("")

    out.append("## 3. 逐行明细")
    out.append("")
    for group in sorted(by_group):
        out.append(f"### {group}")
        out.append("")
        out.append("| ID | 旧版命令 | 当前入口 | 差异 |")
        out.append("|---|---|---|---|")
        for row in rows:
            if row["groups"] != group:
                continue
            diff = row["diff"].replace("|", "\\|")
            out.append(f"| {row['id']} | {row['command']} | {row['entry']} | {diff} |")
        out.append("")

    out.append("## 4. 怎么用这份清单")
    out.append("")
    out.append("- 想「补一个旧版入口」时，先在这里查它是不是已被有意简化：如果是，补之前要先决定「改成旧版的形态」还是「接受简化」。")
    out.append("- 想「改行为」时，改完记得同步 `matrix.tsv` 的 `diff` 列并重跑 `gen_matrix.py` / 本脚本，否则两处会打架。")
    out.append("- 清单里没有的行，要么是 `等价`（有夹具或测试守着），要么是 `增强`（旧版没做）。")
    out.append("")

    OUT.write_text("\n".join(out), encoding="utf-8", newline="\n")
    print(f"written {OUT.name}: {len(rows)} rows, {len(by_group)} groups")


if __name__ == "__main__":
    main()
