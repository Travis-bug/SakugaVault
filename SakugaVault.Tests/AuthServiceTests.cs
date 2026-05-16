using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SakugaVault.Contracts.Auth;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Auth;
using SakugaVault.Services.Users;

namespace SakugaVault.Tests;

public sealed class AuthServiceTests
{
    private static readonly JwtOptions JwtOptions = new()
    {
        Issuer = "SakugaVault.Tests",
        Audience = "SakugaVault.Tests.Client",
        SigningKey = new string('x', 32),
        AccessTokenMinutes = 60
    };

    [Fact]
    public async Task RegisterAsync_NewUser_ReturnsValidJwtAndRefreshToken()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-16T12:00:00Z"));
        var authService = CreateAuthService(testDatabase.DbContext, fakeTime);

        var result = await authService.RegisterAsync(
            new RegisterRequestDto("Monkey D. Luffy", "luffy", "luffy@grandline.test", "StrawHat123"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.RefreshToken));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.AccessToken);
        Assert.Equal(JwtOptions.Issuer, token.Issuer);
        Assert.Contains(JwtOptions.Audience, token.Audiences);
        Assert.Equal(fakeTime.GetUtcNow().AddMinutes(JwtOptions.AccessTokenMinutes).UtcDateTime, token.ValidTo);
        Assert.Equal("luffy@grandline.test", result.Value.User.Email);
        Assert.Equal(1, await testDatabase.DbContext.Users.CountAsync());
        Assert.Equal(1, await testDatabase.DbContext.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsEmailTakenFailure()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedUserAsync(testDatabase.DbContext, "zoro", "zoro@sword.test");
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider());

        var result = await authService.RegisterAsync(
            new RegisterRequestDto("Roronoa Zoro", "piratehunter", "zoro@sword.test", "Santoryu123"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("email_taken", result.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUserName_ReturnsUserNameTakenFailure()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedUserAsync(testDatabase.DbContext, "sanji", "sanji@baratie.test");
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider());

        var result = await authService.RegisterAsync(
            new RegisterRequestDto("Vinsmoke Sanji", "sanji", "blackleg@baratie.test", "AllBlue123"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("username_taken", result.ErrorCode);
    }

    [Theory]
    [InlineData("nami")]
    [InlineData("nami@weather.test")]
    public async Task LoginAsync_ValidIdentifier_ReturnsJwtForEmailAndUserName(string identifier)
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        const string password = "Tangerine123";
        await SeedUserAsync(testDatabase.DbContext, "nami", "nami@weather.test", password);
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider());

        var result = await authService.LoginAsync(new LoginRequestDto(identifier, password), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value!.AccessToken);
        Assert.Equal(JwtOptions.Issuer, token.Issuer);
        Assert.Contains(JwtOptions.Audience, token.Audiences);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsInvalidCredentialsFailure()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedUserAsync(testDatabase.DbContext, "ussop", "ussop@sniper.test", "Kabuto123");
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider());

        var result = await authService.LoginAsync(new LoginRequestDto("ussop", "WrongPassword123"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_RehashNeeded_UpdatesStoredPasswordHash()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        const string password = "Robin12345";
        var user = await SeedUserAsync(testDatabase.DbContext, "robin", "robin@poneglyph.test", password);
        var originalHash = user.PasswordHash;
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider(), new RehashingPasswordHasher());

        var result = await authService.LoginAsync(new LoginRequestDto("robin", password), CancellationToken.None);

        Assert.True(result.Succeeded);

        var updatedUser = await testDatabase.DbContext.Users.SingleAsync(savedUser => savedUser.Id == user.Id);
        Assert.NotEqual(originalHash, updatedUser.PasswordHash);
    }

    [Fact]
    public async Task GetCurrentUserAsync_KnownUser_ReturnsCurrentUserDto()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(testDatabase.DbContext, "franky", "franky@dock.test");
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider());

        var currentUser = await authService.GetCurrentUserAsync(user.Id, CancellationToken.None);

        Assert.NotNull(currentUser);
        Assert.Equal(user.DisplayName, currentUser!.DisplayName);
    }

    [Fact]
    public async Task GetCurrentUserAsync_UnknownUser_ReturnsNull()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider());

        var currentUser = await authService.GetCurrentUserAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(currentUser);
    }

    [Fact]
    public async Task RefreshAsync_ValidRefreshToken_RotatesTokenAndReturnsNewJwt()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-16T12:00:00Z"));
        var authService = CreateAuthService(testDatabase.DbContext, fakeTime);

        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto("Brook", "brook", "brook@soul.test", "Binks1234"),
            CancellationToken.None);

        var refreshResult = await authService.RefreshAsync(registerResult.Value!.RefreshToken, CancellationToken.None);

        Assert.True(refreshResult.Succeeded);
        Assert.NotEqual(registerResult.Value.RefreshToken, refreshResult.Value!.RefreshToken);

        var storedTokens = await testDatabase.DbContext.RefreshTokens.OrderBy(token => token.CreatedAtUtc).ToArrayAsync();
        Assert.Equal(2, storedTokens.Length);
        Assert.True(storedTokens[0].IsRevoked);
        Assert.False(storedTokens[1].IsRevoked);
    }

    [Fact]
    public async Task LogoutAsync_RevokedRefreshToken_PreventsReuse()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var authService = CreateAuthService(testDatabase.DbContext, new FakeTimeProvider());

        var registerResult = await authService.RegisterAsync(
            new RegisterRequestDto("Jinbe", "jinbe", "jinbe@fishman.test", "Helmsman123"),
            CancellationToken.None);

        var logoutResult = await authService.LogoutAsync(registerResult.Value!.RefreshToken, CancellationToken.None);
        var refreshResult = await authService.RefreshAsync(registerResult.Value.RefreshToken, CancellationToken.None);

        Assert.True(logoutResult.Succeeded);
        Assert.False(refreshResult.Succeeded);
        Assert.Equal("invalid_refresh_token", refreshResult.ErrorCode);
    }

    private static AuthService CreateAuthService(
        SakugaVault.Data.SakugaVaultDbContext dbContext,
        FakeTimeProvider fakeTimeProvider,
        IPasswordHasher<ApplicationUser>? passwordHasher = null)
    {
        var userService = new UserService(dbContext);
        return new AuthService(
            userService,
            dbContext,
            passwordHasher ?? new PasswordHasher<ApplicationUser>(),
            Microsoft.Extensions.Options.Options.Create(JwtOptions),
            fakeTimeProvider);
    }

    private static async Task<ApplicationUser> SeedUserAsync(
        SakugaVault.Data.SakugaVaultDbContext dbContext,
        string userName,
        string email,
        string password = "Password123")
    {
        var user = new ApplicationUser
        {
            DisplayName = userName.ToUpperInvariant(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), password)
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    private sealed class RehashingPasswordHasher : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> innerHasher = new();

        public string HashPassword(ApplicationUser user, string password)
        {
            return innerHasher.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            var verification = innerHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
            return verification == PasswordVerificationResult.Success
                ? PasswordVerificationResult.SuccessRehashNeeded
                : verification;
        }
    }
}
