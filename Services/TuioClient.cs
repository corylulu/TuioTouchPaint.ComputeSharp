using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TuioTouchPaint.ComputeSharp.Models;

namespace TuioTouchPaint.ComputeSharp.Services;

/// <summary>
/// Basic TUIO 1.1 client implementation for receiving touch/cursor data
/// </summary>
public class TuioClient : IDisposable
{
    private readonly ILogger<TuioClient> _logger;
    private UdpClient? _udpClient;
    private bool _isRunning = false;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receivingTask;

    // TUIO events
    public event EventHandler<TuioCursor>? CursorAdded;
    public event EventHandler<TuioCursor>? CursorUpdated;
    public event EventHandler<TuioCursor>? CursorRemoved;
    public event EventHandler? FrameFinished;

    // Current cursors
    private readonly Dictionary<int, TuioCursor> _cursors = new();
    
    // Configurable coordinate ranges
    private float _xMin = 0f;
    private float _xMax = 1f;
    private float _yMin = 0f;
    private float _yMax = 1f;
    
    // Reduce logging frequency for performance
    private int _logCounter = 0;
    private readonly int _logInterval = 100; // Log every 100th event

    public int Port { get; private set; } = 3333;

    public TuioClient(ILogger<TuioClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Start listening for TUIO messages on the specified port
    /// </summary>
    public void Start(int port = 3333)
    {
        if (_isRunning)
        {
            _logger.LogWarning("TUIO client is already running");
            return;
        }

        Port = port;
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient(port);
            _isRunning = true;
            
            _receivingTask = Task.Run(ReceiveMessages, _cancellationTokenSource.Token);
            
            _logger.LogInformation($"TUIO client started on port {port}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to start TUIO client on port {port}");
            throw;
        }
    }

    /// <summary>
    /// Stop listening for TUIO messages
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cancellationTokenSource?.Cancel();

        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;

        _receivingTask?.Wait(TimeSpan.FromSeconds(1));
        _receivingTask = null;

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _cursors.Clear();

