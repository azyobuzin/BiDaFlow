using System;

namespace BiDaFlow.Internal
{
    internal sealed class DelegateObserver<T> : IObserver<T>
    {
        private readonly Action<T>? _onNext;
        private readonly Action<Exception>? _onError;
        private readonly Action? _onCompleted;

        public DelegateObserver(Action<T>? onNext, Action<Exception>? onError, Action? onCompleted)
        {
            this._onNext = onNext;
            this._onError = onError;
            this._onCompleted = onCompleted;
        }

        public void OnNext(T value)
        {
            this._onNext?.Invoke(value);
        }

        public void OnError(Exception error)
        {
            this._onError?.Invoke(error);
        }

        public void OnCompleted()
        {
            this._onCompleted?.Invoke();
        }
    }
}
