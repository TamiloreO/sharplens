using System.Text;

namespace Sharplens.Core;

public sealed record AllocationResult(string Name, long Bytes, int Sink, string Note);

/// <summary>
/// Measures managed allocations around small operations, using the runtime's
/// own counter rather than a guess. If the counter is unavailable the value
/// comes back as zero and the UI says so instead of inventing a number.
/// </summary>
public static class AllocationLab
{
    private static int _sink;

    public static IReadOnlyList<AllocationResult> Run()
    {
        return new List<AllocationResult>
        {
            Measure("string += in a loop, 10,000 times", () =>
            {
                var text = string.Empty;
                for (var i = 0; i < 10_000; i++) text += "x";
                return text.Length;
            }, "Each += makes a new string and copies the old one, so the cost grows quadratically."),

            Measure("StringBuilder, 10,000 appends", () =>
            {
                var builder = new StringBuilder();
                for (var i = 0; i < 10_000; i++) builder.Append('x');
                return builder.Length;
            }, "One growing buffer instead of 10,000 strings."),

            Measure("boxing 10,000 ints into object[]", () =>
            {
                var items = new object[10_000];
                for (var i = 0; i < 10_000; i++) items[i] = i;
                return items.Length;
            }, "Assigning a value type to object allocates a box on the heap for each value."),

            Measure("List<int> grown to 10,000", () =>
            {
                var list = new List<int>();
                for (var i = 0; i < 10_000; i++) list.Add(i);
                return list.Count;
            }, "The backing array doubles, so old buffers become garbage along the way."),
        };
    }

    private static AllocationResult Measure(string name, Func<int> action, string note)
    {
        // Warm the code path so JIT and static initialisation are not counted.
        _sink += action();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = action();
        var after = GC.GetAllocatedBytesForCurrentThread();

        _sink = result;
        return new AllocationResult(name, after - before, result, note);
    }
}
