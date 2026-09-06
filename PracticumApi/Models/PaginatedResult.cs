namespace PracticumApi.Models;

public class PaginatedResult<T>(List<T> items, int totalCount, int page, int pageSize)
{
    public List<T> Items { get; } = items;
    public int TotalCount { get; } = totalCount;
    public int Page { get; } = page;
    public int PageSize { get; } = pageSize;

    /// <summary>
    /// Общее количество страниц при текущем размере страницы.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
