using Microsoft.Extensions.Logging;
using ComputeSharp;

namespace TuioTouchPaint.ComputeSharp.Services;

/// <summary>
/// Implementation of coordinate converter for TUIO to canvas coordinate conversion
/// </summary>
public class CoordinateConverter : ICoordinateConverter
{
    private readonly ILogger<CoordinateConverter> _logger;
    
    private (int Width, int Height) _canvasSize = (1920, 1080);
    private float _tuioXMin = 0f;
    private float _tuioXMax = 1f;
    private float _tuioYMin = 0f;
    private float _tuioYMax = 1f;
    
    public (int Width, int Height) CanvasSize 
    { 
        get => _canvasSize; 
        set => _canvasSize = value; 
    }
    
    public (float XMin, float XMax, float YMin, float YMax) TuioRanges
    {
        get => (_tuioXMin, _tuioXMax, _tuioYMin, _tuioYMax);
        set
        {
            (_tuioXMin, _tuioXMax, _tuioYMin, _tuioYMax) = value;
            _logger.LogInformation($"TUIO ranges updated: X[{_tuioXMin} to {_tuioXMax}], Y[{_tuioYMin} to {_tuioYMax}]");
        }
    }
    
    public CoordinateConverter(ILogger<CoordinateConverter> logger)
    {
        _logger = logger;
    }
    
    public Float2 ConvertTuioToCanvas(float tuioX, float tuioY)
    {
        // Normalize TUIO coordinates from configured range to 0.0-1.0
        var normalizedX = (tuioX - _tuioXMin) / (_tuioXMax - _tuioXMin);
        var normalizedY = (tuioY - _tuioYMin) / (_tuioYMax - _tuioYMin);
        
        // Clamp to valid range
        normalizedX = Math.Max(0f, Math.Min(1f, normalizedX));
        normalizedY = Math.Max(0f, Math.Min(1f, normalizedY));
        
        // Convert normalized coordinates to canvas coordinates
        var canvasX = normalizedX * _canvasSize.Width;
        var canvasY = normalizedY * _canvasSize.Height;
        
        return new Float2(canvasX, canvasY);
    }
    
    public Float2 ConvertCanvasToTuio(float canvasX, float canvasY)
    {
        // Convert canvas coordinates to normalized coordinates (0.0-1.0)
        var normalizedX = canvasX / _canvasSize.Width;
        var normalizedY = canvasY / _canvasSize.Height;
        
        // Clamp to valid range
        normalizedX = Math.Max(0f, Math.Min(1f, normalizedX));
        normalizedY = Math.Max(0f, Math.Min(1f, normalizedY));
        
        // Convert normalized coordinates to TUIO range
        var tuioX = _tuioXMin + normalizedX * (_tuioXMax - _tuioXMin);
        var tuioY = _tuioYMin + normalizedY * (_tuioYMax - _tuioYMin);
        
        return new Float2(tuioX, tuioY);
    }
    
    public void SetTuioRanges(float xMin, float xMax, float yMin, float yMax)
    {
        if (xMax <= xMin || yMax <= yMin)
        {
            _logger.LogWarning($"Invalid TUIO ranges: X[{xMin} to {xMax}], Y[{yMin} to {yMax}]. Max values must be greater than min values.");
            return;
        }
        
        _tuioXMin = xMin;
        _tuioXMax = xMax;
        _tuioYMin = yMin;
        _tuioYMax = yMax;
        
        _logger.LogInformation($"TUIO ranges set: X[{xMin} to {xMax}], Y[{yMin} to {yMax}]");
    }
    
    public bool ValidateTuioCoordinates(float tuioX, float tuioY)
    {
        return tuioX >= _tuioXMin && tuioX <= _tuioXMax && 
               tuioY >= _tuioYMin && tuioY <= _tuioYMax;
    }
} 