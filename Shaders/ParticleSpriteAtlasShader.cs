using ComputeSharp;
using SkiaSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Shaders
{
    /// <summary>
    /// Draws each live particle as an 16×16 sprite taken from a horizontal 8-frame atlas.
    /// Atlas layout: 8 tiles in a single row – tile 0 at x=0, tile 7 at right.
    /// The tile index comes from <see cref="GpuParticle.TextureIndex"/> (mod 8).
    /// Uses Z-buffer depth testing for flicker-free order-dependent blending.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ParticleSpriteAtlasShader : IComputeShader
    {
        private readonly ReadWriteBuffer<GpuParticle> particles;
        private readonly int                          maxParticles;
        private readonly ReadOnlyTexture2D<Float4>    atlas;
        private readonly ReadWriteTexture2D<Float4>   target;
        private readonly ReadWriteTexture2D<float>    depthBuffer;  // Z-buffer for depth testing
        private readonly Int2                         texSize;   // target size
        private readonly int                          tileSize;  // sprite side length in px (square)
        private readonly float                        atlasInvWidth;
        private readonly float                        atlasInvHeight;

        public ParticleSpriteAtlasShader(
            ReadWriteBuffer<GpuParticle> particles,
            int maxParticles,
            ReadOnlyTexture2D<Float4> atlas,
            ReadWriteTexture2D<Float4> target,
            ReadWriteTexture2D<float> depthBuffer,
            Int2 texSize,
            int tileSize)
        {
            this.particles        = particles;
            this.maxParticles     = maxParticles;
            this.atlas            = atlas;
            this.target           = target;
            this.depthBuffer      = depthBuffer;
            this.texSize          = texSize;
            this.tileSize         = tileSize;
            this.atlasInvWidth    = 1.0f / (tileSize * 8);
            this.atlasInvHeight   = 1.0f / tileSize;
        }

        public void Execute()
        {
            int id = ThreadIds.X;
            if (id >= maxParticles) return;

            var p = particles[id];
            if (p.Age >= p.MaxLifetime || p.Color.W <= 0.01f) return;

            int radius = tileSize / 2;
            Int2 center = new((int)p.Position.X, (int)p.Position.Y);

            int tileIndex = p.TextureIndex & 7; // 0-7
            int tileOffsetX = tileIndex * tileSize;

            // Use particle age as Z value for depth testing with unique spawn ID for consistency
            // Newer particles (lower age) should appear on top
            // Add unique spawn ID (stored in rotation) to prevent flickering from particle index reuse
            float uniqueSpawnId = p.Rotation; // Unique spawn ID stored in rotation field
            float particleZ = p.Age + (uniqueSpawnId * 0.00001f); // Age + tiny unique offset

            for (int sy = 0; sy < tileSize; sy++)
            {
                for (int sx = 0; sx < tileSize; sx++)
                {
                    Int2 dst = new(center.X + sx - radius, center.Y + sy - radius);
                    if (dst.X < 0 || dst.Y < 0 || dst.X >= texSize.X || dst.Y >= texSize.Y) continue;

                    Int2 texCoord = new(tileOffsetX + sx, sy);
                    Float4 src = atlas[texCoord];
                    src *= p.Color;         // tint + premultiply (p.Color already premul)
                    if (src.W <= 0.001f) continue;

                    // Z-buffer depth test with unique spawn ID for consistent ordering
                    float currentDepth = depthBuffer[dst];
                    
                    // Only proceed if this particle is closer (lower age = newer = on top)
                    if (particleZ < currentDepth)
                    {
                        // Perform premultiplied alpha "over" blending
                        Float4 dstColor = target[dst];
                        Float4 outColor = new Float4();
                        outColor.X = src.X + (1.0f - src.W) * dstColor.X;
                        outColor.Y = src.Y + (1.0f - src.W) * dstColor.Y;
                        outColor.Z = src.Z + (1.0f - src.W) * dstColor.Z;
                        outColor.W = src.W + (1.0f - src.W) * dstColor.W;
                        
                        // Update both color and depth buffer
                        target[dst] = outColor;
                        depthBuffer[dst] = particleZ;
                    }
                }
            }
        }
    }
} 