using ComputeSharp;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Threading.Tasks;
using System.Linq;

namespace TuioTouchPaint.ComputeSharp.Models;

/// <summary>
/// GPU texture atlas that packs up to 8 16×16 frames in a single 128×16 RGBA texture
/// and uploads it once to GPU as a ReadOnlyTexture2D<Float4>.
/// </summary>
public class GpuTextureAtlas : IDisposable
{
    private readonly ILogger<GpuTextureAtlas> _logger;
    private readonly GraphicsDevice _device;

    private const int TileSize   = 256;   // each sprite 128×128
    private const int Frames     = 8;     // 8 frames in a row
    private const int AtlasW     = TileSize * Frames; // 1024
    private const int AtlasH     = TileSize;          // 128

    private readonly Float4[] _pixelFloat = new Float4[AtlasW * AtlasH];
    private ReadOnlyTexture2D<Float4>? _atlasTexture;
    private bool _disposed;

    public GpuTextureAtlas(ILogger<GpuTextureAtlas> logger)
    {
        _logger = logger;
        _device = GraphicsDevice.GetDefault();
    }

    /// <summary>Load PNG/JPG images from a folder and build the atlas.</summary>
    public async Task InitialiseFromFolderAsync(string folder)
    {
        if (!Directory.Exists(folder))
        {
            _logger.LogWarning($"Atlas folder not found: {folder}");
            return;
        }

        var files = Directory.GetFiles(folder, "*.png").Concat(Directory.GetFiles(folder, "*.jpg"))
                              .OrderBy(f => f).Take(Frames).ToArray();
        if (files.Length == 0)
        {
            _logger.LogWarning($"No images found in {folder}");
            return;
        }

        await Task.Run(() => BuildAtlas(files));
        UploadToGpu();
    }

    private void BuildAtlas(string[] files)
    {
        using var atlasBmp = new SKBitmap(AtlasW, AtlasH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas   = new SKCanvas(atlasBmp);
        canvas.Clear(SKColors.Transparent);

        for (int i = 0; i < files.Length; i++)
        {
            using var src = SKBitmap.Decode(files[i]);
            if (src == null) { _logger.LogWarning($"Failed to load {files[i]}"); continue; }
            using var resized = src.Resize(new SKImageInfo(TileSize, TileSize), SKFilterQuality.High);
            if (resized == null) continue;
            canvas.DrawBitmap(resized, i * TileSize, 0);
        }

        // copy to float4 array in RGBA order 0..1
        var span = atlasBmp.GetPixelSpan(); // BGRA order on little-endian → convert
        for (int i = 0; i < _pixelFloat.Length; i++)
        {
            int bi = i * 4;
            byte b = span[bi];
            byte g = span[bi + 1];
            byte r = span[bi + 2];
            byte a = span[bi + 3];
            _pixelFloat[i] = new Float4(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        _logger.LogInformation($"Built GPU atlas {AtlasW}x{AtlasH} from {files.Length} frames");
    }

    private void UploadToGpu()
    {
        _atlasTexture?.Dispose();
        _atlasTexture = _device.AllocateReadOnlyTexture2D<Float4>(AtlasW, AtlasH);
        _atlasTexture.CopyFrom(_pixelFloat);
        _logger.LogInformation("Uploaded texture atlas to GPU");
    }

    public ReadOnlyTexture2D<Float4>? Texture => _atlasTexture;
    public int TileSizePx => TileSize;

    public GpuBrushConfig CreateBrushConfig(string brushId, float size, Float4 color)
    {
        return new GpuBrushConfig
        {
            Size = size,
            Color = color,
            FrameCount = Frames,
            FrameDuration = 0.1f,
            Lifetime = 8.0f,
            EmissionRate = Math.Max(200.0f, size * 50.0f),
            PressureSensitivity = 1.0f,
            Reserved = 0.0f
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _atlasTexture?.Dispose();
        _disposed = true;
    }
} 