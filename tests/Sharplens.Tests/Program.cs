using Sharplens.Core;

var failures = 0;
var checks = 0;

void Check(string name, bool ok, string? detail = null)
{
    checks++;
    if (ok)
    {
        Console.WriteLine($"ok   {name}");
        return;
    }

    failures++;
    Console.WriteLine($"FAIL {name}{(detail is null ? "" : ": " + detail)}");
}

// ---------------------------------------------------------------- async scene

const string twoAwaits = """
    public async Task<int> LoadAsync()
    {
        var first = await Task.FromResult(1);
        var second = await Task.FromResult(2);
        return first + second;
    }
""";

var asyncResult = AsyncInspector.Inspect(twoAwaits);
Check("async snippet compiles", asyncResult.Success, string.Join(" | ", asyncResult.Diagnostics));
Check("state machine type is found", asyncResult.StateMachineType is not null, asyncResult.StateMachineType ?? "null");
Check("state machine implements IAsyncStateMachine",
    asyncResult.Interfaces.Contains("IAsyncStateMachine"), string.Join(",", asyncResult.Interfaces));
Check("two awaits counted", asyncResult.AwaitCount == 2, asyncResult.AwaitCount.ToString());
Check("states listed for start, two resumptions and completion", asyncResult.States.Count == 4,
    string.Join(" | ", asyncResult.States));
Check("state field explained",
    asyncResult.Fields.Any(f => f.Name.Contains("state")), string.Join(",", asyncResult.Fields.Select(f => f.Name)));
Check("builder field explained",
    asyncResult.Fields.Any(f => f.Name.Contains("builder")), string.Join(",", asyncResult.Fields.Select(f => f.Name)));
Check("MoveNext is present",
    asyncResult.Methods.Any(m => m.Name == "MoveNext"), string.Join(",", asyncResult.Methods.Select(m => m.Name)));
Check("compiler generated types are visible",
    asyncResult.AssemblyTypes.Any(t => t.Contains("<")), string.Join(",", asyncResult.AssemblyTypes));

var syncResult = AsyncInspector.Inspect("public int Add(int a, int b) => a + b;");
Check("a synchronous method produces no state machine", syncResult.Success && syncResult.StateMachineType is null);

var broken = AsyncInspector.Inspect("public async Task Broken( { }");
Check("a syntax error is reported rather than thrown",
    !broken.Success && broken.Diagnostics.Count > 0, string.Join(" | ", broken.Diagnostics));

// ----------------------------------------------------------------- linq scene

var trace = LinqLab.Run();
Check("the trace has entries", trace.Count > 8, trace.Count.ToString());
Check("query build is the first line", trace[0].Kind == TraceKind.Build);
Check("no value is yielded before the build step completes",
    trace.First(t => t.Kind == TraceKind.Yield).Step > trace[0].Step);
Check("both enumerations are visible",
    trace.Count(t => t.Kind == TraceKind.Enumerate) == 2,
    trace.Count(t => t.Kind == TraceKind.Enumerate).ToString());

// ---------------------------------------------------------- allocation scene

var allocations = AllocationLab.Run();
Check("allocation lab returns four results", allocations.Count == 4);
Check("every result produced a value", allocations.All(a => a.Sink > 0));

var measured = allocations.Sum(a => a.Bytes);
if (measured <= 0)
{
    Console.WriteLine("note allocation counters report zero on this runtime, so the comparison is skipped");
}
else
{
    var concat = allocations[0].Bytes;
    var builder = allocations[1].Bytes;
    Check("string concatenation allocates more than StringBuilder", concat > builder, $"{concat} vs {builder}");
    Check("boxing allocates", allocations[2].Bytes > 0, allocations[2].Bytes.ToString());
}

Console.WriteLine();
Console.WriteLine($"{checks - failures}/{checks} checks passed");
return failures == 0 ? 0 : 1;
