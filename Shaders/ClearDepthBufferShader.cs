using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Shaders;

/// <summary>
/// Simple compute shader to clear the depth buffer to maximum depth
/// This prepares the depth buffer for Z-testing in the next frame
/// </summary>
[ThreadGroupSize(16, 16, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ClearDepthBufferShader : IComputeShader
{
    private readonly ReadWriteTexture2D<float> depthBuffer;
    
    public ClearDepthBufferShader(ReadWriteTexture2D<float> depthBuffer)
    {
        this.depthBuffer = depthBuffer;
    }
    
    public void Execute()
    {
        // Use 2D thread coordinates directly
        Int2 xy = new((int)ThreadIds.X, (int)ThreadIds.Y);
        
        // Clear to maximum depth (high number) so new particles appear on top
        // Since we're using age as Z, and particles start at age 0, we use a high value
        depthBuffer[xy] = 10000.0f;
    }
} 