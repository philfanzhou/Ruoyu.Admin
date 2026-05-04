namespace Admin.WebApi.Models;

/// <summary>
/// Open subject DTO
/// </summary>
public record OpenSubjectDto(
    string Id,
    int Subject,
    string OpenStartDate,
    string? OpenEndDate,
    bool IsActive);

/// <summary>
/// Request for setting open subjects
/// </summary>
public record SetOpenSubjectsRequest(List<SubjectItem> Subjects);

/// <summary>
/// Subject item
/// </summary>
public record SubjectItem(int Subject, string OpenStartDate, string? OpenEndDate);

/// <summary>
/// Subject option
/// </summary>
public record SubjectOption(int Value, string Name, string DisplayName);
