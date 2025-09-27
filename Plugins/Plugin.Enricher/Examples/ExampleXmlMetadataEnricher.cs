using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Plugin.Enricher.Interfaces;
using Plugin.Enricher.Models;
using Plugin.Enricher.Utilities;
using Shared.Correlation;

namespace Plugin.Enricher.Examples;

/// <summary>
/// Example implementation of IMetadataEnrichmentImplementation that demonstrates enrichment functionality
/// Takes information content and creates enriched XML file cache data object with additional metadata
/// This is a reference implementation showing how to implement custom enrichment logic
/// </summary>
public class ExampleXmlMetadataEnricher : IMetadataEnrichmentImplementation
{
    /// <summary>
    /// Mandatory file extension that this implementation expects to process
    /// This implementation processes XML files containing standardized metadata
    /// </summary>
    public string MandatoryFileExtension => ".xml";
    /// <summary>
    /// Example method that demonstrates MetadataImplementationType functionality
    /// Takes information content and creates enriched XML file cache data object with additional metadata
    /// </summary>
    /// <param name="informationContent">Information content to enrich</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="config">Enrichment configuration</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Complete file cache data object with enriched XML extension, metadata, and content</returns>
    public async Task<object> EnrichToMetadataAsync(
        string informationContent,
        string fileName,
        EnrichmentConfiguration config,
        HierarchicalLoggingContext context,
        ILogger logger)
    {
        try
        {
            logger.LogInformationWithHierarchy(context, "Starting XML metadata enrichment for file: {FileName}", fileName);

            // Create enriched XML content with additional metadata
            var enrichedXmlString = CreateEnrichedXmlContent(informationContent, fileName, context, logger);

            logger.LogInformationWithHierarchy(context, "Successfully created enriched XML metadata for file: {FileName}", fileName);
            
            // Create complete file cache data object with enriched XML content
            var fileCacheDataObject = EnricherCacheHelper.CreateEnrichedFileCacheDataObject(
                fileName, enrichedXmlString, informationContent);
            return await Task.FromResult(fileCacheDataObject);
        }
        catch (Exception ex)
        {
            logger.LogErrorWithHierarchy(context, ex, $"Failed to enrich metadata for file: {fileName}");
            
            // Create error XML and return as file cache data object
            var errorXml = CreateErrorXml(fileName, ex.Message);
            var errorFileCacheDataObject = EnricherCacheHelper.CreateEnrichedFileCacheDataObject(
                fileName, errorXml, informationContent);
            return await Task.FromResult(errorFileCacheDataObject);
        }
    }

