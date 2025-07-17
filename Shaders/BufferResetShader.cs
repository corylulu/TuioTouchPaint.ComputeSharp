using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Shaders;

/// <summary>
/// Simple compute shader to reset integer buffers to zero
/// Used to reset alive count buffer before particle culling
/// </summary>
[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BufferResetShader : IComputeShader
{
    private readonly ReadWriteBuffer<int> buffer;
    private readonly int count;
    
    public BufferResetShader(ReadWriteBuffer<int> buffer, int count)
    {
        this.buffer = buffer;
        this.count = count;
    }
    
    public void Execute()
    {
        var index = ThreadIds.X;
        
        // Only process valid indices
        if (index >= count) return;
        
        // Reset to zero
        buffer[index] = 0;
    }
} 