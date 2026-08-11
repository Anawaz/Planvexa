namespace Planvexa.Modules.Documents.Domain;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>
/// walks the same Lexical JSON tree as <see cref="LexicalJson"/> and emits Markdown.
/// Supports the node set the editor produces: heading, paragraph, quote, callout (rendered as a GitHub-style
/// "> [!NOTE]" blockquote — Lexical has no built-in callout node, see the editor's CalloutNode), code,
/// list/listitem (bullet/number, arbitrarily nested), link, task-reference (rendered as a markdown link to
/// <c>task://{id}</c>), and inline bold/italic/strikethrough/code formatting via the text node's format bitmask.
/// Unrecognized node types fall back to their extracted plain text so export never silently drops content.
/// </summary>
public static class LexicalMarkdown
{
    private const int FormatBold = 1;
    private const int FormatItalic = 2;
    private const int FormatStrikethrough = 4;
    private const int FormatCode = 16;

    public static string ToMarkdown(string? content)
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

        if (node is not JsonObject obj || !obj.TryGetPropertyValue("root", out var rootNode) || rootNode is not JsonObject root
            || !root.TryGetPropertyValue("children", out var children) || children is not JsonArray blocks)
        {
            return LexicalJson.ExtractPlainText(content);
        }

        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            WriteBlock(block, sb, depth: 0);
        }

        return sb.ToString().TrimEnd('\n') + "\n";
    }

    private static void WriteBlock(JsonNode? blockNode, StringBuilder sb, int depth)
    {
        if (blockNode is not JsonObject block || !block.TryGetPropertyValue("type", out var typeVal))
        {
            return;
        }

        var type = typeVal?.GetValue<string>() ?? string.Empty;
        switch (type)
        {
            case "heading":
                var tag = block.TryGetPropertyValue("tag", out var tagVal) ? tagVal?.GetValue<string>() : "h1";
                var level = tag is { Length: 2 } && tag[0] == 'h' && char.IsDigit(tag[1]) ? tag[1] - '0' : 1;
                sb.Append(new string('#', Math.Clamp(level, 1, 6))).Append(' ').Append(InlineText(block)).Append("\n\n");
                break;

            case "paragraph":
                var text = InlineText(block);
                if (text.Length > 0)
                {
                    sb.Append(text).Append("\n\n");
                }

                break;

            case "quote":
                foreach (var line in InlineText(block).Split('\n'))
                {
                    sb.Append("> ").Append(line).Append('\n');
                }

                sb.Append('\n');
                break;

            case "callout":
                sb.Append("> [!NOTE]\n");
                foreach (var line in InlineText(block).Split('\n'))
                {
                    sb.Append("> ").Append(line).Append('\n');
                }

                sb.Append('\n');
                break;

            case "code":
                var language = block.TryGetPropertyValue("language", out var langVal) ? langVal?.GetValue<string>() : null;
                sb.Append("```").Append(language).Append('\n').Append(InlineText(block)).Append("\n```\n\n");
                break;

            case "list":
                var listType = block.TryGetPropertyValue("listType", out var ltVal) ? ltVal?.GetValue<string>() : "bullet";
                if (block.TryGetPropertyValue("children", out var itemsNode) && itemsNode is JsonArray items)
                {
                    var index = 1;
                    foreach (var item in items)
                    {
                        WriteListItem(item, sb, depth, listType == "number", index++);
                    }
                }

                sb.Append('\n');
                break;

            default:
                // Unknown block type (or a bare listitem/link reached at top level) — never drop content.
                var fallback = InlineText(block);
                if (fallback.Length > 0)
                {
                    sb.Append(fallback).Append("\n\n");
                }

                break;
        }
    }

    private static void WriteListItem(JsonNode? itemNode, StringBuilder sb, int depth, bool numbered, int index)
    {
        if (itemNode is not JsonObject item)
        {
            return;
        }

        var indent = new string(' ', depth * 2);
        var marker = numbered ? $"{index}. " : "- ";

        // A listitem's children are either inline content (text/link/task-reference) or a single nested
        // "list" node (Lexical nests sub-lists as a listitem child) — emit inline text on the marker line
        // and recurse into any nested list at depth + 1.
        var inlineChildren = new JsonArray();
        JsonNode? nestedList = null;
        if (item.TryGetPropertyValue("children", out var childrenNode) && childrenNode is JsonArray children)
        {
            foreach (var child in children.ToArray())
            {
                if (child is JsonObject childObj && childObj.TryGetPropertyValue("type", out var childType)
                    && childType?.GetValue<string>() == "list")
                {
                    nestedList = child.DeepClone();
                }
                else
                {
                    inlineChildren.Add(child?.DeepClone());
                }
            }
        }

        var wrapper = new JsonObject { ["children"] = inlineChildren };
        sb.Append(indent).Append(marker).Append(InlineText(wrapper)).Append('\n');

        if (nestedList is not null)
        {
            WriteBlock(nestedList, sb, depth + 1);
        }
    }

    /// <summary>Renders a block's inline children (text/link/linebreak/task-reference) as a single
    /// formatted string, applying bold/italic/strikethrough/code from each text node's format bitmask.</summary>
    private static string InlineText(JsonObject block)
    {
        var sb = new StringBuilder();
        if (block.TryGetPropertyValue("children", out var children) && children is JsonArray array)
        {
            foreach (var child in array)
            {
                WriteInline(child, sb);
            }
        }

        return sb.ToString();
    }

    private static void WriteInline(JsonNode? node, StringBuilder sb)
    {
        if (node is not JsonObject obj || !obj.TryGetPropertyValue("type", out var typeVal))
        {
            return;
        }

        var type = typeVal?.GetValue<string>() ?? string.Empty;
        switch (type)
        {
            case "text":
                var raw = obj.TryGetPropertyValue("text", out var t) ? t?.GetValue<string>() ?? string.Empty : string.Empty;
                var format = obj.TryGetPropertyValue("format", out var f) && f is not null ? f.GetValue<int>() : 0;
                sb.Append(ApplyFormat(raw, format));
                break;

            case "linebreak":
                sb.Append('\n');
                break;

            case "link":
                var url = obj.TryGetPropertyValue("url", out var u) ? u?.GetValue<string>() ?? string.Empty : string.Empty;
                sb.Append('[').Append(InlineText(obj)).Append("](").Append(url).Append(')');
                break;

            case "task-reference":
                var taskId = obj.TryGetPropertyValue("taskId", out var idVal) ? idVal?.GetValue<string>() ?? string.Empty : string.Empty;
                var title = obj.TryGetPropertyValue("title", out var titleVal) ? titleVal?.GetValue<string>() ?? "task" : "task";
                sb.Append('[').Append(title).Append("](task://").Append(taskId).Append(')');
                break;

            default:
                sb.Append(InlineText(obj));
                break;
        }
    }

    private static string ApplyFormat(string text, int format)
    {
        if ((format & FormatCode) != 0)
        {
            text = $"`{text}`";
        }

        if ((format & FormatBold) != 0)
        {
            text = $"**{text}**";
        }

        if ((format & FormatItalic) != 0)
        {
            text = $"*{text}*";
        }

        if ((format & FormatStrikethrough) != 0)
        {
            text = $"~~{text}~~";
        }

        return text;
    }
}
