using System.Runtime.InteropServices;

namespace BiDaFlow.Actors.Internal
{
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Optional<T>
    {
        public bool HasValue { get; }
        public T Value { get; }

        public Optional(T value)
        {
            this.HasValue = true;
            this.Value = value;
        }

        public static Optional<T> None => default;

        public override string ToString()
        {
            return this.HasValue
                ? "Some(" + (this.Value?.ToString() ?? "null") + ")"
                : "None";
        }

        public override bool Equals(object obj)
        {
            return obj is Optional<T> opt &&
                this.HasValue == opt.HasValue &&
                Equals(this.Value, opt.Value);
        }

        public override int GetHashCode()
        {
            return this.HasValue
                ? (this.Value?.GetHashCode() ?? 0)
                : int.MaxValue;
        }
    }
}
