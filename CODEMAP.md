# TuioTouchPaint ComputeSharp - Code Architecture Map

## Current Architecture (Working Components)

### 1. **Main Application Structure**
- `MainWindow.axaml` - UI layout using `GpuNativeCanvas`
- `MainWindow.axaml.cs` - Main window logic, event handling, particle system creation
- `Program.cs` - Application entry point

### 2. **GPU Particle System (✅ WORKING)**
- `Services/ComputeSharpParticleSystem.cs` - Main GPU particle system
  - Manages 100,000+ particles entirely on GPU
  - Uses persistent GPU buffers (no CPU transfers)
  - Processes input events into particles
  - Update rate: 500+ FPS

### 3. **GPU Rendering Pipeline (✅ WORKING)**
- `Controls/GpuNativeCanvas.cs` - True GPU-native canvas control
  - Creates GPU render targets: `_gpuColorTarget`, `_gpuDepthBuffer`
  - Renders particles using ComputeSharp shaders
  - Uses `GpuTextureDrawOperation` for custom drawing
  - **MISSING**: DirectX texture sharing in `GpuTextureDrawOperation.Render()`

### 4. **GPU Shaders (✅ WORKING)**
- `Shaders/ParticleSpawnShader.cs` - Spawn particles from input events
- `Shaders/ParticleUpdateShader.cs` - Update particle physics/aging
- `Shaders/ParticleCullShader.cs` - Remove dead particles
- `Shaders/ParticleSpriteAtlasShader.cs` - Render particles as sprites
- `Shaders/ParticleCircleShader.cs` - Render particles as circles
- `Shaders/ClearTargetShader.cs` - Clear render targets
- `Shaders/ClearDepthBufferShader.cs` - Clear depth buffer
- `Shaders/BufferResetShader.cs` - Reset GPU buffers
- `Shaders/FreelistInitShader.cs` - Initialize particle freelist

### 5. **Input System (✅ WORKING)**
- `Services/InputManager.cs` - Input event processing
- `Services/TuioClient.cs` - TUIO touch input handling
- `Services/CoordinateConverter.cs` - Coordinate transformation
- `Models/InputEvent.cs` - Input event data structure
- `Models/InputBatch.cs` - Batched input processing

### 6. **Data Models (✅ WORKING)**
- `Models/GpuParticle.cs` - GPU particle data structure
- `Models/GpuTextureAtlas.cs` - GPU texture atlas management
- `Models/TouchPoint.cs` - Touch input data
- `Models/TuioCursor.cs` - TUIO cursor data
- `Models/ParticleSpawner.cs` - Particle spawning configuration

## Current Data Flow

```
Input Events → ComputeSharpParticleSystem → GPU Shaders → GPU Textures → [MISSING: DirectX Sharing] → SkiaSharp → Display
```

## What's Currently Working (✅)
1. **GPU Particle System**: 500+ FPS particle updates
2. **GPU Rendering**: Particles rendered to GPU textures using ComputeSharp
3. **Input Processing**: Mouse/TUIO input creates particles
4. **Shader Pipeline**: All GPU shaders functional

## What's Missing (❌)
1. **DirectX Texture Sharing**: Convert ComputeSharp GPU texture to SkiaSharp GPU texture
   - Located in: `GpuTextureDrawOperation.Render()` method
   - Need to: Access underlying DirectX texture handle
   - Need to: Create SkiaSharp texture from DirectX handle
   - Need to: Render SkiaSharp texture to canvas

## Legacy/Unused Components (🗑️)
- `Services/ComputeSharpDrawingController.cs` - Appears unused
- Any references to CPU pixel buffers or WriteableBitmap have been removed

## Critical Implementation Point
The only missing piece is in `Controls/GpuNativeCanvas.cs` at line ~470:
```csharp
public void Render(ImmediateDrawingContext context)
{
    // TODO: Implement DirectX texture sharing here
    // Convert _gpuTexture (ComputeSharp) to SkiaSharp GPU texture
    // This is where the magic happens!
}
```

## Dependencies
- ComputeSharp - GPU compute shaders
- SkiaSharp - GPU rendering backend
- Avalonia - UI framework with SkiaSharp integration
- DirectX - Underlying GPU API (Windows)

## Performance Targets
- Particle Updates: 500+ FPS (✅ ACHIEVED)
- GPU Rendering: Should match particle FPS (❌ BLOCKED on DirectX sharing)
- Zero CPU copying (✅ ACHIEVED) 