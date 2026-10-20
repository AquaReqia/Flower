namespace Flower.Agents.Abstractions;

/// <summary>
/// Bir kaynaktaki (Confluence vb.) kişi referansı. Confluence Data Center çoğu zaman e-posta döndürmez;
/// bu durumda <see cref="Username"/> (örn. sicil/AD kullanıcı adı) ile <see cref="IOrgDirectory"/> üzerinden çözülür.
/// </summary>
public sealed record PersonRef(string? Email, string? Username = null, string? DisplayName = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Username);
}

/// <summary>Kurum dizinindeki kişi.</summary>
public sealed record PersonInfo(string Email, string DisplayName, string? JobTitle = null, string? Department = null);

/// <summary>
/// Kurum dizini. Flower tarafında Microsoft Graph ile implemente edilir
/// (<c>/users/{id}</c>, <c>/users/{id}/manager</c>, <c>onPremisesSamAccountName</c> filtresi).
/// </summary>
public interface IOrgDirectory
{
    /// <summary>Kişi referansını dizindeki kişiye çözer. Bulunamazsa null.</summary>
    Task<PersonInfo?> ResolveAsync(PersonRef person, CancellationToken ct);

    /// <summary>Kişinin yöneticisini döndürür. Yoksa null.</summary>
    Task<PersonInfo?> GetManagerAsync(string email, CancellationToken ct);
}

/// <summary>Varsayılan dizin: yalnızca e-postası olan referansları çözer, yönetici bilgisi yoktur.</summary>
public sealed class NullOrgDirectory : IOrgDirectory
{
    public Task<PersonInfo?> ResolveAsync(PersonRef person, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(person.Email))
        {
            return Task.FromResult<PersonInfo?>(null);
        }

        var email = person.Email.Trim();
        return Task.FromResult<PersonInfo?>(new PersonInfo(email, person.DisplayName ?? email));
    }

    public Task<PersonInfo?> GetManagerAsync(string email, CancellationToken ct) => Task.FromResult<PersonInfo?>(null);
}
