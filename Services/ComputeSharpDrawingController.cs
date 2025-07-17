using Microsoft.Extensions.Logging;
using ComputeSharp;
using TuioTouchPaint.ComputeSharp.Models;
using System;
using System.Collections.Generic;

namespace TuioTouchPaint.ComputeSharp.Services;

/// <summary>
/// Drawing controller that bridges input events to GPU particle system
/// Handles stroke management and translates input to GPU particles
/// </summary>
public class ComputeSharpDrawingController : IDisposable
{
    private readonly ILogger<ComputeSharpDrawingController> _logger;
    private readonly ComputeSharpParticleSystem _particleSystem;
    private readonly ICoordinateConverter _coordinateConverter;
    private readonly IInputManager _inputManager;
    
    // Active strokes tracking
    private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
    private readonly object _strokeLock = new();
    
    // Default brush configuration
    private readonly BrushConfiguration _defaultBrushConfig = new()
    {
                    Size = 4.0f,
        Color = Float4.One, // White
        BrushName = "default"
    };
    
    // Stroke configuration per session
    private readonly Dictionary<int, BrushConfiguration> _sessionBrushConfigs = new();
    
    private bool _isDisposed = false;
    
    public ComputeSharpDrawingController(
        ILogger<ComputeSharpDrawingController> logger,
        ComputeSharpParticleSystem particleSystem,
        ICoordinateConverter coordinateConverter,
        IInputManager inputManager)
    {
        _logger = logger;
        _particleSystem = particleSystem;
        _coordinateConverter = coordinateConverter;
        _inputManager = inputManager;
        
        // Subscribe to input events
        _inputManager.StrokeStarted += OnStrokeStarted;
        _inputManager.StrokeContinued += OnStrokeContinued;
        _inputManager.StrokeEnded += OnStrokeEnded;
        
        _logger.LogInformation("ComputeSharpDrawingController initialized");
    }
    
    /// <summary>
    /// Set brush configuration for a specific session
    /// </summary>
    public void SetBrushConfiguration(int sessionId, BrushConfiguration brushConfig)
    {
        lock (_strokeLock)
        {
            _sessionBrushConfigs[sessionId] = brushConfig;
        }
        
        _logger.LogDebug($"Brush configuration set for session {sessionId}: {brushConfig.BrushName}, Size={brushConfig.Size}");
    }
    
    /// <summary>
    /// Get brush configuration for a session (or default)
    /// </summary>
    public BrushConfiguration GetBrushConfiguration(int sessionId)
    {
        lock (_strokeLock)
        {
            return _sessionBrushConfigs.GetValueOrDefault(sessionId, _defaultBrushConfig);
        }
    }
    
    /// <summary>
    /// Clear all active strokes
    /// </summary>
    public void ClearAllStrokes()
    {
        lock (_strokeLock)
        {
            foreach (var stroke in _activeStrokes.Values)
            {
                _particleSystem.EndStroke(stroke.SessionId);
            }
            
            _activeStrokes.Clear();
        }
        
        _logger.LogInformation("All strokes cleared");
    }
    
    /// <summary>
    /// Get statistics about active strokes
    /// </summary>
    public (int ActiveStrokes, int TotalPoints) GetStrokeStatistics()
    {
        lock (_strokeLock)
        {
            var totalPoints = 0;
            foreach (var stroke in _activeStrokes.Values)
            {
                totalPoints += stroke.Points.Count;
            }
            
            return (_activeStrokes.Count, totalPoints);
        }
    }
    
    private void OnStrokeStarted(object? sender, InputStrokeEventArgs e)
    {
        if (_isDisposed) return;
        
        try
        {
            var brushConfig = GetBrushConfiguration(e.SessionId);
            
            lock (_strokeLock)
            {
                // Create new active stroke
                var stroke = new ActiveStroke
                {
                    SessionId = e.SessionId,
                    StartTime = DateTime.Now,
                    LastUpdateTime = DateTime.Now,
                    BrushConfig = brushConfig,
                    Source = e.Source
                };
                
                // Add first point
                stroke.Points.Add(e.Position);
                
                // Store active stroke
                _activeStrokes[e.SessionId] = stroke;
            }
            
            // Start GPU particle emission
            _particleSystem.StartStroke(
                e.SessionId,
                e.Position.X,
                e.Position.Y,
                brushConfig.BrushName,
                brushConfig.Size,
                brushConfig.Color);
            
            _logger.LogDebug($"Stroke started - Session: {e.SessionId}, Position: ({e.Position.X:F1}, {e.Position.Y:F1}), Source: {e.Source}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error starting stroke for session {e.SessionId}");
        }
    }
    
