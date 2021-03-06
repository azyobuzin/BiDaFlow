﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.EnumerableBlocks
{
    public class EnumerableSourceBlockTests
    {
        [Fact]
        public async Task TestRange()
        {
            var iterator = new TestIterator(3);
            var testBlock = iterator.AsSourceBlock();
            var targetBlock = new BufferBlock<int>();

            testBlock.LinkTo(targetBlock);
            await testBlock.Completion.CompleteSoon();
            iterator.Disposed.IsTrue();

            targetBlock.TryReceiveAll(out var values).IsTrue();
            values.Is(1, 2, 3);
        }

        [Fact]
        public void TestReceive()
        {
            var iterator = new TestIterator(3);
            var testBlock = iterator.AsSourceBlock().IsInstanceOf<IReceivableSourceBlock<int>>();

            // Cannot receive item before link
            testBlock.TryReceive(null, out _).IsFalse();
            iterator.Enumerating.IsFalse();

            // Can receive with link
            testBlock.Receive(TestUtils.SometimeSoon).Is(1);
            iterator.Enumerating.IsTrue();

            // Does not MoveNext after unlink
            testBlock.TryReceive(null, out _).IsFalse();
        }

        [Fact]
        public async Task TestCancelBeforeLink()
        {
            var iterator = new TestIterator(3);
            var cts = new CancellationTokenSource();
            var testBlock = iterator.AsSourceBlock(cts.Token);

            cts.Cancel();
            await testBlock.Completion.CanceledSoon();
        }

        [Fact]
        public async Task TestCancelInOffering()
        {
            var iterator = new TestIterator(3);
            var cts = new CancellationTokenSource(50);
            var testBlock = iterator.AsSourceBlock(cts.Token);
            var targetBlock = new BufferBlock<int>(new DataflowBlockOptions() { BoundedCapacity = 1 });

            testBlock.LinkTo(targetBlock);

            // When link is added, call MoveNext once and buffer the result.
            // The task is not completed until the buffered value is consumed.
            await testBlock.Completion.NeverComplete();

            // targetBlock received 1 soon after linking
            (await targetBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(1);

            // 2 was buffered and targetBlock received it soon after the previous line
            (await targetBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(2);

            // The cancel is requested and no buffer in testBlock
            await testBlock.Completion.CanceledSoon();

            // targetBlock never receives 3
            await Assert.ThrowsAnyAsync<TimeoutException>(() => targetBlock.ReceiveAsync(TestUtils.SometimeSoon));
        }

        [Fact]
        public async Task TestError()
        {
            IEnumerable<int> Generator()
            {
                throw new Exception("test");
#pragma warning disable CS0162 // Unreachable code detected
                yield break;
#pragma warning restore CS0162
            }

            var testBlock = Generator().AsSourceBlock();
            await testBlock.Completion.NeverComplete();

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => testBlock.ReceiveAsync(TestUtils.SometimeSoon));

            var aex = testBlock.Completion.Exception!;
            aex.InnerExceptions.Count.Is(1);
            aex.InnerException!.Message.Is("test");
        }

        [Theory, InlineData(1), InlineData(2)]
        public async Task TestDisposeEnumerator(int receiveCount)
        {
            var iterator = new TestIterator(2);
            var cts = new CancellationTokenSource();
            var testBlock = iterator.AsSourceBlock(cts.Token);

            for (var i = 1; i <= receiveCount; i++)
            {
                (await testBlock.ReceiveAsync(TestUtils.SometimeSoon)).Is(i);
            }

            cts.Cancel();

            await testBlock.Completion.CanceledSoon();
            iterator.Disposed.IsTrue();
        }

        private class TestIterator : IEnumerable<int>, IEnumerator<int>
        {
            private readonly int _maxValue;

            public TestIterator(int maxValue)
            {
                this._maxValue = maxValue;
            }

            public IEnumerator<int> GetEnumerator()
            {
                if (this.Enumerating)
                    throw new InvalidOperationException("GetEnumerator has already been called.");

                this.Enumerating = true;
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator()
                => this.GetEnumerator();

            public int Current { get; private set; }

            object? IEnumerator.Current => this.Current;

            public bool Enumerating { get; private set; }

            public bool Disposed { get; private set; }

            public bool MoveNext()
            {
                if (this.Disposed)
                    throw new ObjectDisposedException(nameof(TestIterator));

                if (this.Current >= this._maxValue)
                    return false;

                this.Current++;
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                this.Disposed = true;
            }
        }
    }
}
