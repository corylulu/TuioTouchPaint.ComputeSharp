using System;
using System.Collections.Generic;
using System.Numerics;
using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Models
{
    /// <summary>
    /// Batches mouse/TUIO inputs for efficient GPU particle spawning
    /// Based on proven GPU particle system patterns
    /// </summary>
    public struct InputEvent
    {
        public Float2 Position;
        public Float2 Velocity;
        public Float4 Color;
        public float Size;
        public float Timestamp;
        public int SessionId;
        public int TextureIndex;
        public float Rotation;
    }

    /// <summary>
    /// Circular buffer for batching input events before sending to GPU
    /// Minimizes CPU-GPU transfers by batching multiple events
    /// </summary>
    public class InputBatch
    {
        private readonly InputEvent[] _events;
        private readonly int _capacity;
        private int _head;
        private int _tail;
        private int _count;

        public InputBatch(int capacity = 1024)
        {
            _capacity = capacity;
            _events = new InputEvent[capacity];
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        private static int _counter;
        private static readonly Random _rng = new();

        public void AddEvent(Vector2 position, Vector2 velocity, Vector4 color, float size, int sessionId)
        {
            int texIdx = _counter++ & 7;
            float rot = (float)(_rng.NextDouble() * MathF.Tau);

            if (_count >= _capacity)
            {
                // Overwrite oldest event (ring buffer behavior)
                _tail = (_tail + 1) % _capacity;
                _count--;
            }

            _events[_head] = new InputEvent
            {
                Position = new Float2(position.X, position.Y),
                Velocity = new Float2(velocity.X, velocity.Y),
                Color = new Float4(color.X, color.Y, color.Z, color.W),
                Size = size,
                Timestamp = (float)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0f,
                SessionId = sessionId,
                TextureIndex = texIdx,
                Rotation = rot
            };

            _head = (_head + 1) % _capacity;
            _count++;
        }

        public ReadOnlySpan<InputEvent> GetEvents()
        {
            if (_count == 0) return ReadOnlySpan<InputEvent>.Empty;

            // Handle wraparound case
            if (_head > _tail)
            {
                return _events.AsSpan(_tail, _count);
            }
            else
            {
                // Events wrap around the circular buffer
                var result = new InputEvent[_count];
                int firstPart = _capacity - _tail;
                Array.Copy(_events, _tail, result, 0, firstPart);
                Array.Copy(_events, 0, result, firstPart, _head);
                return result.AsSpan();
            }
        }

        public void Clear()
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        public int Count => _count;
    }
} 