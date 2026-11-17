using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GUIClient.Agents;

/// <summary>Why a client's configuration file was not edited. Every value but <see cref="None"/> leaves it untouched.</summary>
public enum AgentConfigProblem
{
    None,

    /// <summary>Not valid for its format (JSON, or JSON with comments for VS Code), or only comments.</summary>
    Unparseable,

    /// <summary>Valid JSON, but not an object.</summary>
    RootNotAnObject,

    /// <summary>The servers member exists but is not an object.</summary>
    ContainerNotAnObject,

    /// <summary>The servers member appears twice; which one the client reads is not ours to guess.</summary>
    DuplicateContainer,

    /// <summary>More than one <c>netrisk</c> entry.</summary>
    DuplicateEntry,

    /// <summary>Over <see cref="AgentClientConfigEditor.MaxFileBytes"/>.</summary>
    TooLarge,

    /// <summary>The edit did not prove to change the <c>netrisk</c> entry alone.</summary>
    VerificationFailed
}

public sealed record AgentConfigInspection(AgentConfigProblem Problem, JsonObject? Entry);

public sealed record AgentConfigEdit(AgentConfigProblem Problem, byte[]? Content, bool Changed);

/// <summary>
/// The merge-only writer behind one-click registration (T364, S55). It edits the file as text: it finds the servers
/// object and the <c>netrisk</c> member with <see cref="Utf8JsonReader"/> and splices only that member, so every other
/// byte (servers, keys, order, indentation, VS Code comments, a BOM) is kept. Each edit is then proved with
/// <see cref="OnlyEntryDiffers"/>, and refused if it does not prove out. Pure: bytes in, bytes out.
/// </summary>
public static class AgentClientConfigEditor
{
    /// <summary>A bound on what is read (Claude Code's own file holds per-project state and grows).</summary>
    public const int MaxFileBytes = 32 * 1024 * 1024;

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>The <c>netrisk</c> entry the file holds, if any, or why the file cannot be read.</summary>
    public static AgentConfigInspection Inspect(byte[]? content, AgentClientFormat format)
    {
        if (content is not null && content.Length > MaxFileBytes) return new(AgentConfigProblem.TooLarge, null);
        var document = Load(content, format);
        if (document.Problem != AgentConfigProblem.None) return new(document.Problem, null);
        if (document.Layout?.Entry is not { } entry) return new(AgentConfigProblem.None, null);

        var node = ParseNode(document.Layout.Text.AsSpan(entry.ValueStart, entry.ValueEnd - entry.ValueStart), format);
        return new(AgentConfigProblem.None, node as JsonObject ?? new JsonObject());
    }

    /// <summary>Adds the <c>netrisk</c> entry, or replaces its value; unchanged when it already matches.</summary>
    public static AgentConfigEdit Upsert(byte[]? content, AgentClientFormat format, JsonObject entry)
    {
        if (content is not null && content.Length > MaxFileBytes) return Refused(AgentConfigProblem.TooLarge);
        var document = Load(content, format);
        if (document.Problem != AgentConfigProblem.None) return Refused(document.Problem);

        byte[] edited;
        if (document.Layout is not { } layout)
        {
            // Absent or blank file: a new document holding only the entry.
            var fresh = new JsonObject
            {
                [format.ContainerKey] = new JsonObject { [AgentClientCatalog.EntryName] = entry.DeepClone() }
            };
            edited = Encoding.UTF8.GetBytes(Serialize(fresh, "", "  ", "\n", pretty: true) + "\n");
        }
        else if (layout is { Container: not null, Entry: { } existing })
        {
            var current = ParseNode(layout.Text.AsSpan(existing.ValueStart, existing.ValueEnd - existing.ValueStart), format);
            if (JsonNode.DeepEquals(current, entry)) return new(AgentConfigProblem.None, content, false);

            var (atLineStart, indent) = LineIndent(layout.Text, existing.NameStart);
            edited = layout.Splice(existing.ValueStart, existing.ValueEnd,
                Serialize(entry, indent, layout.Unit, layout.NewLine, atLineStart));
        }
        else if (layout.Container is { } container)
        {
            edited = InsertMember(layout, container, layout.ContainerMember!.NameStart, AgentClientCatalog.EntryName, entry);
        }
        else
        {
            var wrapped = new JsonObject { [AgentClientCatalog.EntryName] = entry.DeepClone() };
            edited = InsertMember(layout, layout.Root, null, format.ContainerKey, wrapped);
        }

        return OnlyEntryDiffers(content, edited, format, entry)
            ? new(AgentConfigProblem.None, edited, true)
            : Refused(AgentConfigProblem.VerificationFailed);
    }

