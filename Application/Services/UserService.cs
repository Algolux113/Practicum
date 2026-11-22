using Application.Interfaces;
using Domain.Exceptions;
using Domain.Models;

namespace Application.Services;

public sealed class UserService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokenService,
    IUnitOfWork unitOfWork) : IUserService
{
    public Task<Guid> RegisterAsync(string login, string password, UserRole role = UserRole.User,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ValidationException(nameof(password), "Пароль обязателен.");

        var user = User.Create(login, passwordHasher.Hash(password), role);
        return unitOfWork.ExecuteAsync(async () =>
        {
            if (await userRepository.FindByLoginAsync(user.Login, cancellationToken) is not null)
                throw new LoginAlreadyExistsException();

            await userRepository.AddAsync(user, cancellationToken);
            return user.Id;
        }, cancellationToken);
    }

    public async Task<string> LoginAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.FindByLoginAsync(login, cancellationToken);
        if (user is null || !passwordHasher.Verify(password, user.PasswordHash))
        {
            // Сопоставимая работа для неизвестного логина и старого хеша.
            if (user is null || passwordHasher.NeedsRehash(user.PasswordHash))
                _ = passwordHasher.Hash(password);
            throw new InvalidCredentialsException();
        }

        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            var userId = user.Id;
            var legacyHash = user.PasswordHash;
            var upgradedHash = passwordHasher.Hash(password);
            user = await unitOfWork.ExecuteAsync(async () =>
            {
                var current = await userRepository.GetForUpdateAsync(userId, cancellationToken);
                if (current is null)
                    throw new InvalidCredentialsException();
                if (current.PasswordHash == legacyHash)
                    current.UpdatePasswordHash(upgradedHash);
                else if (!passwordHasher.Verify(password, current.PasswordHash))
                    throw new InvalidCredentialsException();
                return current;
            }, cancellationToken);
        }

        return tokenService.GenerateToken(user);
    }
}
