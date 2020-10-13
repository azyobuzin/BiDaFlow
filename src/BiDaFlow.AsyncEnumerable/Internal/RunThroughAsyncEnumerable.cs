using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using BiDaFlow.Fluent;

namespace BiDaFlow.Internal
{
    /// <seealso cref="DataflowAsyncEnumerable.RunThroughDataflowBlock{TInput, TOutput}(IAsyncEnumerable{TInput}, Func{CancellationToken, IPropagatorBlock{TInput, TOutput}})"/>
    internal sealed class RunThroughAsyncEnumerable<TInput, TOutput> : IAsyncEnumerable<TOutput>
    {
        private readonly IAsyncEnumerable<TInput> _inputEnumerable;
        private readonly Func<CancellationToken, IPropagatorBlock<TInput, TOutput>> _propagatorFactory;

        public RunThroughAsyncEnumerable(IAsyncEnumerable<TInput> inputEnumerable, Func<CancellationToken, IPropagatorBlock<TInput, TOutput>> propagatorFactory)
        {
            this._inputEnumerable = inputEnumerable;
            this._propagatorFactory = propagatorFactory;
        }

        public IAsyncEnumerator<TOutput> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            var propagatorBlock = this._propagatorFactory(cancellationToken);

            if (propagatorBlock == null)
                throw new InvalidOperationException("propagatorFactory returned null.");

            var sourceBlock = this._inputEnumerable.AsSourceBlock(cancellationToken);
            sourceBlock.LinkTo(propagatorBlock, new DataflowLinkOptions() { PropagateCompletion = true });
            return propagatorBlock.AsAsyncEnumerable().GetAsyncEnumerator(cancellationToken);
        }
    }
}
