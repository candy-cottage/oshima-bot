using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

public sealed class IdentityTests
{
    [Fact]
    public async Task UserDirectory_RegistersLazilyOnlyOnce()
    {
        using TestDatabase db = new();
        IUserDirectory directory = db.Get<IUserDirectory>();

        BotUserPrincipal first = await directory.ResolveAsync(ExternalIdentity.Qq("OPENID1"));
        BotUserPrincipal second = await directory.ResolveAsync(ExternalIdentity.Qq("OPENID1"));

        Assert.True(first.IsNew);
        Assert.False(second.IsNew);
        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal(BotRole.Player, Assert.Single(first.Roles));
        Assert.StartsWith("U", first.Handle);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(1, await context.UserIdentities.CountAsync());
        Assert.Equal(1, await context.AuditLogs.CountAsync());
        Assert.Equal("user.register", (await context.AuditLogs.FirstAsync()).Action);
    }

    [Fact]
    public async Task UserDirectory_UnionSubjectReusesExistingAccount()
    {
        using TestDatabase db = new();
        IUserDirectory directory = db.Get<IUserDirectory>();

        BotUserPrincipal c2c = await directory.ResolveAsync(ExternalIdentity.Qq("C2C-1", "c2c", "UNION-1"));
        BotUserPrincipal group = await directory.ResolveAsync(ExternalIdentity.Qq("GROUP-1", "group", "UNION-1"));

        Assert.Equal(c2c.UserId, group.UserId);
        Assert.False(group.IsNew);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.Equal(2, await context.UserIdentities.CountAsync());
        Assert.Equal(2, await context.AuditLogs.CountAsync()); // register + union_link
    }

    [Fact]
    public async Task UserDirectory_SuperAdminWhitelistGrantsOwner()
    {
        using TestDatabase db = new(new ServerOptions { SuperAdminOpenIds = ["BOSS-OPENID"] });
        BotUserPrincipal principal = await db.Get<IUserDirectory>().ResolveAsync(ExternalIdentity.Qq("BOSS-OPENID"));

        Assert.Contains(BotRole.Owner, principal.Roles);
        Assert.True(principal.HasRole(BotRole.Admin));
        Assert.True(principal.ToClaimsPrincipal().IsInRole("Owner"));
    }

    [Fact]
    public async Task BindCodeService_IsSixDigitsOneTimeAndLinksIdentity()
    {
        using TestDatabase db = new();
        BotUserPrincipal host = await db.Get<IUserDirectory>().ResolveAsync(ExternalIdentity.Qq("HOST"));
        IBindCodeService codes = db.Get<IBindCodeService>();

        string code = await codes.IssueAsync(host.UserId);
        Assert.Matches(@"^\d{6}$", code);
        Assert.Equal(300, codes.TtlSeconds);

        BindRedeemResult redeemed = await codes.RedeemAsync(code, ExternalIdentity.Qq("NEW-OPENID"));
        Assert.Equal(BindRedeemStatus.Success, redeemed.Status);
        Assert.Equal(host.UserId, redeemed.UserId);
        Assert.Equal(BindRedeemStatus.Used, (await codes.RedeemAsync(code, ExternalIdentity.Qq("ANOTHER"))).Status);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.NotNull(await context.UserIdentities.FirstOrDefaultAsync(x => x.Subject == "NEW-OPENID"));
        Assert.Null(await context.UserIdentities.FirstOrDefaultAsync(x => x.Subject == "ANOTHER"));
        Assert.NotNull((await context.BindCodes.FirstAsync()).UsedAtUtc);
    }