    /// <summary>Removes the <c>netrisk</c> entry only; the servers object stays, even when left empty.</summary>
    public static AgentConfigEdit Remove(byte[]? content, AgentClientFormat format)
    {
        if (content is not null && content.Length > MaxFileBytes) return Refused(AgentConfigProblem.TooLarge);
        var document = Load(content, format);
        if (document.Problem != AgentConfigProblem.None) return Refused(document.Problem);
        if (document.Layout is not { Container: { } container, Entry: { } entry } layout)
            return new(AgentConfigProblem.None, content, false);

        var text = layout.Text;
        var index = container.Members.IndexOf(entry);
        var next = layout.NextSignificant(entry.ValueEnd);
        var lineStart = LineStartIncludingBreak(text, entry.NameStart);
        var edits = new List<(int, int, string)>();
        if (next < text.Length && text[next] == (byte)',')
        {
            // Not the last member: the entry and its comma go, with the line break before it.
            edits.Add((lineStart, next + 1, ""));
        }
        else if (index > 0)
        {
            // The last of several: the comma before it goes too, so no trailing comma is left behind. A comment between
            // that comma and the entry belongs to the line above and stays.
            var comma = layout.CommaBetween(container.Members[index - 1].ValueEnd, entry.NameStart);
            if (comma < 0) return Refused(AgentConfigProblem.Unparseable);
            if (comma < lineStart)
            {
                edits.Add((comma, comma + 1, ""));
                edits.Add((lineStart, entry.ValueEnd, ""));
            }
            else
            {
                edits.Add((comma, entry.ValueEnd, ""));
            }
        }
        else
        {
            edits.Add((lineStart, entry.ValueEnd, ""));
        }

        var edited = layout.Splice(edits);
        return OnlyEntryDiffers(content, edited, format, null)
            ? new(AgentConfigProblem.None, edited, true)
            : Refused(AgentConfigProblem.VerificationFailed);
    }

