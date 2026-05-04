using Admin.WebApi.Models;
using FluentAssertions;
using Xunit;

namespace Admin.WebApi.Tests.Models;

public class StudentDtoTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        var id = "student-123";
        var name = "John Doe";
        var grade = 10;
        var accountIds = new List<string> { "account-1", "account-2" };
        var createdAt = 1234567890L;
        var updatedAt = 1234567891L;

        var dto = new StudentDto(id, name, grade, accountIds, createdAt, updatedAt);

        dto.Id.Should().Be(id);
        dto.Name.Should().Be(name);
        dto.Grade.Should().Be(grade);
        dto.IdentityAccountIds.Should().BeEquivalentTo(accountIds);
        dto.CreatedAt.Should().Be(createdAt);
        dto.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void Constructor_WithEmptyAccountIds_ShouldAcceptEmptyList()
    {
        var dto = new StudentDto("id", "name", 1, new List<string>(), 0, 0);

        dto.IdentityAccountIds.Should().BeEmpty();
    }
}

public class CreateStudentRequestTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        var name = "Jane Doe";
        var grade = 11;
        var accountIds = new List<string> { "acc-1", "acc-2" };

        var request = new CreateStudentRequest(name, grade, accountIds);

        request.Name.Should().Be(name);
        request.Grade.Should().Be(grade);
        request.IdentityAccountIds.Should().BeEquivalentTo(accountIds);
    }
}

public class UpdateStudentRequestTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        var name = "Updated Name";
        var grade = 12;
        var accountIds = new List<string> { "acc-3" };

        var request = new UpdateStudentRequest(name, grade, accountIds);

        request.Name.Should().Be(name);
        request.Grade.Should().Be(grade);
        request.IdentityAccountIds.Should().BeEquivalentTo(accountIds);
    }
}

public class LinkUserRequestTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        var identityAccountId = "account-123";

        var request = new LinkUserRequest(identityAccountId);

        request.IdentityAccountId.Should().Be(identityAccountId);
    }
}

public class PagedResponseTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        var items = new List<StudentDto>
        {
            new StudentDto("1", "Student 1", 10, new List<string>(), 0, 0),
            new StudentDto("2", "Student 2", 11, new List<string>(), 0, 0)
        };
        var totalCount = 100;
        var page = 1;
        var pageSize = 20;

        var response = new PagedResponse<StudentDto>(items, totalCount, page, pageSize);

        response.Items.Should().BeEquivalentTo(items);
        response.Total.Should().Be(totalCount);
        response.Page.Should().Be(page);
        response.PageSize.Should().Be(pageSize);
    }
}

public class OperationResponseTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        var success = true;
        var message = "Operation completed successfully";

        var response = new OperationResponse(success, message);

        response.Success.Should().Be(success);
        response.Message.Should().Be(message);
    }

    [Fact]
    public void Constructor_WithFailure_ShouldSetSuccessFalse()
    {
        var response = new OperationResponse(false, "Operation failed");

        response.Success.Should().BeFalse();
        response.Message.Should().Be("Operation failed");
    }
}

public class ErrorResponseTests
{
    [Fact]
    public void Constructor_ShouldSetMessageCorrectly()
    {
        var message = "An error occurred";

        var response = new ErrorResponse(message);

        response.Message.Should().Be(message);
    }
}

public class IdentityAccountDtoTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        var userId = "user-123";
        var username = "johndoe";
        var displayName = "John Doe";
        var phone = "13800138000";
        var remark = "VIP user";

        var dto = new IdentityAccountDto(userId, username, displayName, phone, remark);

        dto.UserId.Should().Be(userId);
        dto.Username.Should().Be(username);
        dto.DisplayName.Should().Be(displayName);
        dto.Phone.Should().Be(phone);
        dto.Remark.Should().Be(remark);
    }

    [Fact]
    public void Constructor_WithEmptyValues_ShouldAcceptEmptyStrings()
    {
        var dto = new IdentityAccountDto("", "", "", "", "");

        dto.UserId.Should().BeEmpty();
        dto.Username.Should().BeEmpty();
        dto.DisplayName.Should().BeEmpty();
        dto.Phone.Should().BeEmpty();
        dto.Remark.Should().BeEmpty();
    }
}
