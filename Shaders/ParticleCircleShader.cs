using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Shaders
{
    /// <summary>
    /// Rasterises each live particle as a solid circle with premultiplied alpha onto a 2D colour target.
    /// Uses Z-buffer depth testing for flicker-free order-dependent blending.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ParticleCircleShader : IComputeShader
    {
        // Input buffers
        private readonly ReadWriteBuffer<GpuParticle> particles;
        private readonly int maxParticles;

        // Output target
        private readonly ReadWriteTexture2D<Float4> target;
        private readonly ReadWriteTexture2D<float> depthBuffer;  // Z-buffer for depth testing
        private readonly Int2 textureSize;

        public ParticleCircleShader(
            ReadWriteBuffer<GpuParticle> particles,
            int maxParticles,
            ReadWriteTexture2D<Float4> target,
            ReadWriteTexture2D<float> depthBuffer,
            Int2 textureSize)
        {
            this.particles = particles;
            this.maxParticles = maxParticles;
            this.target = target;
            this.depthBuffer = depthBuffer;
            this.textureSize = textureSize;
        }

        public void Execute()
        {
            int id = ThreadIds.X;
            if (id >= maxParticles) return;

            var p = particles[id];
            if (p.Age >= p.MaxLifetime || p.Color.W <= 0.01f) return;

            int radius = (int)Hlsl.Clamp(p.Size * 0.5f, 1.0f, 512.0f);
            Int2 center = new((int)p.Position.X, (int)p.Position.Y);

            Float4 srcColor = p.Color; // premultiplied assumed
            
            // Use particle age as Z value for depth testing with unique spawn ID for consistency
            // Newer particles (lower age) should appear on top
            // Add unique spawn ID (stored in rotation) to prevent flickering from particle index reuse
            float uniqueSpawnId = p.Rotation; // Unique spawn ID stored in rotation field
            float particleZ = p.Age + (uniqueSpawnId * 0.00001f); // Age + tiny unique offset

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;

                    Int2 dst = new(center.X + dx, center.Y + dy);
                    if (dst.X < 0 || dst.Y < 0 || dst.X >= textureSize.X || dst.Y >= textureSize.Y) continue;

                    // Z-buffer depth test with unique spawn ID for consistent ordering
                    float currentDepth = depthBuffer[dst];
                    
                    // Only proceed if this particle is closer (lower age = newer = on top)
                    if (particleZ < currentDepth)
                    {
                        // Perform premultiplied alpha "over" blending
                        Float4 dstColor = target[dst];
                        Float4 outColor = new Float4();
                        outColor.X = srcColor.X + (1.0f - srcColor.W) * dstColor.X;
                        outColor.Y = srcColor.Y + (1.0f - srcColor.W) * dstColor.Y;
                        outColor.Z = srcColor.Z + (1.0f - srcColor.W) * dstColor.Z;
                        outColor.W = srcColor.W + (1.0f - srcColor.W) * dstColor.W;
                        
                        // Update both color and depth
                        target[dst] = outColor;
                        depthBuffer[dst] = particleZ;
                    }
                }
            }
        }
    }
} 