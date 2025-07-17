using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Shaders;

/// <summary>
/// Particle culling shader that removes dead particles and returns them to freelist
/// CRITICAL: This shader must return dead particles to freelist for proper particle reuse
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ParticleCullShader : IComputeShader
{
    private readonly ReadWriteBuffer<GpuParticle> particles;
    private readonly ReadWriteBuffer<int> aliveCount;
    private readonly ReadWriteBuffer<int> freeList;
    private readonly ReadWriteBuffer<int> freeListCount;
    private readonly int maxParticles;
    
    public ParticleCullShader(
        ReadWriteBuffer<GpuParticle> particles,
        ReadWriteBuffer<int> aliveCount,
        ReadWriteBuffer<int> freeList,
        ReadWriteBuffer<int> freeListCount,
        int maxParticles)
    {
        this.particles = particles;
        this.aliveCount = aliveCount;
        this.freeList = freeList;
        this.freeListCount = freeListCount;
        this.maxParticles = maxParticles;
    }
    
    public void Execute()
    {
        var index = ThreadIds.X;
        
        // Only process valid particle indices
        if (index >= maxParticles) return;
        
        var particle = particles[index];
        
        // Check if particle is alive
        if (particle.Age < particle.MaxLifetime && particle.Color.W > 0.01f)
        {
            // Particle is alive, increment counter atomically
            Hlsl.InterlockedAdd(ref aliveCount[0], 1);
        }
        else if (particle.InFreelist == 0.0f)
        {
            // Particle is dead AND not yet in freelist - return it to freelist for reuse
            
            // Mark particle as completely dead
            particle.Age = particle.MaxLifetime;
            particle.Color.W = 0.0f;
            particle.InFreelist = 1.0f; // Mark as already returned to freelist
            particles[index] = particle;
            
            // CRITICAL: Add dead particle back to freelist (only once)
            int freelistIndex;
            Hlsl.InterlockedAdd(ref freeListCount[0], 1, out freelistIndex);
            
            // Store the particle index in the freelist
            if (freelistIndex < maxParticles)
            {
                freeList[freelistIndex] = index;
            }
        }
    }
} 