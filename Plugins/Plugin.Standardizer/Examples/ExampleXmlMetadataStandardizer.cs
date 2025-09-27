using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Plugin.Standardizer.Interfaces;
using Plugin.Standardizer.Models;
using Plugin.Standardizer.Utilities;
using Shared.Correlation;

namespace Plugin.Standardizer.Examples;

/// <summary>
/// Example XML implementation of IMetadataStandardizationImplementation
/// Demonstrates how to take fabricated text content and create XML metadata content
/// This is a concrete example showing XML format implementation
/// </summary>
public class ExampleXmlMetadataStandardizer : IMetadataStandardizationImplementation
{
    /// <summary>
    /// Mandatory file extension that this implementation expects to process
    /// This implementation processes text files containing information about audio
    /// </summary>
    public string MandatoryFileExtension => ".txt";
    /// <summary>
    /// Example method that demonstrates MetadataImplementationType functionality
    /// Takes fabricated text content and creates complete XML file cache data object
    /// </summary>
    /// <param name="informationContent">Text content containing information about audio</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="config">Standardization configuration</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Complete file cache data object with XML extension, metadata, and content</returns>
    public async Task<object> StandardizeToMetadataAsync(
        string informationContent,
        string fileName,
        StandardizationConfiguration config,
        HierarchicalLoggingContext context,
        ILogger logger)
    {
        try
        {
            logger.LogInformationWithHierarchy(context, "Starting XML standardization for file: {FileName}", fileName);

            // Create XML document
            var xmlDoc = new XmlDocument();
            var root = xmlDoc.CreateElement("AudioMetadata");
            xmlDoc.AppendChild(root);

            // Add basic file information
            var fileInfo = xmlDoc.CreateElement("FileInformation");
            root.AppendChild(fileInfo);

            var fileNameElement = xmlDoc.CreateElement("FileName");
            fileNameElement.InnerText = fileName;
            fileInfo.AppendChild(fileNameElement);

            var processingDate = xmlDoc.CreateElement("ProcessingDate");
            processingDate.InnerText = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            fileInfo.AppendChild(processingDate);

            // Parse and structure the information content
            var contentMetadata = xmlDoc.CreateElement("ContentMetadata");
            root.AppendChild(contentMetadata);

            // Example: Parse fabricated text content and extract structured information
            var structuredData = ParseInformationContent(informationContent);
            
            foreach (var kvp in structuredData)
            {
                var element = xmlDoc.CreateElement(SanitizeElementName(kvp.Key));
                element.InnerText = kvp.Value;
                contentMetadata.AppendChild(element);
            }

            // Add standardization metadata
            var standardizationInfo = xmlDoc.CreateElement("StandardizationInfo");
            root.AppendChild(standardizationInfo);

            var implementationType = xmlDoc.CreateElement("ImplementationType");
            implementationType.InnerText = config.MetadataImplementationType ?? "ExampleXmlMetadataStandardizer";
            standardizationInfo.AppendChild(implementationType);

            var version = xmlDoc.CreateElement("Version");
            version.InnerText = "1.0";
            standardizationInfo.AppendChild(version);

            // Convert to formatted XML string
            var xmlString = FormatXmlDocument(xmlDoc);

            logger.LogInformationWithHierarchy(context, "Successfully created XML metadata for file: {FileName}", fileName);

            // Create complete file cache data object with XML content
            var fileCacheDataObject = StandardizerCacheHelper.CreateXmlFileCacheDataObject(
                fileName, xmlString, informationContent);
            return await Task.FromResult(fileCacheDataObject);
        }
        catch (Exception ex)
        {
            logger.LogErrorWithHierarchy(context, ex, "Failed to standardize metadata for file: {FileName}", fileName);

            // Create error XML and return as file cache data object
            var errorXml = CreateErrorXml(fileName, ex.Message);
            var errorFileCacheDataObject = StandardizerCacheHelper.CreateXmlFileCacheDataObject(
                fileName, errorXml, informationContent);
            return await Task.FromResult(errorFileCacheDataObject);
        }
    }

    /// <summary>
    /// Example method to parse fabricated text content and extract key-value pairs
    /// This demonstrates how information content can be structured into XML
    /// </summary>
    /// <param name="content">Text content to parse</param>
    /// <returns>Dictionary of extracted metadata</returns>
    private Dictionary<string, string> ParseInformationContent(string content)
    {
        var metadata = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(content))
        {
            metadata["ContentStatus"] = "Empty";
            return metadata;
        }

        // Example parsing logic for fabricated content
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            // Look for key-value patterns like "Title: Some Title" or "Duration: 3:45"
            if (line.Contains(':'))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        metadata[key] = value;
                    }
                }
            }
        }

        // Add some example fabricated metadata if no structured data found
        if (metadata.Count == 0)
        {
            metadata["Title"] = "Audio Recording";
            metadata["Duration"] = "Unknown";
            metadata["Format"] = "Audio File";
            metadata["Description"] = content.Length > 100 ? 
                content.Substring(0, 100) + "..." : content;
        }

        // Add content analysis
        metadata["ContentLength"] = content.Length.ToString();
        metadata["WordCount"] = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.ToString();
        metadata["LineCount"] = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ToString();

        return metadata;
    }

    /// <summary>
    /// Sanitize element names for XML compatibility
    /// </summary>
    /// <param name="name">Original name</param>
    /// <returns>XML-safe element name</returns>
    private string SanitizeElementName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "UnknownElement";

        var sanitized = new StringBuilder();
        
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sanitized.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                sanitized.Append('_');
            }
        }

        var result = sanitized.ToString();
        
        // Ensure it starts with a letter or underscore
        if (result.Length > 0 && !char.IsLetter(result[0]) && result[0] != '_')
        {
            result = "Element_" + result;
        }

        return string.IsNullOrEmpty(result) ? "UnknownElement" : result;
    }

    /// <summary>
    /// Format XML document with proper indentation
    /// </summary>
    /// <param name="xmlDoc">XML document to format</param>
    /// <returns>Formatted XML string</returns>
    private string FormatXmlDocument(XmlDocument xmlDoc)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            Encoding = Encoding.UTF8
        };

        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, settings);
        xmlDoc.Save(xmlWriter);
        return stringWriter.ToString();
    }

    /// <summary>
    /// Create error XML when standardization fails
    /// </summary>
    /// <param name="fileName">File name</param>
    /// <param name="errorMessage">Error message</param>
    /// <returns>Error XML content</returns>
    private string CreateErrorXml(string fileName, string errorMessage)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<AudioMetadata>
  <FileInformation>
    <FileName>{fileName}</FileName>
    <ProcessingDate>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}</ProcessingDate>
  </FileInformation>
  <Error>
    <Message>{errorMessage}</Message>
    <Status>Failed</Status>
  </Error>
</AudioMetadata>";
    }
}
