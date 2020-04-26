using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    public class SupervisedDataflowBlock<T> : IDataflowBlock where T : class, IDataflowBlock
    {
        private readonly Func<Task<T>> _startFunc;
        private readonly Func<AggregateException?, Task<RescueAction>> _rescueFunc;
        private readonly TaskScheduler _taskScheduler;
        private readonly TaskCompletionSource<ValueTask> _tcs;
        private bool _started;
        private T? _currentBlock;
        private readonly Queue<Action<T>> _actionQueue = new Queue<Action<T>>();

        internal SupervisedDataflowBlock(Func<Task<T>> startFunc, Func<AggregateException?, Task<RescueAction>> rescueFunc, TaskScheduler taskScheduler)
        {
            this._startFunc = startFunc ?? throw new ArgumentNullException(nameof(startFunc));
            this._rescueFunc = rescueFunc ?? throw new ArgumentNullException(nameof(rescueFunc));
            this._taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            this._tcs = new TaskCompletionSource<ValueTask>();

            new TaskFactory(taskScheduler).StartNew(this.Restart);
        }

        private object Lock => this._tcs; // any readonly object

        internal void EnqueueAction(Action<T> action)
        {
            T currentBlock;

            lock (this.Lock)
            {
                if (!this._started)
                {
                    this._actionQueue.Enqueue(action);
                    return;
                }

                Debug.Assert(this._currentBlock != null);
                currentBlock = this._currentBlock!;
            }

            action(currentBlock);
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

                    try
                    {
                        self.OnStarted(t.Result);
                    }
                    catch (Exception ex)
                    {
                        self._tcs.TrySetException(ex);
                    }
                },
                this,
                this._taskScheduler
            );
        }

        private void OnStarted(T newBlock)
        {
            if (newBlock == null)
                throw new InvalidOperationException("startFunc returned null.");

            lock (this.Lock)
            {
                Debug.Assert(!this._started);

                this._currentBlock = newBlock;
                this._started = true;

                newBlock.Completion.ContinueWith(
                    (completionTask, state) =>
                    {
                        var self = (SupervisedDataflowBlock<T>)state;

                        lock (self.Lock)
                        {
                            Debug.Assert(self._started);
                            Debug.Assert(self._currentBlock == newBlock);

                            self._started = false;
                            self._currentBlock = null;
                        }

                        self.Rescue(completionTask);
                    },
                    this,
                    this._taskScheduler
                );

                while (this._actionQueue.Count > 0 && !newBlock.Completion.IsCompleted)
                {
                    try
                    {
                        this._actionQueue.Dequeue()?.Invoke(newBlock);
                    }
                    catch (Exception ex)
                    {
                        newBlock.Fault(ex);
                    }
                }
            }
        }

        private void Rescue(Task completionTask)
        {
            var blockException = completionTask.Exception;
            Task<RescueAction> rescueTask;

            try
            {
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
                this._taskScheduler
            );
        }

        Task IDataflowBlock.Completion => this._tcs.Task;

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
