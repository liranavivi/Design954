namespace Plugin.PreFileReader.Utilities;

/// <summary>
/// Utility for expanding file search patterns with brace expansion support
/// Converts patterns like "*.{zip,rar,7z}" into multiple patterns ["*.zip", "*.rar", "*.7z"]
/// Specific to PreFileReaderPlugin for compressed file discovery
/// </summary>
public static class FilePatternExpander
{
    /// <summary>
    /// Expands a search pattern with brace expansion into multiple patterns
    /// </summary>
    /// <param name="pattern">Pattern like "*.{zip,rar,7z}" or "*.*"</param>
    /// <returns>Array of expanded patterns</returns>
    public static string[] ExpandPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return new[] { "*.*" };
        }

        // Check if pattern contains brace expansion
        if (!pattern.Contains("{") || !pattern.Contains("}"))
        {
            return new[] { pattern };
        }

        try
        {
            return ExpandBracePattern(pattern);
        }
        catch (Exception)
        {
            // If expansion fails, return original pattern
            return new[] { pattern };
        }
    }

    /// <summary>
    /// Enumerates files using expanded patterns
    /// </summary>
    /// <param name="directoryPath">Directory to search</param>
    /// <param name="searchPattern">Pattern with potential brace expansion</param>
    /// <param name="searchOption">Search option (default: TopDirectoryOnly)</param>
    /// <returns>Enumerable of file paths</returns>
    public static IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        if (!Directory.Exists(directoryPath))
        {
            return Enumerable.Empty<string>();
        }

        var expandedPatterns = ExpandPattern(searchPattern);
        
        // Use SelectMany to flatten results from multiple patterns
        return expandedPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(directoryPath, pattern, searchOption))
            .Distinct(); // Remove duplicates in case patterns overlap
    }
    
    /// <summary>
    /// Internal method to expand brace patterns
    /// </summary>
    private static string[] ExpandBracePattern(string pattern)
    {
        // Find the brace section
        var braceStart = pattern.IndexOf('{');
        var braceEnd = pattern.IndexOf('}', braceStart);

        if (braceStart == -1 || braceEnd == -1 || braceEnd <= braceStart)
        {
            return new[] { pattern };
        }

        // Extract parts
        var prefix = pattern.Substring(0, braceStart);
        var suffix = pattern.Substring(braceEnd + 1);
        var braceContent = pattern.Substring(braceStart + 1, braceEnd - braceStart - 1);

        // Split by comma and create patterns
        var extensions = braceContent.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var expandedPatterns = new List<string>();

        foreach (var extension in extensions)
        {
            var trimmedExtension = extension.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedExtension))
            {
                expandedPatterns.Add(prefix + trimmedExtension + suffix);
            }
        }

        return expandedPatterns.Count > 0 ? expandedPatterns.ToArray() : new[] { pattern };
    }
 
}
