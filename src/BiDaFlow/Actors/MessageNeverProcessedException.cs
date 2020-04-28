using System;

namespace BiDaFlow.Actors
{
    public class MessageNeverProcessedException : Exception
    {
        public MessageNeverProcessedException()
           : base("The message is never processed.")
        { }

        public MessageNeverProcessedException(string message)
            : base(message)
        { }

        public MessageNeverProcessedException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
