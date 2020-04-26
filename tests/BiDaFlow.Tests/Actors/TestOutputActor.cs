using BiDaFlow.Actors;

namespace BiDaFlow.Tests.Actors
{
    internal class TestOutputActor : Actor<int>
    {
        public Envelope TenTimes(int n)
        {
            return this.CreateMessage(async () =>
            {
                var output = n * 10;
                await this.SendOutputAsync(output).ConfigureAwait(false);
            });
        }

        public void Stop()
        {
            this.Complete();
        }
    }
}
