namespace ComCross.Shared.Interfaces;

/// <summary>
/// Protocol parser interface for parsing raw binary data.
/// All protocol implementations must implement this interface.
/// </summary>
public interface IMessageParser
{
    /// <summary>
    /// Unique identifier for this protocol (e.g., "raw-bytes", "modbus-rtu").
    /// Must be globally unique within the application.
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Human-readable name for this protocol (e.g., "Raw Bytes", "Modbus RTU").
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Protocol version (e.g., "1.0.0").
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// Brief description of the protocol.
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Protocol category (e.g., "Industrial", "IoT", "Custom").
    /// </summary>
    string Category { get; }
    
    /// <summary>
    /// Whether this is a built-in protocol.
    /// Built-in protocols cannot be unregistered.
    /// </summary>
    bool IsBuiltIn { get; }
    
    /// <summary>
    /// Parse raw binary data into structured format.
    /// </summary>
    /// <param name="data">Raw binary data to parse</param>
    /// <returns>Parse result containing parsed data or error information</returns>
    ParseResult Parse(ReadOnlySpan<byte> data);
    
    /// <summary>
    /// Format parsed result into human-readable string.
    /// </summary>
    /// <param name="result">Parse result to format</param>
    /// <param name="options">Formatting options</param>
    /// <returns>Formatted string representation</returns>
    string Format(ParseResult result, FormatOptions options);
    
    /// <summary>
    /// Validate raw binary data without parsing.
    /// Faster than full parse, used for quick validation.
    /// </summary>
    /// <param name="data">Raw binary data to validate</param>
    /// <returns>Validation result indicating if data is valid</returns>
    ValidationResult Validate(ReadOnlySpan<byte> data);
}

/// <summary>
/// Result of protocol parsing operation.
/// </summary>
public sealed class ParseResult
{
    /// <summary>
    /// Whether parsing was successful.
    /// </summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>
    /// Parsed data as key-value pairs.
    /// Key: field name, Value: field value.
    /// </summary>
    public Dictionary<string, object?> Fields { get; init; } = new();
    
    /// <summary>
    /// Error message if parsing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// Original raw data that was parsed.
    /// </summary>
    public byte[] RawData { get; init; } = Array.Empty<byte>();
    
    /// <summary>
    /// Create a successful parse result.
    /// </summary>
    public static ParseResult Success(Dictionary<string, object?> fields, byte[] rawData)
    {
        return new ParseResult
        {
            IsSuccess = true,
            Fields = fields,
            RawData = rawData
        };
    }
    
    /// <summary>
    /// Create a failed parse result.
    /// </summary>
    public static ParseResult Failure(string errorMessage, byte[] rawData)
    {
        return new ParseResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            RawData = rawData
        };
    }
}

/// <summary>
/// Result of protocol validation operation.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Whether validation passed.
    /// </summary>
    public bool IsValid { get; init; }
    
    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// Create a successful validation result.
    /// </summary>
    public static ValidationResult Valid() => new() { IsValid = true };
    
    /// <summary>
    /// Create a failed validation result.
    /// </summary>
    public static ValidationResult Invalid(string errorMessage)
    {
        return new ValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Options for formatting parse results.
/// </summary>
public sealed class FormatOptions
{
    /// <summary>
    /// Whether to include hex representation of raw data.
    /// </summary>
    public bool IncludeHex { get; init; } = true;
    
    /// <summary>
    /// Whether to include ASCII representation of raw data.
    /// </summary>
    public bool IncludeAscii { get; init; } = true;
    
    /// <summary>
    /// Whether to include field names in output.
    /// </summary>
    public bool IncludeFieldNames { get; init; } = true;
    
    /// <summary>
    /// Maximum line length before wrapping.
    /// </summary>
    public int MaxLineLength { get; init; } = 80;
    
    /// <summary>
    /// Default formatting options.
    /// </summary>
    public static FormatOptions Default => new();
}
