namespace MyCabs.Api.Common;

public record PagedResult<T>(IEnumerable<T> Items, int Page, int PageSize, long Total);