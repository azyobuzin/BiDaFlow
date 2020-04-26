using System;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal sealed class DroppingObserver<T> : IObserver<T>
    {
        private readonly ITargetBlock<T> _target;

        public DroppingObserver(ITargetBlock<T> target)
        {
            this._target = target;
        }

        public void OnNext(T value)
        {
            this._target.Post(value);
        }

        public void OnCompleted()
        {
            this._target.Complete();
        }

        public void OnError(Exception error)
        {
            this._target.Fault(error);
        }
    }
}
