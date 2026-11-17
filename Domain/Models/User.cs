using Domain.Exceptions;

namespace Domain.Models;

public class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Login { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }

    public void UpdatePasswordHash(string passwordHash)
    {
        ValidatePasswordHash(passwordHash);
        PasswordHash = passwordHash;
    }

    public static User Create(string login, string passwordHash, UserRole role = UserRole.User)
    {
        if (string.IsNullOrWhiteSpace(login))
            throw new ValidationException(nameof(login), "Логин обязателен.");
        ValidatePasswordHash(passwordHash);
        if (!Enum.IsDefined(role))
            throw new ValidationException(nameof(role), "Указана недопустимая роль.");

        return new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            PasswordHash = passwordHash,
            Role = role
        };
    }

    private static void ValidatePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ValidationException(nameof(passwordHash), "Хеш пароля обязателен.");
    }
}
