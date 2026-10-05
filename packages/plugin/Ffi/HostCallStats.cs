using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     How long each FFI call waited for the query cursor, spent in MusicBee,
    ///     and then spent packing its reply.
    /// </summary>
    /// <remarks>
    ///     Calls run concurrently, so it has a lock of its own and the time in
    ///     MusicBee summed over a window can pass 100%. Returns the lines to log
    ///     instead of logging them.
    /// </remarks>
    public sealed class HostCallStats
    {
        /// <summary>A single call at least this long is logged on its own.</summary>
        public const double SlowCallMs = 100;

        /// <summary>How often the per-kind summary is logged while calls keep coming.</summary>
        public static readonly TimeSpan SummaryEvery = TimeSpan.FromSeconds(60);

        private readonly double _ticksPerMs;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly object _sync = new object();
        private long _windowStart = -1;

        public HostCallStats(long ticksPerSecond)
        {
            _ticksPerMs = ticksPerSecond / 1000.0;
        }

        /// <summary>
        ///     Records one call. <paramref name="slow" /> is set when this call was
        ///     long, <paramref name="summary" /> when a summary window closed.
        /// </summary>
        public void Record(string kind, long waitTicks, long callTicks, long packTicks, long nowTicks,
            out string slow, out string summary)
        {
            slow = null;
            summary = null;
            var waitMs = waitTicks / _ticksPerMs;
            var callMs = callTicks / _ticksPerMs;
            var packMs = packTicks / _ticksPerMs;

            if (callMs >= SlowCallMs)
                slow = string.Format(CultureInfo.InvariantCulture,
                    "host call: {0} took {1:F0}ms after waiting {2:F0}ms for the cursor, packed in {3:F0}ms",
                    kind, callMs, waitMs, packMs);

            lock (_sync)
            {
                Entry entry;
                if (!_entries.TryGetValue(kind, out entry))
                {
                    entry = new Entry();
                    _entries[kind] = entry;
                }
                entry.Add(waitMs, callMs, packMs);

                if (_windowStart < 0)
                {
                    _windowStart = nowTicks;
                    return;
                }
                if ((nowTicks - _windowStart) / _ticksPerMs < SummaryEvery.TotalMilliseconds)
                    return;

                summary = Summarize((nowTicks - _windowStart) / _ticksPerMs);
                _entries.Clear();
                _windowStart = nowTicks;
            }
        }

        private string Summarize(double windowMs)
        {
            var inHostMs = _entries.Values.Sum(e => e.CallTotal);
            var text = new StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture,
                "host calls over {0:F0}s: {1:F0}ms in MusicBee ({2:F1}%)", windowMs / 1000, inHostMs,
                inHostMs * 100 / windowMs);
            foreach (var pair in _entries.OrderByDescending(p => p.Value.CallTotal))
            {
                var e = pair.Value;
                text.AppendFormat(CultureInfo.InvariantCulture,
                    "; {0} n={1} call avg {2:F1} max {3:F0} wait avg {4:F1} max {5:F0} pack avg {6:F1} max {7:F0}",
                    pair.Key, e.Count, e.CallTotal / e.Count, e.CallMax, e.WaitTotal / e.Count, e.WaitMax,
                    e.PackTotal / e.Count, e.PackMax);
            }
            return text.ToString();
        }

        private sealed class Entry
        {
            public int Count;
            public double CallMax;
            public double CallTotal;
            public double PackMax;
            public double PackTotal;
            public double WaitMax;
            public double WaitTotal;

            public void Add(double waitMs, double callMs, double packMs)
            {
                Count++;
                WaitTotal += waitMs;
                CallTotal += callMs;
                PackTotal += packMs;
                if (waitMs > WaitMax) WaitMax = waitMs;
                if (callMs > CallMax) CallMax = callMs;
                if (packMs > PackMax) PackMax = packMs;
            }
        }
    }
}
