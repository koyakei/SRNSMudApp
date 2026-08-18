using System.Linq.Expressions;
using SRNSMudApp.Models.Unions;

namespace SRNSMudApp.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<TSource> WhereSome<TSource, TValue>(
        this IQueryable<TSource> source,
        Option<TValue> option,
        Expression<Func<TValue, Expression<Func<TSource, bool>>>> predicateFactory)
    {
        return option switch
        {
            Some<TValue> some => source.Where(predicateFactory.Compile()(some.Value)),
            None _ => source,
            null => source
        };
    }

    public static IQueryable<TSource> WhereTrue<TSource>(
        this IQueryable<TSource> source,
        bool condition,
        Expression<Func<TSource, bool>> predicate)
    {
        return condition switch
        {
            true => source.Where(predicate),
            false => source
        };
    }
}
