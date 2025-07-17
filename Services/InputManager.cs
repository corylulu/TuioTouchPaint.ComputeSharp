using Microsoft.Extensions.Logging;
using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Services;

/// <summary>
/// Implementation of input manager that handles input from different sources
/// </summary>
public class InputManager : IInputManager
{
    private readonly ILogger<InputManager> _logger;
    private readonly ICoordinateConverter _coordinateConverter;
    private readonly object _lock = new();
    
    private bool _isEnabled = true;
    private readonly Dictionary<int, bool> _activeStrokes = new(); // SessionId -> IsActive
    private bool _isMouseDown = false;
    private InputButton _currentMouseButton = InputButton.None;
    
    // Reduce logging frequency for performance
    private int _logCounter = 0;
    private readonly int _logInterval = 50; // Log every 50th event
    
    public bool IsEnabled 
    { 
        get => _isEnabled; 
        set => _isEnabled = value; 
    }
    
    public event EventHandler<InputStrokeEventArgs>? StrokeStarted;
    public event EventHandler<InputStrokeEventArgs>? StrokeContinued;
    public event EventHandler<InputStrokeEventArgs>? StrokeEnded;
    public event EventHandler? TuioFrameFinished;
    
    public InputManager(ILogger<InputManager> logger, ICoordinateConverter coordinateConverter)
    {
        _logger = logger;
        _coordinateConverter = coordinateConverter;
    }
    
    public void HandleMouseDown(Float2 position, InputButton button)
    {
        if (!_isEnabled) return;
        
        _logger.LogDebug($"Mouse down at: {position}, button: {button}");
        
        if (button == InputButton.Left)
        {
            _isMouseDown = true;
            _currentMouseButton = button;
            
            var eventArgs = new InputStrokeEventArgs(
                position: position,
                sessionId: 0, // Use sessionId 0 for mouse
                source: TouchInputSource.Mouse,
                button: button,
                isImmediateRender: true
            );
            
            lock (_lock)
            {
                _activeStrokes[0] = true;
            }
            
            StrokeStarted?.Invoke(this, eventArgs);
        }
    }
    
    public void HandleMouseMove(Float2 position, InputButton button)
    {
        if (!_isEnabled) return;
        
        if (_isMouseDown && button == InputButton.Left)
        {
            var eventArgs = new InputStrokeEventArgs(
                position: position,
                sessionId: 0, // Use sessionId 0 for mouse
                source: TouchInputSource.Mouse,
                button: button,
                isImmediateRender: true
            );
            
            StrokeContinued?.Invoke(this, eventArgs);
        }
    }
    
    public void HandleMouseUp(Float2 position, InputButton button)
    {
        if (!_isEnabled) return;
        
        if (_isMouseDown && button == InputButton.Left)
        {
            _isMouseDown = false;
            _currentMouseButton = InputButton.None;
            
            var eventArgs = new InputStrokeEventArgs(
                position: position,
                sessionId: 0, // Use sessionId 0 for mouse
                source: TouchInputSource.Mouse,
                button: button,
                isImmediateRender: true
            );
            
            lock (_lock)
            {
                _activeStrokes.Remove(0);
            }
            
            StrokeEnded?.Invoke(this, eventArgs);
        }
    }
    
    public void HandleTouchDown(Float2 position, int touchId)
    {
        if (!_isEnabled) return;
        
        _logger.LogDebug($"Touch down at: {position}, touchId: {touchId}");
        
        var eventArgs = new InputStrokeEventArgs(
            position: position,
            sessionId: touchId,
            source: TouchInputSource.Touch,
            touchId: touchId,
            isImmediateRender: true
        );
        
        lock (_lock)
        {
            _activeStrokes[touchId] = true;
        }
        
        StrokeStarted?.Invoke(this, eventArgs);
    }
    
