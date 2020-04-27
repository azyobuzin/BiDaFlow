using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Internal
{
    internal sealed class LinkManager<T> : IEnumerable<LinkRegistration<T>>
    {
        private readonly LinkedList<LinkRegistration<T>> _links = new LinkedList<LinkRegistration<T>>();
        private readonly Dictionary<ITargetBlock<T>, SingleLinkedList<LinkedListNode<LinkRegistration<T>>>> _targetToNodeTable = new Dictionary<ITargetBlock<T>, SingleLinkedList<LinkedListNode<LinkRegistration<T>>>>();

        public int Count => this._links.Count;

        public void AddLink(LinkRegistration<T> registration, bool append)
        {
            if (append)
            {
                var node = this._links.AddLast(registration);
                var listNode = new SingleLinkedList<LinkedListNode<LinkRegistration<T>>>(node);

                if (this._targetToNodeTable.TryGetValue(registration.Target, out var list))
                {
                    while (list.Next != null) list = list.Next;
                    list.Next = listNode;
                }
                else
                {
                    this._targetToNodeTable.Add(registration.Target, listNode);
                }
            }
            else
            {
                var node = this._links.AddFirst(registration);

                this._targetToNodeTable.TryGetValue(registration.Target, out var listNode);
                this._targetToNodeTable[registration.Target] = new SingleLinkedList<LinkedListNode<LinkRegistration<T>>>(node, listNode);
            }
        }

        public void RemoveLink(LinkRegistration<T> registration)
        {
            var newList = CreateRemovedList(this._targetToNodeTable[registration.Target]);

            if (newList == null)
            {
                this._targetToNodeTable.Remove(registration.Target);
            }
            else
            {
                this._targetToNodeTable[registration.Target] = newList;
            }

            SingleLinkedList<LinkedListNode<LinkRegistration<T>>>? CreateRemovedList(SingleLinkedList<LinkedListNode<LinkRegistration<T>>> listNode)
            {
                if (listNode.Value.Value == registration)
                {
                    this._links.Remove(listNode.Value);
                    return listNode.Next;
                }

                if (listNode.Next == null)
                    throw new ArgumentException("The specified registration is not found.");

                listNode.Next = CreateRemovedList(listNode.Next);
                return listNode;
            }
        }

        public LinkRegistration<T>? GetRegistration(ITargetBlock<T> target)
        {
            return this._targetToNodeTable.TryGetValue(target, out var node)
                ? node.Value.Value
                : null;
        }

        public LinkEnumerator GetEnumerator()
        {
            // Unlike LinkedList.Enumerator, this enumerator allows to remove item in enumerating.
            return new LinkEnumerator(this._links.First);
        }

        IEnumerator<LinkRegistration<T>> IEnumerable<LinkRegistration<T>>.GetEnumerator()
            => this.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => this.GetEnumerator();

        internal struct LinkEnumerator : IEnumerator<LinkRegistration<T>>
        {
            private LinkedListNode<LinkRegistration<T>>? _nextNode;

            public LinkEnumerator(LinkedListNode<LinkRegistration<T>> firstNode)
            {
                this._nextNode = firstNode;
                this.Current = default!;
            }

            public LinkRegistration<T> Current { get; private set; }

            object IEnumerator.Current => this.Current;

            public bool MoveNext()
            {
                if (this._nextNode == null) return false;

                this.Current = this._nextNode.Value;
                this._nextNode = this._nextNode.Next;

                return !this.Current.Unlinked || this.MoveNext();
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }
    }
}
