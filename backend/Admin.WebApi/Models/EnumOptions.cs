namespace Admin.WebApi.Models;

public sealed record EnumOption(int Value, string Name, string DisplayName);

public sealed record EnumOptionsResponse(
    List<EnumOption> UploadStatuses,
    List<EnumOption> Grades,
    List<EnumOption> Subjects,
    List<EnumOption> Classifications,
    List<EnumOption> ReviewStatuses,
    List<EnumOption> MistakeTypes
);
