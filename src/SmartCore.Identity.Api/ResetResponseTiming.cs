using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;

namespace SmartCore.Identity;

public static class ResetResponseTiming
{
    public const int MinimumMilliseconds=200;
    public const int JitterMilliseconds=20;
    private static readonly Meter Meter=new("SmartCore.Identity.Reset");
    private static readonly Counter<long> Overruns=Meter.CreateCounter<long>("reset.response_deadline_overruns");
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
            var remaining=deadline-Stopwatch.GetElapsedTime(started);
            if(remaining>TimeSpan.Zero) await Task.Delay(remaining);
            else Overruns.Add(1,new KeyValuePair<string,object?>("endpoint",endpoint),
                new KeyValuePair<string,object?>("outcome",completed?"success":"failure"));
        }
    }
}
