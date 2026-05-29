using Microsoft.AspNetCore.Mvc;
using Admin.WebApi.Models;
using Ruoyu.Study.Common.Constants;

namespace Admin.WebApi.Controllers;

[Route("api/admin/enum-options")]
[ApiController]
public class EnumOptionsController : ControllerBase
{
    private static readonly List<EnumOption> UploadStatuses = UploadStatusConstants.EnglishNames
        .Select(kvp => new EnumOption(kvp.Key, kvp.Value, UploadStatusConstants.DisplayNames[kvp.Key]))
        .ToList();

    private static readonly List<EnumOption> Grades = GradeConstants.EnglishNames
        .Select(kvp => new EnumOption(kvp.Key, kvp.Value, GradeConstants.FullDisplayNames[kvp.Key]))
        .ToList();

    private static readonly List<EnumOption> Subjects = SubjectConstants.EnglishNames
        .Select(kvp => new EnumOption(kvp.Key, kvp.Value, SubjectConstants.DisplayNames[kvp.Key]))
        .ToList();

    private static readonly List<EnumOption> Classifications = ClassificationConstants.EnglishNames
        .Select(kvp => new EnumOption(kvp.Key, kvp.Value, ClassificationConstants.DisplayNames[kvp.Key]))
        .ToList();

    private static readonly List<EnumOption> ReviewStatuses = ReviewStatusConstants.EnglishNames
        .Select(kvp => new EnumOption(kvp.Key, kvp.Value, ReviewStatusConstants.DisplayNames[kvp.Key]))
        .ToList();

    private static readonly List<EnumOption> MistakeTypes = ErrorTypeConstants.EnglishNames
        .Select(kvp => new EnumOption(kvp.Key, kvp.Value, ErrorTypeConstants.DisplayNames[kvp.Key]))
        .ToList();

    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(new EnumOptionsResponse(
            UploadStatuses,
            Grades,
            Subjects,
            Classifications,
            ReviewStatuses,
            MistakeTypes
        ));
    }
}
