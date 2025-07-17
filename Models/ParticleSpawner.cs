using ComputeSharp;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TuioTouchPaint.ComputeSharp.Models;

/// <summary>
/// Converts touch input to GPU particles for the ComputeSharp system
/// Handles particle creation, emission rates, and input-to-particle mapping
/// </summary>
public class ParticleSpawner
{
    private readonly ILogger<ParticleSpawner> _logger;
    private readonly GpuTextureAtlas _textureAtlas;
    private readonly Dictionary<int, SessionSpawner> _sessionSpawners = new();
    private readonly Random _random = new();

    public ParticleSpawner(ILogger<ParticleSpawner> logger, GpuTextureAtlas textureAtlas)
    {
        _logger = logger;
        _textureAtlas = textureAtlas;
    }

    /// <summary>
    /// Per-session spawner state
    /// </summary>
    private class SessionSpawner
    {
        public int SessionId { get; set; }
        public GpuBrushConfig BrushConfig { get; set; }
        public Float3 LastPosition { get; set; }
        public float TimeSinceLastEmission { get; set; }
        public bool IsActive { get; set; }
        public DateTime StartTime { get; set; }
        public float StrokeDistance { get; set; }
    }

    /// <summary>
    /// Start a new stroke for a session
    /// </summary>
    public void StartStroke(int sessionId, Float3 position, GpuBrushConfig brushConfig)
    {
        //_logger.LogInformation($"🎯 StartStroke called with position ({position.X:F1}, {position.Y:F1})");
        
        var spawner = new SessionSpawner
        {
            SessionId = sessionId,
            BrushConfig = brushConfig,
            LastPosition = position,
            TimeSinceLastEmission = 0.0f,
            IsActive = true,
            StartTime = DateTime.Now,
            StrokeDistance = 0.0f
        };

        _sessionSpawners[sessionId] = spawner;
        //_logger.LogInformation($"🎯 Created spawner with LastPosition ({spawner.LastPosition.X:F1}, {spawner.LastPosition.Y:F1})");
        //_logger.LogDebug($"Started stroke for session {sessionId} at position ({position.X:F1}, {position.Y:F1})");
    }

    /// <summary>
    /// Continue a stroke for a session
    /// </summary>
    public void ContinueStroke(int sessionId, Float3 position, float pressure = 1.0f)
    {
        if (!_sessionSpawners.TryGetValue(sessionId, out var spawner) || !spawner.IsActive)
        {
            _logger.LogWarning($"Attempted to continue stroke for inactive session {sessionId}");
            return;
        }

        // Calculate distance moved
        var distance = CalculateDistance(spawner.LastPosition, position);
        spawner.StrokeDistance += distance;
        spawner.LastPosition = position;

        _logger.LogDebug($"Continued stroke for session {sessionId}, distance: {distance:F1}, total: {spawner.StrokeDistance:F1}");
    }

    /// <summary>
    /// End a stroke for a session
    /// </summary>
    public void EndStroke(int sessionId)
    {
        if (_sessionSpawners.TryGetValue(sessionId, out var spawner))
        {
            spawner.IsActive = false;
            _logger.LogDebug($"Ended stroke for session {sessionId}, total distance: {spawner.StrokeDistance:F1}");
        }
    }

    /// <summary>
    /// Update all spawners and generate particles
    /// </summary>
    public void Update(float deltaTime, List<GpuParticle> particles)
    {
        foreach (var spawner in _sessionSpawners.Values)
        {
            if (spawner.IsActive)
            {
                UpdateSpawner(spawner, deltaTime, particles);
            }
        }
    }

    /// <summary>
    /// Update a single spawner and emit particles
    /// </summary>
    private void UpdateSpawner(SessionSpawner spawner, float deltaTime, List<GpuParticle> particles)
    {
        spawner.TimeSinceLastEmission += deltaTime;

        // Calculate emission interval based on brush config
        var emissionInterval = 1.0f / spawner.BrushConfig.EmissionRate;

        var particlesBefore = particles.Count;
        
        // Debug spawner state before emitting
        //_logger.LogInformation($"🎯 UpdateSpawner: Session {spawner.SessionId}, LastPosition=({spawner.LastPosition.X:F1}, {spawner.LastPosition.Y:F1})");
        
        // Emit particles if enough time has passed
        while (spawner.TimeSinceLastEmission >= emissionInterval)
        {
            EmitParticle(spawner, particles);
            spawner.TimeSinceLastEmission -= emissionInterval;
        }
        
        // var particlesAfter = particles.Count;
        // if (particlesAfter > particlesBefore)
        // {
        //     _logger.LogInformation($"✨ Spawner {spawner.SessionId} created {particlesAfter - particlesBefore} particles (total: {particlesAfter})");
        // }
    }

