using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;

namespace BiDaFlow.Actors
{
    public class SupervisedDataflowBlock<T> : IDataflowBlock where T : IDataflowBlock
    {
        private readonly Func<Task<T>> _startFunc;
        private readonly Func<AggregateException?, Task<RescueAction>> _rescueFunc;
        private readonly TaskCompletionSource<ValueTuple> _tcs = new TaskCompletionSource<ValueTuple>();
        private readonly BehaviorSubject<Optional<T>> _currentBlockSubject = new BehaviorSubject<Optional<T>>(Optional<T>.None);

        internal SupervisedDataflowBlock(Func<Task<T>> startFunc, Func<AggregateException?, Task<RescueAction>> rescueFunc, TaskScheduler taskScheduler)
        {
            this._startFunc = startFunc ?? throw new ArgumentNullException(nameof(startFunc));
            this._rescueFunc = rescueFunc ?? throw new ArgumentNullException(nameof(rescueFunc));
            this.TaskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));

            this._currentBlockSubject.Subscribe(this.SetContinuationToBlock);

            new TaskFactory(taskScheduler).StartNew(this.Restart);
        }

        public Task Completion => this._tcs.Task;

        internal TaskScheduler TaskScheduler { get; }

        internal IObservable<Optional<T>> CurrentBlockObservable => this._currentBlockSubject;

        internal void EnqueueAction(Action<T> action)
        {
            IDisposable? unsubscriber = null;
            var done = false;

            unsubscriber = this._currentBlockSubject
                .Subscribe(opt =>
                {
                    if (done)
                    {
                        unsubscriber?.Dispose();
                        return;
                    }

                    if (opt.HasValue)
                    {
                        done = true;
                        unsubscriber?.Dispose();

                        action(opt.Value);
                    }
                });
        }

        private void Restart()
        {
            Task<T> startTask;
            try
            {
                startTask = this._startFunc();

                if (startTask == null)
                    throw new InvalidOperationException("startFunc returned null.");
            }
            catch (Exception ex)
            {
                this._tcs.TrySetException(ex);
                return;
            }

            startTask.ContinueWith(
                (t, state) =>
                {
                    var self = (SupervisedDataflowBlock<T>)state;

                    if (t.Exception != null)
                    {
                        self._tcs.TrySetException(t.Exception.InnerExceptions);
                        return;
                    }
                    if (t.IsCanceled)
                    {
                        self._tcs.TrySetCanceled();
                        return;
                    }

                    var newBlock = t.Result;

                    if (newBlock == null)
                    {
                        self._tcs.TrySetException(new InvalidOperationException("startFunc returned null."));
                        return;
                    }

                    try
                    {
                        // Notify new block
                        this._currentBlockSubject.OnNext(new Optional<T>(newBlock));
                    }
                    catch (Exception ex)
                    {
                        newBlock.Fault(ex);
                    }
                },
                this,
                this.TaskScheduler
            );
        }

        private void SetContinuationToBlock(Optional<T> blockOpt)
        {
            if (!blockOpt.HasValue) return;

            var newBlock = blockOpt.Value;

            newBlock.Completion.ContinueWith(
                (completionTask, state) => ((SupervisedDataflowBlock<T>)state).Rescue(completionTask),
                this,
                this.TaskScheduler
            );
        }

        private void Rescue(Task completionTask)
        {
            var blockException = completionTask.Exception;
            Task<RescueAction> rescueTask;

            try
            {
                this._currentBlockSubject.OnNext(Optional<T>.None);

                rescueTask = this._rescueFunc(blockException)
                    ?? Task.FromResult(RescueAction.Rethrow);
            }
            catch (Exception ex)
            {
                this._tcs.TrySetException(ex);
                return;
            }

            rescueTask.ContinueWith(
                t =>
                {
                    if (t.Exception != null)
                    {
                        this._tcs.TrySetException(t.Exception.InnerExceptions);
                        return;
                    }
                    if (t.IsCanceled)
                    {
                        this._tcs.TrySetCanceled();
                        return;
                    }

                    switch (t.Result)
                    {
                        case RescueAction.Restart:
                            this.Restart();
                            break;

                        case RescueAction.Complete:
                            this._tcs.TrySetResult(default);
                            break;

                        default:
                            if (blockException?.InnerExceptions.Count > 0)
                            {
                                this._tcs.TrySetException(blockException.InnerExceptions);
                            }
                            else
                            {
                                this._tcs.TrySetResult(default);
                            }
                            break;
                    }
                },
                this.TaskScheduler
            );
        }

        void IDataflowBlock.Complete()
        {
            this.EnqueueAction(x => x.Complete());
        }

        void IDataflowBlock.Fault(Exception exception)
        {
            this.EnqueueAction(x => x.Fault(exception));
        }
    }
}
