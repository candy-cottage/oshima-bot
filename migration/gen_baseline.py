"""Freeze the legacy baseline: file inventory, aggregate hash, dependency versions.

Read-only with respect to the legacy tree. Writes:
    migration/LEGACY_BASELINE.md         summary + key files + dependency versions
    migration/legacy-baseline-files.tsv  full inventory (path, size, lines, sha256)

Run it whenever the baseline path or the legacy tree changes, then commit both files.
The aggregate hash is what every fixture records as `legacyBaseline`.
"""
from __future__ import annotations

import hashlib
import os
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent

LEGACY = Path(os.environ.get(
    "OSHIMA_LEGACY_ROOT",
    r"C:/Users/mili/milimoe/FunGame.Legacy/OshimaGameModule"))
OUT_MD = HERE / "LEGACY_BASELINE.md"
OUT_TSV = HERE / "legacy-baseline-files.tsv"

SKIP = {"bin", "obj", ".vs", ".git", "node_modules"}
CODE_SUFFIXES = {".cs", ".csproj", ".sln", ".json", ".bat"}

KEY_FILES = [
    "OshimaServers/Service/FunGameService.cs",
    "OshimaServers/Service/FunGameOrderList.cs",
    "OshimaWebAPI/Services/RainBOTService.cs",
    "OshimaWebAPI/Controllers/FunGameController.cs",
    "OshimaWebAPI/Services/CSBettingInputHandler.cs",
    "OshimaServers/Model/QQBot.cs",
    "OshimaModules/Items/Collectible/幸运物.cs",
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> int:
    if not LEGACY.exists():
        raise SystemExit(f"找不到旧版基线：{LEGACY}\n设置 OSHIMA_LEGACY_ROOT 指向 OshimaGameModule。")

    files = [p for p in LEGACY.rglob("*")
             if p.is_file() and not any(part in SKIP for part in p.parts)]
    code = sorted(p for p in files if p.suffix in CODE_SUFFIXES)

    total_lines = 0
    inventory = []
    for path in code:
        rel = path.relative_to(LEGACY).as_posix()
        lines = 0
        if path.suffix == ".cs":
            lines = len(path.read_text(encoding="utf-8-sig", errors="replace").splitlines())
            total_lines += lines
        inventory.append((rel, path.stat().st_size, lines, sha256(path)))

    aggregate = hashlib.sha256()
    for rel, _, _, digest in inventory:
        aggregate.update(f"{rel}:{digest}\n".encode())
    fingerprint = aggregate.hexdigest()

    deps: dict[str, set[str]] = {}
    for csproj in sorted(LEGACY.rglob("*.csproj")):
        if any(part in SKIP for part in csproj.parts):
            continue
        text = csproj.read_text(encoding="utf-8-sig", errors="replace")
        for name, version in re.findall(r'Include="([^"]+)"\s+Version="([^"]+)"', text):
            deps.setdefault(name, set()).add(version)

    by_ext: dict[str, int] = {}
    for path in code:
        by_ext[path.suffix] = by_ext.get(path.suffix, 0) + 1

    lines_out = [
        "# 旧版基线冻结清单",
        "",
        f"> 基线路径：`{LEGACY.as_posix()}`",
        "> 用途：行为等价对照的只读参考，禁止在对照过程中升级依赖或修改源码。",
        "> 生成：`python migration/gen_baseline.py`（读取旧版目录，只写本目录下的两个文件）。",
        "> 复核：`migration/legacy-baseline-files.tsv` 逐文件列出路径、大小、行数与 SHA-256。",
        "",
        "## 1. 指纹",
        "",
        "| 项 | 值 |",
        "|---|---|",
        f"| 纳入指纹的文件数 | {len(code)} |",
        f"| `.cs` 文件数 | {by_ext.get('.cs', 0)} |",
        f"| `.cs` 总行数 | {total_lines} |",
        f"| **聚合 SHA-256** | `{fingerprint}` |",
        "",
        "聚合算法：",
        "",
        "```text",
        "entries = []",
        "for path in 按相对路径排序的 {*.cs, *.csproj, *.sln, *.json, *.bat}:",
        "    entries.append(f'{相对路径}:{文件SHA256}\\n')",
        "聚合哈希 = SHA256(entries 拼接)",
        "```",
        "",
        "跳过目录：`bin` `obj` `.vs` `.git` `node_modules`。任一纳入文件变化都会改变聚合指纹；"
        "指纹变化时所有 fixture 的 `legacyBaseline` 必须同步更新，否则说明基线已经漂移。",
        "",
        "## 2. 关键行为文件",
        "",
        "| 文件 | 行数 | 大小 (KB) | SHA-256（前 16 位） |",
        "|---|---:|---:|---|",
    ]
    index = {row[0]: row for row in inventory}
    for rel in KEY_FILES:
        if rel not in index:
            lines_out.append(f"| `{rel}` | — | — | 缺失 |")
            continue
        _, size, lines, digest = index[rel]
        lines_out.append(f"| `{rel}` | {lines} | {size / 1024:.1f} | `{digest[:16]}` |")

    lines_out += ["", "## 3. 依赖版本", "", "| 包 | 版本 |", "|---|---|"]
    for name in sorted(deps):
        lines_out.append(f"| {name} | {', '.join(sorted(deps[name]))} |")

    lines_out += [
        "",
        "## 4. 文件清单",
        "",
        f"共 {len(inventory)} 个文件，完整清单见 `legacy-baseline-files.tsv`"
        "（列：`path` `bytes` `lines` `sha256`）。",
        "",
    ]
    OUT_MD.write_text("\n".join(lines_out), encoding="utf-8", newline="\n")

    with OUT_TSV.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("path\tbytes\tlines\tsha256\n")
        for rel, size, lines_count, digest in inventory:
            handle.write(f"{rel}\t{size}\t{lines_count}\t{digest}\n")

    print(f"指纹 {fingerprint}")
    print(f"文件 {len(inventory)} 个，.cs {by_ext.get('.cs', 0)} 个，{total_lines} 行")
    print(f"已写入 {OUT_MD.relative_to(REPO)} 与 {OUT_TSV.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
