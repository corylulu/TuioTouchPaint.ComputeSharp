using ComputeSharp;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TuioTouchPaint.ComputeSharp.Models;
using TuioTouchPaint.ComputeSharp.Shaders;
using Bool = ComputeSharp.Bool;

namespace TuioTouchPaint.ComputeSharp.Services;

/// <summary>
/// GPU-first particle system with persistent buffers and compute shader optimization
/// Based on research: particles live entirely on GPU, no CPU-GPU transfers during rendering
/// </summary>
public class ComputeSharpParticleSystem : IDisposable
{
    private readonly ILogger<ComputeSharpParticleSystem> _logger;
    private readonly GraphicsDevice _device;
    
    // Static lock to prevent multiple simultaneous initializations
    private static readonly object _globalInitLock = new object();
    
    // GPU-only persistent buffers (never transferred to CPU)
    private ReadWriteBuffer<GpuParticle>? _particleBuffer;
    private ReadWriteBuffer<int>? _freelistBuffer;
    private ReadWriteBuffer<int>? _freelistCountBuffer;
    private ReadWriteBuffer<int>? _aliveCountBuffer; // Separate buffer for alive particle count
    private ReadWriteBuffer<EmitterData>? _emitterBuffer;
    
    // Z-buffer for depth testing (flicker-free blending)
    private ReadWriteTexture2D<float>? _depthBuffer;
    
    // System configuration
    private int _maxParticles = 100000; // 1M particles for A6000
    private float _canvasWidth = 1920;
    private float _canvasHeight = 1080;
    
    // Compute shader work group sizes (based on research)
    private const int SPAWN_WORK_GROUP_SIZE = 64;
    private const int UPDATE_WORK_GROUP_SIZE = 128;
    
    // Active emitters
    private readonly Dictionary<int, EmitterData> _activeEmitters = new();
    private int _nextEmitterId = 1;
    
    // Performance tracking
    private readonly Stopwatch _performanceTimer = new();
    private double _lastUpdateTime = 0;
    private double _averageUpdateTime = 0;
    private int _frameCount = 0;
    
    // System state
    private bool _isInitialized = false;
    private bool _isDisposed = false;
    
    // Unique spawn ID counter to prevent particle index reuse artifacts
    private static int _globalSpawnId = 0;
    
    // Static counter to track instances
    private static int _instanceCount = 0;
    private readonly int _instanceId;

    public ComputeSharpParticleSystem(ILogger<ComputeSharpParticleSystem> logger)
    {
        _logger = logger;
        _device = GraphicsDevice.GetDefault();
        
        _instanceId = System.Threading.Interlocked.Increment(ref _instanceCount);
        _logger.LogInformation($"🚀 GPU-First Particle System created (Instance #{_instanceId})");
    }

