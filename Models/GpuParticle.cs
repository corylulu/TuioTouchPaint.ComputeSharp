using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Models;

/// <summary>
/// GPU-compatible particle structure for ComputeSharp compute shaders
/// Uses only primitive types that can be efficiently processed on GPU
/// </summary>
public struct GpuParticle
{
    /// <summary>
    /// Current position (X, Y, Z - Z can be used for layering)
    /// </summary>
    public Float3 Position;
    
    /// <summary>
    /// Velocity vector (X, Y, Z)
    /// </summary>
    public Float3 Velocity;
    
    /// <summary>
    /// Color (R, G, B, A) - Alpha used for opacity/fading
    /// </summary>
    public Float4 Color;
    
    /// <summary>
    /// Size/radius of the particle
    /// </summary>
    public float Size;
    
    /// <summary>
    /// Age of particle in seconds
    /// </summary>
    public float Age;
    
    /// <summary>
    /// Maximum lifetime in seconds
    /// </summary>
    public float MaxLifetime;
    
    /// <summary>
    /// Texture index for multi-frame brushes (0-based)
    /// </summary>
    public int TextureIndex;
    
    /// <summary>
    /// Session ID (corresponds to TUIO port/input source)
    /// </summary>
    public int SessionId;
    
    /// <summary>
    /// Rotation angle in radians
    /// </summary>
    public float Rotation;
    
    /// <summary>
    /// Angular velocity in radians per second
    /// </summary>
    public float AngularVelocity;
    
    /// <summary>
    /// Pressure value (0.0 - 1.0) affects size/opacity
    /// </summary>
    public float Pressure;
    
    /// <summary>
    /// Freelist flag - 0 = not in freelist, 1 = already returned to freelist
    /// Used to prevent adding dead particles to freelist multiple times
    /// </summary>
    public float InFreelist;
    
    /// <summary>
    /// Whether this particle is alive (computed property for GPU)
    /// </summary>
    public bool IsAlive => Age < MaxLifetime;
    
    /// <summary>
    /// Normalized age (0.0 = just born, 1.0 = about to die)
    /// </summary>
    public float NormalizedAge => MaxLifetime > 0 ? Age / MaxLifetime : 1.0f;
    
    /// <summary>
    /// Create a new GPU particle
    /// </summary>
    public static GpuParticle Create(
        Float3 position,
        Float3 velocity,
        Float4 color,
        float size,
        float lifetime,
        int sessionId,
        int textureIndex = 0,
        float pressure = 1.0f)
    {
        return new GpuParticle
        {
            Position = position,
            Velocity = velocity,
            Color = color,
            Size = size,
            Age = 0.0f,
            MaxLifetime = lifetime,
            TextureIndex = textureIndex,
            SessionId = sessionId,
            Rotation = 0.0f,
            AngularVelocity = 0.0f,
            Pressure = pressure,
            InFreelist = 0.0f
        };
    }
}

/// <summary>
/// GPU brush configuration - simplified for compute shader use
/// </summary>
public struct GpuBrushConfig
{
    /// <summary>
    /// Base size of the brush
    /// </summary>
    public float Size;
    
    /// <summary>
    /// Base color (R, G, B, A)
    /// </summary>
    public Float4 Color;
    
    /// <summary>
    /// Number of texture frames available
    /// </summary>
    public int FrameCount;
    
    /// <summary>
    /// Frame duration in seconds
    /// </summary>
    public float FrameDuration;
    
    /// <summary>
    /// Particle lifetime in seconds
    /// </summary>
    public float Lifetime;
    
    /// <summary>
    /// Emission rate (particles per second)
    /// </summary>
    public float EmissionRate;
    
    /// <summary>
    /// Pressure sensitivity (0.0 = no pressure, 1.0 = full pressure)
    /// </summary>
    public float PressureSensitivity;
    
    /// <summary>
    /// Reserved for future use
    /// </summary>
    public float Reserved;
} 