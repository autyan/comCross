using ComCross.Core.Services;
using System.Reflection;

// List all embedded resources first
var assembly = Assembly.GetAssembly(typeof(LocalizationService));
if (assembly != null)
{
    Console.WriteLine("=== Embedded Resources in ComCross.Core ===");
    var names = assembly.GetManifestResourceNames();
    
    if (names.Length == 0)
    {
        Console.WriteLine("❌ No embedded resources found!\n");
    }
    else
    {
        foreach (var name in names)
        {
            Console.WriteLine($"✓ {name}");
        }
        Console.WriteLine();
    }
}

// Test localization loading
Console.WriteLine("=== ComCross Localization Test ===\n");

var localization = new LocalizationService();

Console.WriteLine($"Current Culture: {localization.CurrentCulture}");
Console.WriteLine($"Available Cultures: {string.Join(", ", localization.AvailableCultures.Select(c => c.Code))}\n");

// Test English
Console.WriteLine("--- English (en-US) ---");
Console.WriteLine($"app.title: {localization.GetString("app.title")}");
Console.WriteLine($"menu.connect: {localization.GetString("menu.connect")}");
Console.WriteLine($"dialog.connect.title: {localization.GetString("dialog.connect.title")}");
Console.WriteLine($"tool.send.button: {localization.GetString("tool.send.button")}\n");

// Test Chinese
Console.WriteLine("--- Chinese (zh-CN) ---");
localization.SetCulture("zh-CN");
Console.WriteLine($"app.title: {localization.GetString("app.title")}");
Console.WriteLine($"menu.connect: {localization.GetString("menu.connect")}");
Console.WriteLine($"dialog.connect.title: {localization.GetString("dialog.connect.title")}");
Console.WriteLine($"tool.send.button: {localization.GetString("tool.send.button")}\n");

// Test missing key
Console.WriteLine("--- Missing Key Test ---");
Console.WriteLine($"missing.key: {localization.GetString("missing.key")}\n");

Console.WriteLine("✅ All tests completed!");
