using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Models;

/// <summary>
/// Represents a single touch point from TUIO or native touch input
/// </summary>
public class TouchPoint
{
    /// <summary>
    /// Unique identifier for this touch point within its session
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// Session ID (typically corresponds to TUIO port or touch device)
    /// </summary>
    public int SessionId { get; set; }
    
    /// <summary>
    /// Position in canvas coordinates
    /// </summary>
    public Float2 Position { get; set; }
    
    /// <summary>
    /// Normalized position (0.0 - 1.0) as received from TUIO
    /// </summary>
    public Float2 NormalizedPosition { get; set; }
    
    /// <summary>
    /// Pressure value (0.0 - 1.0), may not be available for all input types
    /// </summary>
    public float Pressure { get; set; } = 1.0f;
    
    /// <summary>
    /// Velocity of touch movement
    /// </summary>
    public Float2 Velocity { get; set; }
    
    /// <summary>
    /// Motion acceleration
    /// </summary>
    public float MotionAcceleration { get; set; }
    
    /// <summary>
    /// Timestamp when this touch point was created/updated
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Source of this touch input
    /// </summary>
    public TouchInputSource Source { get; set; }

    public TouchPoint()
    {
        Timestamp = DateTime.Now;
    }

    public TouchPoint(int id, int sessionId, Float2 position, Float2 normalizedPosition)
    {
        Id = id;
        SessionId = sessionId;
        Position = position;
        NormalizedPosition = normalizedPosition;
        Timestamp = DateTime.Now;
    }

    /// <summary>
    /// Creates a copy of this touch point with updated position and timestamp
    /// </summary>
    public TouchPoint WithUpdatedPosition(Float2 newPosition, Float2 newNormalizedPosition)
    {
        return new TouchPoint
        {
            Id = Id,
            SessionId = SessionId,
            Position = newPosition,
            NormalizedPosition = newNormalizedPosition,
            Pressure = Pressure,
            Velocity = Velocity,
            MotionAcceleration = MotionAcceleration,
            Timestamp = DateTime.Now,
            Source = Source
        };
    }

    public override string ToString()
    {
        return $"TouchPoint[Id={Id}, Session={SessionId}, Pos=({Position.X:F1},{Position.Y:F1}), Pressure={Pressure:F2}]";
    }
}

/// <summary>
/// Source of touch input
/// </summary>
public enum TouchInputSource
{
    Mouse,
    Touch,
    Stylus,
    Tuio
} 