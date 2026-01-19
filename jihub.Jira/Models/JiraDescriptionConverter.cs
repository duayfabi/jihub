using System.Text.Json;
using System.Text.Json.Serialization;

namespace jihub.Jira.Models;

/// <summary>
/// Custom JSON converter to handle Jira API v3's ADF (Atlassian Document Format) for description field.
/// In v3, description can be either a string (null/empty) or an ADF object.
/// This converter extracts plain text from ADF or returns empty string.
/// </summary>
public class JiraDescriptionConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // If it's null, return empty string
        if (reader.TokenType == JsonTokenType.Null)
        {
            return string.Empty;
        }

        // If it's a string (shouldn't happen in v3, but handle it anyway)
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? string.Empty;
        }

        // If it's an object (ADF format), extract text content
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return ExtractTextFromAdf(doc.RootElement);
        }

        return string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

    /// <summary>
    /// Extracts plain text from ADF (Atlassian Document Format) JSON structure.
    /// ADF structure: { "type": "doc", "version": 1, "content": [...] }
    /// </summary>
    private static string ExtractTextFromAdf(JsonElement element)
    {
        var textParts = new List<string>();
        ExtractTextRecursive(element, textParts);
        return string.Join("", textParts).Trim();
    }

    private static void ExtractTextRecursive(JsonElement element, List<string> textParts)
    {
        if (element.TryGetProperty("type", out var typeElement))
        {
            var type = typeElement.GetString();
            
            // Handle text with possible links
            if (type == "text" && element.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString() ?? string.Empty;
                
                // Check for link mark
                if (element.TryGetProperty("marks", out var marksElement) && marksElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mark in marksElement.EnumerateArray())
                    {
                        if (mark.TryGetProperty("type", out var markType) && markType.GetString() == "link" &&
                            mark.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("href", out var href))
                        {
                            text = $"[{text}]({href.GetString()})";
                            break;
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(text))
                {
                    textParts.Add(text);
                }
            }
            // Handle cards (links)
            else if ((type == "inlineCard" || type == "blockCard") && 
                     element.TryGetProperty("attrs", out var cardAttrs) && 
                     cardAttrs.TryGetProperty("url", out var cardUrl))
            {
                var url = cardUrl.GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    textParts.Add(url);
                }
            }
            // Handle hard breaks
            else if (type == "hardBreak")
            {
                textParts.Add("\n");
            }
            // Handle emojis
            else if (type == "emoji" && element.TryGetProperty("attrs", out var emojiAttrs))
            {
                var emojiText = emojiAttrs.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
                var emojiShortName = emojiAttrs.TryGetProperty("shortName", out var shortNameEl) ? shortNameEl.GetString() : null;
                var emoji = emojiText ?? emojiShortName;
                if (!string.IsNullOrEmpty(emoji))
                {
                    textParts.Add(emoji);
                }
            }
        }

        // If element has "content" array, recurse into it
        if (element.TryGetProperty("content", out var contentElement) && 
            contentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentElement.EnumerateArray())
            {
                ExtractTextRecursive(item, textParts);
            }
        }

        // Handle structural breaks (paragraph, heading)
        if (element.TryGetProperty("type", out var typeElementEnd))
        {
            var type = typeElementEnd.GetString();
            if (type == "paragraph" || type == "heading")
            {
                // Add newline after paragraphs/headings if not already present
                if (textParts.Count > 0 && textParts[^1] != "\n" && !string.IsNullOrWhiteSpace(textParts[^1]))
                {
                    textParts.Add("\n");
                }
            }
        }
    }
}
