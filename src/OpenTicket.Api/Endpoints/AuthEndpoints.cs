using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenTicket.Api.Contracts;
using OpenTicket.Domain.Entities;
using OpenTicket.Infrastructure.Auth;
using OpenTicket.Infrastructure.Persistence;

namespace OpenTicket.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, AppDbContext db, IPasswordHasher hasher, IJwtTokenService jwt) =>
        {
            var emailExists = await db.Users.AnyAsync(u => u.Email == request.Email);
            if (emailExists)
            {
                return Results.Conflict("Email already registered.");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                PasswordHash = hasher.Hash(request.Password),
                DisplayName = request.DisplayName,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            var (token, expiresAtUtc) = jwt.GenerateToken(user);

            return Results.Created("/api/auth/me", new AuthResponse(token, expiresAtUtc, user.Id, user.Email, user.DisplayName));
        });

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, IPasswordHasher hasher, IJwtTokenService jwt) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            {
                return Results.Unauthorized();
            }

            var (token, expiresAtUtc) = jwt.GenerateToken(user);

            return Results.Ok(new AuthResponse(token, expiresAtUtc, user.Id, user.Email, user.DisplayName));
        });

        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
            var displayName = principal.FindFirstValue("displayName");

            return Results.Ok(new { userId, email, displayName });
        }).RequireAuthorization();
    }
}
