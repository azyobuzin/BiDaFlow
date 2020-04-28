﻿using System;
using System.Threading;
using System.Threading.Tasks;
using ChainingAssertion;

namespace BiDaFlow.Tests
{
    internal static class TestUtils
    {
        public static readonly TimeSpan SometimeSoon = new TimeSpan(100 * TimeSpan.TicksPerMillisecond);

        public static CancellationToken CancelAfter(TimeSpan delay)
        {
            return new CancellationTokenSource(delay).Token;
        }

        public static CancellationToken CancelSometimeSoon()
        {
            return CancelAfter(SometimeSoon);
        }

        public static async Task CompleteSoon(this Task task)
        {
            await Task.WhenAny(task, Task.Delay(SometimeSoon)).ConfigureAwait(false);
            task.IsCompleted.IsTrue();
            await task;
        }

        public static async Task<T> CompleteSoon<T>(this Task<T> task)
        {
            await Task.WhenAny(task, Task.Delay(SometimeSoon)).ConfigureAwait(false);
            task.IsCompleted.IsTrue();
            return await task;
        }

        public static async Task CanceledSoon(this Task task)
        {
            await Task.WhenAny(task, Task.Delay(SometimeSoon)).ConfigureAwait(false);
            task.Status.Is(TaskStatus.Canceled);
        }

        public static async Task NeverComplete(this Task task)
        {
            await Task.WhenAny(task, Task.Delay(SometimeSoon)).ConfigureAwait(false);
            task.IsCompleted.IsFalse();
        }
    }
}