    /// <summary>
    /// Whether <paramref name="after"/> differs from <paramref name="before"/> in the <c>netrisk</c> entry alone: same
    /// top-level members and values except the servers object, same servers except <c>netrisk</c>, and <c>netrisk</c>
    /// equal to <paramref name="entry"/> (absent when null). The last check before every write.
    /// </summary>
    public static bool OnlyEntryDiffers(byte[]? before, byte[]? after, AgentClientFormat format, JsonObject? entry)
    {
        try
        {
            var oldRoot = (IsBlank(before) ? new JsonObject() : ParseNode(StripBom(before!), format)) as JsonObject;
            var newRoot = (IsBlank(after) ? new JsonObject() : ParseNode(StripBom(after!), format)) as JsonObject;
            if (oldRoot is null || newRoot is null) return false;
            if (!SameMembersExcept(oldRoot, newRoot, format.ContainerKey)) return false;

            var oldServers = oldRoot[format.ContainerKey] as JsonObject ?? new JsonObject();
            if (newRoot[format.ContainerKey] is not JsonObject newServers)
                return entry is null && !oldRoot.ContainsKey(format.ContainerKey) && !newRoot.ContainsKey(format.ContainerKey);
            if (!SameMembersExcept(oldServers, newServers, AgentClientCatalog.EntryName)) return false;

            return entry is null
                ? !newServers.ContainsKey(AgentClientCatalog.EntryName)
                : JsonNode.DeepEquals(newServers[AgentClientCatalog.EntryName], entry);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The change as a line diff: removed and added lines with their line number and no context (neighbouring lines
    /// are other servers' settings, which can hold their tokens). Long lines are cut and the length is bounded.
    /// </summary>
    public static string Diff(byte[]? before, byte[]? after, string label, int maxLines = 120, int maxLineLength = 240)
    {
        var a = Lines(before);
        var b = Lines(after);
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
            suffix++;

        var removed = a.Skip(prefix).Take(a.Count - prefix - suffix).ToList();
        var added = b.Skip(prefix).Take(b.Count - prefix - suffix).ToList();

        var output = new StringBuilder();
        output.Append("--- ").Append(label).Append('\n');
        output.Append("+++ ").Append(label).Append('\n');
        if (removed.Count == 0 && added.Count == 0) return output.ToString();
        output.Append($"@@ -{prefix + 1},{removed.Count} +{prefix + 1},{added.Count} @@\n");

        var lines = removed.Select(l => "-" + Clip(l, maxLineLength))
            .Concat(added.Select(l => "+" + Clip(l, maxLineLength))).ToList();
        foreach (var line in lines.Take(maxLines)) output.Append(line).Append('\n');
        if (lines.Count > maxLines) output.Append($"… {lines.Count - maxLines} more lines\n");
        return output.ToString();
    }

    // ---- parsing ---------------------------------------------------------------------------------------------------

    private sealed class Member
    {
        public required string Name { get; init; }
        public required int NameStart { get; init; }
        public required int ValueStart { get; init; }
        public required int ValueEnd { get; init; }
    }

    private sealed class ObjectSpan
    {
        public required int Open { get; init; }
        public int Close { get; set; }
        public List<Member> Members { get; } = new();
    }

    private sealed class Layout
    {
        public required bool HasBom { get; init; }
        public required byte[] Text { get; init; }
        public required ObjectSpan Root { get; init; }
        public Member? ContainerMember { get; init; }
        public ObjectSpan? Container { get; init; }
        public Member? Entry { get; init; }
        public required List<(int Start, int End)> Comments { get; init; }
        public required string NewLine { get; init; }
        public required string Unit { get; init; }

        public byte[] Splice(int start, int end, string replacement) => Splice([(start, end, replacement)]);

        /// <summary>Replaces non-overlapping spans of the text; the BOM, if any, is kept in front.</summary>
        public byte[] Splice(IEnumerable<(int Start, int End, string Replacement)> edits)
        {
            using var result = new MemoryStream(Text.Length + 1024);
            if (HasBom) result.Write(Bom);
            var at = 0;
            foreach (var (start, end, replacement) in edits.OrderBy(e => e.Start))
            {
                if (start < at || end < start) throw new ArgumentException("Overlapping edits.");
                result.Write(Text, at, start - at);
                result.Write(Encoding.UTF8.GetBytes(replacement));
                at = end;
            }
            result.Write(Text, at, Text.Length - at);
            return result.ToArray();
        }

        /// <summary>The first byte at or after <paramref name="from"/> that is neither whitespace nor in a comment.</summary>
        public int NextSignificant(int from)
        {
            var i = from;
            while (i < Text.Length)
            {
                if (IsWhitespace(Text[i])) i++;
                else if (CommentAt(i) is { } comment) i = comment.End;
                else break;
            }
            return i;
        }

        /// <summary>The separating comma between two members, outside comments; -1 if none.</summary>
        public int CommaBetween(int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                if (CommentAt(i) is { } comment)
                {
                    i = comment.End - 1;
                    continue;
                }
                if (Text[i] == (byte)',') return i;
            }
            return -1;
        }

        private (int Start, int End)? CommentAt(int position)
        {
            foreach (var comment in Comments)
                if (position >= comment.Start && position < comment.End) return comment;
            return null;
        }
    }

    private sealed record Loaded(AgentConfigProblem Problem, Layout? Layout);

    private static Loaded Load(byte[]? content, AgentClientFormat format)
    {
        if (IsBlank(content)) return new(AgentConfigProblem.None, null);

        var bytes = content!;
        var hasBom = bytes.AsSpan().StartsWith(Bom);
        var text = hasBom ? bytes[Bom.Length..] : bytes;
        var comments = new List<(int, int)>();
        var reader = new Utf8JsonReader(text, new JsonReaderOptions
        {
            CommentHandling = format.Jsonc ? JsonCommentHandling.Allow : JsonCommentHandling.Disallow,
            AllowTrailingCommas = format.Jsonc,
            MaxDepth = 256
        });

        try
        {
            // Only comments and no value: not a document a client can read, and not ours to replace.
            if (!ReadSignificant(ref reader, comments)) return new(AgentConfigProblem.Unparseable, null);
            if (reader.TokenType != JsonTokenType.StartObject) return new(AgentConfigProblem.RootNotAnObject, null);

            var root = new ObjectSpan { Open = (int)reader.TokenStartIndex };
            Member? containerMember = null;
            ObjectSpan? container = null;
            var containerCount = 0;
            while (true)
            {
                ReadSignificant(ref reader, comments);
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    root.Close = (int)reader.TokenStartIndex;
                    break;
                }

                var name = reader.GetString()!;
                var nameStart = (int)reader.TokenStartIndex;
                ReadSignificant(ref reader, comments);
                var valueStart = (int)reader.TokenStartIndex;
                var isContainer = string.Equals(name, format.ContainerKey, StringComparison.Ordinal);
                if (isContainer) containerCount++;
                var first = isContainer && containerCount == 1;

                if (first && reader.TokenType == JsonTokenType.StartObject) container = ReadMembers(ref reader, comments);
                else if (first) return new(AgentConfigProblem.ContainerNotAnObject, null);
                else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();

                var member = new Member
                {
                    Name = name, NameStart = nameStart, ValueStart = valueStart, ValueEnd = (int)reader.BytesConsumed
                };
                root.Members.Add(member);
                if (first) containerMember = member;
            }

            if (ReadSignificant(ref reader, comments)) return new(AgentConfigProblem.Unparseable, null);
            if (containerCount > 1) return new(AgentConfigProblem.DuplicateContainer, null);

            var entries = container?.Members.Where(m => m.Name == AgentClientCatalog.EntryName).ToList() ?? [];
            if (entries.Count > 1) return new(AgentConfigProblem.DuplicateEntry, null);

            return new(AgentConfigProblem.None, new Layout
            {
                HasBom = hasBom,
                Text = text,
                Root = root,
                ContainerMember = containerMember,
                Container = container,
                Entry = entries.SingleOrDefault(),
                Comments = comments,
                NewLine = text.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n",
                Unit = IndentUnit(text, root)
            });
        }
        catch (JsonException)
        {
            return new(AgentConfigProblem.Unparseable, null);
        }
    }

    /// <summary>Reads the members of the object the reader is on (its <c>{</c>), skipping their values.</summary>
    private static ObjectSpan ReadMembers(ref Utf8JsonReader reader, List<(int, int)> comments)
    {
        var span = new ObjectSpan { Open = (int)reader.TokenStartIndex };
        while (true)
        {
            ReadSignificant(ref reader, comments);
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                span.Close = (int)reader.TokenStartIndex;
                return span;
            }

            var name = reader.GetString()!;
            var nameStart = (int)reader.TokenStartIndex;
            ReadSignificant(ref reader, comments);
            var valueStart = (int)reader.TokenStartIndex;
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
            span.Members.Add(new Member
            {
                Name = name, NameStart = nameStart, ValueStart = valueStart, ValueEnd = (int)reader.BytesConsumed
            });
        }
    }

