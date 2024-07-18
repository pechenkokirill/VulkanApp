using System.Runtime.InteropServices;

namespace VulkanApp;
internal static class VulkanInterop
{
    [DllImport("glfw", CallingConvention = CallingConvention.Cdecl, EntryPoint = "glfwGetRequiredInstanceExtensions")]
    public static extern IntPtr GetRequiredInstanceExtensions(out uint count);
}
