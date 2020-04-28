using System;

namespace BiDaFlow.Internal
{
    internal static class IdentityFunc<T>
    {
        public static readonly Func<T, T> Instance = x => x;
    }
}
