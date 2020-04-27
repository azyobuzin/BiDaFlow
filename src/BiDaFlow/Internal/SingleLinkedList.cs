namespace BiDaFlow.Internal
{
    internal sealed class SingleLinkedList<T>
    {
        public T Value { get; set; }

        public SingleLinkedList<T>? Next { get; set; }

        public SingleLinkedList(T value)
        {
            this.Value = value;
        }

        public SingleLinkedList(T value, SingleLinkedList<T>? next)
        {
            this.Value = value;
            this.Next = next;
        }
    }
}
