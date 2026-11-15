using System.Globalization;

namespace OshimaBot.Commands;

/// <summary>
/// 命令索引，三段匹配：**精确动词** → **旧版命令名重写** → **最长动词前缀**（参数可以紧贴动词）。
/// <para>第三段统一了原来「旧版前缀匹配」与「紧贴写法」两件事：
/// <c>/领奖1</c> 与 <c>/领奖 1</c> 等价（中文输入法下漏个空格太常见），
/// <c>/查角色1</c>、<c>/查团队数据3</c> 这类旧版整段写法照旧可用。</para>
/// <para>按别名长度**降序**扫，命中即止 —— 保证「查团队数据」优先于「查数据」、
/// 「商店详情」优先于「商店」，不会被短动词抢走。</para>
/// 所有命令都来自 CommandCatalog 的显式 switch 工厂，不做程序集反射扫描。
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _exact = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>全部别名，按长度降序：「动词 + 参数」不论中间有没有空格都用它挑最长的动词。</summary>
    private readonly List<(string Alias, ICommand Command)> _attached = [];

    public IReadOnlyList<ICommand> All { get; }

    public CommandRegistry(IEnumerable<ICommand> commands)
    {
        All = [.. commands];
        foreach (ICommand command in All)
        {
            foreach (string alias in new[] { command.Name }.Concat(command.Aliases))
            {
                if (!_exact.TryAdd(alias, command))
                {
                    throw new InvalidOperationException($"指令别名重复注册：{alias}（{command.Key}）");
                }
                _attached.Add((alias, command));
            }
        }
        _attached.Sort((left, right) => right.Alias.Length.CompareTo(left.Alias.Length));
    }

    public ICommand? FindByKey(string key)
        => All.FirstOrDefault(command => string.Equals(command.Key, key, StringComparison.OrdinalIgnoreCase));

    public CommandMatch? Resolve(string? raw)
    {
        string text = Normalize(raw);
        if (text.Length == 0) return null;

        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        ICommand? command = null;
        string[] arguments = [];

        if (_exact.TryGetValue(tokens[0], out ICommand? exact))
        {
            command = exact;
            arguments = tokens[1..];
        }
        else if (LegacyAliases.TryRewrite(text, out string rewritten))
        {
            // 旧版命令名兼容要先于前缀匹配：否则「模拟锻造」会被「模拟」前缀抢走，
            // 变成「FunGame模拟 锻造 …」。表里不含自指项，重写后不会回头再命中自己。
            return Resolve(rewritten);
        }
        if (command is null)
        {
            // 动词 + 参数，中间有没有空格都认：/领奖1 等价于 /领奖 1。
            // _attached 按别名长度降序，第一个命中的就是**最长**的动词 ——
            // 「商店详情1」因此落到 商店详情 而不是「商店 + 详情1」；
            // 旧版那种整段前缀写法（查角色1、查团队数据3）也走这条，行为不变。
            foreach ((string alias, ICommand candidate) in _attached)
            {
                if (text.Length <= alias.Length) continue;    // 完全相等由上面的精确匹配处理
                if (!text.StartsWith(alias, StringComparison.OrdinalIgnoreCase)) continue;
                string remainder = text[alias.Length..].Trim();
                if (remainder.Length == 0) continue;
                command = candidate;
                arguments = remainder.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                break;
            }
        }

        if (command is null) return null;

        return new CommandMatch(command, StripPageTokens(arguments), ExtractPage(arguments),
            arguments.Any(IsPageToken));
    }

    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string text = raw.Trim();
        while (text.StartsWith('/')) text = text[1..].TrimStart();
        return text.Trim();
    }

    private static IReadOnlyList<string> StripPageTokens(string[] arguments)
        => arguments.Where(argument => !IsPageToken(argument)).ToArray();

    private static int ExtractPage(IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (IsPageToken(argument)
                && int.TryParse(argument.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int page)
                && page > 0)
            {
                return page;
            }
        }
        return 1;
    }

    private static bool IsPageToken(string value)
        => value.Length > 1
           && (value[0] == 'p' || value[0] == 'P')
           && value.Skip(1).All(char.IsAsciiDigit);
}
