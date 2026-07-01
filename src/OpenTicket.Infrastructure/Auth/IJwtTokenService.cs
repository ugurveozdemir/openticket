using OpenTicket.Domain.Entities;

namespace OpenTicket.Infrastructure.Auth;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}
