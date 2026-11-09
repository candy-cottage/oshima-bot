using System.Text;
using OshimaBot.Game.Services;
using OshimaBot.Messaging;
using OshimaBot.Models;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Services
{
    public sealed class RainBotService(
        FunGameQueryService queries,
        FunGameSimulationService simulations,
        MessageSender messageSender,
        ILogger<RainBotService> logger)
    {
        public async Task<bool> Handler(IBotMessage message, OtherData data, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            string command = message.Detail.Trim();
            if (command.StartsWith('/')) command = command[1..].Trim();
            message.Detail = command;
            if (command.Length == 0) return false;

            BotReply? reply = await RouteCommandAsync(message, command, cancellationToken);
            if (reply is null) return false;

            await SendAsync(message, reply, cancellationToken);
            return true;
        }

        public Task<bool> HandlerByConsole(IBotMessage message, OtherData data, CancellationToken cancellationToken = default)
            => Handler(message, data, cancellationToken);

        private async Task<BotReply?> RouteCommandAsync(IBotMessage message, string command, CancellationToken cancellationToken)
        {
            if (command == "帮助") return BotReply.FromMarkdown(CreateHelp());
            if (command is "战斗帮助" or "角色帮助" or "物品帮助") return BotReply.FromMarkdown(CreateBattleHelp());
            if (command == "重置状态") return "当前版本采用无状态指令路由，无需重置指令状态。";
            if (command == "获取接入码" && !message.IsGroup) return $"你的接入码为 {message.AuthorOpenId}，请妥善保存！";
            if (command == "查询服务器启动时间") return $"OshimaBot 启动时间（UTC）：{FunGameRuntime.StartedAt:yyyy-MM-dd HH:mm:ss}";

            if (command == "预览魔法卡") return queries.GenerateMagicCard();
            if (command == "预览魔法卡包") return queries.GenerateMagicCardPack();

            if (TryArgument(command, "查角色", out string character))
                return RequireResult(queries.GetCharacterInfo(character), "未找到角色。用法：查角色 <编号或名称>");
            if (TryArgument(command, "查技能", out string skill))
            {
                string result = long.TryParse(skill, out long id)
                    ? queries.GetSkillInfo(id, null)
                    : queries.GetSkillInfo(null, skill);
                return RequireResult(result, "未找到技能。用法：查技能 <编号或名称>");
            }
            if (TryArgument(command, "查物品", out string item))
            {
                string result = long.TryParse(item, out long id)
                    ? queries.GetItemInfo(id, null)
                    : queries.GetItemInfo(null, item);
                return RequireResult(result, "未找到物品。用法：查物品 <编号或名称>");
            }

            if (TryArgument(command, "查团队数据", out string teamStats) && int.TryParse(teamStats, out int teamStatsId))
                return RequireResult(await queries.GetStatsAsync(teamStatsId, true, cancellationToken), "未找到角色团队统计。");
            if (TryArgument(command, "查数据", out string stats) && int.TryParse(stats, out int statsId))
                return RequireResult(await queries.GetStatsAsync(statsId, false, cancellationToken), "未找到角色统计。");
            if (command.StartsWith("查团队胜率", StringComparison.OrdinalIgnoreCase))
                return MergeRanking(await queries.GetRankingAsync(true, false, cancellationToken));
            if (command.StartsWith("查个人胜率", StringComparison.OrdinalIgnoreCase))
                return MergeRanking(await queries.GetRankingAsync(false, false, cancellationToken));
            if (command.StartsWith("查团队评分", StringComparison.OrdinalIgnoreCase))
                return MergeRanking(await queries.GetRankingAsync(true, true, cancellationToken));
            if (command.StartsWith("查个人评分", StringComparison.OrdinalIgnoreCase))
                return MergeRanking(await queries.GetRankingAsync(false, true, cancellationToken));

            if (command == "上次的完整日志")
                return MergeToMarkdown("上次的完整日志：", await simulations.GetLastAsync(true, cancellationToken));
            if (command == "上次的日志")
                return MergeToMarkdown("上次的模拟日志：", await simulations.GetLastAsync(false, cancellationToken));

            if (command.StartsWith("团队调试模拟", StringComparison.OrdinalIgnoreCase))
                return await RunSimulationAsync(new SimulationRequest { IsTeam = true, HasMap = true, IsDebug = true, ShowAll = true, MaxRespawnTimesMix = 0 }, cancellationToken);
            if (command.StartsWith("团队地图模拟", StringComparison.OrdinalIgnoreCase))
                return await RunSimulationAsync(new SimulationRequest { IsTeam = true, HasMap = true }, cancellationToken);
            if (command.StartsWith("FunGame团队模拟", StringComparison.OrdinalIgnoreCase))
                return await RunSimulationAsync(new SimulationRequest { IsTeam = true }, cancellationToken);
            if (command.StartsWith("个人地图模拟", StringComparison.OrdinalIgnoreCase))
                return await RunSimulationAsync(new SimulationRequest { HasMap = true, MaxRespawnTimesMix = 0 }, cancellationToken);
            if (TryArgument(command, "混战模拟", out string respawn))
                return await RunSimulationAsync(new SimulationRequest
                {
                    MaxRespawnTimesMix = int.TryParse(respawn, out int times) ? times : 1
                }, cancellationToken);
            if (command.StartsWith("FunGame模拟", StringComparison.OrdinalIgnoreCase))
                return await RunSimulationAsync(new SimulationRequest { MaxRespawnTimesMix = 0 }, cancellationToken);

            if (command is "存档帮助" or "玩法帮助" or "社团帮助" or "活动帮助" or "商店帮助")
                return "该模块尚未迁移到 OshimaBot。首轮已开放角色、物品、战斗查询与模拟指令。";

            return null;
        }

        private async Task<BotReply> RunSimulationAsync(SimulationRequest request, CancellationToken cancellationToken)
        {
            SimulationResult? result = await simulations.RunAsync(request, cancellationToken);
            if (result is null) return "游戏正在模拟中，请勿重复请求！";
            logger.LogInformation("RainBot 模拟完成：Seed={Seed}，Duration={Duration}", result.Seed, result.Duration);
            return MergeToMarkdown($"模拟结果如下（种子：{result.Seed}）：", result.Messages);
        }

        private Task SendAsync(IBotMessage message, BotReply reply, CancellationToken cancellationToken)
            => messageSender.SendAsync(message, reply, cancellationToken);

        public BotReply MergeToMarkdown(string subtitle, IReadOnlyCollection<string> messages)
        {
            if (messages.Count == 0) return "暂无记录，请先运行一次模拟。";

            List<string> selected = [.. messages];
            if (selected.Count > 30)
            {
                selected = [.. selected[..15], $"\r\n……已省略 {messages.Count - 30} 段日志……\r\n", .. selected[^15..]];
            }

            StringBuilder content = new();
            content.AppendLine(subtitle).AppendLine("```");
            foreach (string message in selected)
            {
                if (content.Length > 48_000)
                {
                    content.AppendLine("……日志过长，后续内容已省略……");
                    break;
                }
                content.AppendLine(message);
            }
            content.AppendLine("```");
            return BotReply.FromMarkdown(content.ToString());
        }

        public List<string> MergeMessages(IReadOnlyCollection<string> messages)
        {
            const int groupSize = 5;
            List<string> merged = [];
            List<string> source = [.. messages];
            if (source.Count > 20) source = [source[0], .. source[^20..]];
            for (int index = 0; index < source.Count; index += groupSize)
            {
                merged.Add(string.Join("\r\n", source.Skip(index).Take(groupSize)));
            }
            return merged.Count >= 3 ? [merged[0], .. merged[^2..]] : merged;
        }

        public BotReply CreateMarkdownFromText(string text) => BotReply.FromMarkdown(text);

        private static BotReply MergeRanking(IReadOnlyCollection<string> ranking)
            => ranking.Count == 0 ? "暂无统计数据。" : string.Join("\r\n\r\n", ranking);

        private static BotReply RequireResult(string result, string fallback)
            => string.IsNullOrWhiteSpace(result) ? fallback : result;

        private static bool TryArgument(string command, string prefix, out string argument)
        {
            argument = "";
            if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            argument = command[prefix.Length..].Trim();
            return true;
        }

        private static string CreateHelp() => $"""
            欢迎使用《筽祀牻》OshimaBot 服务器！
            核心库版本：{FunGameInfo.FunGame_Version}

            首轮指令：
            1. 查角色 <编号或名称>
            2. 查技能 <编号或名称>
            3. 查物品 <编号或名称>
            4. 预览魔法卡 / 预览魔法卡包
            5. 查数据 <编号> / 查团队数据 <编号>
            6. 查个人胜率 / 查团队胜率 / 查个人评分 / 查团队评分
            7. 混战模拟 [复活次数] / FunGame模拟 / FunGame团队模拟
            8. 个人地图模拟 / 团队地图模拟 / 团队调试模拟
            9. 上次的日志 / 上次的完整日志
            10. 查询服务器启动时间

            存档、社团、活动和商店等旧模块将在后续迁移。
            """;

        private static string CreateBattleHelp() => """
            《筽祀牻》战斗指令
            1. 查角色 <编号或名称>：查看满级角色资料。
            2. 查技能 <编号或名称>：查看 1、2 级与满级技能资料。
            3. 查物品 <编号或名称>：查看物品资料。
            4. 混战模拟 [复活次数]：开始个人混战模拟。
            5. FunGame模拟：开始无复活混战模拟。
            6. FunGame团队模拟：开始团队模拟。
            7. 个人地图模拟 / 团队地图模拟：开始带地图的模拟。
            8. 团队调试模拟：输出团队地图调试模拟。
            9. 查数据 / 查团队数据：查看角色累计统计。
            10. 查个人胜率 / 查团队胜率 / 查个人评分 / 查团队评分：查看排行。
            11. 上次的日志 / 上次的完整日志：查看最近模拟记录。
            """;
    }
}
