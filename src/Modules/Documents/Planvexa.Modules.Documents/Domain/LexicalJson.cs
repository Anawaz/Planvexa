namespace Planvexa.Modules.Documents.Domain;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>
/// <see cref="Document.Content"/> and <see cref="DocumentVersion.Content"/> are persisted as the raw
/// serialized JSON string produced by Lexical's <c>editorState.toJSON()</c> — the column stays a plain
/// <c>text</c> column (see DocumentsConfigurations) so this string round-trips through the API/DTOs with
/// zero re-encoding; the application layer never parses it except for the read-only derivations below.
///
/// The stored shape (Lexical's actual schema, not ProseMirror's — see 0056_ConvertDocumentContentToLexicalJson.sql):
/// <code>
/// {
///   "root": {
///     "children": [
///       { "type": "paragraph", "children": [
///           { "type": "text", "text": "...", "format": 0, "detail": 0, "mode": "normal", "style": "", "version": 1 }
///         ], "direction": "ltr", "format": "", "indent": 0, "version": 1 }
///     ],
///     "direction": "ltr", "format": "", "indent": 0, "type": "root", "version": 1
///   }
/// }
/// </code>
/// Block node types this module's editor emits: paragraph, heading (tag h1-h6), quote, callout (custom,
/// same shape as quote), code, list/listitem, link, task-reference (decorator leaf with a taskId/title,
/// no children), image (decorator leaf with an imageId/contentType/altText, no children), file-attachment
/// (decorator leaf with an attachmentId/fileName/contentType/sizeBytes, no children), mention
/// (decorator leaf with a userId/name, no children).
/// <see cref="ExtractPlainText"/> and the sibling <see cref="LexicalMarkdown"/> exporter both
/// walk this same "children" tree.
/// </summary>
public static class LexicalJson
{
    private static readonly string[] BlockTypes =
        ["paragraph", "heading", "quote", "callout", "listitem", "code"];

    /// <summary>A single empty paragraph — the default content of a brand-new document.</summary>
    public const string EmptyDocument =
        """{"root":{"children":[{"children":[],"direction":null,"format":"","indent":0,"type":"paragraph","version":1}],"direction":null,"format":"","indent":0,"type":"root","version":1}}""";

    /// <summary>Wraps plain text as a single-paragraph Lexical doc (used for the DB migration and for any
    /// programmatic content creation that doesn't go through the editor).</summary>
    public static string ToJson(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return EmptyDocument;
        }

        var doc = new JsonObject
        {
            ["root"] = new JsonObject
            {
                ["children"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["children"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["detail"] = 0,
                                ["format"] = 0,
                                ["mode"] = "normal",
                                ["style"] = "",
                                ["text"] = plainText,
                                ["type"] = "text",
                                ["version"] = 1,
                            },
                        },
                        ["direction"] = "ltr",
                        ["format"] = "",
                        ["indent"] = 0,
                        ["type"] = "paragraph",
                        ["version"] = 1,
                    },
                },
                ["direction"] = "ltr",
                ["format"] = "",
                ["indent"] = 0,
                ["type"] = "root",
                ["version"] = 1,
            },
        };

        return doc.ToJsonString();
    }

    /// <summary>Extracts the concatenated plain-text content of every "text" node in the Lexical doc,
    /// depth-first, joining block-level nodes (paragraph/heading/quote/callout/listitem/code) with a
    /// newline. Used for search snippets and as the text source for Markdown export fallback. Tolerates
    /// malformed/legacy JSON by returning it verbatim (defensive: search must never silently drop
    /// content) and a plain (non-JSON) string content value is returned unchanged.</summary>
    public static string ExtractPlainText(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content);
        }
        catch (System.Text.Json.JsonException)
        {
            return content;
        }

        if (node is not JsonObject root || !root.TryGetPropertyValue("root", out var rootNode))
        {
            // Not a recognizable Lexical doc (e.g. pre-migration plain text that slipped through) —
            // return as-is rather than dropping content.
            return content;
        }

        var text = new StringBuilder();
        ExtractText(rootNode, text);
        return text.ToString().TrimEnd('\n');
    }

    private static void ExtractText(JsonNode? node, StringBuilder into)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("type", out var type))
                {
                    var typeName = type?.GetValue<string>();
                    if (typeName == "text" && obj.TryGetPropertyValue("text", out var text) && text is not null)
                    {
                        into.Append(text.GetValue<string>());
                    }
                    else if (typeName == "linebreak")
                    {
                        into.Append('\n');
                    }
                    else if (typeName == "task-reference" && obj.TryGetPropertyValue("title", out var title) && title is not null)
                    {
                        into.Append(title.GetValue<string>());
                    }
                    else if (typeName == "image" && obj.TryGetPropertyValue("altText", out var altText) && altText is not null)
                    {
                        into.Append(altText.GetValue<string>());
                    }
                    else if (typeName == "file-attachment" && obj.TryGetPropertyValue("fileName", out var attachmentFileName) && attachmentFileName is not null)
                    {
                        into.Append(attachmentFileName.GetValue<string>());
                    }
                    else if (typeName == "mention" && obj.TryGetPropertyValue("name", out var mentionName) && mentionName is not null)
                    {
                        into.Append('@').Append(mentionName.GetValue<string>());
                    }

                    if (obj.TryGetPropertyValue("children", out var children))
                    {
                        ExtractText(children, into);
                    }

                    if (typeName is not null && Array.IndexOf(BlockTypes, typeName) >= 0 && into.Length > 0 && into[^1] != '\n')
                    {
                        into.Append('\n');
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    ExtractText(item, into);
                }

                break;
        }
    }
}
