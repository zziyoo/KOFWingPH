namespace NewKOF.BattleCore.Presentation;

/// <summary>AIR timing in committed logical frames; visual only.</summary>
public sealed class SpriteTimeline
{
    private readonly int[] _durations;
    private readonly long _prefix;
    private readonly long _loop;
    private readonly bool _infinite;
    private readonly bool _repeat;
    private readonly int _first;

    public SpriteTimeline(IEnumerable<int> durations, int loopStart, bool loop = true, int firstFrame = 0, int? lastFrame = null)
    {
        var all = durations.ToArray();
        int last = lastFrame ?? all.Length - 1;
        if (all.Length == 0 || firstFrame < 0 || last < firstFrame || last >= all.Length || loopStart < 0 || loopStart >= all.Length ||
            all.Any(d => d < -1)) throw new ArgumentException("Invalid AIR timeline.");
        _first = firstFrame;
        _repeat = loop;
        _durations = all[firstFrame..(last + 1)];
        loopStart = Math.Clamp(loopStart - firstFrame, 0, _durations.Length - 1);
        _infinite = _durations.Contains(-1);
        _prefix = _durations.Take(loopStart).Where(d => d > 0).Sum(d => (long)d);
        _loop = _durations.Skip(loopStart).Where(d => d > 0).Sum(d => (long)d);
    }

    public int FrameAt(int elapsed)
    {
        if (elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        long t = elapsed;
        if (_repeat && !_infinite && _loop > 0 && t >= _prefix) t = _prefix + (t - _prefix) % _loop;
        for (int i = 0; i < _durations.Length; i++)
        {
            int duration = _durations[i];
            if (duration == -1 || t < duration) return _first + i;
            t -= duration;
        }
        return _first + _durations.Length - 1;
    }
}
