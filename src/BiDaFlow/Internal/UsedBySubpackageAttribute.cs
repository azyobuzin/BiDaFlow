using System;

namespace BiDaFlow.Internal
{
    /// <summary>
    /// Types and members marked as <see cref="UsedBySubpackageAttribute"/> are used by the sub-packages.
    /// These cannot be broken easily like public members.
    /// </summary>
    [AttributeUsage(AttributeTargets.All)]
    internal sealed class UsedBySubpackageAttribute : Attribute
    {
    }
}
