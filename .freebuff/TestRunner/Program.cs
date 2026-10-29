using System.Reflection;
using System.Runtime.ExceptionServices;

// VSTest's test host cannot attach on this machine, so run the compiled xunit tests in-process.
// This is a temporary check, not part of the project.

var assembly = Assembly.Load("Sedhep.Tests");
var passed = 0;
var failed = new List<string>();

foreach (var type in assembly.GetTypes().Where(t => t.Namespace == "Sedhep.Tests" && t.IsClass))
{
    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    foreach (var method in methods)
    {
        var attributes = method.GetCustomAttributesData();
        var isFact = attributes.Any(a => a.AttributeType.Name == "FactAttribute");
        var inlineData = attributes
            .Where(a => a.AttributeType.Name == "InlineDataAttribute")
            .Select(a => a.ConstructorArguments[0].Value as object[])
            .Where(args => args is not null)
            .Cast<object[]>()
            .ToList();
        var isTheory = attributes.Any(a => a.AttributeType.Name == "TheoryAttribute");

        if (!isFact && !(isTheory && inlineData.Count > 0))
        {
            continue;
        }

        var rows = inlineData.Count > 0 ? inlineData : new List<object[]> { Array.Empty<object>() };
        var instance = Activator.CreateInstance(type);

        foreach (var row in rows)
        {
            var name = $"{type.Name}.{method.Name}({string.Join(", ", row)})";
            try
            {
                var result = method.Invoke(instance, row);
                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                }

                passed++;
            }
            catch (Exception ex)
            {
                var actual = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                failed.Add($"{name}: {actual.GetType().Name}: {actual.Message}");

                if (actual is not Xunit.Sdk.XunitException)
                {
                    ExceptionDispatchInfo.Capture(actual).Throw();
                }
            }
        }
    }
}

Console.WriteLine($"passed: {passed}, failed: {failed.Count}");
foreach (var failure in failed)
{
    Console.WriteLine("  " + failure);
}

return failed.Count == 0 ? 0 : 1;
