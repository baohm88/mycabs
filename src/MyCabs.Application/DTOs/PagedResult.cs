namespace MyCabs.Application.DTOs;

public record PagedResult<T>(IEnumerable<T> Items, int Page, int PageSize, long Total);

public record PagedQuery(int Page = 1, int PageSize = 20);
