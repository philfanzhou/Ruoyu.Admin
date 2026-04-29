namespace Admin.WebApi.Models;

internal sealed record CreateStudentRequest(string Name, int Grade, List<string> IdentityAccountIds);
