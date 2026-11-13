rows = [l.split('\t') for l in open('migration/matrix.tsv', encoding='utf-8').read().splitlines()[1:]]
assert len(rows) == 163, len(rows)

pairs = []
for r in rows:
    legacy, entry = r[2], r[4]
    head = entry if entry and entry != '—' else ''
    pairs.append((legacy, head))

array = "\n".join(f'        ("{legacy}", "{entry}"),' for legacy, entry in pairs)

template = '''using Microsoft.Extensions.Logging.Abstractions;
using OshimaBot.Commands;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// L0 入口等价：旧版帮助里的 163 条命令，必须落到迁移矩阵声明的当前入口
/// （<c>migration/matrix.tsv</c> 的「当前入口」列），或者命中
/// <see cref="LegacyAliases.Redirects"/> 给出明确说明。
/// <para>数据来自矩阵；要加兼容请改 <c>LegacyAliases</c> 或矩阵，再重跑
/// <c>python migration/gen_legacy_entry_test.py</c>。</para>
/// </summary>
public class LegacyCommandEntryTests
{
    /// <summary>旧命令 → 矩阵声明的当前入口（空字符串表示矩阵里是 —，只应给说明）。</summary>
    private static readonly (string Legacy, string Entry)[] LegacyCommands =
    [
%%ARRAY%%
    ];

    private static CommandRegistry CreateRegistry()
    {
        CommandDependencies dependencies = new(
            null!, null!, new NullBindCodes(), new NullSessions(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            NullLogger.Instance);
        return CommandCatalog.Build(dependencies);
    }

    [Fact]
    public void LegacyCommands_ResolveOrExplain()
    {
        Assert.Equal(163, LegacyCommands.Length);
        CommandRegistry registry = CreateRegistry();

        List<string> unresolved = [];
        foreach ((string legacy, string _) in LegacyCommands)
        {
            if (registry.Resolve(legacy) is not null) continue;
            if (LegacyAliases.Explain(legacy) is not null) continue;
            unresolved.Add(legacy);
        }

        Assert.True(unresolved.Count == 0,
            $"以下旧命令既不能解析也没有说明（共 {unresolved.Count} 条）：\\n  " + string.Join("\\n  ", unresolved));
    }

    /// <summary>
    /// 能解析的旧命令必须落到矩阵声明的入口之一，避免被前缀匹配带偏；
    /// 已用 <see cref="LegacyAliases.Redirects"/> 给出说明的行（参数形态变了）也算通过。
    /// </summary>
    [Fact]
    public void LegacyCommands_LandOnTheDeclaredEntry()
    {
        CommandRegistry registry = CreateRegistry();
        List<string> wrong = [];

        foreach ((string legacy, string entry) in LegacyCommands)
        {
            CommandMatch? actual = registry.Resolve(legacy);
            string? explanation = LegacyAliases.Explain(legacy);

            if (entry.Length == 0)
            {
                if (actual is not null) wrong.Add($"{legacy} → 矩阵说没有入口，实际解析到 {actual.Command.Key}");
                else if (explanation is null) wrong.Add($"{legacy} → 矩阵说没有入口，也没有说明");
                continue;
            }

            // 矩阵的「当前入口」可能列多个（用 · 分隔），命中任一即算对齐
            string[] candidates = entry.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            List<string> keys = [];
            foreach (string candidate in candidates)
            {
                string head = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('/');
                CommandMatch? resolved = registry.Resolve(head);
                if (resolved is not null) keys.Add(resolved.Command.Key);
            }

            if (keys.Count == 0)
            {
                wrong.Add($"{legacy} → 矩阵声明的入口 /{entry} 一个都不存在");
                continue;
            }
            if (actual is not null && keys.Contains(actual.Command.Key, StringComparer.OrdinalIgnoreCase)) continue;
            if (explanation is not null) continue;   // 参数形态已变：给说明不算错

            wrong.Add(actual is null
                ? $"{legacy} → 解析失败（应落到 {string.Join(" / ", keys)}）"
                : $"{legacy} → 落到 {actual.Command.Key}，矩阵声明的是 {string.Join(" / ", keys)}");
        }

        Assert.True(wrong.Count == 0,
            $"以下旧命令落点与矩阵不一致（共 {wrong.Count} 条）：\\n  " + string.Join("\\n  ", wrong));
    }

    [Fact]
    public void LegacyRewrites_PointAtARealCommand()
    {
        CommandRegistry registry = CreateRegistry();
        List<string> broken = [];

        foreach ((string legacy, string target) in LegacyAliases.Rewrites)
        {
            if (registry.Resolve(target) is null) broken.Add($"{legacy} → {target}");
        }

        Assert.True(broken.Count == 0, "重写目标不存在：\\n  " + string.Join("\\n  ", broken));
    }

    [Fact]
    public void LegacyCommands_KeepTheirArguments()
    {
        CommandRegistry registry = CreateRegistry();

        // 重写只替换动词，参数原样接在后面：模拟锻造 @1 x40 → 锻造 模拟 @1 x40
        CommandMatch? match = registry.Resolve("模拟锻造 @1 x40");
        Assert.NotNull(match);
        Assert.Equal("forge", match!.Command.Key);
        Assert.Equal(["模拟", "@1", "x40"], match.Args);

        // 纯改名同样保留参数：市场出售 3 8000 → 上架 3 8000
        CommandMatch? renamed = registry.Resolve("市场出售 3 8000");
        Assert.NotNull(renamed);
        Assert.Equal(["3", "8000"], renamed!.Args);
    }

    [Fact]
    public void ScopeExcludedCommands_ExplainInsteadOfFailing()
    {
        foreach (string command in new[] { "生命之泉", "酒馆", "加物", "减物" })
        {
            Assert.Null(CreateRegistry().Resolve(command));
            Assert.False(string.IsNullOrWhiteSpace(LegacyAliases.Explain(command)), $"{command} 缺少排除说明");
        }
    }

    private sealed class NullBindCodes : IBindCodeService
    {
        public int TtlSeconds => 60;
        public Task<string> IssueAsync(long userId, string purpose = "qq_bind", CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<BindRedeemResult> RedeemAsync(string code, ExternalIdentity identity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NullSessions : IUserSessionService
    {
        public Task<UserSessionState?> GetAsync(long userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SetPendingAsync(long userId, string action, string payloadJson, TimeSpan ttl,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task ClearPendingAsync(long userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SetPageCursorAsync(long userId, string cursor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
'''

open('OshimaBot.Tests/LegacyCommandEntryTests.cs', 'w', encoding='utf-8', newline='\n').write(
    template.replace('%%ARRAY%%', array))
print('written', len(pairs))
