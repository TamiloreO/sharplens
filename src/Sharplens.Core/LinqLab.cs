using System.Collections;

namespace Sharplens.Core;

public sealed record TraceLine(int Step, string Text, TraceKind Kind);

public enum TraceKind
{
    Build,
    Enumerate,
    Yield,
    Transform,
    Note
}

/// <summary>
/// Runs a LINQ query over an instrumented sequence. The log is the lesson:
/// building the query does nothing, and every enumeration starts again.
/// </summary>
public static class LinqLab
{
    public static IReadOnlyList<TraceLine> Run()
    {
        var lines = new List<TraceLine>();
        var step = 0;

        void Add(string text, TraceKind kind) => lines.Add(new TraceLine(++step, text, kind));

        Add("var query = source.Where(IsEven).Select(Square);", TraceKind.Build);
        var query = new LoggingSequence(new[] { 1, 2, 3, 4, 5 }, () => Add("source yields a value", TraceKind.Yield))
            .Where(IsEven)
            .Select(Square);
        Add("query assigned. No element has been read.", TraceKind.Note);

        Add("foreach (var value in query)", TraceKind.Enumerate);
        foreach (var value in query)
        {
            Add($"loop body receives {value}", TraceKind.Transform);
        }

        Add("first enumeration finished", TraceKind.Note);

        Add("foreach again (same query variable)", TraceKind.Enumerate);
        var count = query.Count();
        Add($"Count() returned {count}, which enumerated everything a second time", TraceKind.Note);

        return lines;

        static bool IsEven(int value) => value % 2 == 0;
        static int Square(int value) => value * value;
    }

    /// <summary>Yields values and reports each yield, so the log shows who pulled them.</summary>
    private sealed class LoggingSequence : IEnumerable<int>
    {
        private readonly int[] _values;
        private readonly Action _onYield;

        public LoggingSequence(int[] values, Action onYield)
        {
            _values = values;
            _onYield = onYield;
        }

        public IEnumerator<int> GetEnumerator()
        {
            foreach (var value in _values)
            {
                _onYield();
                yield return value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
