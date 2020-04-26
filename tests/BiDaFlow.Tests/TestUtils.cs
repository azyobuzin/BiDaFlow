using System;
using System.Threading;

namespace BiDaFlow.Tests
{
    internal static class TestUtils
    {
        public static CancellationToken CancelAfter(TimeSpan delay)
        {
            return new CancellationTokenSource(delay).Token;
        }

        public static CancellationToken CancelSometimeSoon()
        {
            return CancelAfter(new TimeSpan(100 * TimeSpan.TicksPerMillisecond));
        }
    }
}
