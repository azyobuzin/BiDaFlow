using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace BiDaFlow.Internal
{
    internal sealed class AsyncAutoResetEvent : IValueTaskSource
    {
        private int _state;
        private ManualResetValueTaskSourceCore<ValueTuple> _taskHelper;

        public AsyncAutoResetEvent(bool initialState)
        {
            this._state = (int)(initialState ? StateEnum.Signaled : StateEnum.NotSignaled);
        }

        public bool RunContinuationsAsynchronously
        {
            get => this._taskHelper.RunContinuationsAsynchronously;
            set => this._taskHelper.RunContinuationsAsynchronously = value;
        }

        public ValueTask WaitAsync()
        {
            if (this._state == (int)StateEnum.Awaiting)
                throw new InvalidOperationException("The previous task has not been completed.");

            this._taskHelper.Reset();

            while (true)
            {
                var currentState = Volatile.Read(ref this._state);

                if (currentState == (int)StateEnum.Signaled)
                {
                    if (Interlocked.CompareExchange(ref this._state, (int)StateEnum.NotSignaled, currentState) == currentState)
                    {
                        this._taskHelper.SetResult(default);
                        break;
                    }
                }
                else if (currentState == (int)StateEnum.NotSignaled)
                {
                    if (Interlocked.CompareExchange(ref this._state, (int)StateEnum.Awaiting, currentState) == currentState)
                    {
                        break;
                    }
                }
                else
                {
                    throw new InvalidOperationException("Invalid state.");
                }
            }

            return new ValueTask(this, this._taskHelper.Version);
        }

        public void Set()
        {
            while (true)
            {
                var currentState = Volatile.Read(ref this._state);

                if (currentState == (int)StateEnum.Awaiting)
                {
                    if (Interlocked.CompareExchange(ref this._state, (int)StateEnum.NotSignaled, currentState) == currentState)
                    {
                        this._taskHelper.SetResult(default);
                        break;
                    }
                }
                else if (currentState == (int)StateEnum.NotSignaled)
                {
                    if (Interlocked.CompareExchange(ref this._state, (int)StateEnum.Signaled, currentState) == currentState)
                    {
                        break;
                    }
                }
                else
                {
                    // already signaled
                    break;
                }
            }
        }

        void IValueTaskSource.GetResult(short token)
        {
            this._taskHelper.GetResult(token);
        }

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
        {
            return this._taskHelper.GetStatus(token);
        }

        void IValueTaskSource.OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            this._taskHelper.OnCompleted(continuation, state, token, flags);
        }

        private enum StateEnum
        {
            NotSignaled,
            Signaled,
            Awaiting,
        }
    }
}
