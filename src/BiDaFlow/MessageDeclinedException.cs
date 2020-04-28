using System;

namespace BiDaFlow
{
    public class MessageDeclinedException : Exception
    {
        public MessageDeclinedException()
            : base("The message was declined by the target block.")
        { }

        public MessageDeclinedException(string message)
            : base(message)
        { }

        public MessageDeclinedException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
