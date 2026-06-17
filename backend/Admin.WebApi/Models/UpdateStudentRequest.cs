namespace Admin.WebApi.Models;

public sealed record UpdateStudentRequest(string Name, int Grade, List<string>? IdentityAccountIds);
