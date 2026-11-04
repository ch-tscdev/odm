using System.Linq.Expressions;
using System.Reflection;
using Odm.Modules.Devices.Domain;

namespace Odm.Modules.Devices.Application;

/// <summary>
/// Translates group rules into an expression that EF Core turns into SQL.
/// Text comparisons are case-insensitive.
/// </summary>
public static class GroupFilter
{
    private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo EndsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    // Wrap the value in an object so that EF passes it to the database as a parameter (not as a literal).
    private sealed class ValueBox(string value)
    {
        public string Value { get; } = value;
    }

    /// <summary>Filter of a stored group; catch-all groups need all groups to exclude the members of the others.</summary>
    public static Expression<Func<Device, bool>> For(DeviceGroup group, IReadOnlyCollection<DeviceGroup> allGroups) =>
        For(group.Id, group.MatchMode, group.Rules, group.IsCatchAll, allGroups);

    /// <summary>
    /// Filter of a group definition (also unsaved, for the preview). <paramref name="selfId"/> is the group itself, which is
    /// never one of the "other" groups. A catch-all group matches its own rules (all devices if it has none) and none of
    /// the other groups that count for catch-all groups; other catch-all groups never count.
    /// </summary>
    public static Expression<Func<Device, bool>> For(
        Guid? selfId, GroupMatchMode mode, IReadOnlyCollection<GroupRule> rules, bool isCatchAll,
        IReadOnlyCollection<DeviceGroup> allGroups)
    {
        if (!isCatchAll)
        {
            return Build(mode, rules);
        }

        var device = Expression.Parameter(typeof(Device), "d");
        Expression body = rules.Count == 0 ? Expression.Constant(true) : Rebind(Build(mode, rules), device);

        Expression? inOther = null;
        foreach (var other in allGroups.Where(g => g.Id != selfId && !g.IsCatchAll && g.CountsForCatchAll && g.Rules.Count > 0))
        {
            var predicate = Rebind(Build(other.MatchMode, other.Rules), device);
            inOther = inOther is null ? predicate : Expression.OrElse(inOther, predicate);
        }

        if (inOther is not null)
        {
            body = Expression.AndAlso(body, Expression.Not(inOther));
        }

        return Expression.Lambda<Func<Device, bool>>(body, device);
    }

    /// <summary>Uses the body of a filter with another parameter so that several filters can be combined.</summary>
    private static Expression Rebind(Expression<Func<Device, bool>> filter, ParameterExpression device) =>
        new ParameterReplacer(filter.Parameters[0], device).Visit(filter.Body);

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>A group without rules contains no devices.</summary>
    public static Expression<Func<Device, bool>> Build(GroupMatchMode mode, IReadOnlyCollection<GroupRule> rules)
    {
        if (rules.Count == 0)
        {
            return _ => false;
        }

        var device = Expression.Parameter(typeof(Device), "d");
        Expression? body = null;

        foreach (var rule in rules)
        {
            var predicate = BuildRule(device, rule);
            body = body is null
                ? predicate
                : mode == GroupMatchMode.All
                    ? Expression.AndAlso(body, predicate)
                    : Expression.OrElse(body, predicate);
        }

        return Expression.Lambda<Func<Device, bool>>(body!, device);
    }

    private static Expression BuildRule(ParameterExpression device, GroupRule rule)
    {
        if (rule.Field == GroupField.OsFamily)
        {
            var family = Enum.Parse<OsFamily>(rule.Value, ignoreCase: true);
            return Expression.Equal(
                Expression.Property(device, nameof(Device.OsFamily)),
                Expression.Constant(family));
        }

        var member = Expression.Property(device, PropertyName(rule.Field));
        var lowered = Expression.Call(member, ToLower);
        var value = Expression.Property(
            Expression.Constant(new ValueBox(rule.Value.ToLowerInvariant())),
            nameof(ValueBox.Value));

        Expression comparison = rule.Operator switch
        {
            MatchOperator.Is => Expression.Equal(lowered, value),
            MatchOperator.StartsWith => Expression.Call(lowered, StartsWith, value),
            MatchOperator.EndsWith => Expression.Call(lowered, EndsWith, value),
            MatchOperator.Contains => Expression.Call(lowered, Contains, value),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Operator, null)
        };

        // An empty field never matches, explicitly: in SQL "NOT (NULL LIKE ...)" would be NULL (no match), which would wrongly
        // drop devices with empty fields from catch-all groups; in memory it avoids a NullReferenceException.
        return Expression.AndAlso(Expression.NotEqual(member, Expression.Constant(null, typeof(string))), comparison);
    }

    private static string PropertyName(GroupField field) => field switch
    {
        GroupField.Name => nameof(Device.Name),
        GroupField.OsName => nameof(Device.OsName),
        GroupField.OsVersion => nameof(Device.OsVersion),
        GroupField.PrimaryUser => nameof(Device.PrimaryUser),
        GroupField.OrgUnit => nameof(Device.PrimaryUserOrgUnit),
        GroupField.CostCenter => nameof(Device.CostCenter),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };
}
