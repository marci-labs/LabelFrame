// net48 编译腿的 init / record 支持（IsExternalInit 自 .NET 5 起才由运行时提供）
#if NET48
namespace System.Runtime.CompilerServices
{
    using System.CodeDom.Compiler;

    [GeneratedCode("LabelFrame.Bootstrapper", "1.0.0.0")]
    internal sealed class IsExternalInit
    {
    }
}
#endif