    /// <summary>
    /// Create enriched XML content with additional metadata and analysis
    /// Enriches existing XML content by adding fabricated enrichment fields
    /// </summary>
    private static string CreateEnrichedXmlContent(string informationContent, string fileName, HierarchicalLoggingContext context, ILogger logger)
    {
        try
        {
            var xmlDoc = new XmlDocument();

            // Check if informationContent is already XML (from StandardizerPlugin)
            if (IsValidXml(informationContent))
            {
                logger.LogInformationWithHierarchy(context, "Input content is XML, enriching existing XML structure for: {FileName}", fileName);

                // Load existing XML and enrich it
                xmlDoc.LoadXml(informationContent);

                // Add enrichment section to existing XML
                var existingRoot = xmlDoc.DocumentElement;
                if (existingRoot != null)
                {
                    var enrichmentSection = xmlDoc.CreateElement("EnrichmentData");
                    existingRoot.AppendChild(enrichmentSection);

                    // Add enrichment fields to existing XML
                    AddEnrichmentFields(xmlDoc, enrichmentSection, informationContent, fileName);
                }
            }
            else
            {
                logger.LogInformationWithHierarchy(context, "Input content is not XML, creating new enriched XML structure for: {FileName}", fileName);

                // Create new XML structure for non-XML content
                var root = xmlDoc.CreateElement("EnrichedMetadata");
                xmlDoc.AppendChild(root);

                // Add original information content
                var originalSection = xmlDoc.CreateElement("OriginalContent");
                originalSection.InnerText = informationContent;
                root.AppendChild(originalSection);

                // Add enrichment metadata
                var enrichmentSection = xmlDoc.CreateElement("EnrichmentData");
                root.AppendChild(enrichmentSection);

                // Add enrichment fields
                AddEnrichmentFields(xmlDoc, enrichmentSection, informationContent, fileName);
            }

            // Add processing metadata to root
            var documentRoot = xmlDoc.DocumentElement;
            if (documentRoot != null)
            {
                var processingMetadata = xmlDoc.CreateElement("ProcessingMetadata");
                processingMetadata.SetAttribute("enrichmentType", "ExampleXmlMetadataEnricher");
                processingMetadata.SetAttribute("version", "1.0");
                processingMetadata.SetAttribute("processingTimestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                documentRoot.AppendChild(processingMetadata);
            }

            // Return formatted XML
            var stringBuilder = new StringBuilder();
            using var xmlWriter = XmlWriter.Create(stringBuilder, new XmlWriterSettings 
            { 
                Indent = true, 
                IndentChars = "  ",
                NewLineChars = "\n"
            });
            xmlDoc.WriteTo(xmlWriter);
            xmlWriter.Flush();

            return stringBuilder.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarningWithHierarchy(context, ex, $"Failed to create enriched XML content for {fileName}, creating minimal XML");
            return CreateMinimalEnrichedXml(informationContent, fileName);
        }
    }

    /// <summary>
    /// Check if the input string is valid XML
    /// </summary>
    private static bool IsValidXml(string xmlString)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(xmlString))
                return false;

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlString);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Add enrichment fields to the XML document
    /// </summary>
    private static void AddEnrichmentFields(XmlDocument xmlDoc, XmlElement enrichmentSection, string informationContent, string fileName)
    {
        // Add file analysis
        var fileAnalysis = xmlDoc.CreateElement("FileAnalysis");
        fileAnalysis.SetAttribute("fileName", fileName);
        fileAnalysis.SetAttribute("contentLength", informationContent.Length.ToString());
        fileAnalysis.SetAttribute("enrichedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        enrichmentSection.AppendChild(fileAnalysis);

        // Add content analysis
        var contentAnalysis = xmlDoc.CreateElement("ContentAnalysis");
        contentAnalysis.SetAttribute("wordCount", CountWords(informationContent).ToString());
        contentAnalysis.SetAttribute("lineCount", CountLines(informationContent).ToString());
        contentAnalysis.SetAttribute("hasNumericData", ContainsNumericData(informationContent).ToString());
        enrichmentSection.AppendChild(contentAnalysis);

        // Add enriched keywords
        var keywordsSection = xmlDoc.CreateElement("ExtractedKeywords");
        var keywords = ExtractKeywords(informationContent);
        foreach (var keyword in keywords)
        {
            var keywordElement = xmlDoc.CreateElement("Keyword");
            keywordElement.InnerText = keyword;
            keywordsSection.AppendChild(keywordElement);
        }
        enrichmentSection.AppendChild(keywordsSection);

        // Add fabricated enrichment fields (additional metadata)
        var fabricatedData = xmlDoc.CreateElement("FabricatedEnrichmentFields");

        // Add sentiment analysis (fabricated)
        var sentimentElement = xmlDoc.CreateElement("SentimentAnalysis");
        sentimentElement.SetAttribute("sentiment", DetermineFabricatedSentiment(informationContent));
        sentimentElement.SetAttribute("confidence", "0.85");
        fabricatedData.AppendChild(sentimentElement);

        // Add topic classification (fabricated)
        var topicElement = xmlDoc.CreateElement("TopicClassification");
        topicElement.SetAttribute("primaryTopic", DetermineFabricatedTopic(informationContent));
        topicElement.SetAttribute("confidence", "0.78");
        fabricatedData.AppendChild(topicElement);

        // Add language detection (fabricated)
        var languageElement = xmlDoc.CreateElement("LanguageDetection");
        languageElement.SetAttribute("detectedLanguage", "en-US");
        languageElement.SetAttribute("confidence", "0.92");
        fabricatedData.AppendChild(languageElement);

        // Add readability score (fabricated)
        var readabilityElement = xmlDoc.CreateElement("ReadabilityScore");
        readabilityElement.SetAttribute("score", CalculateFabricatedReadabilityScore(informationContent).ToString());
        readabilityElement.SetAttribute("level", "intermediate");
        fabricatedData.AppendChild(readabilityElement);

        enrichmentSection.AppendChild(fabricatedData);
    }

    /// <summary>
    /// Create minimal enriched XML when full processing fails
    /// </summary>
    private static string CreateMinimalEnrichedXml(string informationContent, string fileName)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<EnrichedMetadata>
  <OriginalContent>{System.Security.SecurityElement.Escape(informationContent)}</OriginalContent>
  <EnrichmentData>
    <FileAnalysis fileName=""{System.Security.SecurityElement.Escape(fileName)}"" 
                  contentLength=""{informationContent.Length}"" 
                  enrichedAt=""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}"" />
  </EnrichmentData>
  <ProcessingMetadata enrichmentType=""ExampleXmlMetadataEnricher"" 
                      version=""1.0"" 
                      processingTimestamp=""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}"" />
