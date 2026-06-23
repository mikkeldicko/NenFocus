namespace NenFocus.Domain.Constants;

public static class ProcessUtils
{
    public static string NormalizeProcessName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        try
        {
            string fileName = Path.GetFileName(input);

            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                fileName = fileName[..^4];

            return fileName.Trim().ToLowerInvariant();
        }
        catch
        {
            return input.Trim().ToLowerInvariant();
        }
    }
}
