using System;

namespace BiDaFlow.Actors
{
    public class KilledBySupervisorException : Exception
    {
        public KilledBySupervisorException()
            : base("The block is killed by the supervisor.")
        { }

        public KilledBySupervisorException(string message)
            : base(message)
        { }

        public KilledBySupervisorException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
