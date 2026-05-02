using Microsoft.Extensions.Options;

namespace ASCIIBot.Services;

public sealed class ConcurrencyGate
{
    private readonly object _lock = new();
    private readonly Dictionary<ulong, int> _userJobs = new();
    private int _globalJobs;
    private readonly int _maxGlobal;
    private readonly int _maxPerUser;

    public ConcurrencyGate(IOptions<BotOptions> options)
    {
        _maxGlobal  = options.Value.MaxGlobalJobs;
        _maxPerUser = options.Value.MaxJobsPerUser;
    }

    public bool TryAcquire(ulong userId, out ConcurrencyHandle handle, out ConcurrencyRejection rejection)
    {
        lock (_lock)
        {
            if (_globalJobs >= _maxGlobal)
            {
                handle     = default;
                rejection  = ConcurrencyRejection.GlobalBusy;
                return false;
            }

            _userJobs.TryGetValue(userId, out var userCount);
            if (userCount >= _maxPerUser)
            {
                handle     = default;
                rejection  = ConcurrencyRejection.UserBusy;
                return false;
            }

            _globalJobs++;
            _userJobs[userId] = userCount + 1;
            handle     = new ConcurrencyHandle(this, userId);
            rejection  = ConcurrencyRejection.None;
            return true;
        }
    }

    internal void Release(ulong userId)
    {
        lock (_lock)
        {
            _globalJobs--;
            if (_userJobs.TryGetValue(userId, out var count))
            {
                if (count <= 1)
                    _userJobs.Remove(userId);
                else
                    _userJobs[userId] = count - 1;
            }
        }
    }
}

public enum ConcurrencyRejection { None, UserBusy, GlobalBusy }

public struct ConcurrencyHandle : IDisposable
{
    private ConcurrencyGate? _gate;
    private readonly ulong _userId;

    public ConcurrencyHandle(ConcurrencyGate gate, ulong userId)
    {
        _gate   = gate;
        _userId = userId;
    }

    public void Dispose()
    {
        if (_gate is null) return;
        _gate.Release(_userId);
        _gate = null;
    }
}
