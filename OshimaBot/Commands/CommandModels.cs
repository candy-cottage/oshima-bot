using System.Security.Claims;
using OshimaBot.Gameplay;
using OshimaBot.Messaging;
using OshimaBot.Models;
using OshimaBot.Security;
using OshimaBot.Services;

namespace OshimaBot.Commands;

public static class CommandGroups
{
    public const string System = "系统";
    public const string Query = "查询";
    public const string Stats = "统计";
    public const string Simulation = "战斗模拟";
    public const string Player = "旅人存档";
    public const string Economy = "经济战斗";
    public const string Legacy = "版本状态";
}

/// <summary>命令工厂所需的全部依赖，全部由 DI 显式传入，不做反射扫描。</summary>
public sealed record CommandDependencies(
    FunGameQueryService Queries,
    FunGameSimulationService Simulations,
    IBindCodeService BindCodes,
    IUserSessionService Sessions,
    IPlayerService Players,
    IEconomyService Economy,
    IRoomService Rooms,
    IClubService Clubs,
    IMailService Mail,
    IActivityService Activities,
    IExploreService Explore,
    ILogger Logger);

public interface ICommand
{
    string Key { get; }
    string Name { get; }
    IReadOnlyList<string> Aliases { get; }
    string Usage { get; }
    string Description { get; }
    string Group { get; }
    BotRole RequiredRole { get; }

    /// <summary>兼容旧版「前缀 + 参数」写法（如「查角色1」「团队调试模拟」）。</summary>
    bool PrefixMatch { get; }

    /// <summary>0 表示不启用冷却。</summary>
    int CooldownMilliseconds { get; }

    /// <summary>是否写入 audit_logs（写操作/重操作才开）。</summary>
    bool Audit { get; }

    Task<BotReply> ExecuteAsync(CommandContext context);
}

/// <summary>显式注册的委托命令：每个命令的元数据与执行逻辑都在 CommandCatalog 的 switch 里可见。</summary>
public sealed class DelegateCommand(
    string key,
    string name,
    string[] aliases,
    string usage,
    string description,
    string group,
    Func<CommandContext, Task<BotReply>> handler,
    bool prefixMatch = false,
    BotRole requiredRole = BotRole.Player,
    int cooldownMilliseconds = 0,
    bool audit = false) : ICommand
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public IReadOnlyList<string> Aliases { get; } = aliases;
    public string Usage { get; } = usage;
    public string Description { get; } = description;
    public string Group { get; } = group;
    public bool PrefixMatch { get; } = prefixMatch;
    public BotRole RequiredRole { get; } = requiredRole;
    public int CooldownMilliseconds { get; } = cooldownMilliseconds;
    public bool Audit { get; } = audit;

    public Task<BotReply> ExecuteAsync(CommandContext context) => handler(context);
}

public sealed record CommandMatch(ICommand Command, IReadOnlyList<string> Args, int Page);

public sealed class CommandContext
{
    public required IBotMessage Message { get; init; }
    public required string Raw { get; init; }
    public required string Verb { get; init; }
    public required IReadOnlyList<string> Args { get; init; }
    public required int Page { get; init; }
    public required CommandRegistry Registry { get; init; }
    public required CommandDependencies Dependencies { get; init; }
    public required OtherData Data { get; init; }
    public required CancellationToken CancellationToken { get; init; }

    public long UserId => Message.FunGameUID;
    public ClaimsPrincipal? Principal => Message.Principal;
    public bool IsGroup => Message.IsGroup;
    public string ArgumentText => string.Join(' ', Args);
    public string? FirstArgument => Args.Count > 0 ? Args[0] : null;
    public bool HasFlag(string flag) => Args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    public bool TryInt(int index, out int value)
    {
        value = 0;
        return index < Args.Count && int.TryParse(Args[index], out value);
    }

    /// <summary>@编号：把列表序号解析成用户不必记忆的内部 ID。</summary>
    public string? Reference(int index = 0)
        => index < Args.Count && Args[index].StartsWith('@') ? Args[index][1..] : null;

    /// <summary>--seed=123 / --yes 这类选项。</summary>
    public string? Option(string name)
    {
        string prefix = $"--{name}=";
        string? raw = Args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return raw is null ? null : raw[prefix.Length..];
    }

    public bool HasRole(BotRole required)
        => Principal?.FindAll(ClaimTypes.Role)
            .Any(claim => Enum.TryParse(claim.Value, out BotRole role) && role >= required) ?? false;
}
