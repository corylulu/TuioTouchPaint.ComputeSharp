using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Shaders;

/// <summary>
/// Compute shader for updating GPU particles
/// Handles position updates, age tracking, fade logic, and texture cycling
/// Designed for high-performance processing of 100,000+ particles
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ParticleUpdateShader : IComputeShader
{
    /// <summary>
    /// Particle buffer to update
    /// </summary>
    private readonly ReadWriteBuffer<GpuParticle> particles;
    
    /// <summary>
    /// Time elapsed since last update (in seconds)
    /// </summary>
    private readonly float deltaTime;
    
    /// <summary>
    /// Current time for texture animation calculations
    /// </summary>
    private readonly float currentTime;
    
    /// <summary>
    /// Canvas width for boundary checking
    /// </summary>
    private readonly float canvasWidth;
    
    /// <summary>
    /// Canvas height for boundary checking
    /// </summary>
    private readonly float canvasHeight;
    
    /// <summary>
    /// Global particle settings
    /// </summary>
    private readonly float globalGravity;
    private readonly float globalDrag;
    private readonly float fadeStartThreshold;

    public ParticleUpdateShader(
        ReadWriteBuffer<GpuParticle> particles,
        float deltaTime,
        float currentTime,
        float canvasWidth,
        float canvasHeight,
        float globalGravity = 0.0f,
        float globalDrag = 0.98f,
        float fadeStartThreshold = 0.7f)
    {
        this.particles = particles;
        this.deltaTime = deltaTime;
        this.currentTime = currentTime;
        this.canvasWidth = canvasWidth;
        this.canvasHeight = canvasHeight;
        this.globalGravity = globalGravity;
        this.globalDrag = globalDrag;
        this.fadeStartThreshold = fadeStartThreshold;
    }

    /// <summary>
    /// Execute the compute shader for one particle
    /// </summary>
    public void Execute()
    {
        // Get the current particle index
        var index = ThreadIds.X;
        
        // Load particle data
        var particle = particles[index];
        
        // Skip dead particles
        if (particle.Age >= particle.MaxLifetime)
        {
            return;
        }
        
        // Update age
        particle.Age += deltaTime;
        
        // PAINT BEHAVIOR: Particles stay where they're painted (no position updates)
        // Don't update position - particles should remain stationary like paint
        
        // PAINT BEHAVIOR: No rotation animation for paint particles
        // Don't update rotation - keep particles oriented as painted
        
        // PAINT BEHAVIOR: No physics for paint particles
        // Don't apply physics - paint doesn't move after being applied
        
        // Update opacity based on age (fade out only near end of lifetime)
        UpdatePaintOpacity(ref particle);
        
        // Texture animation disabled for paint particles – keep per-particle frame
        
        // Write back the updated particle
        particles[index] = particle;
    }
    
    /// <summary>
    /// Update particle opacity for paint behavior (fade only near end of lifetime)
    /// </summary>
    private void UpdatePaintOpacity(ref GpuParticle particle)
    {
        var normalizedAge = particle.Age / particle.MaxLifetime;
        
        // Start fading earlier and make it more dramatic
        if (normalizedAge >= fadeStartThreshold)
        {
            // Calculate fade progress in the remaining lifetime
            var fadeRange = 1.0f - fadeStartThreshold;
            var fadeProgress = (normalizedAge - fadeStartThreshold) / fadeRange;
            
            // Apply more dramatic fade curve - starts slow, accelerates
            var fadeMultiplier = 1.0f - (fadeProgress * fadeProgress * fadeProgress);
            
            // CRITICAL FIX: Set alpha directly based on fade multiplier, not multiply existing alpha
            // This prevents exponential decay that causes flickering
            particle.Color.W = fadeMultiplier;
        }
        else
        {
            // Before fade threshold, particles should be fully opaque
            particle.Color.W = 1.0f;
        }
        // Kill particles that have faded to nearly invisible
        if (particle.Color.W < 0.02f)
        {
            particle.Age = particle.MaxLifetime; // Mark as dead
            particle.Color.W = 0.0f;
        }
        
        // Clamp opacity
        particle.Color.W = Hlsl.Clamp(particle.Color.W, 0.0f, 1.0f);
    }
    
    /// <summary>
    /// Update texture animation for multi-frame brushes
    /// </summary>
    private void UpdateTextureAnimation(ref GpuParticle particle)
    {
        // For now, we'll use a simple time-based animation
        // In a full implementation, this would use the brush configuration
        var animationSpeed = 10.0f; // Frames per second
        var frameIndex = (int)(currentTime * animationSpeed) % 8; // Assume max 8 frames
        
        particle.TextureIndex = frameIndex;
    }
    
    /// <summary>
    /// Apply boundary conditions (keep particles within canvas or remove them)
    /// </summary>
    private void ApplyBoundaryConditions(ref GpuParticle particle)
    {
        // Option 1: Kill particles that go out of bounds
        if (particle.Position.X < -50.0f || particle.Position.X > canvasWidth + 50.0f ||
            particle.Position.Y < -50.0f || particle.Position.Y > canvasHeight + 50.0f)
        {
            // Mark particle as dead by setting age to max lifetime
            particle.Age = particle.MaxLifetime;
            particle.Color.W = 0.0f;
        }
        
        // Option 2: Bounce particles off boundaries (commented out)
        /*
        if (particle.Position.X < 0.0f || particle.Position.X > canvasWidth)
        {
            particle.Velocity.X *= -0.8f; // Bounce with energy loss
            particle.Position.X = Hlsl.Clamp(particle.Position.X, 0.0f, canvasWidth);
        }
        
        if (particle.Position.Y < 0.0f || particle.Position.Y > canvasHeight)
        {
            particle.Velocity.Y *= -0.8f; // Bounce with energy loss
            particle.Position.Y = Hlsl.Clamp(particle.Position.Y, 0.0f, canvasHeight);
        }
        */
    }
}

 