using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;

namespace BiDaFlow.Actors
{
    public class SupervisedBlock<T> : IDataflowBlock where T : IDataflowBlock
    {
        private readonly Func<Task<T>> _startFunc;
        private readonly Func<T, AggregateException?, Task<RescueAction>> _rescueFunc;
        private readonly TaskCompletionSource<ValueTuple> _tcs = new TaskCompletionSource<ValueTuple>();
        private readonly BehaviorSubject<Optional<T>> _currentBlockSubject = new BehaviorSubject<Optional<T>>(Optional<T>.None);

        internal SupervisedBlock(Func<Task<T>> startFunc, Func<T, AggregateException?, Task<RescueAction>> rescueFunc, TaskScheduler taskScheduler)
        {
            this._startFunc = startFunc ?? throw new ArgumentNullException(nameof(startFunc));
            this._rescueFunc = rescueFunc ?? throw new ArgumentNullException(nameof(rescueFunc));
            this.TaskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));

            this._currentBlockSubject.Subscribe(this.SetContinuationToBlock);

            this.Completion.ContinueWith(
                (_, state) => ((SupervisedBlock<T>)state)._currentBlockSubject.OnCompleted(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.DenyChildAttach,
                taskScheduler);

            new TaskFactory(taskScheduler).StartNew(this.Restart);
        }

        public Task Completion => this._tcs.Task;

        internal TaskScheduler TaskScheduler { get; }

        internal IObservable<Optional<T>> CurrentBlockObservable => this._currentBlockSubject;

        internal Optional<T> CurrentBlock => this._currentBlockSubject.Value;

        public bool TryGetWrappedBlock(out T block)
        {
            var opt = this.CurrentBlock;
            block = opt.Value;
            return opt.HasValue;
        }

        internal void EnqueueAction(Action<T> action)
        {
            this.CurrentBlockObservable
                .Where(x => x.HasValue)
                .ReceiveOnce((block, ex, completed) =>
                {
                    if (ex == null && !completed)
                        action(block.Value);
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
                    var self = (SupervisedBlock<T>)state;

                    if (t.Exception != null)
                    {
                        self._tcs.TrySetException(t.Exception.InnerExceptions);
                        return;
                    }
                    if (t.IsCanceled || (t.Result is var newBlock && newBlock is null))
                    {
                        self._tcs.TrySetCanceled();
                        return;
                    }

                    try
                    {
                        // Notify new block
                        this._currentBlockSubject.OnNext(new Optional<T>(t.Result));
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
                (completionTask, state) => ((SupervisedBlock<T>)state).Rescue(completionTask),
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
                var blockOpt = this.CurrentBlock;

                if (!blockOpt.HasValue)
                    throw new InvalidOperationException("CurrentBlock is None.");

                this._currentBlockSubject.OnNext(Optional<T>.None);

                rescueTask = this._rescueFunc(blockOpt.Value, blockException)
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
