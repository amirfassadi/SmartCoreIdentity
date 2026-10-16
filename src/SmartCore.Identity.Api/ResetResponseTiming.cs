using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;

namespace SmartCore.Identity;

public sealed record ResetTimingSample(double WorkMilliseconds,double TotalMilliseconds,double DeadlineMilliseconds);

public static class ResetResponseTiming
{
    public const int MinimumMilliseconds=200;
    public const int JitterMilliseconds=20;
    private static readonly Meter Meter=new("SmartCore.Identity.Reset");
    private static readonly Counter<long> Overruns=Meter.CreateCounter<long>("reset.response_deadline_overruns");
    private static readonly AsyncLocal<Action<ResetTimingSample>?> TestObserver=new();
    // Trusted Development diagnostics only; never enable for public callers.
    public static IDisposable Diagnostics(Action<ResetTimingSample> observe)
    {var previous=TestObserver.Value;TestObserver.Value=observe;return new DiagnosticScope(previous);}
    private sealed class DiagnosticScope(Action<ResetTimingSample>? previous):IDisposable
    {public void Dispose()=>TestObserver.Value=previous;}
    // A response deadline selected before account lookup, independent of its outcome.
    // Work must dispose its transactions/connections before this asynchronous wait.
    public static async Task<T> Run<T>(string endpoint,Func<Task<T>> work)
    {
        var started=Stopwatch.GetTimestamp();
        var deadline=TimeSpan.FromMilliseconds(MinimumMilliseconds+RandomNumberGenerator.GetInt32(JitterMilliseconds+1));
        var completed=false;
        try {var result=await work();completed=true;return result;}
        finally
        {
            var workElapsed=Stopwatch.GetElapsedTime(started);
            var remaining=deadline-workElapsed;
            if(remaining>TimeSpan.Zero) await Task.Delay(remaining);
            else Overruns.Add(1,new KeyValuePair<string,object?>("endpoint",endpoint),
                new KeyValuePair<string,object?>("outcome",completed?"success":"failure"));
            TestObserver.Value?.Invoke(new(workElapsed.TotalMilliseconds,Stopwatch.GetElapsedTime(started).TotalMilliseconds,deadline.TotalMilliseconds));
        }
    }
}
