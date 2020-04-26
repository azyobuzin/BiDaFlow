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
            var ex = await Assert.ThrowsAsync<Exception>(() => actor.ThrowToSender().PostAndReceiveReplyAsync());
            ex.Message.Is("test1");
            actor.Completion.IsCompleted.IsFalse();
        }

        [Fact]
        public async Task TestThrowToReceiver()
        {
            var actor = new TestErrorHandlingActor();

            // If an exception is thrown to the receiver, the task should be canceled.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actor.ThrowToReceiver().PostAndReceiveReplyAsync());

            var aex = Assert
                .Throws<AggregateException>(() => actor.Completion.Wait(TestUtils.CancelSometimeSoon()))
                .Flatten();
            aex.InnerExceptions.Count.Is(1);
            aex.InnerException.Message.Is("test2");
        }

        [Fact]
        public void TestDiscardReply()
        {
            var actor = new TestErrorHandlingActor();
            actor.ThrowToSender().DiscardReply().Post().IsTrue();

            // If the reply is discarded, the exception is thrown to the receiver even if handleErrorByReceiver is false.
            var aex = Assert
                .Throws<AggregateException>(() => actor.Completion.Wait(TestUtils.CancelSometimeSoon()))
                .Flatten();
            aex.InnerExceptions.Count.Is(1);
            aex.InnerException.Message.Is("test1");
        }

        private class TestErrorHandlingActor : Actor
        {
            public EnvelopeWithReply<int> ThrowToSender()
            {
                return this.CreateMessageWithReply(
                    new Func<int>(() => throw new Exception("test1"))
                );
            }

            public EnvelopeWithReply<int> ThrowToReceiver()
            {
                return this.CreateMessageWithReply(
                    new Func<int>(() => throw new Exception("test2")),
                    true
                );
            }
        }
    }
}
