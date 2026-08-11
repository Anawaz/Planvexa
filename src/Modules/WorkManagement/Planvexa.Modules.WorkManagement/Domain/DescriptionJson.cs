namespace Planvexa.Modules.WorkManagement.Domain;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// <see cref="WorkItem.Description"/> stays a plain <c>string?</c> at the domain/service
/// layer (every existing call site keeps working unchanged) but is PERSISTED as ProseMirror/Lexical-shaped
/// JSON (<c>{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"..."}]}]}</c>)
/// so the rich-text editor can consume it without another migration. The EF value converter
/// (see WorkItemConfiguration) calls <see cref="ToJson"/> on write and <see cref="FromText"/> on read —
/// this is the ONLY place that (de)serializes the wrapper; nothing else in the codebase needs to know the
/// storage shape changed. The frontend still renders plain text for now by reading the extracted string.
/// </summary>
public static class DescriptionJson
{
    /// <summary>Wraps plain text as a single-paragraph doc, or <c>null</c> for an empty description.</summary>
    public static string? ToJson(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }

        var doc = new JsonObject
        {
            ["type"] = "doc",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "paragraph",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = plainText },
                    },
                },
            },
        };

        return doc.ToJsonString();
    }

    /// <summary>Extracts the concatenated plain-text content of every "text" node in the doc, depth-first,
    /// joining paragraph/block boundaries with a newline. Tolerates malformed/legacy JSON by returning it
    /// verbatim (defensive: a bad migration must never silently drop content).</summary>
    public static string? FromText(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json);
            var text = new System.Text.StringBuilder();
            ExtractText(node, text);
            var result = text.ToString().TrimEnd('\n');
            return result.Length > 0 ? result : null;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void ExtractText(JsonNode? node, System.Text.StringBuilder into)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("type", out var type) && type?.GetValue<string>() == "text"
                    && obj.TryGetPropertyValue("text", out var text) && text is not null)
                {
                    into.Append(text.GetValue<string>());
                }

                if (obj.TryGetPropertyValue("content", out var content))
                {
                    ExtractText(content, into);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    ExtractText(item, into);
                    if (item is JsonObject o && o.TryGetPropertyValue("type", out var t)
                        && t?.GetValue<string>() is "paragraph" or "heading" && into.Length > 0)
                    {
                        into.Append('\n');
                    }
                }

                break;
        }
    }
}
