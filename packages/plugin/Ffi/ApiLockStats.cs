using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     How long each FFI call waited for the MusicBee API lock and then held it.
    /// </summary>
    /// <remarks>
    ///     Not thread-safe on its own: every call is made while holding the API lock
    ///     it measures. Returns the lines to log instead of logging them, so the
    ///     caller can write them after releasing the lock.
    /// </remarks>
    public sealed class ApiLockStats
    {
        /// <summary>A single hold at least this long is logged on its own.</summary>
        public const double SlowHoldMs = 100;

        /// <summary>How often the per-kind summary is logged while calls keep coming.</summary>
        public static readonly TimeSpan SummaryEvery = TimeSpan.FromSeconds(60);

        private readonly double _ticksPerMs;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private long _windowStart = -1;

        public ApiLockStats(long ticksPerSecond)
        {
            _ticksPerMs = ticksPerSecond / 1000.0;
        }

        /// <summary>
        ///     Records one call. <paramref name="slow" /> is set when this hold was
        ///     long, <paramref name="summary" /> when a summary window closed.
        /// </summary>
        public void Record(string kind, long waitTicks, long holdTicks, long nowTicks,
            out string slow, out string summary)
        {
            slow = null;
            summary = null;
            var waitMs = waitTicks / _ticksPerMs;
            var holdMs = holdTicks / _ticksPerMs;

            Entry entry;
            if (!_entries.TryGetValue(kind, out entry))
            {
                entry = new Entry();
                _entries[kind] = entry;
            }
            entry.Add(waitMs, holdMs);

            if (holdMs >= SlowHoldMs)
                slow = string.Format(CultureInfo.InvariantCulture,
                    "api lock: {0} held {1:F0}ms after waiting {2:F0}ms", kind, holdMs, waitMs);

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

        private string Summarize(double windowMs)
        {
            var heldMs = _entries.Values.Sum(e => e.HoldTotal);
            var text = new StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture,
                "api lock over {0:F0}s: held {1:F0}ms ({2:F1}%)", windowMs / 1000, heldMs, heldMs * 100 / windowMs);
            foreach (var pair in _entries.OrderByDescending(p => p.Value.HoldTotal))
            {
                var e = pair.Value;
                text.AppendFormat(CultureInfo.InvariantCulture,
                    "; {0} n={1} hold avg {2:F1} max {3:F0} wait avg {4:F1} max {5:F0}",
                    pair.Key, e.Count, e.HoldTotal / e.Count, e.HoldMax, e.WaitTotal / e.Count, e.WaitMax);
            }
            return text.ToString();
        }

        private sealed class Entry
        {
            public int Count;
            public double HoldMax;
            public double HoldTotal;
            public double WaitMax;
            public double WaitTotal;

            public void Add(double waitMs, double holdMs)
            {
                Count++;
                WaitTotal += waitMs;
                HoldTotal += holdMs;
                if (waitMs > WaitMax) WaitMax = waitMs;
                if (holdMs > HoldMax) HoldMax = holdMs;
            }
        }
    }
}
