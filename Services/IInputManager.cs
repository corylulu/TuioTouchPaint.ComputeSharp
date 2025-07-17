using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Services;

/// <summary>
/// Interface for managing input handling from different sources
/// </summary>
public interface IInputManager
{
    /// <summary>
    /// Whether input handling is enabled
    /// </summary>
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// Event fired when an input stroke starts
    /// </summary>
    event EventHandler<InputStrokeEventArgs>? StrokeStarted;
    
    /// <summary>
    /// Event fired when an input stroke continues
    /// </summary>
    event EventHandler<InputStrokeEventArgs>? StrokeContinued;
    
    /// <summary>
    /// Event fired when an input stroke ends
    /// </summary>
    event EventHandler<InputStrokeEventArgs>? StrokeEnded;
    
    /// <summary>
    /// Event fired when a TUIO frame is finished (for batched rendering)
    /// </summary>
    event EventHandler? TuioFrameFinished;
    
    /// <summary>
    /// Handle mouse input down
    /// </summary>
    /// <param name="position">Mouse position</param>
    /// <param name="button">Mouse button pressed</param>
    void HandleMouseDown(Float2 position, InputButton button);
    
    /// <summary>
    /// Handle mouse input move
    /// </summary>
    /// <param name="position">Mouse position</param>
    /// <param name="button">Currently pressed button</param>
    void HandleMouseMove(Float2 position, InputButton button);
    
    /// <summary>
    /// Handle mouse input up
    /// </summary>
    /// <param name="position">Mouse position</param>
    /// <param name="button">Mouse button released</param>
    void HandleMouseUp(Float2 position, InputButton button);
    
    /// <summary>
    /// Handle touch input down
    /// </summary>
    /// <param name="position">Touch position</param>
    /// <param name="touchId">Touch identifier</param>
    void HandleTouchDown(Float2 position, int touchId);
    
    /// <summary>
    /// Handle touch input move
    /// </summary>
    /// <param name="position">Touch position</param>
    /// <param name="touchId">Touch identifier</param>
    void HandleTouchMove(Float2 position, int touchId);
    
    /// <summary>
    /// Handle touch input up
    /// </summary>
    /// <param name="position">Touch position</param>
    /// <param name="touchId">Touch identifier</param>
    void HandleTouchUp(Float2 position, int touchId);
    
    /// <summary>
    /// Handle TUIO cursor added
    /// </summary>
    /// <param name="cursor">TUIO cursor data</param>
    void HandleTuioCursorAdded(TuioCursor cursor);
    
    /// <summary>
    /// Handle TUIO cursor updated
    /// </summary>
    /// <param name="cursor">TUIO cursor data</param>
    void HandleTuioCursorUpdated(TuioCursor cursor);
    
    /// <summary>
    /// Handle TUIO cursor removed
    /// </summary>
    /// <param name="cursor">TUIO cursor data</param>
    void HandleTuioCursorRemoved(TuioCursor cursor);
    
    /// <summary>
    /// Handle TUIO frame finished
    /// </summary>
    void HandleTuioFrameFinished();
    
    /// <summary>
    /// Get active stroke count
    /// </summary>
    int GetActiveStrokeCount();
    
    /// <summary>
    /// Clear all active strokes
    /// </summary>
    void ClearActiveStrokes();
}

/// <summary>
/// Input button enumeration
/// </summary>
public enum InputButton
{
    None,
    Left,
    Right,
    Middle
}

/// <summary>
/// Event arguments for input stroke events
/// </summary>
public class InputStrokeEventArgs : EventArgs
{
    /// <summary>
    /// Position of the input
    /// </summary>
    public Float2 Position { get; }
    
    /// <summary>
    /// Session ID for this stroke
    /// </summary>
    public int SessionId { get; }
    
    /// <summary>
    /// Source of the input
    /// </summary>
    public TouchInputSource Source { get; }
    
    /// <summary>
    /// Touch identifier (for touch input)
    /// </summary>
    public int TouchId { get; }
    
    /// <summary>
    /// Button pressed (for mouse input)
    /// </summary>
    public InputButton Button { get; }
    
    /// <summary>
    /// Pressure value (if available)
    /// </summary>
    public float Pressure { get; }
    
    /// <summary>
    /// Whether this is an immediate render request
    /// </summary>
    public bool IsImmediateRender { get; }
    
    public InputStrokeEventArgs(
        Float2 position, 
        int sessionId, 
        TouchInputSource source, 
        int touchId = 0, 
        InputButton button = InputButton.None, 
        float pressure = 1.0f, 
        bool isImmediateRender = false)
    {
        Position = position;
        SessionId = sessionId;
        Source = source;
        TouchId = touchId;
        Button = button;
        Pressure = pressure;
        IsImmediateRender = isImmediateRender;
    }
} 