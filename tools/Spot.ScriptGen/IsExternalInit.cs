// Polyfill so C# records / init-only members compile on netstandard2.0 (required target for Roslyn
// source generators). The type only needs to exist; the compiler references it by name.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
