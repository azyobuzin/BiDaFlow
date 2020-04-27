using System;

namespace BiDaFlow.Internal
{
    internal static class ObservableExtensions
    {
        public static IDisposable Subscribe<T>(this IObservable<T> observable, Action<T>? onNext)
        {
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            return observable.Subscribe(new DelegateObserver<T>(onNext, null, null));
        }

        public static IDisposable Subscribe<T>(this IObservable<T> observable, Action<T>? onNext, Action<Exception> onError, Action? onCompleted)
        {
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            return observable.Subscribe(new DelegateObserver<T>(onNext, onError, onCompleted));
        }
    }
}
