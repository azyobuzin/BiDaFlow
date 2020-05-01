using System;
using BiDaFlow.Internal;

namespace BiDaFlow.Actors.Internal
{
    internal sealed class BehaviorSubject<T> : IObservable<T>, IObserver<T>
    {
        private readonly DoubleLinkedList<IObserver<T>> _subscribers = new DoubleLinkedList<IObserver<T>>();

        private T _value;
        private bool _isCompleted;
        private Exception? _error;

        public BehaviorSubject(T initialValue)
        {
            this._value = initialValue;
        }

        public T Value
        {
            get
            {
                lock (this.Lock) return this._value;
            }
        }

        private object Lock => this._subscribers;

        public void OnNext(T value)
        {
            DoubleLinkedList<IObserver<T>>.Node? subscriberNode;

            lock (this.Lock)
            {
                if (this._isCompleted) return;

                this._value = value;
                subscriberNode = this._subscribers.First;
            }

            for (; subscriberNode != null; subscriberNode = subscriberNode.Next)
            {
                subscriberNode.Value.OnNext(value);
            }
        }

        public void OnError(Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));

            DoubleLinkedList<IObserver<T>>.Node? subscriberNode;

            lock (this.Lock)
            {
                if (this._isCompleted) return;

                this._isCompleted = true;
                this._error = error;

                subscriberNode = this._subscribers.First;

                this._subscribers.Clear();
            }

            for (; subscriberNode != null; subscriberNode = subscriberNode.Next)
            {
                subscriberNode.Value.OnError(error);
            }
        }

        public void OnCompleted()
        {
            DoubleLinkedList<IObserver<T>>.Node? subscriberNode;

            lock (this.Lock)
            {
                if (this._isCompleted) return;

                this._isCompleted = true;

                subscriberNode = this._subscribers.First;

                // Clear the list to GC subscribers
                this._subscribers.Clear();
            }

            for (; subscriberNode != null; subscriberNode = subscriberNode.Next)
            {
                subscriberNode.Value.OnCompleted();
            }
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            DoubleLinkedList<IObserver<T>>.Node node;
            T value;

            lock (this.Lock)
            {
                if (this._isCompleted)
                    goto AlreadyCompleted;

                node = this._subscribers.AddLast(observer);
                value = this.Value;
            }

            observer.OnNext(value);

            return new ActionDisposable(() =>
            {
                lock (this.Lock) this._subscribers.Remove(node);
            });

        AlreadyCompleted:
            if (this._error == null)
                observer.OnCompleted();
            else
                observer.OnError(this._error);

            return ActionDisposable.Nop;
        }
    }
}
