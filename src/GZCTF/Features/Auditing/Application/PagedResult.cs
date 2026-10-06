namespace GZCTF.Features.Auditing.Application;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