    /// <summary>
    /// Emit a single particle
    /// </summary>
    private void EmitParticle(SessionSpawner spawner, List<GpuParticle> particles)
    {
        // Calculate texture index based on stroke time or distance
        var elapsed = (float)(DateTime.Now - spawner.StartTime).TotalSeconds;
        var frameIndex = (int)(elapsed / spawner.BrushConfig.FrameDuration) % spawner.BrushConfig.FrameCount;

        // Add some randomness to position
        var randomOffset = new Float3(
            (float)(_random.NextDouble() - 0.5) * spawner.BrushConfig.Size * 0.2f,
            (float)(_random.NextDouble() - 0.5) * spawner.BrushConfig.Size * 0.2f,
            0.0f
        );

        // Test the addition step by step
        var baseX = spawner.LastPosition.X;
        var baseY = spawner.LastPosition.Y;
        var offsetX = randomOffset.X;
        var offsetY = randomOffset.Y;
        var finalX = baseX + offsetX;
        var finalY = baseY + offsetY;
        
        var position = new Float3(finalX, finalY, 0.0f);
        
        //_logger.LogInformation($"🎯 EmitParticle: base=({baseX:F1}, {baseY:F1}) + offset=({offsetX:F1}, {offsetY:F1}) = final=({finalX:F1}, {finalY:F1}) -> position=({position.X:F1}, {position.Y:F1})");

        // Create random velocity for particle movement
        var velocity = new Float3(
            (float)(_random.NextDouble() - 0.5) * 10.0f,
            (float)(_random.NextDouble() - 0.5) * 10.0f,
            0.0f
        );

        // Create particle
        var particle = GpuParticle.Create(
            position,
            velocity,
            spawner.BrushConfig.Color,
            spawner.BrushConfig.Size,
            spawner.BrushConfig.Lifetime,
            spawner.SessionId,
            frameIndex
        );

        particles.Add(particle);
        
        //_logger.LogInformation($"🎨 Created particle at ({position.X:F1}, {position.Y:F1}) size={spawner.BrushConfig.Size:F1} color=({spawner.BrushConfig.Color.X:F1}, {spawner.BrushConfig.Color.Y:F1}, {spawner.BrushConfig.Color.Z:F1}, {spawner.BrushConfig.Color.W:F1})");
    }

    /// <summary>
    /// Calculate distance between two 3D points
    /// </summary>
    private static float CalculateDistance(Float3 a, Float3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Get brush configuration for a session
    /// </summary>
    public GpuBrushConfig GetBrushConfig(int sessionId, string brushId, float size, Float4 color)
    {
        return _textureAtlas.CreateBrushConfig(brushId, size, color);
    }

    /// <summary>
    /// Get active session count
    /// </summary>
    public int GetActiveSessionCount()
    {
        return _sessionSpawners.Values.Count(s => s.IsActive);
    }

    /// <summary>
    /// Clear all inactive sessions
    /// </summary>
    public void ClearInactiveSessions()
    {
        var toRemove = _sessionSpawners.Where(kvp => !kvp.Value.IsActive).Select(kvp => kvp.Key).ToList();
        foreach (var sessionId in toRemove)
        {
            _sessionSpawners.Remove(sessionId);
        }
    }

    /// <summary>
    /// Get session statistics
    /// </summary>
    public (int TotalSessions, int ActiveSessions, float AverageStrokeDistance) GetStatistics()
    {
        var totalSessions = _sessionSpawners.Count;
        var activeSessions = _sessionSpawners.Values.Count(s => s.IsActive);
        var averageDistance = _sessionSpawners.Values.Any() ? 
            _sessionSpawners.Values.Average(s => s.StrokeDistance) : 0.0f;

        return (totalSessions, activeSessions, averageDistance);
    }
} 