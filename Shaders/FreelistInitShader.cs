using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Shaders;

/// <summary>
/// Compute shader to initialize the freelist buffer with all particle indices
/// This is run once at startup to populate the freelist and initialize particle data
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct FreelistInitShader : IComputeShader
{
    private readonly ReadWriteBuffer<int> freelist;
    private readonly ReadWriteBuffer<int> freelistCount;
    private readonly ReadWriteBuffer<GpuParticle> particles;
    private readonly int maxParticles;
    
    public FreelistInitShader(
        ReadWriteBuffer<int> freelist,
        ReadWriteBuffer<int> freelistCount,
        ReadWriteBuffer<GpuParticle> particles,
        int maxParticles)
    {
        this.freelist = freelist;
        this.freelistCount = freelistCount;
        this.particles = particles;
        this.maxParticles = maxParticles;
    }
    
    public void Execute()
    {
        var index = ThreadIds.X;
        
        // Initialize freelist with all particle indices
        if (index < maxParticles)
        {
            freelist[index] = index;
            
            // Initialize particle data - all particles start as dead and in freelist
            var particle = new GpuParticle();
            particle.Position = new Float3(0.0f, 0.0f, 0.0f);
            particle.Velocity = new Float3(0.0f, 0.0f, 0.0f);
            particle.Color = new Float4(0.0f, 0.0f, 0.0f, 0.0f);
            particle.Size = 0.0f;
            particle.Age = 1000.0f; // Very old = dead
            particle.MaxLifetime = 1.0f; // Short lifetime
            particle.TextureIndex = 0;
            particle.SessionId = 0;
            particle.Rotation = 0.0f;
            particle.AngularVelocity = 0.0f;
            particle.Pressure = 0.0f;
            particle.InFreelist = 1.0f; // Mark as already in freelist
            
            particles[index] = particle;
        }
        
        // Set freelist count to maxParticles (thread 0 only)
        if (index == 0)
        {
            freelistCount[0] = maxParticles;
        }
    }
} 