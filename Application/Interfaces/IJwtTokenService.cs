using Domain.Models;

namespace Application.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(User user);
}
