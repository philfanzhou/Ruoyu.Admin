namespace Admin.WebApi.Models;

public sealed record IdentityAccountDto(string UserId, string Username, string DisplayName, string Phone, string Remark);

internal sealed record IdentityUserItem(string UserId, string Username, string Phone, string Remark, string DisplayName);
