namespace Admin.WebApi.Models;

internal sealed record UpdateStudentRequest(string Name, int Grade, List<string>? IdentityAccountIds);
