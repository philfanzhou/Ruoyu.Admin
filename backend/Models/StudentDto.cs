namespace Admin.WebApi.Models;

internal sealed record StudentDto(
    string Id,
    string Name,
    int Grade,
    IReadOnlyList<string> IdentityAccountIds,
    long CreatedAt,
    long UpdatedAt);
