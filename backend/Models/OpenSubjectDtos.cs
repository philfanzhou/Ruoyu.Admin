namespace Admin.WebApi.Models;

/// <summary>
/// 开放学科 DTO
/// </summary>
public record OpenSubjectDto(
    string Id,
    int Subject,
    string OpenStartDate,
    string? OpenEndDate,
    bool IsActive);

/// <summary>
/// 设置开放学科请求
/// </summary>
public record SetOpenSubjectsRequest(List<SubjectItem> Subjects);

/// <summary>
/// 学科项
/// </summary>
public record SubjectItem(int Subject, string OpenStartDate, string? OpenEndDate);

/// <summary>
/// 学科选项
/// </summary>
public record SubjectOption(int Value, string Name, string DisplayName);