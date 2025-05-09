using System;
using System.Collections;
using System.Collections.Generic;

namespace Orchestrator.Supervisor
{
    /// <summary>
    /// A threadsafe, fixed-size FIFO queue. When capacity is exceeded,
    /// the oldest element is automatically dequeued.
    /// </summary>
    public class FixedSizedQueue<T> : IEnumerable<T>
    {
        private readonly object _lock = new object();
        private readonly Queue<T> _queue;

        /// <summary>
        /// Maximum number of items the queue will hold.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Creates a new FixedSizedQueue with the given capacity.
        /// </summary>
        public FixedSizedQueue(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0");

            Capacity = capacity;
            _queue = new Queue<T>(capacity);
        }

        /// <summary>
        /// Enqueues an item. If the queue is at capacity, dequeues the oldest first.
        /// </summary>
        public void Enqueue(T item)
        {
            lock (_lock)
            {
                if (_queue.Count == Capacity)
                    _queue.Dequeue();

                _queue.Enqueue(item);
            }
        }

        /// <summary>
        /// Returns a snapshot of the items in FIFO order.
        /// </summary>
        public T[] Items
        {
            get
            {
                lock (_lock)
                {
                    return _queue.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            // Enumerate over a snapshot to avoid locking the whole enumeration
            foreach (var item in Items)
                yield return item;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
