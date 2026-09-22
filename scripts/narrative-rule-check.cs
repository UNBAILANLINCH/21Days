// 手动检查入口：只运行不依赖 Unity 生命周期的 Narrative／Dialogue NUnit 用例。
// 用本机 csc 编译后由 Unity 随附 Mono 执行；不替代 Unity Test Runner 或 PlayMode 验证。
using System;
using System.IO;
using System.Reflection;

internal static class NarrativeRuleCheck
{
    private static int Main(string[] args)
    {
        foreach (string dependency in args) Assembly.LoadFrom(Path.GetFullPath(dependency));
        return Run();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Run()
    {
        int passed = 0;
        int failed = 0;
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.Namespace != "Game.Tests.EditMode.Dialogue" && type.Namespace != "Game.Tests.EditMode.Narrative") continue;
            foreach (MethodInfo method in type.GetMethods())
            {
                bool test = false;
                foreach (CustomAttributeData attribute in method.GetCustomAttributesData())
                    test |= attribute.AttributeType.FullName == "NUnit.Framework.TestAttribute";
                if (!test) continue;
                try
                {
                    method.Invoke(Activator.CreateInstance(type), null);
                    Console.WriteLine("PASS " + type.Name + "." + method.Name);
                    passed++;
                }
                catch (Exception exception)
                {
                    Console.WriteLine("FAIL " + type.Name + "." + method.Name + ": " + (exception.InnerException ?? exception));
                    failed++;
                }
            }
        }
        Console.WriteLine("Pure rule checks: " + passed + " passed, " + failed + " failed. Unity runtime checks not run.");
        return failed == 0 && passed > 0 ? 0 : 1;
    }
}
