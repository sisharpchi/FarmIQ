namespace FarmIQ.Admin.Components;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PublicPageAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RouteAccessAttribute : Attribute
{
    public RouteAccessAttribute(params string[] roles)
    {
        Roles = roles ?? [];
    }

    public IReadOnlyCollection<string> Roles { get; }
}
