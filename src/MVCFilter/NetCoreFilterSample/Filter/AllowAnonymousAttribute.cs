namespace NetCoreFilter.Filter;

/// <summary>
/// 允许所有访问特性
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
public class AllowAnonymousAttribute : Attribute
{
}