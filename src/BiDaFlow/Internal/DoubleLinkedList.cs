using System.Diagnostics;

namespace BiDaFlow.Internal
{
    [UsedBySubpackage]
    internal sealed class DoubleLinkedList<T>
    {
        public Node? First { get; private set; }
        public Node? Last { get; private set; }
        public int Count { get; private set; }

        public Node AddFirst(T value)
        {
            var node = new Node(value, this.First, null);

            if (this.First != null)
            {
                Debug.Assert(this.First.Previous == null);
                this.First.Previous = node;
            }

            this.First = node;

            if (this.Last == null)
                this.Last = node;

            this.Count++;

            return node;
        }

        public Node AddLast(T value)
        {
            var node = new Node(value, null, this.Last);

            if (this.Last != null)
            {
                Debug.Assert(this.Last.Next == null);
                this.Last.Next = node;
            }

            this.Last = node;

            if (this.First == null)
                this.First = node;

            this.Count++;

            return node;
        }

        public void Remove(Node node)
        {
            // Unlike System.Collections.Generic.LinkedList,
            // Next and Previous of the removed node will not be cleared.

            if (node.Previous == null)
            {
                this.First = node.Next;
            }
            else
            {
                node.Previous.Next = node.Next;
            }

            if (node.Next == null)
            {
                this.Last = node.Previous;
            }
            else
            {
                node.Next.Previous = node.Previous;
            }

            this.Count--;
        }

        public void Clear()
        {
            this.First = null;
            this.Last = null;
            this.Count = 0;
        }

        public class Node
        {
            public T Value { get; set; }
            public Node? Next { get; set; }
            public Node? Previous { get; set; }

            public Node(T value, Node? next, Node? previous)
            {
                this.Value = value;
                this.Next = next;
                this.Previous = previous;
            }
        }
    }
}
