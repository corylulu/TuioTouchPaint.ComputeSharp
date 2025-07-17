using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TuioTouchPaint.ComputeSharp.Services;
using TuioTouchPaint.ComputeSharp.Models;
using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Controls
{
    /// <summary>
    /// Custom Avalonia control for rendering GPU particles with DirectX 12 via ComputeSharp
    /// </summary>
    public class ComputeSharpCanvas : Control
    {
        private readonly ILogger<ComputeSharpCanvas> _logger;
        private ComputeSharpParticleSystem _particleSystem;
        private Timer? _updateTimer;
        private readonly object _bitmapLock = new();
        
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private DateTime _lastFrameTime = DateTime.Now;
        
        // Actual rendering FPS measurement
        private double _actualRenderFPS = 0.0;
        private int _renderFrameCount = 0;
        private DateTime _renderFpsStartTime = DateTime.Now;
        
        // Synchronization flag
        private bool _needsBitmapUpdate = false;
        
        // Input state
        private bool _isPointerPressed = false;
        
        // Test mode flag
        private bool _isInTestMode = false;
        
        // Rendering state
        private WriteableBitmap? _renderBitmap;
        private byte[]? _pixelBuffer;

        // GPU colour target that the compute shaders will draw into
        private ReadWriteTexture2D<Float4>? _gpuColorTarget;
        private ReadWriteTexture2D<float>? _gpuDepthBuffer; // GPU depth buffer for particle rendering
        private Float4[]? _gpuReadbackBuffer; // CPU staging for texture readback
        private int _canvasWidth = 1920;
        private int _canvasHeight = 1080;
        
        private GpuTextureAtlas? _atlas;
        private ReadOnlyTexture2D<Float4>? _atlasTexture;
        
        public ComputeSharpCanvas()
        {
            // Default constructor for XAML
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<ComputeSharpCanvas>();
            
            // DON'T create particle system here - it will be injected by MainWindow
            // This prevents duplicate ComputeSharp pipeline registrations
            _particleSystem = null!; // Will be set by SetParticleSystem method
            
            // Enable input events
            Focusable = true;
            
            _logger.LogInformation("ComputeSharpCanvas created (particle system will be injected)");
        }
        
        public ComputeSharpCanvas(ILogger<ComputeSharpCanvas> logger, ComputeSharpParticleSystem particleSystem)
        {
            _logger = logger;
            _particleSystem = particleSystem;
            
            // Create update timer for particle system updates
            _updateTimer = new Timer(UpdateParticles, null, 0, 8);
            
            // Enable input events
            Focusable = true;
            
            _logger.LogInformation("ComputeSharpCanvas created with DI");
        }
        
        /// <summary>
        /// Set the particle system after construction (for XAML constructor)
        /// </summary>
        public void SetParticleSystem(ComputeSharpParticleSystem particleSystem)
        {
            _particleSystem = particleSystem;
            
            // Create update timer now that we have the particle system
            _updateTimer = new Timer(UpdateParticles, null, 0, 8);
            
            _logger.LogInformation("ComputeSharpCanvas particle system injected and timer started");
        }
        
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            
            if (!_isInitialized)
            {
                InitializeAsync();
            }
        }
        
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            
            if (_isInitialized)
            {
                UpdateCanvasSize();
                InitializeRenderBitmap();
                _needsBitmapUpdate = true;
                _logger.LogInformation($"Canvas resized to: {_canvasWidth}x{_canvasHeight}");
            }
        }
        
        private async void InitializeAsync()
        {
            try
            {
                _logger.LogInformation("Initializing ComputeSharpCanvas...");
                
                // Initialize particle system
                await _particleSystem.InitializeAsync();

                // Load GPU texture atlas (first 8 frames from PaintBrush folder if available)
                if (_atlas == null)
                {
                    var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
                    _atlas = new GpuTextureAtlas(loggerFactory.CreateLogger<GpuTextureAtlas>());
                    var brushPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Brushes", "PaintBrush");
                    await _atlas.InitialiseFromFolderAsync(brushPath);
                    _atlasTexture = _atlas.Texture;
                }
                
                // Set canvas size
                UpdateCanvasSize();
                
                // Initialize render bitmap
                InitializeRenderBitmap();
                
                _isInitialized = true;
                _needsBitmapUpdate = true;
                _logger.LogInformation("ComputeSharpCanvas initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize ComputeSharpCanvas");
            }
        }
        
        /// <summary>
        /// Initialize the canvas with a specific particle system (for DI integration)
        /// </summary>
        public async Task InitializeWithParticleSystem(ComputeSharpParticleSystem particleSystem)
        {
            try
            {
                _logger.LogInformation("Initializing ComputeSharpCanvas with provided particle system...");
                
                // Dispose old particle system if it exists and is different
                if (_particleSystem != null && _particleSystem != particleSystem)
                {
                    _particleSystem.Dispose();
                }
                
                // Use the provided particle system
                _particleSystem = particleSystem;
                
                // Set canvas size and update particle system
                UpdateCanvasSize();
                
                // Initialize render bitmap
                InitializeRenderBitmap();
                
                _isInitialized = true;
                _needsBitmapUpdate = true;
                _logger.LogInformation("ComputeSharpCanvas initialized with provided particle system successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize ComputeSharpCanvas with provided particle system");
                throw;
            }
        }
        
        private void UpdateCanvasSize()
        {
            _canvasWidth = Math.Max(1, (int)Bounds.Width);
            _canvasHeight = Math.Max(1, (int)Bounds.Height);
            
            if (_canvasWidth == 0 || _canvasHeight == 0)
            {
                _canvasWidth = 1920;
                _canvasHeight = 1080;
            }
            
            // Update particle system canvas size to match actual canvas
            _particleSystem.SetCanvasSize(_canvasWidth, _canvasHeight);
            
            _logger.LogDebug($"Canvas size updated: {_canvasWidth}x{_canvasHeight}");
        }
        
        private void InitializeRenderBitmap()
        {
            lock (_bitmapLock)
            {
                // Dispose old bitmap
                _renderBitmap?.Dispose();
                
                // Create new render bitmap
                _renderBitmap = new WriteableBitmap(
                    new PixelSize(_canvasWidth, _canvasHeight),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);
                
                // Initialize pixel buffer
                _pixelBuffer = new byte[_canvasWidth * _canvasHeight * 4]; // BGRA
                
                // Allocate GPU colour target, depth buffer, and CPU readback buffer
                _gpuColorTarget?.Dispose();
                _gpuDepthBuffer?.Dispose();
                _gpuColorTarget = GraphicsDevice.GetDefault()
                    .AllocateReadWriteTexture2D<Float4>(_canvasWidth, _canvasHeight);
                _gpuDepthBuffer = GraphicsDevice.GetDefault()
                    .AllocateReadWriteTexture2D<float>(_canvasWidth, _canvasHeight);

                _gpuReadbackBuffer = new Float4[_canvasWidth * _canvasHeight];
                
                _logger.LogDebug($"Render bitmap initialized: {_canvasWidth}x{_canvasHeight}");
            }
        }
        
        private void UpdateParticles(object? state)
        {
            if (_isDisposed || !_isInitialized || _particleSystem == null)
                return;
            
            try
            {
                // Calculate delta time
                var currentTime = DateTime.Now;
                var deltaTime = (float)(currentTime - _lastFrameTime).TotalSeconds;
                _lastFrameTime = currentTime;
                
                // Update particle system
                _particleSystem.Update(deltaTime);
                
                // Mark that bitmap needs updating
                _needsBitmapUpdate = true;
                
                // Trigger UI update on main thread
                Dispatcher.UIThread.Post(() => {
                    if (!_isDisposed)
                        InvalidateVisual();
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateParticles");
            }
        }
        
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            
            // Measure actual rendering FPS
            UpdateActualRenderFPS();
            
            if (!_isInitialized)
            {
                // Draw loading state
                var loadingText = new FormattedText(
                    "Initializing GPU Particle System...",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    24,
                    Brushes.White);
                
                context.DrawText(loadingText, new Point(50, 50));
                return;
            }
            
            lock (_bitmapLock)
            {
                // Update bitmap if needed
                if (_needsBitmapUpdate && _renderBitmap != null && _pixelBuffer != null)
                {
                    UpdateBitmapFromParticles();
                    _needsBitmapUpdate = false;
                }
                
                // Draw the bitmap - this is fast!
                if (_renderBitmap != null)
                {
                    context.DrawImage(_renderBitmap, new Rect(0, 0, _canvasWidth, _canvasHeight));
                }
            }
            
            // Draw FPS counter
            DrawFPS(context);
        }
        
        private void UpdateBitmapFromParticles()
        {
            if (_pixelBuffer == null || _gpuColorTarget == null || _gpuDepthBuffer == null || _gpuReadbackBuffer == null)
                return;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // --- 1. GPU clear ---
            var dev = GraphicsDevice.GetDefault();
            var clearShader = new TuioTouchPaint.ComputeSharp.Shaders.ClearTargetShader(_gpuColorTarget, new Float4(0, 0, 0, 0));
            dev.For(_canvasWidth, _canvasHeight, clearShader);
            
            // Clear depth buffer
            var clearDepthShader = new TuioTouchPaint.ComputeSharp.Shaders.ClearDepthBufferShader(_gpuDepthBuffer);
            dev.For(_canvasWidth, _canvasHeight, clearDepthShader);

            var clearTime = stopwatch.ElapsedMilliseconds;
            
            // --- 2. GPU rasterise ---
            var particleBuffer = _particleSystem.GetParticleBuffer();
            if (particleBuffer != null)
            {
                // CRITICAL FIX: Only render alive particles, not all 1M particles
                var stats = _particleSystem.GetStatistics();
                int aliveParticles = stats.AliveParticles;
                
                // Only render if we have alive particles to avoid unnecessary GPU work
                if (aliveParticles > 0)
                {
                    // Still dispatch for all particles but let GPU skip dead ones
                    // This is more efficient than CPU-side filtering
                    int estimated = _particleSystem.GetEstimatedParticleCount();
                    
                    if (_atlasTexture != null)
                    {
                        var spriteShader = new TuioTouchPaint.ComputeSharp.Shaders.ParticleSpriteAtlasShader(
                            particleBuffer,
                            estimated,
                            _atlasTexture,
                            _gpuColorTarget,
                            _gpuDepthBuffer,
                            new Int2(_canvasWidth, _canvasHeight),
                            _atlas?.TileSizePx ?? 128);
                        dev.For(estimated, spriteShader);
                    }
                    else
                    {
                        var circleShader = new TuioTouchPaint.ComputeSharp.Shaders.ParticleCircleShader(
                            particleBuffer,
                            estimated,
                            _gpuColorTarget,
                            _gpuDepthBuffer,
                            new Int2(_canvasWidth, _canvasHeight));
                        dev.For(estimated, circleShader);
                    }
                }
            }

            var renderTime = stopwatch.ElapsedMilliseconds;
            
            // --- 3. Read back to CPU byte buffer ---
            _gpuColorTarget.CopyTo(_gpuReadbackBuffer);

            var readbackTime = stopwatch.ElapsedMilliseconds;
            
            for (int i = 0; i < _gpuReadbackBuffer.Length; i++)
            {
                var c = _gpuReadbackBuffer[i];
                int idx = i * 4;
                // Clamp and convert premultiplied RGBA to BGRA8
                _pixelBuffer[idx + 2] = (byte)Math.Clamp(c.X * 255f, 0f, 255f); // R
                _pixelBuffer[idx + 1] = (byte)Math.Clamp(c.Y * 255f, 0f, 255f); // G
                _pixelBuffer[idx + 0] = (byte)Math.Clamp(c.Z * 255f, 0f, 255f); // B
                _pixelBuffer[idx + 3] = (byte)Math.Clamp(c.W * 255f, 0f, 255f); // A
            }

            var convertTime = stopwatch.ElapsedMilliseconds;
            
            if (_renderBitmap != null)
            {
                try
                {
                    using var lockedBitmap = _renderBitmap.Lock();
                    System.Runtime.InteropServices.Marshal.Copy(_pixelBuffer, 0, lockedBitmap.Address, _pixelBuffer.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating bitmap from GPU particles");
                }
            }
            
            var totalTime = stopwatch.ElapsedMilliseconds;
            
            // Log performance breakdown every 60 frames
            if (_renderFrameCount % 60 == 0 && totalTime > 5) // Only log if significant time
            {
                _logger.LogDebug($"🎯 Render timing: Clear={clearTime}ms, " +
                                $"Render={renderTime - clearTime}ms, Readback={readbackTime - renderTime}ms, " +
                                $"Convert={convertTime - readbackTime}ms, Copy={totalTime - convertTime}ms, " +
                                $"Total={totalTime}ms | Resolution={_canvasWidth}x{_canvasHeight}");
            }
        }
        
        private void DrawParticleCircle(int centerX, int centerY, int radius, byte r, byte g, byte b, byte a)
        {
            // Convert source color to premultiplied alpha components once outside the loops
            float srcAlphaF = a / 255f;
            byte srcRPremul = (byte)(r * srcAlphaF);
            byte srcGPremul = (byte)(g * srcAlphaF);
            byte srcBPremul = (byte)(b * srcAlphaF);

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y <= radius * radius)
                    {
                        int pixelX = centerX + x;
                        int pixelY = centerY + y;

                        if (pixelX >= 0 && pixelX < _canvasWidth && pixelY >= 0 && pixelY < _canvasHeight)
                        {
                            int index = (pixelY * _canvasWidth + pixelX) * 4;
                            if (index < _pixelBuffer!.Length - 3)
                            {
                                // Destination (existing) premultiplied components
                                byte dstBPremul = _pixelBuffer[index];
                                byte dstGPremul = _pixelBuffer[index + 1];
                                byte dstRPremul = _pixelBuffer[index + 2];
                                byte dstA = _pixelBuffer[index + 3];

                                float dstAlphaF = dstA / 255f;

                                // Source-over blending in premultiplied alpha
                                float outAlphaF = srcAlphaF + dstAlphaF * (1f - srcAlphaF);
                                if (outAlphaF < 0.0001f) // Fully transparent, skip write
                                {
                                    continue;
                                }

                                float outRPremul = srcRPremul + dstRPremul * (1f - srcAlphaF);
                                float outGPremul = srcGPremul + dstGPremul * (1f - srcAlphaF);
                                float outBPremul = srcBPremul + dstBPremul * (1f - srcAlphaF);

                                // Write back blended premultiplied values
                                _pixelBuffer[index] = (byte)Math.Clamp(outBPremul, 0, 255);
                                _pixelBuffer[index + 1] = (byte)Math.Clamp(outGPremul, 0, 255);
                                _pixelBuffer[index + 2] = (byte)Math.Clamp(outRPremul, 0, 255);
                                _pixelBuffer[index + 3] = (byte)Math.Clamp(outAlphaF * 255f, 0, 255);
                            }
                        }
                    }
                }
            }
        }
        
        private void UpdateActualRenderFPS()
        {
            _renderFrameCount++;
            var elapsed = DateTime.Now - _renderFpsStartTime;
            if (elapsed.TotalSeconds >= 1.0)
            {
                _actualRenderFPS = _renderFrameCount / elapsed.TotalSeconds;
                _renderFrameCount = 0;
                _renderFpsStartTime = DateTime.Now;
            }
        }
        
        private void DrawFPS(DrawingContext context)
        {
            var stats = _particleSystem.GetStatistics();
            var particleUpdateFPS = stats.AvgUpdateTimeMs > 0 ? 1000.0 / stats.AvgUpdateTimeMs : 0;
            
            var formattedText = new FormattedText(
                $"Render FPS: {_actualRenderFPS:F1} | Update FPS: {particleUpdateFPS:F1}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                12,
                Brushes.Lime);
            
            context.DrawText(formattedText, new Point(10, 10));
            
            // Show particle stats
            var particleText = new FormattedText(
                $"Particles: {stats.AliveParticles:N0} | Resolution: {_canvasWidth}x{_canvasHeight}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                12,
                Brushes.Yellow);
            
            context.DrawText(particleText, new Point(10, 30));
        }
        

        
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            
            if (!_isInitialized)
            {
                _logger.LogWarning("Pointer pressed but canvas not initialized");
                return;
            }
            
            var position = e.GetPosition(this);
            _logger.LogInformation($"🖱️  POINTER PRESSED at: ({position.X:F1}, {position.Y:F1})");
            
            _isPointerPressed = true;
            
            // Start particle emission with much smaller size
            _particleSystem.StartStroke(0, (float)position.X, (float)position.Y, "default", 4.0f);
            _logger.LogInformation($"🎨 Started stroke at ({position.X:F1}, {position.Y:F1})");
            
            // Force immediate update to create particles
            _particleSystem.Update(0.016f);
            
            // Check if particles were created
            var stats = _particleSystem.GetStatistics();
            _logger.LogInformation($"📊 After stroke start: {stats.TotalParticles} total, {stats.AliveParticles} alive");
        }
        
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            
            if (!_isInitialized)
                return;
            
            var position = e.GetPosition(this);
            
            // Continue particle emission if pointer is pressed
            if (_isPointerPressed)
            {
                _particleSystem.ContinueStroke(0, (float)position.X, (float)position.Y);
                // Force immediate update to create particles
                _particleSystem.Update(0.016f);
            }
        }
        
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            
            if (!_isInitialized)
                return;
            
            var position = e.GetPosition(this);
            _logger.LogDebug($"Pointer released at: {position}");
            
            _isPointerPressed = false;
            
            // End particle emission
            _particleSystem.EndStroke(0);
        }
        
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Dispose();
        }
        
        #region Test Methods
        
        /// <summary>
        /// Test method: Draw pixels directly to canvas buffer
        /// </summary>
        public void TestRawPixelDrawing()
        {
            _logger.LogInformation("🔴 TEST: Drawing raw pixels directly to canvas buffer");
            
            if (_pixelBuffer == null)
            {
                _logger.LogError("❌ Pixel buffer is null");
                return;
            }
            
            try
            {
                // Enable test mode (prevents buffer clearing)
                _isInTestMode = true;
                
                // Clear buffer
                Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
                
                // Calculate centered coordinates based on actual canvas size
                int centerX = _canvasWidth / 2;
                int centerY = _canvasHeight / 2;
                int squareSize = Math.Min(_canvasWidth, _canvasHeight) / 10; // 10% of smallest dimension
                
                // Draw first test pattern (red square - left of center)
                int redStartX = centerX - squareSize - 20;
                int redStartY = centerY - squareSize / 2;
                for (int y = redStartY; y < redStartY + squareSize; y++)
                {
                    for (int x = redStartX; x < redStartX + squareSize; x++)
                    {
                        if (x >= 0 && x < _canvasWidth && y >= 0 && y < _canvasHeight)
                        {
                            var index = (y * _canvasWidth + x) * 4;
                            if (index < _pixelBuffer.Length - 3)
                            {
                                _pixelBuffer[index] = 0;     // B
                                _pixelBuffer[index + 1] = 0; // G
                                _pixelBuffer[index + 2] = 255; // R (Red square)
                                _pixelBuffer[index + 3] = 255; // A
                            }
                        }
                    }
                }
                
                // Draw second test pattern (cyan square - right of center)
                int cyanStartX = centerX + 20;
                int cyanStartY = centerY - squareSize / 2;
                for (int y = cyanStartY; y < cyanStartY + squareSize; y++)
                {
                    for (int x = cyanStartX; x < cyanStartX + squareSize; x++)
                    {
                        if (x >= 0 && x < _canvasWidth && y >= 0 && y < _canvasHeight)
                        {
                            var index = (y * _canvasWidth + x) * 4;
                            if (index < _pixelBuffer.Length - 3)
                            {
                                _pixelBuffer[index] = 255;   // B
                                _pixelBuffer[index + 1] = 255; // G
                                _pixelBuffer[index + 2] = 0;   // R (Cyan square)
                                _pixelBuffer[index + 3] = 255; // A
                            }
                        }
                    }
                }
                
                // Update bitmap and force redraw
                if (_renderBitmap != null && _pixelBuffer != null)
                {
                    using var lockedBitmap = _renderBitmap.Lock();
                    System.Runtime.InteropServices.Marshal.Copy(_pixelBuffer, 0, lockedBitmap.Address, _pixelBuffer.Length);
                }
                InvalidateVisual();
                
                // Disable test mode after 5 seconds
                Task.Delay(5000).ContinueWith(_ => {
                    _isInTestMode = false;
                    _logger.LogInformation("🔴 Test mode disabled");
                });
                
                _logger.LogInformation($"✅ Raw pixel drawing test completed - should see red and cyan squares centered on {_canvasWidth}x{_canvasHeight} canvas");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Raw pixel drawing test failed");
            }
        }
        
        /// <summary>
        /// Test method: Force canvas redraw
        /// </summary>
        public void TestForceRedraw()
        {
            _logger.LogInformation("🟤 TEST: Force canvas redraw");
            
            try
            {
                // Force immediate rendering
                UpdateBitmapFromParticles();
                if (_renderBitmap != null && _pixelBuffer != null)
                {
                    using var lockedBitmap = _renderBitmap.Lock();
                    System.Runtime.InteropServices.Marshal.Copy(_pixelBuffer, 0, lockedBitmap.Address, _pixelBuffer.Length);
                }
                InvalidateVisual();
                
                _logger.LogInformation("✅ Force redraw test completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Force redraw test failed");
            }
        }
        
        /// <summary>
        /// Test method: Test different background colors
        /// </summary>
        public void TestBackgroundColors()
        {
            _logger.LogInformation("🟢 TEST: Testing background colors");
            
            if (_pixelBuffer == null)
            {
                _logger.LogError("❌ Pixel buffer is null");
                return;
            }
            
            try
            {
                // Enable test mode (prevents buffer clearing)
                _isInTestMode = true;
                
                // Test with different background colors
                var colors = new (byte r, byte g, byte b, string name)[]
                {
                    (255, 0, 0, "Red"),
                    (0, 255, 0, "Green"),
                    (0, 0, 255, "Blue"),
                    (255, 255, 0, "Yellow"),
                    (255, 0, 255, "Magenta"),
                    (0, 255, 255, "Cyan"),
                    (128, 128, 128, "Gray"),
                    (0, 0, 0, "Black"),
                };
                
                var currentColorIndex = 0;
                var color = colors[currentColorIndex];
                
                // Fill buffer with test background color
                for (int i = 0; i < _pixelBuffer.Length; i += 4)
                {
                    _pixelBuffer[i] = color.b;     // B
                    _pixelBuffer[i + 1] = color.g; // G
                    _pixelBuffer[i + 2] = color.r; // R
                    _pixelBuffer[i + 3] = 255;     // A
                }
                
                // Update bitmap and force redraw
                if (_renderBitmap != null && _pixelBuffer != null)
                {
                    using var lockedBitmap = _renderBitmap.Lock();
                    System.Runtime.InteropServices.Marshal.Copy(_pixelBuffer, 0, lockedBitmap.Address, _pixelBuffer.Length);
                }
                InvalidateVisual();
                
                // Disable test mode after 5 seconds
                Task.Delay(5000).ContinueWith(_ => {
                    _isInTestMode = false;
                    _logger.LogInformation("🔴 Test mode disabled");
                });
                
                _logger.LogInformation($"✅ Background color test completed - should see {color.name} background on {_canvasWidth}x{_canvasHeight} canvas");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Background color test failed");
            }
        }
        
        #endregion
        
        public void Dispose()
        {
            if (_isDisposed)
                return;
            
            _isDisposed = true;
            
            _updateTimer?.Dispose();
            _particleSystem?.Dispose();
            _renderBitmap?.Dispose();
            _gpuColorTarget?.Dispose();
            _gpuDepthBuffer?.Dispose();
            _atlas?.Dispose();
            
            _logger.LogInformation("ComputeSharpCanvas disposed");
        }
    }
} 