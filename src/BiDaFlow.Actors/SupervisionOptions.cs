namespace BiDaFlow.Actors
{
    public class SupervisionOptions
    {
        /// <summary>
        /// Gets or sets whether an exception thrown by the child will propagated to the supervisor. 
        /// </summary>
        public bool OneForAll { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the supervisor kills (throws <see cref="KilledBySupervisorException"/> to) the child
        /// when the supervisor is being completed.
        /// </summary>
        public bool KillWhenCompleted { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the supervisor waits for completion of the child when the supervisor is being completed.
        /// </summary>
        public bool WaitCompletion { get; set; } = true;

        internal static readonly SupervisionOptions Default = new SupervisionOptions();
    }
}