    private void OnStrokeContinued(object? sender, InputStrokeEventArgs e)
    {
        if (_isDisposed) return;
        
        try
        {
            lock (_strokeLock)
            {
                if (_activeStrokes.TryGetValue(e.SessionId, out var stroke))
                {
                    // Update stroke
                    stroke.Points.Add(e.Position);
                    stroke.LastUpdateTime = DateTime.Now;
                    
                    // Calculate velocity if we have previous points
                    if (stroke.Points.Count >= 2)
                    {
                        var prevPoint = stroke.Points[stroke.Points.Count - 2];
                        var deltaTime = (DateTime.Now - stroke.LastUpdateTime).TotalSeconds;
                        
                        if (deltaTime > 0)
                        {
                            var dx = e.Position.X - prevPoint.X;
                            var dy = e.Position.Y - prevPoint.Y;
                            var velocity = Math.Sqrt(dx * dx + dy * dy) / deltaTime;
                            stroke.CurrentVelocity = (float)velocity;
                        }
                    }
                }
                else
                {
                    _logger.LogWarning($"Stroke continued for unknown session {e.SessionId}");
                    return;
                }
            }
            
            // Continue GPU particle emission
                            _particleSystem.UpdateStroke(e.SessionId, e.Position.X, e.Position.Y);
            
            _logger.LogTrace($"Stroke continued - Session: {e.SessionId}, Position: ({e.Position.X:F1}, {e.Position.Y:F1})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error continuing stroke for session {e.SessionId}");
        }
    }
    
    private void OnStrokeEnded(object? sender, InputStrokeEventArgs e)
    {
        if (_isDisposed) return;
        
        try
        {
            ActiveStroke? completedStroke = null;
            
            lock (_strokeLock)
            {
                if (_activeStrokes.TryGetValue(e.SessionId, out completedStroke))
                {
                    // Add final point
                    completedStroke.Points.Add(e.Position);
                    completedStroke.EndTime = DateTime.Now;
                    
                    // Remove from active strokes
                    _activeStrokes.Remove(e.SessionId);
                }
                else
                {
                    _logger.LogWarning($"Stroke ended for unknown session {e.SessionId}");
                }
            }
            
            // End GPU particle emission
            _particleSystem.EndStroke(e.SessionId);
            
            if (completedStroke != null)
            {
                var duration = (completedStroke.EndTime - completedStroke.StartTime).TotalSeconds;
                _logger.LogDebug($"Stroke ended - Session: {e.SessionId}, Duration: {duration:F2}s, Points: {completedStroke.Points.Count}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error ending stroke for session {e.SessionId}");
        }
    }
    
    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        // Unsubscribe from input events
        _inputManager.StrokeStarted -= OnStrokeStarted;
        _inputManager.StrokeContinued -= OnStrokeContinued;
        _inputManager.StrokeEnded -= OnStrokeEnded;
        
        // Clear all active strokes
        ClearAllStrokes();
        
        _logger.LogInformation("ComputeSharpDrawingController disposed");
    }
}

/// <summary>
/// Represents an active stroke being drawn
/// </summary>
public class ActiveStroke
{
    public int SessionId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public DateTime EndTime { get; set; }
    public BrushConfiguration BrushConfig { get; set; } = new();
    public TouchInputSource Source { get; set; }
    public List<Float2> Points { get; set; } = new();
    public float CurrentVelocity { get; set; }
}

/// <summary>
/// Brush configuration for drawing
/// </summary>
public class BrushConfiguration
{
    public string BrushName { get; set; } = "default";
    public float Size { get; set; } = 4.0f;
    public Float4 Color { get; set; } = Float4.One;
    public float Opacity { get; set; } = 1.0f;
    public float MinSize { get; set; } = 1.0f;
    public float MaxSize { get; set; } = 8.0f;
    public bool VelocityBasedSize { get; set; } = true;
    public float FadeTime { get; set; } = 5.0f;
} 