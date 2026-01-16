using Microsoft.Extensions.Logging;

namespace ComCross.Shared.Interfaces;

/// <summary>
/// Abstract base class for protocol parsers.
/// Provides common functionality and logging support.
/// </summary>
public abstract class BaseMessageParser : IMessageParser
{
    protected readonly ILogger _logger;
    
    protected BaseMessageParser(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <inheritdoc/>
    public abstract string Id { get; }
    
    /// <inheritdoc/>
    public abstract string Name { get; }
    
    /// <inheritdoc/>
    public abstract string Version { get; }
    
    /// <inheritdoc/>
    public abstract string Description { get; }
    
    /// <inheritdoc/>
    public abstract string Category { get; }
    
    /// <inheritdoc/>
    public abstract bool IsBuiltIn { get; }
    
    /// <inheritdoc/>
    public ParseResult Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.IsEmpty)
            {
                return ParseResult.Failure("Data is empty", Array.Empty<byte>());
            }
            
            _logger.LogDebug("Parsing {DataLength} bytes with protocol {ProtocolId}", 
                data.Length, Id);
            
            var result = ParseCore(data);
            
            if (result.IsSuccess)
            {
                _logger.LogDebug("Successfully parsed {DataLength} bytes, found {FieldCount} fields", 
                    data.Length, result.Fields.Count);
            }
            else
            {
                _logger.LogWarning("Failed to parse data: {ErrorMessage}", result.ErrorMessage);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing data with protocol {ProtocolId}", Id);
            return ParseResult.Failure($"Unexpected error: {ex.Message}", data.ToArray());
        }
    }
    
    /// <inheritdoc/>
    public string Format(ParseResult result, FormatOptions options)
    {
        try
        {
            if (!result.IsSuccess)
            {
                return $"[Parse Error] {result.ErrorMessage}";
            }
            
            return FormatCore(result, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting parse result");
            return $"[Format Error] {ex.Message}";
        }
    }
    
    /// <inheritdoc/>
    public ValidationResult Validate(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.IsEmpty)
            {
                return ValidationResult.Invalid("Data is empty");
            }
            
            return ValidateCore(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating data");
            return ValidationResult.Invalid($"Validation error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Core parsing logic to be implemented by derived classes.
    /// </summary>
    protected abstract ParseResult ParseCore(ReadOnlySpan<byte> data);
    
    /// <summary>
    /// Core formatting logic to be implemented by derived classes.
    /// </summary>
    protected abstract string FormatCore(ParseResult result, FormatOptions options);
    
    /// <summary>
    /// Core validation logic to be implemented by derived classes.
    /// Default implementation calls ParseCore and checks for success.
    /// Override for more efficient validation without full parsing.
    /// </summary>
    protected virtual ValidationResult ValidateCore(ReadOnlySpan<byte> data)
    {
        var parseResult = ParseCore(data);
        return parseResult.IsSuccess 
            ? ValidationResult.Valid() 
            : ValidationResult.Invalid(parseResult.ErrorMessage ?? "Unknown error");
    }
    
    /// <summary>
    /// Helper method to convert byte span to hex string.
    /// </summary>
    protected static string ToHexString(ReadOnlySpan<byte> data, string separator = " ")
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }
        
        return string.Join(separator, data.ToArray().Select(b => b.ToString("X2")));
    }
    
    /// <summary>
    /// Helper method to convert byte span to ASCII string.
    /// Non-printable characters are replaced with '.'.
    /// </summary>
    protected static string ToAsciiString(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }
        
        var chars = new char[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            var b = data[i];
            chars[i] = b >= 32 && b <= 126 ? (char)b : '.';
        }
        
        return new string(chars);
    }
}
