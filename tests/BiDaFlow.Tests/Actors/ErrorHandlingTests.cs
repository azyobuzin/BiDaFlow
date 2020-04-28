using System;
using System.Threading.Tasks;
using BiDaFlow.Actors;
using ChainingAssertion;
using Xunit;

namespace BiDaFlow.Tests.Actors
{
    public partial class ErrorHandlingTests
    {
        [Fact]
        public async Task TestThrowToSender()
        {
            var actor = new TestErrorHandlingActor();

            var ex = await Assert.ThrowsAsync<Exception>(() =>
                actor.Throw().PostAndReceiveReplyAsync().CompleteSoon());
            ex.Message.Is("test1");

            actor.Completion.IsCompleted.IsFalse();
        }

        [Fact]
        public async Task TestDiscardReply()
        {
            var actor = new TestErrorHandlingActor();
            actor.Throw().DiscardReply().Post().IsTrue();

            // If the reply is discarded, the exception is thrown to the receiver.
            var ex = await Assert.ThrowsAsync<Exception>(() => actor.Completion.CompleteSoon());
            ex.Message.Is("test1");
        }

        private class TestErrorHandlingActor : Actor
        {
            public EnvelopeWithReply<int> Throw()
            {
                return this.CreateMessageWithReply(
                    new Func<int>(() => throw new Exception("test1"))
                );
            }
        }
    }
}
