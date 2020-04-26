using System;
using System.Threading;

namespace BiDaFlow.Internal
{
    internal sealed class ActionDisposable : IDisposable
    {
        private Action? _action;

        public ActionDisposable(Action? action)
        {
            this._action = action;
        }

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref this._action, null);
            action?.Invoke();
        }

        public static ActionDisposable Nop { get; } = new ActionDisposable(null);
    }
}