    public void HandleTouchMove(Float2 position, int touchId)
    {
        if (!_isEnabled) return;
        
        lock (_lock)
        {
            if (!_activeStrokes.ContainsKey(touchId))
            {
                _logger.LogWarning($"Touch move for inactive touch {touchId}");
                return;
            }
        }
        
        var eventArgs = new InputStrokeEventArgs(
            position: position,
            sessionId: touchId,
            source: TouchInputSource.Touch,
            touchId: touchId,
            isImmediateRender: true
        );
        
        StrokeContinued?.Invoke(this, eventArgs);
    }
    
    public void HandleTouchUp(Float2 position, int touchId)
    {
        if (!_isEnabled) return;
        
        var eventArgs = new InputStrokeEventArgs(
            position: position,
            sessionId: touchId,
            source: TouchInputSource.Touch,
            touchId: touchId,
            isImmediateRender: true
        );
        
        lock (_lock)
        {
            _activeStrokes.Remove(touchId);
        }
        
        StrokeEnded?.Invoke(this, eventArgs);
    }
    
    public void HandleTuioCursorAdded(TuioCursor cursor)
    {
        if (!_isEnabled) return;
        
        // Process TUIO events asynchronously to avoid blocking
        Task.Run(() =>
        {
            var canvasPos = _coordinateConverter.ConvertTuioToCanvas(cursor.X, cursor.Y);
            
            var eventArgs = new InputStrokeEventArgs(
                position: canvasPos,
                sessionId: cursor.SessionId,
                source: TouchInputSource.Tuio,
                pressure: 1.0f,
                isImmediateRender: false // TUIO uses batched rendering
            );
            
            lock (_lock)
            {
                _activeStrokes[cursor.SessionId] = true;
            }
            
            StrokeStarted?.Invoke(this, eventArgs);
        });
    }
    
    public void HandleTuioCursorUpdated(TuioCursor cursor)
    {
        if (!_isEnabled) return;
        
        // Process TUIO events asynchronously to avoid blocking
        Task.Run(() =>
        {
            // Reduce logging frequency for performance
            _logCounter++;
            if (_logCounter % _logInterval == 0)
            {
                _logger.LogDebug($"TUIO cursor updated: {cursor} (logged every {_logInterval} events)");
            }
            
            var canvasPos = _coordinateConverter.ConvertTuioToCanvas(cursor.X, cursor.Y);
            
            var eventArgs = new InputStrokeEventArgs(
                position: canvasPos,
                sessionId: cursor.SessionId,
                source: TouchInputSource.Tuio,
                pressure: 1.0f,
                isImmediateRender: false // TUIO uses batched rendering
            );
            
            lock (_lock)
            {
                if (!_activeStrokes.ContainsKey(cursor.SessionId))
                {
                    _logger.LogWarning($"TUIO cursor update for inactive session {cursor.SessionId}");
                    return;
                }
            }
            
            StrokeContinued?.Invoke(this, eventArgs);
        });
    }
    
    public void HandleTuioCursorRemoved(TuioCursor cursor)
    {
        if (!_isEnabled) return;
        
        // Process TUIO events asynchronously to avoid blocking
        Task.Run(() =>
        {
            var canvasPos = _coordinateConverter.ConvertTuioToCanvas(cursor.X, cursor.Y);
            
            var eventArgs = new InputStrokeEventArgs(
                position: canvasPos,
                sessionId: cursor.SessionId,
                source: TouchInputSource.Tuio,
                pressure: 1.0f,
                isImmediateRender: false // TUIO uses batched rendering
            );
            
            lock (_lock)
            {
                _activeStrokes.Remove(cursor.SessionId);
            }
            
            StrokeEnded?.Invoke(this, eventArgs);
        });
    }
    
    public void HandleTuioFrameFinished()
    {
        if (!_isEnabled) return;
        
        // Fire frame finished event for batched rendering
        TuioFrameFinished?.Invoke(this, EventArgs.Empty);
    }
    
    public int GetActiveStrokeCount()
    {
        lock (_lock)
        {
            return _activeStrokes.Count;
        }
    }
    
    public void ClearActiveStrokes()
    {
        lock (_lock)
        {
            _activeStrokes.Clear();
        }
        
        _isMouseDown = false;
        _currentMouseButton = InputButton.None;
        
        _logger.LogInformation("Cleared all active strokes");
    }
} 