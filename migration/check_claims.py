"""核对矩阵/差异表里的「证据引用」是否还成立。

三类引用必须能对上真实代码，否则文档会悄悄变成空头支票：

1. `SomeTests.Method` / `SomeTests.Prefix*` —— 测试类与方法要真的存在；
2. 「当前入口」列里的 `/命令` —— 必须是 CommandCatalog 里注册过的 key / name / alias
   （固定商店走 `StoreCommand(...)` 批量注册，要单独认）；
3. `DIV-0xx` —— 由 `gen_matrix.py --check` 负责，这里不重复；
4. 夹具探针引用的常量必须是**活常量**——除声明外还有引用。恒为 0 又没人读的「费用常量」
   会让夹具锁住一个改了也不生效的数字（`TrainCostCredits` / `BossCostCredits` 就是这么漏掉的）；
5. `DIVERGENCES.md` 里提到的 `FIX-xxx` 必须有对应的 .json，且「待建」只能标给还没建的；
6. 冻结种子回归样本（`Fixtures/Seeded/regression.json`）必须存在、指纹与内容自洽、
   且真的被测试读（`SeededRegressionTests`）。CI 不编译 C#，冻结样本的真伪只能在这里守；
7. 模拟的开局摘要（`Fixtures/Seeded/simulation.json`）必须是完整 SHA-256，且区间字段没写反。

这是「代码与描述一致性」的机械部分。语义部分（「旧版 6 槽、新版 3 槽」这类事实断言）
机器判断不了，只能人工复核；但至少不会再出现「引用的测试早就改名了」「入口早就删了」。
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
TESTS_DIR = ROOT / "OshimaBot.Tests"
CATALOG = ROOT / "OshimaBot" / "Commands" / "CommandCatalog.cs"
MATRIX = HERE / "matrix.tsv"
DIVERGENCES = HERE / "DIVERGENCES.md"
FIXTURES = TESTS_DIR / "Fixtures" / "Legacy"
EXTERNAL = HERE / "matrix_external.tsv"
PROBE = TESTS_DIR / "LegacyFixture.cs"
SEEDED_FIXTURE = TESTS_DIR / "Fixtures" / "Seeded" / "regression.json"
SEEDED_TESTS = TESTS_DIR / "SeededRegressionTests.cs"
SIMULATION_FIXTURE = TESTS_DIR / "Fixtures" / "Seeded" / "simulation.json"
SIMULATION_TESTS = TESTS_DIR / "SimulationSeedTests.cs"
PRODUCTION = [ROOT / "OshimaBot", ROOT / "OshimaBot.Game"]

# 冻结样本至少覆盖：抽卡 4 项、签到 2 项、探索 2 项、秘境、市场 2 项、兑换、分解、
# 锻造 2 项、赛马、三张消耗表、运势、商店日切点、十连端到端。
MIN_SEEDED_PROBES = 20

TEST_REF = re.compile(r"\b(\w+Tests)\.([\w*]+)")
COMMAND_REF = re.compile(r"/([^\s|·（）()`,]+)")


def load_tests() -> dict[str, set[str]]:
    classes: dict[str, set[str]] = {}
    for path in TESTS_DIR.glob("*.cs"):
        text = path.read_text(encoding="utf-8")
        named = re.findall(r"class (\w+Tests)\b", text)
        for name in named:
            classes.setdefault(name, set())
        for match in re.finditer(
            r"(?:public|internal)\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|void)\s+(\w+)\s*\(", text
        ):
            for name in named:
                classes[name].add(match.group(1))
    return classes


def load_commands() -> set[str]:
    text = CATALOG.read_text(encoding="utf-8")

    keys = set(re.findall(r'"([a-z0-9-]+)"\s*=>\s*new DelegateCommand', text))
    keys |= set(re.findall(r'"([a-z0-9-]+)"\s*=>\s*StoreCommand', text))
    names = set(re.findall(r'name:\s*"([^"]+)"', text))

    aliases: set[str] = set()
    for match in re.finditer(r"aliases:\s*\[([^\]]*)\]", text):
        aliases.update(re.findall(r'"([^"]+)"', match.group(1)))

    # 固定商店：StoreCommand("key", "名字", ["别名"...], ...)
    store = re.compile(
        r'StoreCommand\(\s*"[a-z0-9-]+"\s*,\s*"([^"]+)"\s*,\s*\[([^\]]*)\]'
    )
    for match in store.finditer(text):
        names.add(match.group(1))
        aliases.update(re.findall(r'"([^"]+)"', match.group(2)))

    return keys | names | aliases


def production_sources() -> dict[Path, str]:
    files: dict[Path, str] = {}
    for base in PRODUCTION:
        for path in base.rglob("*.cs"):
            if any(part in {"obj", "bin"} for part in path.parts):
                continue
            files[path] = path.read_text(encoding="utf-8")
    return files


def dead_probe_constants() -> list[str]:
    """探针读到的常量里，除声明外没有任何引用的一律算死常量。

    判据：`Holder.Name` 在业务代码里 0 次，且 `Name` 在 Holder 自己的文件里不超过 1 次
    （1 次就是那行声明）。允许「只在定义文件的其它函数里用」——ForgeRules.Needy 属于这种，
    它被 Plan 用，而 Plan 本身被 ForgeService 调。
    """
    probe = PROBE.read_text(encoding="utf-8")
    sources = production_sources()

    holders: dict[str, Path] = {}
    for path, text in sources.items():
        for match in re.finditer(r"(?:static\s+)?(?:sealed\s+)?class\s+(\w+)", text):
            holders.setdefault(match.group(1), path)

    problems: list[str] = []
    seen: set[tuple[str, str]] = set()
    for holder, name in re.findall(r"\b(\w+)\.(\w+)\b", probe):
        if holder not in holders or (holder, name) in seen:
            continue
        seen.add((holder, name))
        if not re.search(rf"\b{name}\b", holders[holder].read_text(encoding="utf-8")):
            continue                      # 不是该类的成员（可能是局部变量/类型名），跳过
        qualified = sum(text.count(f"{holder}.{name}") for text in sources.values())
        in_def = len(re.findall(rf"\b{name}\b", holders[holder].read_text(encoding="utf-8")))
        if qualified == 0 and in_def <= 1:
            problems.append(f"{holder}.{name} 只声明、无人引用（夹具锁了个不生效的值）")
    return problems


def dangling_fixtures() -> list[str]:
    """差异登记里引用的夹具必须真的存在；已建成的不能再写「待建」。"""
    if not DIVERGENCES.exists():
        return []
    existing = {p.stem for p in FIXTURES.glob("*.json")}
    problems: list[str] = []
    for number, line in enumerate(DIVERGENCES.read_text(encoding="utf-8").splitlines(), 1):
        for name in re.findall(r"FIX-[A-Z0-9\-]+", line):
            if name not in existing:
                problems.append(f"DIVERGENCES.md:{number} 引用了不存在的夹具 {name}")
            elif "待建" in line:
                problems.append(f"DIVERGENCES.md:{number} 的 {name} 已建成，却还标着「待建」")
    return problems


def seeded_fixture() -> list[str]:
    """冻结种子回归样本：文件在、指纹自洽、探针够多、并且真的被测试读。

    指纹算法必须和 C# 侧 `SeedScenario.Canonicalize` + `SeededRegressionTests.Digest` 一致：
    按 Ordinal 排序的 `id\\t值` 用 `\\n` 连接，再取 SHA-256 小写十六进制。
    """
    if not SEEDED_FIXTURE.exists():
        return [f"缺少冻结种子样本 {SEEDED_FIXTURE.relative_to(ROOT)}（同种子回归要它才算数）"]

    problems: list[str] = []
    try:
        data = json.loads(SEEDED_FIXTURE.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        return [f"{SEEDED_FIXTURE.name} 不是合法 JSON：{error}"]

    probes = data.get("Probes") or {}
    if len(probes) < MIN_SEEDED_PROBES:
        problems.append(f"{SEEDED_FIXTURE.name} 只有 {len(probes)} 条探针，少于预期的 {MIN_SEEDED_PROBES} 条")

    body = "\n".join(f"{key}\t{probes[key]}" for key in sorted(probes))
    digest = hashlib.sha256(body.encode("utf-8")).hexdigest()
    if digest != data.get("Digest"):
        problems.append(
            f"{SEEDED_FIXTURE.name} 的 digest 与内容不符（被手工改过，或改了没重新生成）"
            "；重新生成：OSHIMA_UPDATE_SEEDS=1 dotnet test --filter SeededRegression")

    if not SEEDED_TESTS.exists():
        problems.append("缺少 SeededRegressionTests.cs，冻结样本没人比对")
    else:
        text = SEEDED_TESTS.read_text(encoding="utf-8")
        if "OSHIMA_UPDATE_SEEDS" not in text:
            problems.append("SeededRegressionTests.cs 里没有重新生成的开关说明（OSHIMA_UPDATE_SEEDS）")
        if "SeedScenario" not in text:
            problems.append("SeededRegressionTests.cs 没有引用 SeedScenario（样本与场景脱钩了）")

    return problems


def simulation_fixture() -> list[str]:
    """模拟的冻结样本：开局摘要像样、区间没写反、并且真的被测试读。

    整局逐字复现做不到（局内决策在引擎侧），所以这里只冻开局摘要 + 结构性区间；
    门禁能查的是「文件还在、摘要格式对、区间不是空转」，语义部分靠测试本身。
    """
    if not SIMULATION_FIXTURE.exists():
        return [f"缺少模拟冻结样本 {SIMULATION_FIXTURE.relative_to(ROOT)}"]

    try:
        data = json.loads(SIMULATION_FIXTURE.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        return [f"{SIMULATION_FIXTURE.name} 不是合法 JSON：{error}"]

    problems: list[str] = []
    digest = str(data.get("OpeningDigest") or "")
    if not re.fullmatch(r"[0-9a-f]{64}", digest):
        problems.append(f"{SIMULATION_FIXTURE.name} 的 OpeningDigest 不是 64 位小写十六进制"
                        "；重新冻结：OSHIMA_UPDATE_SEEDS=1 dotnet test --filter SimulationSeed")

    for low, high in (("KillsMin", "KillsMax"), ("RoundsMin", "RoundsMax")):
        if not isinstance(data.get(low), int) or not isinstance(data.get(high), int):
            problems.append(f"{SIMULATION_FIXTURE.name} 缺少 {low}/{high}")
        elif data[low] > data[high]:
            problems.append(f"{SIMULATION_FIXTURE.name} 的 {low} > {high}（区间写反了）")

    if not SIMULATION_TESTS.exists():
        problems.append("缺少 SimulationSeedTests.cs，模拟冻结样本没人比对")
    else:
        text = SIMULATION_TESTS.read_text(encoding="utf-8")
        if "OSHIMA_UPDATE_SEEDS" not in text:
            problems.append("SimulationSeedTests.cs 里没有重新冻结的开关说明（OSHIMA_UPDATE_SEEDS）")

    return problems


def rows():
    header, *body = MATRIX.read_text(encoding="utf-8").splitlines()
    columns = header.split("\t")
    for line in body:
        if line.strip():
            yield dict(zip(columns, line.split("\t")))

    header, *body = EXTERNAL.read_text(encoding="utf-8").splitlines()
    for line in body:
        if not line.strip():
            continue
        parts = line.split("\t")
        yield {"id": parts[0], "entry": parts[2], "diff": parts[4]}


def main() -> int:
    tests = load_tests()
    commands = load_commands()
    errors: list[str] = []
    count = 0

    for row in rows():
        count += 1
        rid = row["id"]
        note = row.get("diff") or ""
        entry = (row.get("entry") or "").replace("`", "").strip()

        for cls, method in TEST_REF.findall(note):
            if cls not in tests:
                errors.append(f"{rid}: 引用了不存在的测试类 {cls}")
                continue
            if method == "*" or method.endswith("*"):
                prefix = method.rstrip("*")
                if prefix and not any(x.startswith(prefix) for x in tests[cls]):
                    errors.append(f"{rid}: {cls}.{method} 没有任何匹配的方法")
                continue
            if method not in tests[cls]:
                errors.append(f"{rid}: {cls}.{method} 不存在")

        if entry and entry != "—":
            for command in COMMAND_REF.findall(entry):
                if "/" in command:      # /QQBot/bind 这类 HTTP 路径，不是指令
                    continue
                if command not in commands:
                    errors.append(f"{rid}: 入口 /{command} 不在 CommandCatalog 里")

    errors.extend(dead_probe_constants())
    errors.extend(dangling_fixtures())
    errors.extend(seeded_fixture())
    errors.extend(simulation_fixture())

    if errors:
        print(f"引用核对失败，共 {len(errors)} 项：")
        for error in errors:
            print("  -", error)
        return 1

    print(f"引用核对通过：{count} 行，测试类 {len(tests)} 个，可触发入口 {len(commands)} 种")
    return 0


if __name__ == "__main__":
    sys.exit(main())
