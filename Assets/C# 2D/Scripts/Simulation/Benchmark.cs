using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

public class CallStats
{
    private List<long> calls = new();

    public void AddCall(long call)
    {
        calls.Add(call);
    }

    public int Count => calls.Count;

    public double GetAverage() => calls.Average();
    public double GetSum() => calls.Sum();
    public string GetStats() => $"Calls: {calls.Count}, Avg: {calls.Average()}";
}

public static class Watcher
{
    private static ConcurrentDictionary<string, CallStats> _stats = new();

    public static void ExecuteWithTimer(string name, Action action)
    {
        var sw = new Stopwatch();
        sw.Start();
        action();
        sw.Stop();
        _stats.GetOrAdd(name, new CallStats()).AddCall(sw.ElapsedTicks);
    }

    public static int Count => _stats.First().Value.Count;

    public static string LogImportant()
    {
        var sb = new StringBuilder();
        var sortedStats = _stats.OrderBy(x => int.Parse(string.Concat(x.Key.TakeWhile(char.IsDigit))));

        foreach (var stat in sortedStats)
        {
            if (stat.Value.GetAverage() > 0)
                sb.AppendLine(stat.Key + ": " + stat.Value.GetAverage());
        }

        return sb.ToString();
    }

    public static string Log()
    {
        var sb = new StringBuilder();
        var sortedStats = _stats.OrderBy(x => int.Parse(string.Concat(x.Key.TakeWhile(char.IsDigit))));

        foreach (var stat in sortedStats)
            sb.AppendLine(stat.Key + ": " + stat.Value.GetAverage());

        return sb.ToString();
    }

    public static void Reset()
    {
        _stats.Clear();
    }
}