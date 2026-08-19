namespace PracticumApi.Models;

public class PaginatedResult<T>(List<T> items, int totalCount, int page, int pageSize)
{
    public List<T> Items { get; set; } = items;
    public int TotalCount { get; set; } = totalCount;
    public int Page { get; set; } = page;
    public int PageSize { get; set; } = pageSize;
}