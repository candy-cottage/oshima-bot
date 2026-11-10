using FunGame.Core.Entity;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using Xunit;
using Xunit.Abstractions;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Tests;

/// <summary>
/// 战斗执行器（FunGameActionQueue）接入验证：
/// 能起一场真实战斗、拿到结果与日志，并且同种子可复现。
/// </summary>
public class FunGameActionQueueTests(ITestOutputHelper output)
{
    static FunGameActionQueueTests() => FunGameRuntime.EnsureInitialized();

    private static CoreCharacter Fighter(int index, int level)
    {
        CoreCharacter character = FunGameService.Characters[index].Copy();
        character.Guid = Guid.NewGuid();
        character.Level = level;
        return character;
    }

    [Fact]
    public void RunMix_FinishesWithLogAndOutcome()
    {
        BattleResult result = FunGameActionQueue.RunMix(
            [Fighter(0, 20), Fighter(1, 20)],
            new BattleOptions { Seed = 20261110, MaxRound = 100 });

        Assert.NotEmpty(result.Log);
        Assert.True(result.Rounds > 0, "应该至少进行一回合");
        Assert.True(result.HasWinner || result.IsDraw, "必须有胜负或平局");
        output.WriteLine($"rounds={result.Rounds}, winner={result.Winner?.Name}, draw={result.IsDraw}, " +
                         $"lines={result.Log.Count}, time={result.TotalTime:F2}");
        output.WriteLine(result.Tail(3));
    }

    [Fact]
    public void RunMix_SameSeedIsDeterministic()
    {
        static (string Winner, int Rounds) Play()
        {
            BattleResult r = FunGameActionQueue.RunMix(
                [Fighter(0, 30), Fighter(1, 30)],
                new BattleOptions { Seed = 424242, MaxRound = 120 });
            return (r.Winner?.Name ?? "draw", r.Rounds);
        }

        (string Winner, int Rounds) first = Play();
        (string Winner, int Rounds) second = Play();
        Assert.Equal(first.Winner, second.Winner);
        Assert.Equal(first.Rounds, second.Rounds);
    }

    [Fact]
    public void RunMix_DifferentSeedsCanDiverge()
    {
        // 不断言必然不同（可能恰好同结果），只确认不同种子都能正常跑完。
        BattleResult a = FunGameActionQueue.RunMix([Fighter(0, 25), Fighter(1, 25)],
            new BattleOptions { Seed = 1, MaxRound = 80 });
        BattleResult b = FunGameActionQueue.RunMix([Fighter(0, 25), Fighter(1, 25)],
            new BattleOptions { Seed = 999999, MaxRound = 80 });

        Assert.True(a.Rounds > 0 && b.Rounds > 0);
        Assert.NotEmpty(a.Log);
        Assert.NotEmpty(b.Log);
    }

    [Fact]
    public void RunTeam_ReportsTeamScores()
    {
        CoreCharacter a1 = Fighter(0, 20);
        CoreCharacter a2 = Fighter(1, 20);
        CoreCharacter b1 = Fighter(2, 20);
        CoreCharacter b2 = Fighter(3, 20);

        List<Team> teams = [new("红队", [a1, a2]), new("蓝队", [b1, b2])];
        BattleResult result = FunGameActionQueue.RunTeam(teams,
            new BattleOptions { Seed = 777, MaxRound = 120 });

        Assert.True(result.Rounds > 0);
        Assert.Equal(2, result.TeamScores.Count);
        Assert.Contains("红队", result.TeamScores.Keys);
        output.WriteLine($"scores={string.Join(",", result.TeamScores.Select(kv => $"{kv.Key}:{kv.Value}"))}");
    }

    [Fact]
    public async Task RunMixAsync_Completes()
    {
        BattleResult result = await FunGameActionQueue.RunMixAsync(
            [Fighter(0, 15), Fighter(1, 15)],
            new BattleOptions { Seed = 5, MaxRound = 60 });

        Assert.NotEmpty(result.Log);
    }
}
