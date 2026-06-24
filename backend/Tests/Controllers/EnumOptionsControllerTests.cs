using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Ruoyu.Study.Common.Constants;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// EnumOptionsController UT：覆盖 GetAll 聚合返回所有枚举选项。
/// </summary>
public class EnumOptionsControllerTests
{
    private readonly EnumOptionsController _controller = new();

    [Fact]
    public void GetAll_ReturnsOkWithEnumOptionsResponse()
    {
        var result = _controller.GetAll();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeOfType<EnumOptionsResponse>();
    }

    [Fact]
    public void GetAll_UploadStatuses_HasExpectedCountAndMapping()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        response.UploadStatuses.Should().HaveCount(UploadStatusConstants.EnglishNames.Count);

        foreach (var option in response.UploadStatuses)
        {
            option.Name.Should().Be(UploadStatusConstants.EnglishNames[option.Value]);
            option.DisplayName.Should().Be(UploadStatusConstants.DisplayNames[option.Value]);
        }
    }

    [Fact]
    public void GetAll_Grades_HasExpectedCountAndMapping()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        response.Grades.Should().HaveCount(GradeConstants.EnglishNames.Count);

        foreach (var option in response.Grades)
        {
            option.Name.Should().Be(GradeConstants.EnglishNames[option.Value]);
            // 注意：Grades 使用 FullDisplayNames 而非 DisplayNames
            option.DisplayName.Should().Be(GradeConstants.FullDisplayNames[option.Value]);
        }
    }

    [Fact]
    public void GetAll_Subjects_HasExpectedCountAndMapping()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        response.Subjects.Should().HaveCount(SubjectConstants.EnglishNames.Count);

        foreach (var option in response.Subjects)
        {
            option.Name.Should().Be(SubjectConstants.EnglishNames[option.Value]);
            option.DisplayName.Should().Be(SubjectConstants.DisplayNames[option.Value]);
        }
    }

    [Fact]
    public void GetAll_Classifications_HasExpectedCountAndMapping()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        response.Classifications.Should().HaveCount(ClassificationConstants.EnglishNames.Count);

        foreach (var option in response.Classifications)
        {
            option.Name.Should().Be(ClassificationConstants.EnglishNames[option.Value]);
            option.DisplayName.Should().Be(ClassificationConstants.DisplayNames[option.Value]);
        }
    }

    [Fact]
    public void GetAll_ReviewStatuses_HasExpectedCountAndMapping()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        response.ReviewStatuses.Should().HaveCount(ReviewStatusConstants.EnglishNames.Count);

        foreach (var option in response.ReviewStatuses)
        {
            option.Name.Should().Be(ReviewStatusConstants.EnglishNames[option.Value]);
            option.DisplayName.Should().Be(ReviewStatusConstants.DisplayNames[option.Value]);
        }
    }

    [Fact]
    public void GetAll_AllListsAreNonEmpty()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        response.UploadStatuses.Should().NotBeEmpty();
        response.Grades.Should().NotBeEmpty();
        response.Subjects.Should().NotBeEmpty();
        response.Classifications.Should().NotBeEmpty();
        response.ReviewStatuses.Should().NotBeEmpty();
    }

    [Fact]
    public void GetAll_GradesFirstItemIsPrimary1()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        var firstGrade = response.Grades[0];
        firstGrade.Value.Should().Be(1);
        firstGrade.Name.Should().Be("GRADE_PRIMARY_1");
        firstGrade.DisplayName.Should().Be("小学一年级");
    }

    [Fact]
    public void GetAll_SubjectsContainsEnglish()
    {
        var result = _controller.GetAll();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = (EnumOptionsResponse)okResult.Value!;

        var english = response.Subjects.Should().ContainSingle(s => s.Name == "ENGLISH").Subject;
        english.Value.Should().Be(3);
        english.DisplayName.Should().Be("英语");
    }
}
