namespace Admin.WebApi.Models;

public sealed record CreateStudentRequest(string Name, int Grade, List<string> IdentityAccountIds);
