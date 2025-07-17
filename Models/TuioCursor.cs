namespace TuioTouchPaint.ComputeSharp.Models;

/// <summary>
/// Represents a TUIO cursor (touch point) with position and motion data
/// </summary>
public class TuioCursor
{
    /// <summary>
    /// Unique session identifier for this cursor
    /// </summary>
    public int SessionId { get; set; }
    
    /// <summary>
    /// Normalized X position (0.0 - 1.0)
    /// </summary>
    public float X { get; set; }
    
    /// <summary>
    /// Normalized Y position (0.0 - 1.0)
    /// </summary>
    public float Y { get; set; }
    
    /// <summary>
    /// X velocity component
    /// </summary>
    public float VelocityX { get; set; }
    
    /// <summary>
    /// Y velocity component
    /// </summary>
    public float VelocityY { get; set; }
    
    /// <summary>
    /// Motion acceleration
    /// </summary>
    public float Acceleration { get; set; }
    
    /// <summary>
    /// Timestamp when this cursor data was received
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Convert normalized coordinates to screen coordinates
    /// </summary>
    public (float ScreenX, float ScreenY) ToScreenCoordinates(float screenWidth, float screenHeight)
    {
        return (X * screenWidth, Y * screenHeight);
    }
    
    /// <summary>
    /// Calculate distance from another cursor
    /// </summary>
    public float DistanceFrom(TuioCursor other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
    
    /// <summary>
    /// Calculate velocity magnitude
    /// </summary>
    public float VelocityMagnitude => (float)Math.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY);
    
    public override string ToString()
    {
        return $"TuioCursor [Session: {SessionId}, Pos: ({X:F3}, {Y:F3}), Vel: ({VelocityX:F3}, {VelocityY:F3})]";
    }
} 