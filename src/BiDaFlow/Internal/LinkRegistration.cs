using System;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    [UsedBySubpackage]
    internal sealed class LinkRegistration<T>
    {
        public ITargetBlock<T> Target { get; }

        private readonly bool _propagateCompletion;
        private readonly Action<LinkRegistration<T>>? _unlinkCallback;
        private int _remainingMessages;
        private int _unlinked;

        public LinkRegistration(ITargetBlock<T> target, int maxMessages, bool propagateCompletion, Action<LinkRegistration<T>>? unlinkCallback)
        {
            if (maxMessages <= 0 && maxMessages != DataflowBlockOptions.Unbounded)
                throw new ArgumentOutOfRangeException(nameof(maxMessages));

            this.Target = target ?? throw new ArgumentNullException(nameof(target));
            this._remainingMessages = maxMessages;
            this._propagateCompletion = propagateCompletion;
            this._unlinkCallback = unlinkCallback;
        }

        public bool Unlinked => this._unlinked != 0;

        public void DecrementRemainingMessages()
        {
            if (this._remainingMessages == DataflowBlockOptions.Unbounded)
                return;

            var newValue = this._remainingMessages - 1;
            if (newValue < 0) throw new InvalidOperationException("newValue is " + newValue);

            this._remainingMessages = newValue;

            if (newValue == 0) this.Unlink();
        }

        public void Complete(Exception? exception)
        {
            if (!this._propagateCompletion || this.Unlinked)
                return;

            if (exception == null)
            {
                this.Target.Complete();
            }
            else
            {
                this.Target.Fault(exception);
            }
        }

        public void Unlink()
        {
            if (Interlocked.Exchange(ref this._unlinked, 1) == 0)
            {
                this._unlinkCallback?.Invoke(this);
            }
        }
    }
}
