using Microsoft.Extensions.Caching.Memory;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Models;
using OshimaBot.Security;

namespace OshimaBot.Commands;

/// <summary>
/// 全局指令中间件：解析 → 权限 → 冷却 → 会话游标 → 执行 → 审计 → 异常兜底。
/// 只负责产出 BotReply，发送仍由 scoped 的 MessageSender 完成。
/// </summary>
public sealed class CommandRouter(
    CommandRegistry registry,
    CommandDependencies dependencies,
    IServiceScopeFactory scopeFactory,
    IMemoryCache cooldown,
    ILogger<CommandRouter> logger)
{
    public async Task<BotReply?> RouteAsync(IBotMessage message, OtherData data, CancellationToken cancellationToken = default)
    {
        string raw = message.Detail?.Trim() ?? "";
        if (raw.Length == 0) return null;

        CommandMatch? match = registry.Resolve(raw);
        if (match is null)
        {
            // 旧版命令：参数形态已变或功能不迁移时给出明确说明，而不是「未识别的指令」。
            string? explanation = LegacyAliases.Explain(raw);
            if (explanation is not null)
            {
                logger.LogDebug("旧命令已改版：{Command}", raw);
                return CommandReplies.Error("旧命令已改版", "没有执行任何操作", explanation, "/帮助");
            }
            logger.LogDebug("未识别的指令：{Command}", raw);
            return null;
        }

        CommandContext context = new()
        {
            Message = message,
            Raw = raw,
            Verb = match.Command.Name,
            Args = match.Args,
            Page = match.Page,
            Registry = registry,
            Dependencies = dependencies,
            Data = data,
            CancellationToken = cancellationToken
        };

        // Player 是默认最低角色：Principal 为空（第三方/用户库暂不可用）时仍允许 P0 查询。
        if (match.Command.RequiredRole > BotRole.Player && !context.HasRole(match.Command.RequiredRole))
        {
            return CommandReplies.Error("权限不足", "指令没有执行", $"需要 {match.Command.RequiredRole} 及以上角色", "/帮助");
        }

        if (match.Command.CooldownMilliseconds > 0 && !TryEnterCooldown(context, match.Command))
        {
            return CommandReplies.Error("操作太快", "指令没有执行", "稍等片刻再试", "/帮助");
        }

        try
        {
            BotReply reply = await match.Command.ExecuteAsync(context);
            if (match.Page > 1 && context.UserId > 0)
            {
                await dependencies.Sessions.SetPageCursorAsync(
                    context.UserId, $"{match.Command.Key}:p{match.Page}", cancellationToken);
            }
            if (match.Command.Audit) await WriteAuditAsync(context, match.Command, cancellationToken);
            return reply;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "指令执行失败：Key={Key}, Raw={Raw}", match.Command.Key, raw);
            return CommandReplies.Error("指令执行时出现异常", "没有产生结果", "稍后重试，或输入 /帮助 查看用法", "/帮助");
        }
    }

    private bool TryEnterCooldown(CommandContext context, ICommand command)
    {
        string key = $"OshimaBot:Cooldown:{context.UserId}:{command.Key}";
        if (cooldown.TryGetValue(key, out _)) return false;
        cooldown.Set(key, true, TimeSpan.FromMilliseconds(command.CooldownMilliseconds));
        return true;
    }

    private async Task WriteAuditAsync(CommandContext context, ICommand command, CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = context.UserId > 0 ? context.UserId : null,
                Action = $"command.{command.Key}",
                Target = context.Raw.Length > 200 ? context.Raw[..200] : context.Raw,
                PayloadJson = "{}",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "指令审计写入失败：Key={Key}", command.Key);
        }
    }
}
