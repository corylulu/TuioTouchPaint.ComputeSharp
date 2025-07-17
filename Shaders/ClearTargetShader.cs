using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Shaders
{
    /// <summary>
    /// Simple compute shader that fills a 2D texture with a constant colour.
    /// </summary>
    [ThreadGroupSize(16, 16, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ClearTargetShader : IComputeShader
    {
        private readonly ReadWriteTexture2D<Float4> target;
        private readonly Float4 clearColor;

        public ClearTargetShader(ReadWriteTexture2D<Float4> target, Float4 clearColor)
        {
            this.target = target;
            this.clearColor = clearColor;
        }

        public void Execute()
        {
            Int2 xy = new((int)ThreadIds.X, (int)ThreadIds.Y);
            target[xy] = clearColor;
        }
    }
} 