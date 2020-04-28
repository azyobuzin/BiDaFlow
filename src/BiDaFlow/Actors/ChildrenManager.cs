using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Internal;

namespace BiDaFlow.Actors
{
    internal sealed class ChildrenManager
    {
        private readonly Actor _parent;
        private readonly LinkedList<SupervisedChild> _children = new LinkedList<SupervisedChild>();
        private readonly CancellationTokenSource _globalCts = new CancellationTokenSource();
        private bool _isCompleted;

        public ChildrenManager(Actor parent)
        {
            this._parent = parent;
        }

        public IDisposable SuperviseChild(IDataflowBlock block, SupervisionOptions? options)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            options ??= SupervisionOptions.Default;

            LinkedListNode<SupervisedChild> node;

            lock (this._children)
            {
                if (this._isCompleted)
                    throw new InvalidOperationException("This supervisor has been completed.");

                node = this._children.AddLast(new SupervisedChild(block, options.KillWhenCompleted, options.WaitCompletion));
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(this._globalCts.Token);
            var detacher = new ActionDisposable(() =>
            {
                cts.Cancel();

                lock (this._children)
                {
                    this._children.Remove(node);
                }
            });

            var oneForAll = options.OneForAll;

            block.Completion.ContinueWith(
                completionTask =>
                {
                    detacher.Dispose();

                    if (oneForAll)
                    {
                        var aex = completionTask.Exception;
                        if (aex == null) return;

                        lock (this._children)
                        {
                            if (!this._isCompleted)
                            {
                                this._parent.Fault(aex);
                            }
                        }
                    }
                },
                cts.Token,
                TaskContinuationOptions.None,
                this._parent.Engine.TaskScheduler
            );

            return detacher;
        }

        public Task SupervisorOnCompleted(AggregateException? actorError)
        {
            SupervisedChild[] childrenSnapshot;

            lock (this._children)
            {
                if (this._isCompleted)
                    throw new InvalidOperationException("This supervisor has been completed.");

                this._isCompleted = true;

                childrenSnapshot = this._children.ToArray();
            }

            this._globalCts.Cancel();

            foreach (var child in childrenSnapshot)
            {
                if (child.KillWhenCompleted && !child.Block.Completion.IsCompleted)
                    child.Block.Fault(new KilledBySupervisorException());
            }

            var tasks = childrenSnapshot
                .Where(x => x.WaitCompletion)
                .Select(x => x.Block.Completion);

            return Task.WhenAll(tasks)
                .ContinueWith(
                    t =>
                    {
                        var exceptions = new List<Exception>();

                        if (actorError != null)
                            exceptions.AddRange(actorError.InnerExceptions);

                        if (t.Exception != null)
                        {
                            foreach (var ex in t.Exception.InnerExceptions)
                            {
                                if (!(ex is KilledBySupervisorException))
                                    exceptions.Add(ex);
                            }
                        }

                        if (exceptions.Count == 0) return Task.CompletedTask;

                        var tcs = new TaskCompletionSource<ValueTuple>();
                        tcs.TrySetException(exceptions);
                        return tcs.Task;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    this._parent.Engine.TaskScheduler
                ).Unwrap();
        }
    }
}
