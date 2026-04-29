namespace Admin.WebApi.Models;

internal sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