        _logger.LogInformation("TUIO client stopped");
    }

    /// <summary>
    /// Main message receiving loop
    /// </summary>
    private async Task ReceiveMessages()
    {
        var localEndPoint = new IPEndPoint(IPAddress.Any, Port);

        while (_isRunning && !_cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync();
                ProcessMessage(result.Buffer);
            }
            catch (ObjectDisposedException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving TUIO message");
            }
        }
    }

    /// <summary>
    /// Process incoming TUIO message (could be OSC bundle or single message)
    /// </summary>
    private void ProcessMessage(byte[] data)
    {
        try
        {
            _logCounter++;
            
            // Reduce logging frequency for performance
            if (_logCounter % _logInterval == 0)
            {
                _logger.LogDebug($"Received TUIO data: {data.Length} bytes (logged every {_logInterval} messages)");
                
                // Log raw data for debugging (first 32 bytes)
                var preview = BitConverter.ToString(data.Take(Math.Min(32, data.Length)).ToArray());
                _logger.LogDebug($"Raw data preview: {preview}");
            }
            
            // Check if this is an OSC bundle or single message
            if (IsOscBundle(data))
            {
                if (_logCounter % _logInterval == 0)
                {
                    _logger.LogDebug("Processing OSC bundle");
                }
                ProcessOscBundle(data);
            }
            else
            {
                if (_logCounter % _logInterval == 0)
                {
                    _logger.LogDebug("Processing single OSC message");
                }
                var message = ParseOscMessage(data);
                if (message != null)
                {
                    ProcessParsedMessage(message);
                }
                else
                {
                    _logger.LogWarning("Failed to parse OSC message");
                    _logger.LogDebug($"Failed message data: {BitConverter.ToString(data.Take(Math.Min(64, data.Length)).ToArray())}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TUIO message");
        }
    }

    /// <summary>
    /// Process an OSC bundle containing multiple messages
    /// </summary>
    private void ProcessOscBundle(byte[] data)
    {
        try
        {
            int index = 8; // Skip "#bundle\0"
            
            // Skip timetag (8 bytes)
            index += 8;
            
            // Process bundle elements
            while (index < data.Length)
            {
                // Read element size (4 bytes, big-endian)
                if (index + 4 > data.Length) break;
                
                int elementSize = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, index));
                index += 4;
                
                if (index + elementSize > data.Length) break;
                
                // Extract element data
                byte[] elementData = new byte[elementSize];
                Array.Copy(data, index, elementData, 0, elementSize);
                
                // Parse the element (could be a message or nested bundle)
                if (IsOscBundle(elementData))
                {
                    // Nested bundle
                    ProcessOscBundle(elementData);
                }
                else
                {
                    // OSC message
                    var message = ParseOscMessage(elementData);
                    if (message != null)
                    {
                        ProcessParsedMessage(message);
                    }
                }
                
                index += elementSize;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OSC bundle");
        }
    }

    /// <summary>
    /// Process a parsed OSC message
    /// </summary>
    private void ProcessParsedMessage(OscMessage message)
    {
        if (_logCounter % _logInterval == 0)
        {
            _logger.LogDebug($"Parsed OSC message: {message.Address} with {message.Arguments.Length} arguments");
        }
        
        if (message.Address?.StartsWith("/tuio/2Dcur") == true)
        {
            ProcessCursorMessage(message);
        }
        else
        {
            if (_logCounter % _logInterval == 0)
            {
                _logger.LogDebug($"Ignoring non-cursor TUIO message: {message.Address}");
            }
        }
    }

    /// <summary>
    /// Parse OSC message structure
    /// </summary>
    private OscMessage? ParseOscMessage(byte[] data)
    {
        try
        {
            if (data.Length < 8) // Minimum: address + type tag
            {
                _logger.LogWarning($"OSC message too short: {data.Length} bytes");
                return null;
            }

            int index = 0;
            
            // Read address pattern
            var address = ReadOscString(data, ref index);
            if (string.IsNullOrEmpty(address))
            {
                _logger.LogWarning("Failed to read OSC address");
                return null;
            }

            // Align to 4-byte boundary
            index = AlignTo4ByteBoundary(index);
            
            if (index >= data.Length)
            {
                _logger.LogWarning("No type tag found in OSC message");
                return null;
            }

            // Read type tag string
            var typeTag = ReadOscString(data, ref index);
            if (string.IsNullOrEmpty(typeTag) || !typeTag.StartsWith(","))
            {
                _logger.LogWarning($"Invalid type tag: '{typeTag}'");
                return null;
            }

            // Align to 4-byte boundary
            index = AlignTo4ByteBoundary(index);

            // Read arguments based on type tags
            var args = new List<object>();
            for (int i = 1; i < typeTag.Length; i++)
            {
                if (index >= data.Length)
                {
                    //_logger.LogWarning($"Not enough data for argument {i}: type '{typeTag[i]}'");
                    break;
                }

                switch (typeTag[i])
                {
                    case 'i': // int32
                        if (index + 4 <= data.Length)
                        {
                            args.Add(ReadOscInt32(data, ref index));
                        }
                        else
                        {
                            _logger.LogWarning("Not enough data for int32 argument");
                            return null;
                        }
                        break;
                        
                    case 'f': // float32
                        if (index + 4 <= data.Length)
                        {
                            args.Add(ReadOscFloat32(data, ref index));
                        }
                        else
                        {
                            _logger.LogWarning("Not enough data for float32 argument");
                            return null;
                        }
                        break;
                        
                    case 'd': // double (float64)
                        if (index + 8 <= data.Length)
                        {
                            args.Add(ReadOscFloat64(data, ref index));
                        }
                        else
                        {
                            _logger.LogWarning("Not enough data for float64 argument");
                            return null;
                        }
                        break;
                        
                    case 's': // string
                        var stringArg = ReadOscString(data, ref index);
                        args.Add(stringArg ?? "");
                        index = AlignTo4ByteBoundary(index);
                        break;
                        
                    default:
                        //_logger.LogWarning($"Unknown OSC type tag: '{typeTag[i]}' at position {i}");
                        // Skip unknown types by continuing
                        break;
                }
            }

            var message = new OscMessage { Address = address, Arguments = args.ToArray() };
            //_logger.LogDebug($"Successfully parsed OSC message: {address} with {args.Count} arguments");
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing OSC message");
            return null;
        }
    }

    /// <summary>
    /// Check if data represents an OSC bundle
    /// </summary>
    private bool IsOscBundle(byte[] data)
    {
        if (data.Length < 16) return false; // Minimum bundle size
        
        // Check for "#bundle" string (7 chars + null terminator = 8 bytes)
        var bundleHeader = new byte[] { 0x23, 0x62, 0x75, 0x6E, 0x64, 0x6C, 0x65, 0x00 }; // "#bundle\0"
        
        for (int i = 0; i < 8; i++)
        {
            if (data[i] != bundleHeader[i])
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// Align index to 4-byte boundary
    /// </summary>
    private int AlignTo4ByteBoundary(int index)
    {
        return (index + 3) & ~3;
    }

    /// <summary>
    /// Read OSC string from byte array (includes null termination and padding)
    /// </summary>
    private string? ReadOscString(byte[] data, ref int index)
    {
        if (index >= data.Length) return null;
        
        var start = index;
        
        // Find null terminator
        while (index < data.Length && data[index] != 0)
        {
            index++;
        }
        
        if (index >= data.Length)
        {
            _logger.LogWarning("OSC string not null-terminated");
            return null;
        }
        
        // Extract string content
        var result = Encoding.UTF8.GetString(data, start, index - start);
        
        // Move past null terminator
        index++;
        
        return result;
    }

    /// <summary>
    /// Read OSC int32 from byte array
    /// </summary>
    private int ReadOscInt32(byte[] data, ref int index)
    {
        if (index + 4 > data.Length) throw new ArgumentException("Not enough data for int32");
        
        var result = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, index));
        index += 4;
        return result;
    }

    /// <summary>
    /// Read OSC float32 from byte array
    /// </summary>
    private float ReadOscFloat32(byte[] data, ref int index)
    {
        if (index + 4 > data.Length) throw new ArgumentException("Not enough data for float32");
        
        var bytes = new byte[4];
        Array.Copy(data, index, bytes, 0, 4);
        
        // Convert from network byte order (big-endian) to host byte order
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        
        var result = BitConverter.ToSingle(bytes, 0);
        index += 4;
        return result;
    }

    private double ReadOscFloat64(byte[] data, ref int index)
    {
        if (index + 8 > data.Length) throw new ArgumentException("Not enough data for float64");
        
        var bytes = new byte[8];
        Array.Copy(data, index, bytes, 0, 8);
        
        // Log raw bytes for debugging
        var rawHex = BitConverter.ToString(bytes);
        
        // Convert from network byte order (big-endian) to host byte order
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        
        var result = BitConverter.ToDouble(bytes, 0);
        
        // Log double parsing for debugging if needed
        _logger.LogDebug($"ReadOscFloat64: Raw bytes: {rawHex}, Result: {result}");
        
        index += 8;
        return result;
    }

    /// <summary>
    /// Process TUIO cursor message
    /// </summary>
    private void ProcessCursorMessage(OscMessage message)
    {
        if (message.Arguments.Length < 1) 
        {
            _logger.LogWarning("TUIO cursor message has no arguments");
            return;
        }

        var command = message.Arguments[0] as string;
        if (_logCounter % _logInterval == 0)
        {
            _logger.LogDebug($"Processing TUIO cursor command: {command} with {message.Arguments.Length} arguments");
        }
        
        switch (command)
        {
            case "set":
                if (message.Arguments.Length >= 6)
                {
                    ProcessCursorSet(message.Arguments);
                }
                else
                {
                    _logger.LogWarning($"TUIO cursor 'set' command has insufficient arguments: {message.Arguments.Length}");
                }
                break;
            case "alive":
                ProcessCursorAlive(message.Arguments.Skip(1).ToArray());
                break;
            case "fseq":
                FrameFinished?.Invoke(this, EventArgs.Empty);
                break;
            default:
                if (_logCounter % _logInterval == 0)
                {
                    _logger.LogDebug($"Unknown TUIO cursor command: {command}");
                }
                break;
        }
    }

    /// <summary>
    /// Process TUIO cursor set command
    /// </summary>
    private void ProcessCursorSet(object[] args)
    {
        try
        {
            var sessionId = Convert.ToInt32(args[1]);
            
            // Handle both float and double coordinate values
            var x = args[2] switch
            {
                float f => f,
                double d => (float)d,
                _ => Convert.ToSingle(args[2])
            };
            
            var y = args[3] switch
            {
                float f => f,
                double d => (float)d,
                _ => Convert.ToSingle(args[3])
            };
            
            var velocityX = args.Length > 4 ? args[4] switch
            {
                float f => f,
                double d => (float)d,
                _ => Convert.ToSingle(args[4])
            } : 0f;
            
            var velocityY = args.Length > 5 ? args[5] switch
            {
                float f => f,
                double d => (float)d,
                _ => Convert.ToSingle(args[5])
            } : 0f;
            
            var acceleration = args.Length > 6 ? args[6] switch
            {
                float f => f,
                double d => (float)d,
                _ => Convert.ToSingle(args[6])
            } : 0f;

            // Clean up very small floating point values (treat as zero)
            const float epsilon = 1e-6f; // Threshold for considering values as zero
            var originalY = y; // Keep track for debug output
            if (Math.Abs(x) < epsilon) x = 0f;
            if (Math.Abs(y) < epsilon) y = 0f;
            if (Math.Abs(velocityX) < epsilon) velocityX = 0f;
            if (Math.Abs(velocityY) < epsilon) velocityY = 0f;
            if (Math.Abs(acceleration) < epsilon) acceleration = 0f;
            
            // Log when Y coordinate was cleaned up (only occasionally)
            if (Math.Abs(originalY) < epsilon && originalY != 0f && _logCounter % _logInterval == 0)
            {
                _logger.LogDebug($"Cleaned tiny Y value {originalY} → 0");
            }

            // Log coordinate info for debugging (only occasionally)
            if (_logCounter % _logInterval == 0)
            {
                var xType = args[2].GetType().Name;
                var yType = args[3].GetType().Name;
                _logger.LogDebug($"ProcessCursorSet: Session {sessionId}, X: {x} ({xType}), Y: {y} ({yType}), VelX: {velocityX}, VelY: {velocityY}");
            }
            
            // Check if coordinates are in expected TUIO range (after cleanup)
            if (x < _xMin || x > _xMax || y < _yMin || y > _yMax)
            {
                _logger.LogWarning($"TUIO coordinates out of expected range X[{_xMin}-{_xMax}], Y[{_yMin}-{_yMax}]: X={x}, Y={y}");
            }

            var cursor = new TuioCursor
            {
                SessionId = sessionId,
                X = x,
                Y = y,
                VelocityX = velocityX,
                VelocityY = velocityY,
                Acceleration = acceleration,
                Timestamp = DateTime.UtcNow
            };

            if (_cursors.ContainsKey(sessionId))
            {
                _cursors[sessionId] = cursor;
                CursorUpdated?.Invoke(this, cursor);
            }
            else
            {
                _cursors[sessionId] = cursor;
                CursorAdded?.Invoke(this, cursor);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing cursor set command");
        }
    }

    /// <summary>
    /// Process TUIO cursor alive command
    /// </summary>
    private void ProcessCursorAlive(object[] args)
    {
        var aliveCursors = new HashSet<int>();
        
        foreach (var arg in args)
        {
            if (int.TryParse(arg.ToString(), out int sessionId))
            {
                aliveCursors.Add(sessionId);
            }
        }

        // Remove cursors that are no longer alive
        var toRemove = _cursors.Keys.Where(id => !aliveCursors.Contains(id)).ToList();
        foreach (var sessionId in toRemove)
        {
            var cursor = _cursors[sessionId];
            _cursors.Remove(sessionId);
            CursorRemoved?.Invoke(this, cursor);
        }
    }

    /// <summary>
    /// Set the expected coordinate ranges for TUIO input validation
    /// </summary>
    public void SetCoordinateRanges(float xMin, float xMax, float yMin, float yMax)
    {
        _xMin = xMin;
        _xMax = xMax;
        _yMin = yMin;
        _yMax = yMax;
        
        _logger.LogInformation($"TUIO coordinate ranges updated: X[{xMin} to {xMax}], Y[{yMin} to {yMax}]");
    }

    /// <summary>
    /// Get all currently active cursors
    /// </summary>
    public IReadOnlyDictionary<int, TuioCursor> GetActiveCursors()
    {
        return _cursors.AsReadOnly();
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Simple OSC message structure
    /// </summary>
    private class OscMessage
    {
        public string Address { get; set; } = "";
        public object[] Arguments { get; set; } = Array.Empty<object>();
    }
} 