    /// <summary>
    /// Initialize the GPU-first particle system with persistent buffers
    /// </summary>
    public Task InitializeAsync()
    {
        if (_isInitialized) 
        {
            _logger.LogDebug("GPU-first particle system already initialized, skipping...");
            return Task.CompletedTask;
        }
        
        // Use global lock to prevent race conditions during initialization across all instances
        lock (_globalInitLock)
        {
            if (_isInitialized) return Task.CompletedTask; // Double-check after acquiring lock
            
            try
            {
                _logger.LogInformation("⚡ Initializing GPU-first particle system...");
                
                // Create persistent GPU buffers (never transferred to CPU)
                _particleBuffer = _device.AllocateReadWriteBuffer<GpuParticle>(_maxParticles);
                _freelistBuffer = _device.AllocateReadWriteBuffer<int>(_maxParticles);
                _freelistCountBuffer = _device.AllocateReadWriteBuffer<int>(1);
                _aliveCountBuffer = _device.AllocateReadWriteBuffer<int>(1); // Track alive particle count
                _emitterBuffer = _device.AllocateReadWriteBuffer<EmitterData>(32); // Max 32 emitters
                
                // Create depth buffer for Z-testing (flicker-free blending)
                _depthBuffer = _device.AllocateReadWriteTexture2D<float>((int)_canvasWidth, (int)_canvasHeight);
                
                // Initialize freelist with all particle indices
                InitializeFreelist();
                
                _isInitialized = true;
                _logger.LogInformation($"✅ GPU-first particle system initialized: {_maxParticles:N0} particles, {GetGpuMemoryUsage():F1} MB GPU memory");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize GPU-first particle system");
                throw;
            }
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initialize freelist with all particle indices (GPU operation)
    /// </summary>
    private void InitializeFreelist()
    {
        // Create initialization shader
        var initShader = new FreelistInitShader(_freelistBuffer!, _freelistCountBuffer!, _particleBuffer!, _maxParticles);
        
        // Dispatch initialization - each thread processes one particle index
        _device.For(_maxParticles, initShader);
        
        // Verify initialization
        var freelistCount = GetFreelistCount();
        _logger.LogInformation($"🔄 Initialized freelist with {_maxParticles:N0} indices, actual count: {freelistCount:N0}");
    }

    /// <summary>
    /// Start a new particle stroke (unified spawning system)
    /// </summary>
    public void StartStroke(int sessionId, float x, float y, string brushId = "default", float size = 4.0f, Float4? color = null)
    {
        if (!_isInitialized) return;
        
        try
        {
            // UNIFIED SPAWNING: Create InputEvent and use ProcessInputEvents
            var currentTime = (float)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0f;
            var inputEvent = new InputEvent
            {
                Position = new Float2(x, y),
                Velocity = new Float2(0.0f, 0.0f),
                Color = color ?? Float4.One,
                Size = size,
                Timestamp = currentTime,
                SessionId = sessionId,
                TextureIndex = 0,
                Rotation = 0.0f
            };
            
            // Spawn particles immediately using unified system
            ProcessInputEvents(new[] { inputEvent });
            
            _logger.LogDebug($"🎯 Started GPU stroke: session={sessionId}, pos=({x:F1},{y:F1}), size={size:F1}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to start stroke");
        }
    }

    /// <summary>
    /// Update stroke position (unified spawning system)
    /// </summary>
    public void UpdateStroke(int sessionId, float x, float y, float? pressure = null)
    {
        if (!_isInitialized) return;
        
        try
        {
            // UNIFIED SPAWNING: Create InputEvent and use ProcessInputEvents
            var currentTime = (float)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0f;
            var inputEvent = new InputEvent
            {
                Position = new Float2(x, y),
                Velocity = new Float2(0.0f, 0.0f),
                Color = Float4.One,
                Size = 4.0f * (pressure ?? 1.0f),
                Timestamp = currentTime,
                SessionId = sessionId,
                TextureIndex = 0,
                Rotation = 0.0f
            };
            
            // Spawn particles immediately using unified system
            ProcessInputEvents(new[] { inputEvent });
            
            //_logger.LogDebug($"🎯 Updated GPU stroke: session={sessionId}, pos=({x:F1},{y:F1})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update stroke");
        }
    }

    /// <summary>
    /// End stroke (unified spawning system)
    /// </summary>
    public void EndStroke(int sessionId)
    {
        // UNIFIED SPAWNING: No emitter cleanup needed
        // Particles are created instantly via ProcessInputEvents
        _logger.LogDebug($"🔚 Ended stroke: session={sessionId}");
    }

    /// <summary>
    /// Main update loop - GPU-only operations
    /// </summary>
    public void Update(float deltaTime)
    {
        if (!_isInitialized) 
        {
            _logger.LogWarning("🚨 Update called but system not initialized");
            return;
        }
        
        if (_isDisposed)
        {
            _logger.LogWarning("🚨 Update called but system is disposed");
            return;
        }
        
        _performanceTimer.Restart();
        
        try
        {
            // Step 1: Clear depth buffer for new frame
            ClearDepthBuffer();
            
            // Step 2: Spawn new particles (GPU)
            SpawnParticles(deltaTime);
            
            // Step 3: Update all particles (GPU)
            UpdateParticles(deltaTime);
            
            // Step 4: Cull dead particles (GPU)
            CullDeadParticles();
            
            // Performance tracking
            _lastUpdateTime = _performanceTimer.Elapsed.TotalMilliseconds;
            _averageUpdateTime = (_averageUpdateTime * _frameCount + _lastUpdateTime) / (_frameCount + 1);
            _frameCount++;
            
            // Debug FPS calculation
            if (_frameCount % 600 == 0)
            {
                var fps = _averageUpdateTime > 0 ? 1000.0 / _averageUpdateTime : 0;
                _logger.LogDebug($"🎯 FPS Tracking: _lastUpdateTime={_lastUpdateTime:F2}ms, _averageUpdateTime={_averageUpdateTime:F2}ms, calculated FPS={fps:F1}");
            }
            
            if (_frameCount % 60 == 0)
            {
                var stats = GetStatistics();
                var freelistCount = GetFreelistCount();
                //_logger.LogDebug($"🔥 GPU update: {_lastUpdateTime:F2}ms avg, {_activeEmitters.Count} emitters, Alive={stats.AliveParticles}, Free={freelistCount}");
            }
            
            // Debug particle updates every 10 frames
            if (_frameCount % 10 == 0)
            {
                var stats = GetStatistics();
                if (stats.AliveParticles > 0)
                {
                    //_logger.LogDebug($"📈 Frame {_frameCount}: {stats.AliveParticles} alive particles, {_lastUpdateTime:F2}ms update time");
                    
                    // Log particle aging info every 60 frames to track fadeout
                    if (_frameCount % 600 == 0)
                    {
                        _logger.LogDebug($"🕐 Particle aging: Alive={stats.AliveParticles}, fade starts at 60% lifetime (fade duration = 3.2s out of 8s total)");
                        
                        var activeParticles = GetActiveParticles();
                        if (activeParticles.Length > 0)
                        {
                            var sampleParticle = activeParticles[0];
                            var normalizedAge = sampleParticle.Age / sampleParticle.MaxLifetime;
                            _logger.LogDebug($"🎨 Sample particle: age={sampleParticle.Age:F2}s/{sampleParticle.MaxLifetime:F2}s ({normalizedAge:P}), alpha={sampleParticle.Color.W:F3}");
                        }
                    }
                }
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("device") && ex.Message.Contains("lost"))
        {
            _logger.LogError(ex, "🔌 GPU device lost - attempting recovery...");
            
            // Try to reinitialize the system
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100); // Wait a bit for driver recovery
                    await InitializeAsync();
                    _logger.LogInformation("✅ GPU device recovered successfully");
                }
                catch (Exception reinitEx)
                {
                    _logger.LogError(reinitEx, "❌ Failed to recover GPU device");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GPU update failed");
        }
    }

    /// <summary>
    /// Spawn particles on GPU
    /// </summary>
    private void SpawnParticles(float deltaTime)
    {
        // PAINT APP: Particles are only spawned from mouse/TUIO input events
        // No automatic emitter-based spawning to avoid unwanted particles
        return;
    }

    /// <summary>
    /// Update all particles on GPU
    /// </summary>
    private void UpdateParticles(float deltaTime)
    {
        var currentTime = (float)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0f;
        var updateShader = new ParticleUpdateShader(
            _particleBuffer!,
            deltaTime,
            currentTime, // Pass actual current time for texture animation
            _canvasWidth,
            _canvasHeight,
            0.0f, // No gravity for paint particles
            0.98f, // Default drag (not used for paint)
            0.6f // Start fading at 60% of lifetime (last 40% fades out = 3.2 seconds)
        );
        
        // Dispatch shader for all particles (ComputeSharp expects total thread count, not work groups)
        _device.For(_maxParticles, updateShader);
    }

    /// <summary>
    /// Cull dead particles and add to freelist (GPU)
    /// </summary>
    private void CullDeadParticles()
    {
        // Reset alive count buffer using GPU shader (cannot access from CPU)
        var resetShader = new BufferResetShader(_aliveCountBuffer!, 1);
        _device.For(1, resetShader);
        
        var cullShader = new ParticleCullShader(
            _particleBuffer!,
            _aliveCountBuffer!, // Separate buffer for alive particle count
            _freelistBuffer!,
            _freelistCountBuffer!,
            _maxParticles);
        
        // Dispatch shader for all particles (ComputeSharp expects total thread count, not work groups)
        _device.For(_maxParticles, cullShader);
    }

    /// <summary>
    /// Update emitter buffer on GPU
    /// </summary>
    private void UpdateEmitterBuffer()
    {
        var emitterArray = _activeEmitters.Values.ToArray();
        if (emitterArray.Length > 0)
        {
            // Copy emitter data to GPU buffer
            _emitterBuffer!.CopyFrom(emitterArray);
        }
    }

    /// <summary>
    /// Get particle buffer for rendering (GPU-only)
    /// </summary>
    public ReadWriteBuffer<GpuParticle>? GetParticleBuffer()
    {
        return _particleBuffer;
    }
    
    /// <summary>
    /// Get depth buffer for Z-testing (GPU-only)
    /// </summary>
    public ReadWriteTexture2D<float>? GetDepthBuffer()
    {
        return _depthBuffer;
    }

    /// <summary>
    /// Get estimated particle count (for rendering)
    /// </summary>
    public int GetEstimatedParticleCount()
    {
        return _maxParticles; // Render all, let GPU handle alive/dead
    }

    /// <summary>
    /// Clear all particles (GPU operation)
    /// </summary>
    public void ClearAllParticles()
    {
        if (!_isInitialized) return;
        
        try
        {
            // Clear all emitters
            _activeEmitters.Clear();
            UpdateEmitterBuffer();
            
            // Reset freelist
            InitializeFreelist();
            // Clear depth buffer
            ClearDepthBuffer();
            
            _logger.LogInformation("🧹 All particles cleared on GPU");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to clear particles");
        }
    }
    
    /// <summary>
    /// Clear the depth buffer to prepare for new frame
    /// </summary>
    public void ClearDepthBuffer()
    {
        if (!_isInitialized || _depthBuffer == null) return;
        
        try
        {
            // Use a clear shader to reset depth buffer to maximum depth
            var clearShader = new ClearDepthBufferShader(_depthBuffer);
            _device.For((int)_canvasWidth, (int)_canvasHeight, clearShader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to clear depth buffer");
        }
    }

    /// <summary>
    /// Set canvas size and recreate depth buffer
    /// </summary>
    public void SetCanvasSize(float width, float height)
    {
        _canvasWidth = width;
        _canvasHeight = height;
        
        // Recreate depth buffer with new size
        if (_isInitialized && _depthBuffer != null)
        {
            _depthBuffer.Dispose();
            _depthBuffer = _device.AllocateReadWriteTexture2D<float>((int)width, (int)height);
        }
        
        _logger.LogDebug($"📐 Canvas size updated: {width}x{height}");
    }

    /// <summary>
    /// Get GPU memory usage
    /// </summary>
    private float GetGpuMemoryUsage()
    {
        var particleSize = System.Runtime.InteropServices.Marshal.SizeOf<GpuParticle>();
        var totalBytes = _maxParticles * particleSize + // Particle buffer
                        _maxParticles * sizeof(int) + // Freelist buffer
                        sizeof(int) + // Freelist count buffer
                        32 * System.Runtime.InteropServices.Marshal.SizeOf<EmitterData>(); // Emitter buffer
        
        return totalBytes / (1024.0f * 1024.0f); // MB
    }

    /// <summary>
    /// Get performance statistics
    /// </summary>
    public (double AverageUpdateTime, int FrameCount, int ActiveEmitters) GetPerformanceStats()
    {
        return (_averageUpdateTime, _frameCount, _activeEmitters.Count);
    }

    /// <summary>
    /// Get system statistics (compatibility method)
    /// </summary>
    public (int TotalParticles, int AliveParticles, double UpdateTimeMs, double AvgUpdateTimeMs) GetStatistics()
    {
        if (!_isInitialized || _aliveCountBuffer == null)
            return (_maxParticles, 0, _lastUpdateTime, _averageUpdateTime);
        
        try
        {
            // Read alive count from GPU buffer
            var aliveCountArray = new int[1];
            _aliveCountBuffer.CopyTo(aliveCountArray);
            int aliveCount = aliveCountArray[0];
            
            return (_maxParticles, aliveCount, _lastUpdateTime, _averageUpdateTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to read alive particle count from GPU");
            return (_maxParticles, 0, _lastUpdateTime, _averageUpdateTime);
        }
    }

    /// <summary>
    /// Get GPU memory usage in MB (compatibility method)
    /// </summary>
    public float GetGpuMemoryUsageMB()
    {
        return GetGpuMemoryUsage();
    }
    
    /// <summary>
    /// Get current freelist count (available particle slots)
    /// </summary>
    public int GetFreelistCount()
    {
        if (!_isInitialized || _freelistCountBuffer == null)
            return 0;
        
        try
        {
            // Read freelist count from GPU buffer
            var freelistCountArray = new int[1];
            _freelistCountBuffer.CopyTo(freelistCountArray);
            return freelistCountArray[0];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to read freelist count from GPU");
            return 0;
        }
    }

    /// <summary>
    /// Get active particles (compatibility method) - copies from GPU to CPU for rendering
    /// </summary>
    public GpuParticle[] GetActiveParticles()
    {
        if (!_isInitialized || _particleBuffer == null)
            return Array.Empty<GpuParticle>();
        
        try
        {
            // Copy particles from GPU to CPU for rendering
            var allParticles = new GpuParticle[_maxParticles];
            _particleBuffer.CopyTo(allParticles);
            
            // Filter out dead particles (age >= maxLifetime or alpha <= 0)
            var aliveParticles = allParticles
                .Where(p => p.Age < p.MaxLifetime && p.Color.W > 0.01f)
                .ToArray();
            
            return aliveParticles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get active particles from GPU");
            return Array.Empty<GpuParticle>();
        }
    }

    /// <summary>
    /// Continue stroke (compatibility method)
    /// </summary>
    public void ContinueStroke(int sessionId, float x, float y, float pressure = 1.0f)
    {
        UpdateStroke(sessionId, x, y, pressure);
    }

    /// <summary>
    /// Process batched input events and spawn particles
    /// </summary>
    public void ProcessInputEvents(ReadOnlySpan<InputEvent> inputEvents)
    {
        if (inputEvents.Length == 0) return;
        
        try
        {
            // Get freelist count before spawning
            var freelistCountBefore = GetFreelistCount();
            var aliveCountBefore = GetStatistics().AliveParticles;
            
            //_logger.LogInformation($"🎯 Processing {inputEvents.Length} input events in batches (1 particle per event = {inputEvents.Length * 1} total particles)");
            //_logger.LogInformation($"📊 Before spawn: Alive={aliveCountBefore}, Freelist={freelistCountBefore}");
            
            // Process events in safe batches to prevent GPU timeout
            const int maxBatchSize = 512; // Safe batch size to prevent GPU hangs
            int eventsProcessed = 0;
            int totalEvents = inputEvents.Length;
            
            while (eventsProcessed < totalEvents)
            {
                int batchSize = Math.Min(maxBatchSize, totalEvents - eventsProcessed);
                var batchEvents = inputEvents.Slice(eventsProcessed, batchSize);
                
                // Create input buffer for this batch
                using var inputBuffer = GraphicsDevice.GetDefault().AllocateReadOnlyBuffer(batchEvents.ToArray());
                
                // Get unique spawn ID for this batch to prevent particle index reuse artifacts
                var uniqueSpawnId = (uint)System.Threading.Interlocked.Increment(ref _globalSpawnId);
                
                // Run particle spawn shader for this batch
                var spawnShader = new ParticleSpawnShader(
                    _particleBuffer!,
                    _freelistBuffer!,
                    _freelistCountBuffer!,
                    inputBuffer,
                    batchSize,
                    _maxParticles,
                    (float)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0f,
                    8.0f, // 8 seconds for paint particles
                    1,   // More particles per input event for continuous paint trail
                    uniqueSpawnId // Pass unique spawn ID to prevent flickering
                );
                
                // Dispatch this batch
                GraphicsDevice.GetDefault().For(batchSize, spawnShader);
                
                eventsProcessed += batchSize;
            }
            
            // Get freelist count after spawning
            var freelistCountAfter = GetFreelistCount();
            var aliveCountAfter = GetStatistics().AliveParticles;
            
            //_logger.LogInformation($"✅ Spawned particles from {inputEvents.Length} input events (processed in batches)");
            //_logger.LogInformation($"📊 After spawn: Alive={aliveCountAfter}, Freelist={freelistCountAfter}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing input events for particle spawning");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _particleBuffer?.Dispose();
        _freelistBuffer?.Dispose();
        _freelistCountBuffer?.Dispose();
        _aliveCountBuffer?.Dispose();
        _emitterBuffer?.Dispose();
        _depthBuffer?.Dispose();
        
        _isDisposed = true;
        _logger.LogInformation("🗑️ GPU-first particle system disposed");
    }
}

/// <summary>
/// Emitter data structure for GPU
/// </summary>
public struct EmitterData
{
    public int SessionId;
    public Float3 Position;
    public Float4 Color;
    public float Size;
    public float SpawnRate;
    public Bool IsActive;
    public float LastSpawnTime;
    public int TotalParticlesSpawned;
    public float Reserved1; // Padding for alignment
    public float Reserved2; // Padding for alignment
    public float Reserved3; // Padding for alignment
} 