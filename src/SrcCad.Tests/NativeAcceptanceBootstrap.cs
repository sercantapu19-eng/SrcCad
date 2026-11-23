#if NATIVE_ACCEPTANCE
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class NativeAcceptanceBootstrap
{
    public static void Run()
    {
        Console.WriteLine($"NATIVE PREFLIGHT: base={AppContext.BaseDirectory}, cwd={Environment.CurrentDirectory}, x64={Environment.Is64BitProcess}");
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Native acceptance requires Windows x64.");
        foreach (var name in new[] { "SrcCad.Kernel.Occt.dll", "ijwhost.dll", "TKernel.dll" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(path)) throw new FileNotFoundException("Native deployment is incomplete: " + path, path);
            Console.WriteLine("NATIVE FILE: " + path);
        }
        try
        {
            // An absolute path removes dependence on the shell working directory.
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "SrcCad.Kernel.Occt.dll"));
            RunNative();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("NATIVE LOAD/TEST FAILURE (no native PASS recorded):");
            Console.Error.WriteLine(ex.ToString());
            Console.Error.WriteLine("Bridge, ijwhost, OCCT and their x64 VC++ runtime dependencies must be available in the test deployment. See logs/solid-deployment.log.");
            throw;
        }
    }

    // Do not allow native type resolution to happen before the diagnostic try/catch.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunNative() => NativeAcceptanceTests.Run();
}
#endif
