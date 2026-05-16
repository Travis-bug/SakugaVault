namespace SakugaVault.Extensions;

/// <summary>
/// Central place for named CORS policies.
/// Keeping the policy name here avoids string duplication as the API grows and makes future
/// frontend environments easier to manage.
/// </summary>
public static class CorsPolicyNames
{
    public const string Frontend = "Frontend";
}
