using System.IO;

namespace NenFocus.Domain.Utils;

public static class ProcessUtils
{
    public static string NormalizeProcessName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        try
        {
            // Handles paths like "C:\Windows\explorer.exe" -> "explorer"
            string fileName = Path.GetFileName(input);

            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName[..^4];
            }

            return fileName.Trim().ToLowerInvariant();
        }
        catch
        {
            return input.Trim().ToLowerInvariant();
        }
    }
}