using System;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal sealed class LinkedTarget<T>
    {
        public ITargetBlock<T> Target { get; }

        public IDisposable? UnlinkerForCompletion { get; }

        private int _remainingMessages;
        private bool _unlinked;

        public LinkedTarget(ITargetBlock<T> target, IDisposable? unlinkerForCompletion, int maxMessages)
        {
            if (maxMessages < 0 && maxMessages != DataflowBlockOptions.Unbounded)
                throw new ArgumentOutOfRangeException(nameof(maxMessages));

            this.Target = target ?? throw new ArgumentNullException(nameof(target));
            this.UnlinkerForCompletion = unlinkerForCompletion;
            this._remainingMessages = maxMessages;
        }

        public bool IsLinked => !this._unlinked && this._remainingMessages != 0;

        public void DecrementRemainingMessages()
        {
            if (this._remainingMessages == DataflowBlockOptions.Unbounded)
                return;

            var newValue = this._remainingMessages - 1;
            if (newValue < 0) throw new InvalidOperationException("newValue is " + newValue);

            this._remainingMessages = newValue;

            if (newValue == 0) this.Unlink();
        }

        public void Unlink()
        {
            this._unlinked = true;
            this.UnlinkerForCompletion?.Dispose();
        }
    }
}
