using System.Globalization;

namespace OshimaBot.Commands;

/// <summary>
/// 命令索引：精确动词 + 旧版前缀双轨匹配。别名按长度降序，保证「查团队数据」优先于「查数据」。
/// 所有命令都来自 CommandCatalog 的显式 switch 工厂，不做程序集反射扫描。
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Alias, ICommand Command)> _prefixes = [];

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
                if (command.PrefixMatch) _prefixes.Add((alias, command));
            }
        }
        _prefixes.Sort((left, right) => right.Alias.Length.CompareTo(left.Alias.Length));
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
        else
        {
            foreach ((string alias, ICommand candidate) in _prefixes)
            {
                if (!text.StartsWith(alias, StringComparison.OrdinalIgnoreCase)) continue;
                command = candidate;
                string remainder = text[alias.Length..].Trim();
                arguments = remainder.Length == 0
                    ? []
                    : remainder.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                break;
            }
        }
        if (command is null) return null;

        return new CommandMatch(command, StripPageTokens(arguments), ExtractPage(arguments));
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
