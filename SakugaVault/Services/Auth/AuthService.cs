using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SakugaVault.Contracts.Auth;
using SakugaVault.Data;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Common;
using SakugaVault.Services.Users;

namespace SakugaVault.Services.Auth;

/// <summary>
/// Auth implementation that handles password hashing and self-issued JWT tokens.
/// If you later switch to the exact HMS auth scheme or an external identity provider, this is the seam you replace.
/// </summary>
public sealed class AuthService(
    IUserService userService,
    SakugaVaultDbContext dbContext,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IOptions<JwtOptions> jwtOptionsAccessor,
    TimeProvider timeProvider) : IAuthService
{
    private readonly JwtOptions jwtOptions = jwtOptionsAccessor.Value;

    public async Task<OperationResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var existingEmail = await userService.GetByEmailAsync(request.Email, cancellationToken);
        if (existingEmail is not null)
        {
            return OperationResult<AuthResponseDto>.Failure("email_taken", "An account with that email already exists.");
        }

        var userNameExists = await userService.UserNameExistsAsync(request.UserName, cancellationToken);
        if (userNameExists)
        {
            return OperationResult<AuthResponseDto>.Failure("username_taken", "That username is already in use.");
        }

        var user = new ApplicationUser
        {
            DisplayName = request.DisplayName.Trim(),
            UserName = request.UserName.Trim(),
            NormalizedUserName = Normalize(request.UserName),
            Email = request.Email.Trim(),
            NormalizedEmail = Normalize(request.Email)
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user = await userService.CreateAsync(user, cancellationToken);

        return OperationResult<AuthResponseDto>.Success(await BuildAuthResponseAsync(user, cancellationToken));
    }

    public async Task<OperationResult<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdentifierAsync(request.Identifier, cancellationToken);
        if (user is null)
        {
            return OperationResult<AuthResponseDto>.Failure("invalid_credentials", "Invalid username/email or password.");
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return OperationResult<AuthResponseDto>.Failure("invalid_credentials", "Invalid username/email or password.");
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await userService.UpdateAsync(user, cancellationToken);
        }

        return OperationResult<AuthResponseDto>.Success(await BuildAuthResponseAsync(user, cancellationToken));
    }

    public async Task<OperationResult<AuthResponseDto>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var persistedToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.Token == refreshToken, cancellationToken);

        if (persistedToken is null || persistedToken.IsRevoked || persistedToken.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            return OperationResult<AuthResponseDto>.Failure("invalid_refresh_token", "The refresh token is invalid, expired, or revoked.");
        }

        persistedToken.IsRevoked = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return OperationResult<AuthResponseDto>.Success(await BuildAuthResponseAsync(persistedToken.User, cancellationToken));
    }

    public async Task<OperationResult<bool>> LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var persistedToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.Token == refreshToken, cancellationToken);

        if (persistedToken is null)
        {
            return OperationResult<bool>.Failure("invalid_refresh_token", "The refresh token could not be found.");
        }

        if (!persistedToken.IsRevoked)
        {
            persistedToken.IsRevoked = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return OperationResult<bool>.Success(true);
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(userId, cancellationToken);
        return user is null ? null : ToCurrentUser(user);
    }

    private async Task<AuthResponseDto> BuildAuthResponseAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(jwtOptions.AccessTokenMinutes);
        var refreshTokenExpiresAt = issuedAt.AddDays(7);
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims =
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("username", user.UserName)
            };

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var serializedToken = new JwtSecurityTokenHandler().WriteToken(token);
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = GenerateRefreshTokenValue(),
            ExpiresAtUtc = refreshTokenExpiresAt
        };

        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto(serializedToken, expiresAt, refreshToken.Token, ToCurrentUser(user));
    }

    private static CurrentUserDto ToCurrentUser(ApplicationUser user) =>
        new(user.Id, user.DisplayName, user.UserName, user.Email);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string GenerateRefreshTokenValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncoder.Encode(bytes);
    }
}
