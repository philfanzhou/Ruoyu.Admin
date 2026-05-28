using Microsoft.AspNetCore.Mvc;
using Admin.WebApi.Models;

namespace Admin.WebApi.Controllers;

[Route("api/admin/enum-options")]
[ApiController]
public class EnumOptionsController : ControllerBase
{
    private static readonly List<EnumOption> UploadStatuses =
    [
        new(1, "PENDING", "待处理"),
        new(2, "PROCESSING", "处理中"),
        new(3, "COMPLETED", "已完成"),
        new(4, "FAILED", "失败"),
        new(5, "RETURNED", "已退回"),
    ];

    private static readonly List<EnumOption> Grades =
    [
        new(1, "GRADE_PRIMARY_1", "小学一年级"),
        new(2, "GRADE_PRIMARY_2", "小学二年级"),
        new(3, "GRADE_PRIMARY_3", "小学三年级"),
        new(4, "GRADE_PRIMARY_4", "小学四年级"),
        new(5, "GRADE_PRIMARY_5", "小学五年级"),
        new(6, "GRADE_PRIMARY_6", "小学六年级"),
        new(7, "GRADE_MIDDLE_1", "初中一年级"),
        new(8, "GRADE_MIDDLE_2", "初中二年级"),
        new(9, "GRADE_MIDDLE_3", "初中三年级"),
        new(10, "GRADE_HIGH_1", "高中一年级"),
        new(11, "GRADE_HIGH_2", "高中二年级"),
        new(12, "GRADE_HIGH_3", "高中三年级"),
    ];

    private static readonly List<EnumOption> Subjects =
    [
        new(1, "CHINESE", "语文"),
        new(2, "MATHEMATICS", "数学"),
        new(3, "ENGLISH", "英语"),
        new(4, "PHYSICS", "物理"),
        new(5, "CHEMISTRY", "化学"),
        new(6, "BIOLOGY", "生物"),
        new(7, "HISTORY", "历史"),
        new(8, "GEOGRAPHY", "地理"),
        new(9, "POLITICS", "政治"),
    ];

    private static readonly List<EnumOption> Classifications =
    [
        new(1, "MISTAKE", "错题"),
        new(2, "HOMEWORK", "作业"),
    ];

    private static readonly List<EnumOption> ReviewStatuses =
    [
        new(1, "PENDING_REVIEW", "待审核"),
        new(2, "CONFIRMED", "已确认"),
        new(3, "REJECTED", "已驳回"),
    ];

    private static readonly List<EnumOption> MistakeTypes =
    [
        new(1, "CONCEPT", "概念理解"),
        new(2, "CALCULATION", "计算错误"),
        new(3, "MISREAD", "审题不清"),
        new(4, "METHOD", "思路错误"),
        new(5, "OMISSION", "步骤遗漏"),
        new(6, "OTHER", "其他"),
    ];

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
