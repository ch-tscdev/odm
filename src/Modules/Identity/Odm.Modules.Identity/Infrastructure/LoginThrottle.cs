using Microsoft.Extensions.Caching.Memory;

namespace Odm.Modules.Identity.Infrastructure;

/// <summary>
/// Limits failed sign-ins per account (in addition to the limit per client address): after <see cref="MaxFailures"/>
/// failures within <see cref="Window"/> the account cannot sign in until the window has passed. The response stays the
/// same as for a wrong password, so the lock does not reveal whether an account exists.
/// </summary>
public sealed class LoginThrottle(IMemoryCache cache, TimeProvider time)
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset Since;
    }

    public bool IsLocked(string userName) =>
        cache.TryGetValue(Key(userName), out Counter? counter) && counter!.Failures >= MaxFailures &&
        time.GetUtcNow() - counter.Since < Window;

    public void RecordFailure(string userName)
    {
        var now = time.GetUtcNow();
        var key = Key(userName);
        if (!cache.TryGetValue(key, out Counter? counter) || now - counter!.Since >= Window)
        {
            counter = new Counter { Since = now };
        }

        Interlocked.Increment(ref counter.Failures);
        cache.Set(key, counter, counter.Since + Window);
    }

    public void Reset(string userName) => cache.Remove(Key(userName));

    private static string Key(string userName) => $"odm.login.{userName.Trim().ToLowerInvariant()}";
}
