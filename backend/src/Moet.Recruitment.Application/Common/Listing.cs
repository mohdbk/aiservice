using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Moet.Recruitment.Application.Common;

/// <summary>Paging, sorting and search for every list: ?page=1&amp;size=25&amp;sort=created&amp;dir=desc&amp;q=...</summary>
public sealed record ListQuery(int? Page = null, int? Size = null, string? Sort = null, string? Dir = null, string? Q = null)
{
    public static readonly int[] Sizes = [10, 25, 50, 100];

    public int PageNo => Math.Max(1, Page ?? 1);
    public int PageSize => Sizes.Contains(Size ?? 0) ? Size!.Value : 25;
    public bool Descending => !string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase);
    public string Term => (Q ?? "").Trim();
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int Size, int Pages, string Sort, string Dir);

/// <summary>A sortable column: the key the client sends and the expression it sorts by (only listed keys are allowed).</summary>
public sealed class Sorts<T>
{
    readonly Dictionary<string, LambdaExpression> _keys = new(StringComparer.OrdinalIgnoreCase);
    public string Default { get; }
    public bool DefaultDescending { get; }

    public Sorts(string defaultKey, bool defaultDescending = true)
    {
        Default = defaultKey;
        DefaultDescending = defaultDescending;
    }

    public Sorts<T> Add<TKey>(string key, Expression<Func<T, TKey>> by)
    {
        _keys[key] = by;
        return this;
    }

    public (IQueryable<T> Query, string Key, bool Desc) Apply(IQueryable<T> q, ListQuery lq)
    {
        var key = lq.Sort != null && _keys.ContainsKey(lq.Sort) ? lq.Sort : Default;
        var desc = lq.Sort != null && _keys.ContainsKey(lq.Sort) ? lq.Descending : DefaultDescending;
        var expr = _keys[key];
        var method = desc ? "OrderByDescending" : "OrderBy";
        var call = Expression.Call(typeof(Queryable), method, [typeof(T), expr.ReturnType], q.Expression, Expression.Quote(expr));
        return (q.Provider.CreateQuery<T>(call), key, desc);
    }
}

public static class Listing
{
    /// <summary>Sorts, pages and projects a query in the database.</summary>
    public static async Task<PageResult<TOut>> PageAsync<T, TOut>(this IQueryable<T> q, ListQuery lq, Sorts<T> sorts,
        Expression<Func<T, TOut>> select, CancellationToken ct = default)
    {
        var total = await q.CountAsync(ct);
        var (sorted, key, desc) = sorts.Apply(q, lq);
        var size = lq.PageSize;
        var pages = Math.Max(1, (total + size - 1) / size);
        var page = Math.Min(lq.PageNo, pages);
        var items = await sorted.Skip((page - 1) * size).Take(size).Select(select).ToListAsync(ct);
        return new PageResult<TOut>(items, total, page, size, pages, key, desc ? "desc" : "asc");
    }

    /// <summary>The same for a list already in memory (for example, wording keys).</summary>
    public static PageResult<T> Page<T>(this IEnumerable<T> rows, ListQuery lq, IReadOnlyDictionary<string, Func<T, object?>> sorts, string defaultKey)
    {
        var key = lq.Sort != null && sorts.ContainsKey(lq.Sort) ? lq.Sort : defaultKey;
        var desc = lq.Sort != null && sorts.ContainsKey(lq.Sort) && lq.Descending;
        var list = (desc ? rows.OrderByDescending(sorts[key]) : rows.OrderBy(sorts[key])).ToList();
        var size = lq.PageSize;
        var pages = Math.Max(1, (list.Count + size - 1) / size);
        var page = Math.Min(lq.PageNo, pages);
        return new PageResult<T>(list.Skip((page - 1) * size).Take(size).ToList(), list.Count, page, size, pages, key, desc ? "desc" : "asc");
    }
}
