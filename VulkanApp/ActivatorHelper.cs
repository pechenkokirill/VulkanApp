using System.Reflection;

namespace VulkanApp;
internal static class ActivatorHelper
{
    public static T CreateInstance<T>(params object?[]? args) where T : class
    {
        return (T)Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, args, null)!;
    }

    public static void SetField<T>(object? instance, string filedName, object? value)
    {
        typeof(T).GetField(filedName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(instance, value);
    }
}
