namespace Planvexa.Modules.Documents.Domain;

using System.Text;
using System.Text.Json.Nodes;

/// <summary>
/// walks the same Lexical JSON tree as <see cref="LexicalJson"/> and emits Markdown.
/// Supports the node set the editor produces: heading, paragraph, quote, callout (rendered as a GitHub-style
/// "> [!NOTE]" blockquote — Lexical has no built-in callout node, see the editor's CalloutNode), code,
/// list/listitem (bullet/number/check, arbitrarily nested — check renders as GFM "- [ ] "/"- [x] "), table (rendered as a GitHub-Flavored-Markdown table,
/// first row as header), link, task-reference (rendered as a markdown link to
/// <c>task://{id}</c>), image (rendered as a Markdown image referencing <c>image://{imageId}</c> — see the
/// editor's ImageNode), file-attachment (rendered as a Markdown link referencing
/// <c>attachment://{attachmentId}</c> — see the editor's FileAttachmentNode), and inline
/// bold/italic/strikethrough/code formatting via the text node's format bitmask, and @-mention (rendered
/// as @[name](userId) — see the editor's MentionNode).
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

            case "image":
                var imageId = block.TryGetPropertyValue("imageId", out var imageIdVal) ? imageIdVal?.GetValue<string>() ?? string.Empty : string.Empty;
                var altText = block.TryGetPropertyValue("altText", out var altVal) ? altVal?.GetValue<string>() ?? string.Empty : string.Empty;
                sb.Append("![").Append(altText).Append("](image://").Append(imageId).Append(")\n\n");
                break;

            case "file-attachment":
                var attachmentId = block.TryGetPropertyValue("attachmentId", out var attachmentIdVal) ? attachmentIdVal?.GetValue<string>() ?? string.Empty : string.Empty;
                var attachmentFileName = block.TryGetPropertyValue("fileName", out var fileNameVal) ? fileNameVal?.GetValue<string>() ?? string.Empty : string.Empty;
                sb.Append('[').Append(attachmentFileName).Append("](attachment://").Append(attachmentId).Append(")\n\n");
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
                        WriteListItem(item, sb, depth, listType, index++);
                    }
                }

                sb.Append('\n');
                break;

            case "table":
                if (block.TryGetPropertyValue("children", out var rowsNode) && rowsNode is JsonArray rows)
                {
                    var rowCells = rows
                        .OfType<JsonObject>()
                        .Select(row => row.TryGetPropertyValue("children", out var cellsNode) && cellsNode is JsonArray cells
                            ? cells.OfType<JsonObject>().Select(cell => EscapeTableCell(InlineText(cell))).ToArray()
                            : [])
                        .Where(cells => cells.Length > 0)
                        .ToArray();

                    if (rowCells.Length > 0)
                    {
                        var columnCount = rowCells.Max(cells => cells.Length);
                        WriteTableRow(sb, rowCells[0], columnCount);
                        sb.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", columnCount))).Append('\n');
                        foreach (var cells in rowCells.Skip(1))
                        {
                            WriteTableRow(sb, cells, columnCount);
                        }
                    }

                    sb.Append('\n');
                }

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

    private static void WriteListItem(JsonNode? itemNode, StringBuilder sb, int depth, string? listType, int index)
    {
        if (itemNode is not JsonObject item)
        {
            return;
        }

        var indent = new string(' ', depth * 2);
        var marker = listType switch
        {
            "number" => $"{index}. ",
            "check" => item.TryGetPropertyValue("checked", out var checkedVal) && checkedVal is not null && checkedVal.GetValue<bool>()
                ? "- [x] "
                : "- [ ] ",
            _ => "- ",
        };

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

    private static void WriteTableRow(StringBuilder sb, IReadOnlyList<string> cells, int columnCount)
    {
        sb.Append('|');
        for (var i = 0; i < columnCount; i++)
        {
            sb.Append(' ').Append(i < cells.Count ? cells[i] : string.Empty).Append(" |");
        }

        sb.Append('\n');
    }

    /// <summary>GFM table cells can't contain literal pipes or newlines — escape/replace so the row stays
    /// on one line and doesn't break the table's column structure.</summary>
    private static string EscapeTableCell(string text) => text.Replace("|", "\\|").Replace("\n", "<br>");

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

            case "mention":
                // Same @[name](userId) wire format as the Tiptap comment/description editor's mention
                // node (see mentionExtension.ts) so mentions read consistently across both editors.
                var mentionUserId = obj.TryGetPropertyValue("userId", out var userIdVal) ? userIdVal?.GetValue<string>() ?? string.Empty : string.Empty;
                var mentionName = obj.TryGetPropertyValue("name", out var nameVal) ? nameVal?.GetValue<string>() ?? mentionUserId : mentionUserId;
                sb.Append("@[").Append(mentionName).Append("](").Append(mentionUserId).Append(')');
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
