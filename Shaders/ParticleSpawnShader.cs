using System;
using System.Numerics;
using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Shaders
{
    /// <summary>
    /// Spawns particles from batched input events
    /// Based on proven GPU particle system patterns from AMD and industry research
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ParticleSpawnShader : IComputeShader
    {
        private readonly ReadWriteBuffer<GpuParticle> _particles;
        private readonly ReadWriteBuffer<int> _freeList;
        private readonly ReadWriteBuffer<int> _freeListCount;
        private readonly ReadOnlyBuffer<InputEvent> _inputEvents;
        private readonly int _eventCount;
        private readonly int _maxParticles;
        private readonly float _currentTime;
        private readonly float _particleLifetime;
        private readonly int _particlesPerEvent;
        private readonly uint _uniqueSpawnId;

        public ParticleSpawnShader(
            ReadWriteBuffer<GpuParticle> particles,
            ReadWriteBuffer<int> freeList,
            ReadWriteBuffer<int> freeListCount,
            ReadOnlyBuffer<InputEvent> inputEvents,
            int eventCount,
            int maxParticles,
            float currentTime,
            float particleLifetime = 3.0f,
            int particlesPerEvent = 10,
            uint uniqueSpawnId = 0)
        {
            _particles = particles;
            _freeList = freeList;
            _freeListCount = freeListCount;
            _inputEvents = inputEvents;
            _eventCount = eventCount;
            _maxParticles = maxParticles;
            _currentTime = currentTime;
            _particleLifetime = particleLifetime;
            _particlesPerEvent = particlesPerEvent;
            _uniqueSpawnId = uniqueSpawnId;
        }

        /// <summary>
        /// Simple GPU-friendly random number generator
        /// </summary>
        private static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        private static float Random01(uint seed)
        {
            return (Hash(seed) & 0x00FFFFFF) / 16777216.0f;
        }

        private static Float2 RandomInCircle(uint seed)
        {
            float angle = Random01(seed) * 6.28318530718f; // 2 * PI
            float radius = Hlsl.Sqrt(Random01(seed + 1));
            return new Float2(Hlsl.Cos(angle) * radius, Hlsl.Sin(angle) * radius);
        }

            public void Execute()
    {
        var threadId = (int)ThreadIds.X;
        
        // Exit if we're beyond the number of input events (safety check)
        if (threadId >= _eventCount) return;
        
        // Additional bounds check for safety
        if (threadId < 0) return;

        var inputEvent = _inputEvents[threadId];
            
            // Spawn multiple particles per input event
            for (int i = 0; i < _particlesPerEvent; i++)
            {
                // Try to get a free particle index with validation
                int oldCount;
                Hlsl.InterlockedAdd(ref _freeListCount[0], -1, out oldCount);
                int freeIndex = oldCount - 1;
                
                // If no free particles available, restore count and give up
                if (freeIndex < 0)
                {
                    Hlsl.InterlockedAdd(ref _freeListCount[0], 1);
                    break;
                }
                
                // Validate freelist index bounds
                if (freeIndex >= _maxParticles)
                {
                    Hlsl.InterlockedAdd(ref _freeListCount[0], 1);
                    break;
                }

                // Get the actual particle index from the free list
                var particleIndex = _freeList[freeIndex];
                
                // Validate particle index bounds
                if (particleIndex < 0 || particleIndex >= _maxParticles)
                {
                    Hlsl.InterlockedAdd(ref _freeListCount[0], 1);
                    break;
                }
                
                // Create unique seed based on spawn ID, thread ID, and particle index
                // This ensures particles have consistent properties even when indices are recycled
                var seed = (uint)(_uniqueSpawnId * 10000 + threadId * 100 + particleIndex * 10 + i);
                
                // Create small random offset for natural paint texture
                var smallOffset = RandomInCircle(seed) * 2.0f; // Very small 2 pixel spread for concentrated paint
                
                // Create stationary paint particle
                var particle = new GpuParticle();
                particle.Position = new Float3(
                    inputEvent.Position.X + smallOffset.X,
                    inputEvent.Position.Y + smallOffset.Y,
                    0.0f
                );
                // PAINT BEHAVIOR: Particles stay where they're placed (no velocity)
                particle.Velocity = new Float3(0.0f, 0.0f, 0.0f);
                
                particle.Color = new Float4(
                    inputEvent.Color.X + (Random01(seed + 100) - 0.5f) * 0.1f, // Less color variation
                    inputEvent.Color.Y + (Random01(seed + 101) - 0.5f) * 0.1f,
                    inputEvent.Color.Z + (Random01(seed + 102) - 0.5f) * 0.1f,
                    1.0f
                );
                particle.Size = inputEvent.Size * (0.9f + Random01(seed + 200) * 0.2f); // Less size variation
                particle.Age = 0.0f;
                particle.MaxLifetime = _particleLifetime * (0.9f + Random01(seed + 300) * 0.2f); // 8 seconds ± 10%
                particle.SessionId = inputEvent.SessionId;
                particle.TextureIndex = inputEvent.TextureIndex & 7;
                // CRITICAL: Store unique spawn ID in rotation field for consistent depth sorting
                particle.Rotation = Hlsl.AsFloat(_uniqueSpawnId * 1000 + (uint)particleIndex);
                particle.AngularVelocity = 0.0f; // No rotation animation
                particle.Pressure = 1.0f;
                particle.InFreelist = 0.0f; // Mark as not in freelist (alive)

                _particles[particleIndex] = particle;
            }
        }
    }
} 