    [Fact]
    public async Task BindCodeService_RejectsExpiredConflictAndMissingUser()
    {
        using TestDatabase db = new();
        IUserDirectory directory = db.Get<IUserDirectory>();
        BotUserPrincipal host = await directory.ResolveAsync(ExternalIdentity.Qq("HOST"));
        BotUserPrincipal other = await directory.ResolveAsync(ExternalIdentity.Qq("TAKEN"));
        IBindCodeService codes = db.Get<IBindCodeService>();

        Assert.Equal(BindRedeemStatus.NotFound, (await codes.RedeemAsync("000000", ExternalIdentity.Qq("X"))).Status);
        Assert.Equal(BindRedeemStatus.Conflict,
            (await codes.RedeemAsync(await codes.IssueAsync(host.UserId), ExternalIdentity.Qq("TAKEN"))).Status);
        Assert.Equal(BindRedeemStatus.InvalidUser,
            (await codes.RedeemAsync(await codes.IssueAsync(9999), ExternalIdentity.Qq("X"))).Status);
        Assert.NotEqual(host.UserId, other.UserId);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        context.BindCodes.Add(new BindCode
        {
            Code = "123456", UserId = host.UserId, Purpose = "test",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1), CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2)
        });
        await context.SaveChangesAsync();
        Assert.Equal(BindRedeemStatus.Expired, (await codes.RedeemAsync("123456", ExternalIdentity.Qq("X"))).Status);
    }

    [Fact]
    public async Task ApiKeyAuthenticator_ValidatesHashRevocationAndExpiry()
    {
        using TestDatabase db = new();
        const string secret = "a-high-entropy-random-key-0123456789";
        ApiKeyAuthenticator authenticator = db.Get<ApiKeyAuthenticator>();
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.ApiKeys.Add(new ApiKey
            {
                Name = "test", KeyHash = ApiKeyDefaults.Hash(secret), ScopesJson = "[\"thirdparty\",\"bind\"]",
                CreatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        ApiKeyIdentity? valid = await authenticator.AuthenticateAsync(secret);
        Assert.NotNull(valid);
        Assert.Contains("thirdparty", valid!.Scopes);
        Assert.Null(await authenticator.AuthenticateAsync("wrong-key"));

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            ApiKey row = await context.ApiKeys.FirstAsync();
            row.RevokedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        Assert.Null(await authenticator.AuthenticateAsync(secret));

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            ApiKey row = await context.ApiKeys.FirstAsync();
            row.RevokedAtUtc = null;
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }
        Assert.Null(await authenticator.AuthenticateAsync(secret));
    }

    [Fact]
    public async Task DatabaseInitializer_BootstrapsApiKeyHashOnly()
    {
        using TestDatabase db = new();
        DatabaseInitializer initializer = new(
            db.Provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }),
            Options.Create(new ServerOptions { ApiKey = "bootstrap-secret" }),
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);

        ApiKeyAuthenticator authenticator = db.Get<ApiKeyAuthenticator>();
        Assert.NotNull(await authenticator.AuthenticateAsync("bootstrap-secret"));
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        ApiKey row = await context.ApiKeys.FirstAsync();
        Assert.NotEqual("bootstrap-secret", row.KeyHash);
        Assert.Equal(64, row.KeyHash.Length);
    }

    [Fact]
    public void Principals_CarryRolesAndScopes()
    {
        ApiKeyIdentity key = new(7, 42, "svc", ["thirdparty", "bind"], [BotRole.Admin]);
        ClaimsPrincipal principal = key.ToClaimsPrincipal(ApiKeyDefaults.Scheme);
        Assert.Equal("42", principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        Assert.True(principal.IsInRole("Admin"));
        Assert.Contains(principal.FindAll("scope"), claim => claim.Value == "bind");

        BotUserPrincipal user = new(1, "U000001", "", [BotRole.Owner], false);
        Assert.True(user.HasRole(BotRole.Admin));
        Assert.False(new BotUserPrincipal(2, "U2", "", [BotRole.Player], false).HasRole(BotRole.Admin));
        Assert.Equal(ApiKeyDefaults.Hash("abc"), ApiKeyDefaults.Hash("abc"));
        Assert.Equal(new[] { BotRole.Player }, ApiKeyDefaults.ParseRoles([0, 99, 0]));
    }

    /// <summary>共享的 SQLite 内存库 + 真实 DI 容器，保证测的是真实 EF Core 行为。</summary>
    private sealed class TestDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public TestDatabase(ServerOptions? serverOptions = null)
        {
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(serverOptions ?? new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<IBindCodeService, BindCodeService>();
            services.AddSingleton<ApiKeyAuthenticator>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
