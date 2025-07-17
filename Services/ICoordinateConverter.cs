using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Services;

/// <summary>
/// Interface for converting coordinates between TUIO and canvas coordinate systems
/// </summary>
public interface ICoordinateConverter
{
    /// <summary>
    /// Current canvas size for coordinate calculations
    /// </summary>
    (int Width, int Height) CanvasSize { get; set; }
    
    /// <summary>
    /// TUIO coordinate ranges
    /// </summary>
    (float XMin, float XMax, float YMin, float YMax) TuioRanges { get; set; }
    
    /// <summary>
    /// Convert TUIO coordinates to canvas coordinates
    /// </summary>
    /// <param name="tuioX">TUIO X coordinate (normally 0.0 - 1.0)</param>
    /// <param name="tuioY">TUIO Y coordinate (normally 0.0 - 1.0)</param>
    /// <returns>Canvas coordinates</returns>
    Float2 ConvertTuioToCanvas(float tuioX, float tuioY);
    
    /// <summary>
    /// Convert canvas coordinates to TUIO coordinates
    /// </summary>
    /// <param name="canvasX">Canvas X coordinate</param>
    /// <param name="canvasY">Canvas Y coordinate</param>
    /// <returns>TUIO coordinates</returns>
    Float2 ConvertCanvasToTuio(float canvasX, float canvasY);
    
    /// <summary>
    /// Set TUIO coordinate ranges
    /// </summary>
    /// <param name="xMin">Minimum X value</param>
    /// <param name="xMax">Maximum X value</param>
    /// <param name="yMin">Minimum Y value</param>
    /// <param name="yMax">Maximum Y value</param>
    void SetTuioRanges(float xMin, float xMax, float yMin, float yMax);
    
    /// <summary>
    /// Validate that TUIO coordinate is within configured ranges
    /// </summary>
    /// <param name="tuioX">TUIO X coordinate</param>
    /// <param name="tuioY">TUIO Y coordinate</param>
    /// <returns>True if coordinates are valid</returns>
    bool ValidateTuioCoordinates(float tuioX, float tuioY);
} 