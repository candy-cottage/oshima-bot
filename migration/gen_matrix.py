"""Render migration/MIGRATION_MATRIX.md from migration/matrix.tsv and validate it.

The matrix has exactly one editable source: matrix.tsv. This script never rewrites
the mapping data — it only renders and checks, so a re-run can never regress rows.

Usage (from the repo root or from migration/):
    python migration/gen_matrix.py            # validate + render
    python migration/gen_matrix.py --check    # validate only (CI)

Exit code is non-zero when validation fails, so CI can use --check as a gate.
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent

MATRIX_TSV = HERE / "matrix.tsv"
EXTERNAL_TSV = HERE / "matrix_external.tsv"
OUT = HERE / "MIGRATION_MATRIX.md"
DIVERGENCES = HERE / "DIVERGENCES.md"
FIXTURE_DIR = REPO / "OshimaBot.Tests" / "Fixtures" / "Legacy"

LEGACY_ROOT = Path(os.environ.get(
    "OSHIMA_LEGACY_ROOT",
    r"C:/Users/mili/milimoe/FunGame.Legacy/OshimaGameModule"))
ORDER_LIST = LEGACY_ROOT / "OshimaServers/Service/FunGameOrderList.cs"

COLUMNS = ["id", "groups", "command", "legacy_src", "entry", "impl",
           "level", "status", "diff", "fixture"]

GROUP_ORDER = ["存档", "角色", "物品", "战斗", "玩法", "社团", "活动", "商店"]
GROUP_PREFIX = {g: p for g, p in zip(
    GROUP_ORDER, ["ARC", "CHR", "ITM", "BAT", "PLY", "CLB", "ACT", "SHO"])}
HELP_DICT_LABEL = {
    "ArchiveHelp": "存档", "CharacterHelp": "角色", "ItemHelp": "物品",
    "BattleHelp": "战斗", "PlayHelp": "玩法", "ClubHelp": "社团",
    "ActivityHelp": "活动", "StoreHelp": "商店",
}

STATUS_ORDER = ["等价", "增强", "简化替代", "部分缺失", "缺失", "有意差异", "scope-excluded"]
STATUS_NEEDS_LEVEL = {"等价", "增强", "简化替代", "部分缺失"}
LEVELS = {"L0", "L1", "L2", "L3", "—"}

PLANNED_PREFIX = "待建 "


def parse_legacy_help() -> "collections.OrderedDict[str, list[str]] | None":
    """Return {command: [groups...]} in legacy help order, or None when the legacy tree is absent.

    CI runners do not have the legacy checkout, so a missing baseline only disables the
    command-set comparison; the structural checks below still run.
    """
    if not ORDER_LIST.exists():
        print(f"警告：未找到旧版帮助文件 {ORDER_LIST}，跳过命令集合与顺序比对"
              "（结构校验仍然执行）。设置 OSHIMA_LEGACY_ROOT 可启用完整校验。", file=sys.stderr)
        return None

    text = ORDER_LIST.read_text(encoding="utf-8-sig")
    entries: "collections.OrderedDict[str, list[str]]" = collections.OrderedDict()
    current = None
    for line in text.splitlines():
        m = re.search(r"public static Dictionary<string, string> (\w+) \{ get; \} = new\(\) \{", line)
        if m:
            current = HELP_DICT_LABEL.get(m.group(1))
            continue
        if current and line.strip() in ("};", "}"):
            current = None
            continue
        if current and ".CreateCmdInput()" in line:
            for cmd in re.findall(r'"([^"\r\n]+)"\s*\.CreateCmdInput\(\)', line):
                groups = entries.setdefault(cmd, [])
                if current not in groups:
                    groups.append(current)
    return entries


def read_tsv(path: Path, columns: list[str]) -> list[dict[str, str]]:
    lines = path.read_text(encoding="utf-8").splitlines()
    header = lines[0].split("\t")
    if header != columns:
        raise SystemExit(f"{path.name} 表头不符：{header}，期望 {columns}")
    rows = []
    for index, line in enumerate(lines[1:], 2):
        if not line.strip():
            continue
        cells = line.split("\t")
        if len(cells) != len(columns):
            raise SystemExit(f"{path.name}:{index} 有 {len(cells)} 列，期望 {len(columns)}")
        rows.append(dict(zip(columns, (c.strip() for c in cells))))
    return rows


def existing_fixtures() -> set[str]:
    if not FIXTURE_DIR.exists():
        return set()
    return {p.stem for p in FIXTURE_DIR.glob("*.json")}


def validate(rows: list[dict[str, str]],
             legacy: "collections.OrderedDict[str, list[str]] | None") -> list[str]:
    errors: list[str] = []
    fixtures = existing_fixtures()
    divergence_ids = set(re.findall(r"^#+ (DIV-\d{3})", DIVERGENCES.read_text(encoding="utf-8"), re.M)) \
        if DIVERGENCES.exists() else set()

    if legacy is not None and len(rows) != len(legacy):
        errors.append(f"行数 {len(rows)} 与旧版唯一入口 {len(legacy)} 不一致")

    seen: set[str] = set()
    for row in rows:
        rid, cmd = row["id"], row["command"]

        if cmd in seen:
            errors.append(f"{rid}: 命令重复 {cmd}")
        seen.add(cmd)

        if legacy is not None:
            if cmd not in legacy:
                errors.append(f"{rid}: 命令 {cmd} 不在旧版帮助中")
            else:
                declared = [g.strip() for g in row["groups"].split(" / ")]
                unknown = [g for g in declared if g not in legacy[cmd]]
                if unknown:
                    errors.append(f"{rid}: 帮助分类 {unknown} 不属于 {cmd}（旧版为 {legacy[cmd]}）")

        if not re.fullmatch(r"[A-Z]{3}-\d{3}", rid):
            errors.append(f"{rid}: 行为 ID 格式应为 XXX-000")
            continue

        if row["groups"]:
            first = row["groups"].split(" / ")[0]
            prefix = GROUP_PREFIX.get(first)
            if prefix and not rid.startswith(prefix + "-"):
                errors.append(f"{rid}: 前缀与首个分类 {first} 不符（应为 {prefix}-）")

        level, status = row["level"], row["status"]
        if level not in LEVELS:
            errors.append(f"{rid}: 未知等价等级 {level}")
        if status not in STATUS_ORDER:
            errors.append(f"{rid}: 未知状态 {status}")
            continue
        if status in STATUS_NEEDS_LEVEL and level == "—":
            errors.append(f"{rid}: 状态 {status} 必须给出 L0–L3 等级")
        if status in {"缺失", "scope-excluded"} and level != "—":
            errors.append(f"{rid}: 状态 {status} 应使用 — 作为等级（当前 {level}）")

        fixture = row["fixture"]
        if fixture.startswith(PLANNED_PREFIX):
            planned = fixture[len(PLANNED_PREFIX):].strip()
            if not planned.startswith("FIX-"):
                errors.append(f"{rid}: 待建夹具名应为 FIX-*，当前 {planned}")
            elif planned in fixtures:
                errors.append(f"{rid}: {planned} 已存在，去掉「{PLANNED_PREFIX.strip()}」前缀")
        elif fixture and fixture != "—" and fixture.startswith("FIX-"):
            if fixture not in fixtures:
                errors.append(f"{rid}: 引用不存在的夹具 {fixture}（未建成请写「{PLANNED_PREFIX.strip()} {fixture}」）")

        for ref in re.findall(r"DIV-\d{3}", row["diff"]):
            if ref not in divergence_ids:
                errors.append(f"{rid}: 引用了不存在的差异登记 {ref}（DIVERGENCES.md 里没有该条）")

    if legacy is not None:
        missing = [c for c in legacy if c not in seen]
        if missing:
            errors.append(f"缺少旧版命令：{', '.join(missing)}")

        # 顺序漂移会让行为 ID 与历史记录错位，只在命令集合完全一致时检查
        if not missing and len(rows) == len(legacy):
            expected = [c for c in legacy if c in seen]
            actual = [r["command"] for r in rows]
            if expected != actual:
                first = next(i for i, (a, b) in enumerate(zip(expected, actual)) if a != b)
                errors.append(f"行顺序偏离旧版帮助顺序（第 {first + 1} 行：应为 {expected[first]}，实际 {actual[first]}）")

    return errors


def coverage(rows: list[dict[str, str]]) -> dict[str, int]:
    fixtures = existing_fixtures()
    scoped = [r for r in rows if r["status"] == "scope-excluded"]
    approved = [r for r in rows if r["status"] != "scope-excluded"]
    proven = [r for r in approved if r["fixture"].startswith("FIX-") and r["fixture"] in fixtures]
    planned = [r for r in approved if r["fixture"].startswith(PLANNED_PREFIX)]
    return {
        "total": len(rows),
        "scoped": len(scoped),
        "approved": len(approved),
        "equivalent": len([r for r in approved if r["status"] in ("等价", "增强")]),
        "proven": len(proven),
        "planned": len(planned),
    }


def render(rows: list[dict[str, str]], external: list[dict[str, str]]) -> str:
    stat = collections.Counter(r["status"] for r in rows)
    by_group: dict[str, collections.Counter[str]] = collections.defaultdict(collections.Counter)
    for r in rows:
        by_group[r["groups"].split(" / ")[0]][r["status"]] += 1
    cov = coverage(rows)
    total = cov["total"]

    lines = [
        "# OshimaBot 旧版帮助条目迁移矩阵",
        "",
        "> 数据源：`migration/matrix.tsv`（163 行，人工维护的唯一位置）。",
        "> 渲染与校验：`python migration/gen_matrix.py`；CI 用 `--check` 只校验不写文件。",
        "> 旧版基线：`migration/LEGACY_BASELINE.md`（`FunGame.Core 2.1` / `OshimaGameModule`）。",
        "> 计划与差距分析：本地 `docs/2026-11-13/MIGRATION_PLAN.md`、`docs/2026-11-13/MIGRATION_GAP_ANALYSIS.md`（不入库）。",
        "> 本文件由脚本生成，**不要手工编辑**；改数据请改 `matrix.tsv`。",
        "",
        "## 0. 口径说明",
        "",
        "| 指标 | 数值 | 说明 |",
        "|---|---:|---|",
        "| 帮助条目（字典 key 行） | 176 | 3 / 29 / 24 / 17 / 33 / 32 / 6 / 32 |",
        "| 帮助内命令名 | 181 | 一行含两个命令的条目拆开计 |",
        f"| **去重后唯一入口（本表行数）** | **{total}** | 同名命令在多个分类重复出现 |",
        f"| 批准迁移范围 | {cov['approved']} | 不含 `scope-excluded` |",
        f"| 范围排除 | {cov['scoped']} | 生命之泉 / 酒馆 / 加物 / 减物 |",
        f"| 有夹具落地的行 | {cov['proven']} | Fixture 列指向真实存在的 `.json` |",
        f"| 只有待建夹具的行 | {cov['planned']} | 已声明 L0–L2 但尚无对照样本 |",
        "",
        "状态定义：",
        "",
        "| 状态 | 含义 | 等级要求 |",
        "|---|---|---|",
        "| `等价` | 入口、状态与数值都与旧版一致 | 必须 L0–L3 |",
        "| `增强` | 保留旧行为，另有新架构增益 | 必须 L0–L3 |",
        "| `简化替代` | 做得到，但入口或交互与旧版不同 | 必须 L0–L3 |",
        "| `部分缺失` | 主体可用、部分子能力未做 | 必须 L0–L3 |",
        "| `缺失` | 旧版有、新版没有入口 | — |",
        "| `有意差异` | 已实现，但按产品决策与旧版不同，见 `DIVERGENCES.md` | 可标已对拍的部分 |",
        "| `scope-excluded` | 正式不迁移（不计入批准范围） | — |",
        "",
        "`Fixture` 列：指向存在的夹具表示已有对照样本；`待建 FIX-…` 表示该行声明了等价等级"
        "但尚无夹具，属于**未证明的等价声明**，`gen_matrix.py --check` 会在夹具建成后要求去掉前缀。",
        "",
        "## 1. 总览",
        "",
        "| 状态 | 条目数 | 占全量 | 占批准范围 |",
        "|---|---:|---:|---:|",
    ]
    for status in STATUS_ORDER:
        count = stat.get(status, 0)
        if not count:
            continue
        approved_share = "—" if status == "scope-excluded" else f"{count / cov['approved'] * 100:.1f}%"
        lines.append(f"| {status} | {count} | {count / total * 100:.1f}% | {approved_share} |")

    lines += ["", "| 分类 | 条目 | 等价/增强 | 简化替代 | 部分缺失 | 缺失 | 有意差异 | scope-excluded |",
              "|---|---:|---:|---:|---:|---:|---:|---:|"]
    for group in GROUP_ORDER:
        d = by_group.get(group, collections.Counter())
        lines.append(
            f"| {group} | {sum(d.values())} | {d.get('等价', 0) + d.get('增强', 0)} | "
            f"{d.get('简化替代', 0)} | {d.get('部分缺失', 0)} | {d.get('缺失', 0)} | "
            f"{d.get('有意差异', 0)} | {d.get('scope-excluded', 0)} |")

    lines += [
        "",
        "## 2. 矩阵",
        "",
        "| 行为 ID | 帮助分类 | 旧入口 | 旧实现 | 当前入口 | 当前实现 | 等价 | 状态 | 差异 / 备注 | Fixture |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for row in rows:
        cells = [row[c].replace("|", "\\|") for c in COLUMNS]
        lines.append("| " + " | ".join(cells) + " |")

    lines += [
        "",
        "## 3. 矩阵外功能（旧版无帮助条目）",
        "",
        "| 行为 ID | 功能 | 当前入口 | 状态 | 说明 |",
        "|---|---|---|---|---|",
    ]
    for row in external:
        cells = [row[c].replace("|", "\\|") for c in ("id", "name", "entry", "status", "note")]
        lines.append("| " + " | ".join(cells) + " |")

    lines += [
        "",
        f"合计矩阵内 {total} 条 + 矩阵外 {len(external)} 条。",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="只校验，不写文件")
    args = parser.parse_args()

    rows = read_tsv(MATRIX_TSV, COLUMNS)
    external = read_tsv(EXTERNAL_TSV, ["id", "name", "entry", "status", "note"])
    legacy = parse_legacy_help()

    errors = validate(rows, legacy)
    if errors:
        print(f"矩阵校验失败，共 {len(errors)} 项：", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    cov = coverage(rows)
    print(f"矩阵校验通过：{cov['total']} 行，批准范围 {cov['approved']}，"
          f"范围排除 {cov['scoped']}，有夹具 {cov['proven']}，待建夹具 {cov['planned']}")
    print("状态分布：" + json.dumps(
        dict(collections.Counter(r["status"] for r in rows)), ensure_ascii=False))

    if args.check:
        return 0

    OUT.write_text(render(rows, external), encoding="utf-8", newline="\n")
    print(f"已写入 {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
