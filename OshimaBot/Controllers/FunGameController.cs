using OshimaBot.Game.Services;
using OshimaBot.Models;
using OshimaBot.Services;
using Microsoft.AspNetCore.Mvc;

namespace OshimaBot.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public sealed class FunGameController(
        FunGameQueryService queries,
        FunGameSimulationService simulations) : ControllerBase
    {
        [HttpGet("status")]
        public IActionResult GetStatus() => Ok(new
        {
            core = "FunGame.Core v3",
            startedAt = FunGameRuntime.StartedAt,
            isSimulationRunning = simulations.IsRunning,
            characters = FunGameService.Characters.Count,
            skills = FunGameService.AllSkills.Count,
            items = FunGameService.AllItems.Count
        });

        [HttpGet("characters")]
        public IReadOnlyList<CatalogEntry> GetCharacters() => queries.GetCharacters();

        [HttpGet("skills")]
        public IReadOnlyList<CatalogEntry> GetSkills() => queries.GetSkills();

        [HttpGet("items")]
        public IReadOnlyList<CatalogEntry> GetItems() => queries.GetItems();

        [HttpGet("test")]
        public Task<ActionResult<IReadOnlyList<string>>> GetTest(
            [FromQuery] bool? isweb = null,
            [FromQuery] bool? isteam = null,
            [FromQuery] bool? showall = null,
            [FromQuery] int? maxRespawnTimesMix = null,
            [FromQuery] bool? hasMap = null,
            [FromQuery] int? seed = null,
            [FromQuery] int? characterLevel = null,
            [FromQuery] int? skillLevel = null,
            [FromQuery] int? normalAttackLevel = null,
            [FromQuery] bool? enablePeriodicDrop = null,
            [FromQuery] long? fixedWeaponId = null,
            CancellationToken cancellationToken = default)
            => RunSimulation(false, isweb, isteam, showall, maxRespawnTimesMix, hasMap, seed,
                characterLevel, skillLevel, normalAttackLevel, enablePeriodicDrop, fixedWeaponId, cancellationToken);

        [HttpGet("testdebug")]
        public Task<ActionResult<IReadOnlyList<string>>> GetTestDebug(
            [FromQuery] bool? isweb = null,
            [FromQuery] bool? isteam = null,
            [FromQuery] bool? showall = null,
            [FromQuery] int? maxRespawnTimesMix = null,
            [FromQuery] bool? hasMap = null,
            [FromQuery] int? seed = null,
            [FromQuery] int? characterLevel = null,
            [FromQuery] int? skillLevel = null,
            [FromQuery] int? normalAttackLevel = null,
            [FromQuery] bool? enablePeriodicDrop = null,
            [FromQuery] long? fixedWeaponId = null,
            CancellationToken cancellationToken = default)
            => RunSimulation(true, isweb, isteam, showall, maxRespawnTimesMix, hasMap, seed,
                characterLevel, skillLevel, normalAttackLevel, enablePeriodicDrop, fixedWeaponId, cancellationToken);

        private async Task<ActionResult<IReadOnlyList<string>>> RunSimulation(
            bool debug, bool? isWeb, bool? isTeam, bool? showAll, int? maxRespawnTimes,
            bool? hasMap, int? seed, int? characterLevel, int? skillLevel, int? normalAttackLevel,
            bool? enablePeriodicDrop, long? fixedWeaponId, CancellationToken cancellationToken)
        {
            SimulationResult? result = await simulations.RunAsync(new SimulationRequest
            {
                IsWeb = isWeb ?? true,
                IsTeam = isTeam ?? false,
                ShowAll = showAll ?? false,
                MaxRespawnTimesMix = maxRespawnTimes ?? 1,
                HasMap = hasMap ?? false,
                IsDebug = debug,
                Seed = seed,
                CharacterLevel = characterLevel ?? 10,
                SkillLevel = skillLevel ?? 2,
                NormalAttackLevel = normalAttackLevel ?? 2,
                EnablePeriodicDrop = enablePeriodicDrop ?? true,
                FixedWeaponId = fixedWeaponId ?? 0
            }, cancellationToken);

            if (result is null)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "游戏正在模拟中，请勿重复请求！",
                    Status = StatusCodes.Status409Conflict
                });
            }
            return Ok(result.Messages);
        }

        [HttpGet("last")]
        public async Task<IReadOnlyList<string>> GetLast([FromQuery] bool full = false, CancellationToken cancellationToken = default)
            => await simulations.GetLastAsync(full, cancellationToken);

        [HttpGet("stats")]
        public async Task<BotReply> GetStats([FromQuery] int? id = null, CancellationToken cancellationToken = default)
            => await queries.GetStatsAsync(id ?? 0, false, cancellationToken);

        [HttpGet("teamstats")]
        public async Task<BotReply> GetTeamStats([FromQuery] int? id = null, CancellationToken cancellationToken = default)
            => await queries.GetStatsAsync(id ?? 0, true, cancellationToken);

        [HttpGet("winraterank")]
        public async Task<List<string>> GetWinrateRank([FromQuery] bool? isteam = null, CancellationToken cancellationToken = default)
            => await queries.GetRankingAsync(isteam ?? false, false, cancellationToken);

        [HttpGet("ratingrank")]
        public async Task<List<string>> GetRatingRank([FromQuery] bool? isteam = null, CancellationToken cancellationToken = default)
            => await queries.GetRankingAsync(isteam ?? false, true, cancellationToken);

        [HttpGet("characterinfo")]
        public BotReply GetCharacterInfo([FromQuery] int? id = null, [FromQuery] string? name = null)
            => id.HasValue ? queries.GetCharacterInfo(id.Value) : queries.GetCharacterInfo(name ?? "");

        [HttpGet("skillinfo")]
        public BotReply GetSkillInfo([FromQuery] long? id = null)
            => queries.GetSkillInfo(id, null);

        [HttpGet("skillinfoname")]
        public BotReply GetSkillInfoByName([FromQuery] string? name = null)
            => queries.GetSkillInfo(null, name);

        [HttpGet("iteminfo")]
        public BotReply GetItemInfo([FromQuery] long? id = null)
            => queries.GetItemInfo(id, null);

        [HttpGet("iteminfoname")]
        public BotReply GetItemInfoByName([FromQuery] string? name = null)
            => queries.GetItemInfo(null, name);

        [HttpGet("newmagiccard")]
        public BotReply GenerateMagicCard() => queries.GenerateMagicCard();

        [HttpGet("newmagiccardpack")]
        public BotReply GenerateMagicCardPack() => queries.GenerateMagicCardPack();
    }
}
