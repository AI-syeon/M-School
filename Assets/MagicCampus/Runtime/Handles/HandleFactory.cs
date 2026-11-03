using System;
using MagicCampus.Contracts;

namespace MagicCampus.Runtime
{
    public sealed class HandleFactory : IHandleFactory
    {
        private readonly ICompletionQueue queue;
        private int created;
        private int live;

        public HandleFactory(ICompletionQueue queue)
        {
            this.queue = RuntimeGuard.Required(queue, nameof(queue));
        }

        // Debug counters: handles created, and handles not yet terminal.
        public int CreatedCount => created;
        public int LiveCount => live;

        public IHandleController<T> Create<T>(string operationId)
        {
            var handle = new Handle<T>(operationId, queue, OnHandleTerminal);
            created++;
            live++;
            return handle;
        }

        private void OnHandleTerminal()
        {
            live--;
        }
    }
}
