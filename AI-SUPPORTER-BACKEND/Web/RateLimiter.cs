using System.Collections.Concurrent;

namespace AI_Supporter;

/// <summary>Простой in-memory rate-limiter (фиксированное окно по ключу).</summary>
public static class RateLimiter
{
    private sealed class Bucket
    {
        public DateTime WindowStart;
        public int Count;
    }

    private static readonly ConcurrentDictionary<string, Bucket> Buckets = new();

    public static bool Allow(string key, int max, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var bucket = Buckets.GetOrAdd(key, _ => new Bucket { WindowStart = now });
        lock (bucket)
        {
            if (now - bucket.WindowStart >= window)
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }
            if (bucket.Count >= max) return false;
            bucket.Count++;
            return true;
        }
    }

    public static void Reset(string key)
    {
        Buckets.TryRemove(key, out _);
    }
}