    private static bool ReadSignificant(ref Utf8JsonReader reader, List<(int, int)> comments)
    {
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.Comment) return true;
            comments.Add(((int)reader.TokenStartIndex, (int)reader.BytesConsumed));
        }
        return false;
    }

    private static JsonNode? ParseNode(ReadOnlySpan<byte> json, AgentClientFormat format) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = format.Jsonc ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
            AllowTrailingCommas = format.Jsonc,
            MaxDepth = 256
        });

    // ---- editing ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Inserts <c>"name": value</c> as the first member of <paramref name="target"/> in the file's own style: on its own
    /// line with the indentation of the members already there, or compact in a one-line file.
    /// </summary>
    private static byte[] InsertMember(Layout layout, ObjectSpan target, int? targetNameStart, string name, JsonNode value)
    {
        var text = layout.Text;
        var key = JsonValue.Create(name)!.ToJsonString();
        if (target.Members.Count > 0)
        {
            var (atLineStart, indent) = LineIndent(text, target.Members[0].NameStart);
            var member = atLineStart
                ? layout.NewLine + indent + key + ": " + Serialize(value, indent, layout.Unit, layout.NewLine, true) + ","
                : key + ":" + Serialize(value, "", layout.Unit, layout.NewLine, false) + ",";
            return layout.Splice(target.Open + 1, target.Open + 1, member);
        }

        // An empty object: one level deeper than the line it opens on.
        var (openAtLineStart, openIndent) = targetNameStart is { } start ? LineIndent(text, start) : (true, "");
        if (!openAtLineStart)
            return layout.Splice(target.Open + 1, target.Open + 1,
                key + ":" + Serialize(value, "", layout.Unit, layout.NewLine, false));

        var memberIndent = openIndent + layout.Unit;
        var line = layout.NewLine + memberIndent + key + ": " +
                   Serialize(value, memberIndent, layout.Unit, layout.NewLine, true);
        return IsAllWhitespace(text.AsSpan(target.Open + 1, target.Close - target.Open - 1))
            ? layout.Splice(target.Open + 1, target.Close, line + layout.NewLine + openIndent)
            : layout.Splice(target.Open + 1, target.Open + 1, line);
    }

    private static string Serialize(JsonNode value, string baseIndent, string unit, string newLine, bool pretty)
    {
        var uniform = unit.Length is > 0 and <= 127 && unit.All(c => c == unit[0]) && unit[0] is ' ' or '\t';
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = pretty,
                   IndentCharacter = uniform ? unit[0] : ' ',
                   IndentSize = uniform ? unit.Length : 2,
                   NewLine = newLine,
                   // A local configuration file, not HTML: keep a path such as /Users/João readable. Quotes,
                   // backslashes and control characters are still escaped.
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            value.WriteTo(writer);
        }
        var json = Encoding.UTF8.GetString(stream.ToArray());
        // A JSON string never holds a raw line break, so every break here is the writer's own.
        return pretty ? json.Replace(newLine, newLine + baseIndent, StringComparison.Ordinal) : json;
    }

    // ---- text helpers ----------------------------------------------------------------------------------------------

    /// <summary>Whether the token at <paramref name="position"/> starts its line, and the whitespace before it.</summary>
    private static (bool AtLineStart, string Indent) LineIndent(byte[] text, int position)
    {
        var i = position - 1;
        while (i >= 0 && text[i] is (byte)' ' or (byte)'\t') i--;
        if (i >= 0 && text[i] != (byte)'\n') return (false, "");
        return (true, Encoding.UTF8.GetString(text, i + 1, position - i - 1));
    }

    /// <summary>Where a member's line begins, with the line break before it, when nothing else is on that line.</summary>
    private static int LineStartIncludingBreak(byte[] text, int position)
    {
        var i = position - 1;
        while (i >= 0 && text[i] is (byte)' ' or (byte)'\t') i--;
        if (i < 0 || text[i] != (byte)'\n') return position;
        return i > 0 && text[i - 1] == (byte)'\r' ? i - 1 : i;
    }

    /// <summary>The indentation of one level: that of the first top-level member, or two spaces.</summary>
    private static string IndentUnit(byte[] text, ObjectSpan root)
    {
        if (root.Members.Count == 0) return "  ";
        var (atLineStart, indent) = LineIndent(text, root.Members[0].NameStart);
        return atLineStart && indent.Length > 0 ? indent : "  ";
    }

    private static bool SameMembersExcept(JsonObject before, JsonObject after, string except)
    {
        var oldKeys = before.Select(p => p.Key).Where(k => k != except).ToList();
        var newKeys = after.Select(p => p.Key).Where(k => k != except).ToList();
        return oldKeys.SequenceEqual(newKeys, StringComparer.Ordinal)
               && oldKeys.All(k => JsonNode.DeepEquals(before[k], after[k]));
    }

    private static List<string> Lines(byte[]? content)
    {
        if (content is null || content.Length == 0) return [];
        return Encoding.UTF8.GetString(StripBom(content)).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
    }

    private static string Clip(string line, int max) =>
        line.Length <= max ? line : line[..max] + $"… ({line.Length - max} more characters)";

    private static byte[] StripBom(byte[] content) =>
        content.AsSpan().StartsWith(Bom) ? content[Bom.Length..] : content;

    private static bool IsBlank(byte[]? content) => content is null || IsAllWhitespace(StripBom(content));

    private static bool IsAllWhitespace(ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
            if (!IsWhitespace(b)) return false;
        return true;
    }

    private static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static AgentConfigEdit Refused(AgentConfigProblem problem) => new(problem, null, false);
}
