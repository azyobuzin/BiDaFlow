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

        internal static MessageNeverProcessedException CreateDeclined()
        {
            return new MessageNeverProcessedException("The message was declined by the target block.");
        }

        internal static MessageNeverProcessedException CreateCompleted()
        {
            return new MessageNeverProcessedException("The message was enqueued to the actor but the actor has been completed.");
        }
    }
}
