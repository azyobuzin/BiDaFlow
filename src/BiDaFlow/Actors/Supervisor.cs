using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BiDaFlow.Actors
{
    public static class Supervisor
    {
        public static SupervisedBlock<T> OneForOne<T>(Func<Task<T>> startFunc, Func<T, AggregateException?, Task<RescueAction>> rescueFunc)
            where T : IDataflowBlock
        {
            return new SupervisedBlock<T>(startFunc, rescueFunc, TaskScheduler.Default);
        }

        public static SupervisedBlock<T> OneForOne<T>(Func<T> startFunc, Func<T, AggregateException?, Task<RescueAction>> rescueFunc)
            where T : IDataflowBlock
        {
            return new SupervisedBlock<T>(() => Task.FromResult(startFunc()), rescueFunc, TaskScheduler.Default);
        }
    }
}
