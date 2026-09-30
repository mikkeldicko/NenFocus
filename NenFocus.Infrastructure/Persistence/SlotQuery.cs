using System.Linq.Expressions;
using NenFocus.Domain.ValueObjects;

namespace NenFocus.Infrastructure.Persistence;

/// <summary>
/// Plumbing, no rules: applies one of <see cref="TimeSlot.Filters"/> to whichever entity holds a
/// slot, by putting the entity's slot where the filter's parameter was — e.g.
/// <c>OnSlot(b => b.Scheduled, TimeSlot.Filters.Overlaps(day))</c> becomes
/// <c>b => b.Scheduled.Start &lt; … &amp;&amp; … &lt; b.Scheduled.End</c>, which EF turns into SQL (Gap 88).
/// </summary>
internal static class SlotQuery
{
    public static Expression<Func<TEntity, TResult>> OnSlot<TEntity, TResult>(
        Expression<Func<TEntity, TimeSlot>> slotOf,
        Expression<Func<TimeSlot, TResult>> rule)
    {
        var body = new Replace(rule.Parameters[0], slotOf.Body).Visit(rule.Body);
        return Expression.Lambda<Func<TEntity, TResult>>(body, slotOf.Parameters);
    }

    public static Expression<Func<TEntity, bool>> Not<TEntity>(Expression<Func<TEntity, bool>> filter)
        => Expression.Lambda<Func<TEntity, bool>>(Expression.Not(filter.Body), filter.Parameters);

    private sealed class Replace(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
