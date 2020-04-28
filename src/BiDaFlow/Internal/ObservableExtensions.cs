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

        public static IDisposable Subscribe<T>(this IObservable<T> observable, Action<T>? onNext, Action<Exception>? onError, Action? onCompleted)
        {
            if (observable == null) throw new ArgumentNullException(nameof(observable));

            return observable.Subscribe(new DelegateObserver<T>(onNext, onError, onCompleted));
        }

        public static IDisposable ReceiveOnce<T>(this IObservable<T> observable, Action<T, Exception?, bool> action)
        {
            var lockObj = new object();
            var received = false;
            IDisposable? unsubscriber = null;

            var d = observable.Subscribe(
                x =>
                {
                    if (!CheckReceived()) return;
                    action(x, null, false);
                },
                ex =>
                {
                    if (!CheckReceived()) return;
                    action(default!, ex, false);
                },
                () =>
                {
                    if (!CheckReceived()) return;
                    action(default!, null, true);
                });

            lock (lockObj)
            {
                unsubscriber = d;
            }

            if (received)
            {
                unsubscriber.Dispose();
                return ActionDisposable.Nop;
            }

            return unsubscriber;

            bool CheckReceived()
            {
                lock (lockObj)
                {
                    unsubscriber?.Dispose();
                    if (received) return false;
                    received = true;
                }
                return true;
            }
        }

        public static IObservable<T> Where<T>(this IObservable<T> observable, Func<T, bool> predicate)
        {
            if (observable == null) throw new ArgumentNullException(nameof(observable));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return new WhereObservable<T>(observable, predicate);
        }

        private sealed class WhereObservable<T> : IObservable<T>
        {
            private readonly IObservable<T> _observable;
            private readonly Func<T, bool> _predicate;

            public WhereObservable(IObservable<T> observable, Func<T, bool> predicate)
            {
                this._observable = observable;
                this._predicate = predicate;
            }

            public IDisposable Subscribe(IObserver<T> observer)
            {
                return this._observable
                    .Subscribe(
                        x =>
                        {
                            if (this._predicate(x))
                                observer.OnNext(x);
                        },
                        observer.OnError,
                        observer.OnCompleted
                    );
            }
        }
    }
}
