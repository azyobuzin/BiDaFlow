using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BiDaFlow.Internal
{
    internal sealed class TaskSchedulerAutoResetEvent
    {
#if THREADPOOL
        private static readonly WaitCallback s_threadPoolCallback = state => ((Action)state)();
#endif

        private bool _signaled;
        private readonly TaskScheduler _taskScheduler;
        private Action? _continuation;

        public TaskSchedulerAutoResetEvent(bool initialState, TaskScheduler? taskScheduler)
        {
            this._signaled = initialState;
            this._taskScheduler = taskScheduler ?? TaskScheduler.Default;
        }

        public Awaiter GetAwaiter() => new(this);

        public void Set()
        {
            this._signaled = true;
            this.Continue();
        }

        private void Continue()
        {
            var continuation = Interlocked.Exchange(ref this._continuation, null);
            if (continuation == null) return;

#if THREADPOOL
            if (this._taskScheduler == TaskScheduler.Default)
            {
                ThreadPool.UnsafeQueueUserWorkItem(s_threadPoolCallback, continuation);
            }
            else
#endif
            {
                _ = Task.Factory.StartNew(continuation, CancellationToken.None, TaskCreationOptions.DenyChildAttach, this._taskScheduler);
            }
        }

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            private readonly TaskSchedulerAutoResetEvent _parent;

            public Awaiter(TaskSchedulerAutoResetEvent parent)
            {
                this._parent = parent;
            }

            public bool IsCompleted => this._parent._signaled && TaskScheduler.Current == this._parent._taskScheduler;

            public void OnCompleted(Action continuation)
            {
                throw new NotSupportedException();
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                var oldContinuation = Interlocked.CompareExchange(ref this._parent._continuation, continuation, null);
                if (oldContinuation != null) throw new InvalidOperationException();

                if (this._parent._signaled) this._parent.Continue();
            }

            public void GetResult()
            {
                // Reset the state
                this._parent._continuation = null;
                Interlocked.MemoryBarrier();
                this._parent._signaled = false;
            }
        }
    }
}