</EnrichedMetadata>";
    }

    /// <summary>
    /// Create error XML for failed enrichment
    /// </summary>
    private static string CreateErrorXml(string fileName, string errorMessage)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<EnrichedMetadata>
  <Error>
    <Message>{System.Security.SecurityElement.Escape(errorMessage)}</Message>
    <FileName>{System.Security.SecurityElement.Escape(fileName)}</FileName>
    <Timestamp>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}</Timestamp>
  </Error>
  <ProcessingMetadata enrichmentType=""ExampleXmlMetadataEnricher"" 
                      version=""1.0"" 
                      processingTimestamp=""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}"" />
</EnrichedMetadata>";
    }

    /// <summary>
    /// Count words in content
    /// </summary>
    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;
        return content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Count lines in content
    /// </summary>
    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        return content.Split('\n').Length;
    }

    /// <summary>
    /// Check if content contains numeric data
    /// </summary>
    private static bool ContainsNumericData(string content)
    {
        return content.Any(char.IsDigit);
    }

    /// <summary>
    /// Extract simple keywords from content
    /// </summary>
    private static List<string> ExtractKeywords(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return new List<string>();

        var words = content.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?' }, 
                                 StringSplitOptions.RemoveEmptyEntries)
                          .Where(w => w.Length > 3)
                          .Select(w => w.ToLowerInvariant())
                          .Distinct()
                          .Take(10)
                          .ToList();

        return words;
    }

    /// <summary>
    /// Determine fabricated sentiment for demonstration purposes
    /// </summary>
    private static string DetermineFabricatedSentiment(string content)
    {
        // Simple fabricated sentiment analysis based on content length and keywords
        var positiveWords = new[] { "good", "great", "excellent", "positive", "success", "happy" };
        var negativeWords = new[] { "bad", "terrible", "negative", "failure", "sad", "error" };

        var lowerContent = content.ToLowerInvariant();
        var positiveCount = positiveWords.Count(word => lowerContent.Contains(word));
        var negativeCount = negativeWords.Count(word => lowerContent.Contains(word));

        if (positiveCount > negativeCount)
            return "positive";
        else if (negativeCount > positiveCount)
            return "negative";
        else
            return "neutral";
    }

    /// <summary>
    /// Determine fabricated topic classification for demonstration purposes
    /// </summary>
    private static string DetermineFabricatedTopic(string content)
    {
        var lowerContent = content.ToLowerInvariant();

        if (lowerContent.Contains("audio") || lowerContent.Contains("sound") || lowerContent.Contains("music"))
            return "audio-media";
        else if (lowerContent.Contains("data") || lowerContent.Contains("information") || lowerContent.Contains("metadata"))
            return "data-processing";
        else if (lowerContent.Contains("file") || lowerContent.Contains("document") || lowerContent.Contains("content"))
            return "document-management";
        else
            return "general";
    }

    /// <summary>
    /// Calculate fabricated readability score for demonstration purposes
    /// </summary>
    private static int CalculateFabricatedReadabilityScore(string content)
    {
        // Simple fabricated readability calculation
        var wordCount = CountWords(content);
        var lineCount = CountLines(content);

        // Fabricated formula: base score modified by content characteristics
        var baseScore = 50;
        var wordComplexity = Math.Min(wordCount / 10, 30); // Max 30 points for word complexity
        var structureBonus = Math.Min(lineCount * 2, 20); // Max 20 points for structure

        return Math.Min(baseScore + wordComplexity + structureBonus, 100);
    }
}
