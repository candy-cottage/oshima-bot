using OshimaBot.Gameplay;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 赛马小游戏（旧版 <c>HorseRacing.RunHorseRacing</c> 的移植）：积分曲线、可复现性、战报结构。
/// <para>纯模拟，不碰数据库，所以直接跑 <see cref="HorseRace.Run"/>。</para>
/// </summary>
public sealed class HorseRaceTests
{
    private static readonly (long UserId, string Name)[] Entrants =
    [
        (1, "甲"), (2, "乙"), (3, "丙"), (4, "丁")
    ];

    [Fact]
    public void CalculatePoints_FollowsLegacyCurve()
    {
        // 第 1 名 10 分，之后每名 ×0.8 向下取整，最低 1 分
        Assert.Equal(10, HorseRace.CalculatePoints(1));
        Assert.Equal(8, HorseRace.CalculatePoints(2));
        Assert.Equal(6, HorseRace.CalculatePoints(3));
        Assert.Equal(4, HorseRace.CalculatePoints(4));
        Assert.Equal(3, HorseRace.CalculatePoints(5));
        Assert.Equal(2, HorseRace.CalculatePoints(6));
        Assert.Equal(1, HorseRace.CalculatePoints(7));
        Assert.Equal(1, HorseRace.CalculatePoints(20));   // 衰减到 0 时兜底成 1
        Assert.Equal(0, HorseRace.CalculatePoints(0));
    }

    [Fact]
    public void Run_IsDeterministicForSameSeed()
    {
        HorseRaceOutcome first = HorseRace.Run(Entrants, new SeededRandomSource(2026));
        HorseRaceOutcome second = HorseRace.Run(Entrants, new SeededRandomSource(2026));

        Assert.Equal(first.TrackLength, second.TrackLength);
        Assert.Equal(first.Finished, second.Finished);
        Assert.Equal(first.Ranking.Select(x => (x.UserId, x.Rank, x.Position, x.Points)),
            second.Ranking.Select(x => (x.UserId, x.Rank, x.Position, x.Points)));
        Assert.Equal(first.Log, second.Log);
    }

    [Fact]
    public void Run_ProducesRankedOutcomeWithTrackTranscript()
    {
        HorseRaceOutcome outcome = HorseRace.Run(Entrants, new SeededRandomSource(7));

        Assert.Equal(Entrants.Length, outcome.Ranking.Count);
        Assert.InRange(outcome.TrackLength, 8, 15);                 // 旧版 Next(8, 16)
        Assert.NotEmpty(outcome.Log);
        Assert.Contains(outcome.Log, block => block.Contains("比赛开始"));
        Assert.Contains(outcome.Log, block => block.Contains("赛道状况"));
        Assert.Contains(outcome.Log, block => block.Contains("比赛结果"));

        // 名次按位置降序；冠军一定拿 10 分
        List<HorseRaceEntry> ranking = [.. outcome.Ranking];
        for (int i = 1; i < ranking.Count; i++)
        {
            Assert.True(ranking[i - 1].Position >= ranking[i].Position,
                $"{ranking[i - 1].Name}({ranking[i - 1].Position}) 应排在 {ranking[i].Name}({ranking[i].Position}) 前");
        }
        Assert.Equal(1, ranking[0].Rank);
        Assert.Equal(10, ranking[0].Points);
        Assert.All(ranking, entry => Assert.True(entry.Points >= 1));
    }

    [Fact]
    public void Run_CoversEveryoneEvenWhenTrackIsLong()
    {
        // 100 回合跑不完也要给名次，不能漏人
        HorseRaceOutcome outcome = HorseRace.Run(Entrants, new SeededRandomSource(99));

        Assert.Equal(Entrants.Select(x => x.UserId).OrderBy(x => x),
            outcome.Ranking.Select(x => x.UserId).OrderBy(x => x));
        Assert.All(outcome.Ranking, entry => Assert.True(entry.Points >= 1));
    }
}
