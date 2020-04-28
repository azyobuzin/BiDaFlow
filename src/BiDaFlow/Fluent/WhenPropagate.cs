using System;

namespace BiDaFlow.Fluent
{
    [Flags]
    public enum WhenPropagate
    {
        Complete = 1,
        Fault = 2,
        Both = Complete | Fault,
    }
}
