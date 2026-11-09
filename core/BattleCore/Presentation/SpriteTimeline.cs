namespace NewKOF.BattleCore.Presentation;

/// <summary>AIR timing in committed logical frames; visual only.</summary>
public sealed class SpriteTimeline
{
    private readonly int[] _durations;
    private readonly long _prefix;
    private readonly long _loop;
    private readonly bool _infinite;

    public SpriteTimeline(IEnumerable<int> durations, int loopStart)
    {
        _durations = durations.ToArray();
        if (_durations.Length == 0 || loopStart < 0 || loopStart >= _durations.Length ||
            _durations.Any(d => d < -1)) throw new ArgumentException("Invalid AIR timeline.");
        _infinite = _durations.Contains(-1);
        _prefix = _durations.Take(loopStart).Where(d => d > 0).Sum(d => (long)d);
        _loop = _durations.Skip(loopStart).Where(d => d > 0).Sum(d => (long)d);
    }

    public int FrameAt(int elapsed)
    {
        if (elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        long t = elapsed;
        if (!_infinite && _loop > 0 && t >= _prefix) t = _prefix + (t - _prefix) % _loop;
        for (int i = 0; i < _durations.Length; i++)
        {
            int duration = _durations[i];
            if (duration == -1 || t < duration) return i;
            t -= duration;
        }
        return _durations.Length - 1;
    }
}
