using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;

namespace ComCross.Adapters.Serial;

/// <summary>
/// Serial port adapter implementation
/// </summary>
public sealed class SerialAdapter : IDeviceAdapter
{
    private readonly ISerialPortAccessManager _accessManager;
    private LinuxSerialScanSettings? _linuxScanSettings;

    public SerialAdapter()
    {
        // Use platform-specific access manager
        _accessManager = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new LinuxSerialPortAccessManager()
            : new DefaultSerialPortAccessManager();
    }

    public SerialAdapter(ISerialPortAccessManager accessManager)
    {
        _accessManager = accessManager ?? throw new ArgumentNullException(nameof(accessManager));
    }
    
    /// <summary>
    /// Configure Linux serial port scan settings
    /// </summary>
    public void ConfigureLinuxScan(LinuxSerialScanSettings settings)
    {
        _linuxScanSettings = settings;
    }

    public Task<IReadOnlyList<Device>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var portSet = new HashSet<string>(SerialPort.GetPortNames());
        
        // On Linux, use configurable scan patterns
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var scanSettings = _linuxScanSettings ?? new LinuxSerialScanSettings();
            
            foreach (var pattern in scanSettings.ScanPatterns)
            {
                var scannedPorts = ScanLinuxPorts(pattern);
                foreach (var port in scannedPorts)
                {
                    // Check if port matches any exclude pattern
                    bool shouldExclude = false;
                    foreach (var excludePattern in scanSettings.ExcludePatterns)
                    {
                        if (MatchesPattern(port, excludePattern))
                        {
                            shouldExclude = true;
                            break;
                        }
                    }
                    
                    if (!shouldExclude)
                    {
                        portSet.Add(port);
                    }
                }
            }
        }
        
        var devices = portSet
            .OrderBy(p => p)
            .Select(port => new Device
            {
                Port = port,
                Name = port,
                Description = GetPortDescription(port),
                Manufacturer = "Unknown",
                IsFavorite = false
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Device>>(devices);
    }
    
    private static IEnumerable<string> ScanLinuxPorts(string pattern)
    {
        var results = new List<string>();
        
        try
        {
            // Extract directory and file pattern
            var lastSlash = pattern.LastIndexOf('/');
            if (lastSlash < 0) return results;
            
            var directory = pattern.Substring(0, lastSlash);
            var filePattern = pattern.Substring(lastSlash + 1);
            
            if (!Directory.Exists(directory)) return results;
            
            // Convert wildcard pattern to regex
            var regexPattern = "^" + Regex.Escape(filePattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            var regex = new Regex(regexPattern);
            
            foreach (var file in Directory.GetFiles(directory))
            {
                var fileName = Path.GetFileName(file);
                if (regex.IsMatch(fileName) && File.Exists(file))
                {
                    results.Add(file);
                }
            }
        }
        catch
        {
            // Ignore errors in scanning
        }
        
        return results;
    }
    
    private static bool MatchesPattern(string path, string pattern)
    {
        // Exact match or wildcard match
        if (path == pattern) return true;
        
        try
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            var regex = new Regex(regexPattern);
            return regex.IsMatch(path);
        }
        catch
        {
            return false;
        }
    }
    
    private static string GetPortDescription(string port)
    {
        if (port.Contains("USB")) return "USB Serial Port";
        if (port.Contains("ACM")) return "USB CDC-ACM Device";
        if (port.Contains("AMA")) return "ARM UART Port";
        if (port.Contains("pts")) return "Pseudo Terminal";
        if (port.Contains("vserial")) return "Virtual Serial Port";
        return $"Serial Port {port}";
    }

    public IDeviceConnection OpenConnection(string port)
    {
        return new SerialConnection(port, _accessManager);
    }
}
