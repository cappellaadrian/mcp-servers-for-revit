// Revara fork: what the plugin's socket layer has to do with the requests Revara's MCP server sends.
// The requests here are synthetic but shaped like the real ones: one JSON-RPC send_code_to_revit object
// with no line break at the end, C# code with quotes, line breaks and accented letters escaped in JSON,
// and a JSON payload as parameter, at the sizes measured for Revara's 10 golden houses (20074 to 29226
// bytes, each longer than the plugin's 8192-byte read). The real bytes are tested in Revara's own repo.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Core;
using Xunit;

public class MessageFramerTests
{
    private const int ReadBytes = 8192;           // the plugin's buffer (SocketService.cs)
    private const int Limit = 16 * 1024 * 1024;   // MaxMessageLength in SocketService.cs
    private const string Heartbeat = "{\"jsonrpc\":\"2.0\",\"method\":\"send_code_to_revit\",\"params\":{\"code\":\"return new { ok = true, ts = System.DateTime.Now.Ticks };\",\"parameters\":[]},\"id\":\"1\"}";

    private static readonly int[] MeasuredSizes = { 20074, 21222, 21594, 23108, 24528, 23616, 24695, 26908, 25325, 29226 };

    public static IEnumerable<object[]> Golden() =>
        Enumerable.Range(1, 10).Select(n => new object[] { $"casa-{n:00}" });

    // A request of exactly the measured size for that house.
    private static byte[] Fixture(string name)
    {
        int target = MeasuredSizes[int.Parse(name.Substring(5)) - 1];
        var code = new StringBuilder("public static object __rbl_build() {\n");
        string Request() => new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "send_code_to_revit",
            ["params"] = new JObject
            {
                ["code"] = code.ToString(),
                ["parameters"] = new JArray(new JObject { ["levels"] = new JArray("Nivel 1", "Ático"), ["note"] = "año {x}" }.ToString(Newtonsoft.Json.Formatting.None)),
            },
            ["id"] = "1761780000000123456",
        }.ToString(Newtonsoft.Json.Formatting.None);
        for (int i = 0; Encoding.UTF8.GetByteCount(Request()) < target; i++)
            code.Append($"    var w{i} = doc.GetElement(\"Muro {i}\"); // pared ñ {{ }}\n");
        while (Encoding.UTF8.GetByteCount(Request()) > target) code.Length--;
        while (Encoding.UTF8.GetByteCount(Request()) < target) code.Append(' ');
        byte[] bytes = Encoding.UTF8.GetBytes(Request());
        Assert.Equal(target, bytes.Length);
        return bytes;
    }

    // Feeds the bytes the way the plugin reads them: at most 8192 per Read.
    private static List<string> Feed(MessageFramer framer, byte[] bytes, int readBytes = ReadBytes)
    {
        var messages = new List<string>();
        for (int i = 0; i < bytes.Length; i += readBytes)
        {
            int count = Math.Min(readBytes, bytes.Length - i);
            var buffer = new byte[ReadBytes];
            Array.Copy(bytes, i, buffer, 0, count);
            messages.AddRange(framer.Append(buffer, count));
        }
        return messages;
    }

    [Theory]
    [MemberData(nameof(Golden))]
    public void A_golden_request_without_a_line_break_arrives_whole(string name)
    {
        byte[] bytes = Fixture(name);
        Assert.True(bytes.Length > ReadBytes, $"{name} weighs {bytes.Length} bytes");
        var messages = Feed(new MessageFramer(Limit), bytes);
        Assert.Single(messages);
        Assert.Equal(Encoding.UTF8.GetString(bytes), messages[0]);
        Assert.Equal("send_code_to_revit", (string)JObject.Parse(messages[0])["method"]);
    }

    [Theory]
    [MemberData(nameof(Golden))]
    public void A_golden_request_with_a_line_break_arrives_whole(string name)
    {
        byte[] bytes = Fixture(name).Concat(Encoding.UTF8.GetBytes("\n")).ToArray();
        var messages = Feed(new MessageFramer(Limit), bytes);
        Assert.Single(messages);
        Assert.Equal(Encoding.UTF8.GetString(Fixture(name)), messages[0]);
    }

    [Fact]
    public void A_build_and_a_heartbeat_in_the_same_read_are_two_messages_when_each_ends_in_a_line_break()
    {
        byte[] bytes = Fixture("casa-01").Concat(Encoding.UTF8.GetBytes("\n" + Heartbeat + "\n")).ToArray();
        // The last read holds the tail of the build and the whole heartbeat.
        var messages = Feed(new MessageFramer(Limit), bytes);
        Assert.Equal(2, messages.Count);
        Assert.Equal(Heartbeat, messages[1]);
    }

    [Fact]
    public void Without_line_breaks_a_build_and_a_heartbeat_in_the_same_read_become_one_message_that_is_not_json()
    {
        // Why the bridge ends every request with a line break: two unterminated objects together do not parse,
        // so nothing comes out until a line break, and then they come out as one line that is not JSON
        // (SocketService answers ParseError and closes the connection).
        byte[] bytes = Fixture("casa-01").Concat(Encoding.UTF8.GetBytes(Heartbeat)).ToArray();
        var framer = new MessageFramer(Limit);
        Assert.Empty(Feed(framer, bytes));
        var later = Feed(framer, Encoding.UTF8.GetBytes("\n"));
        Assert.Single(later);
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => JToken.Parse(later[0]));
    }

    [Fact]
    public void A_character_split_between_two_reads_survives()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"params\":{\"code\":\"// Nivel Ático, año\"}}\n");
        int split = Array.IndexOf(bytes, (byte)0xC3) + 1;   // inside the two bytes of "Á"
        var framer = new MessageFramer(Limit);
        var first = framer.Append(bytes.Take(split).ToArray(), split);
        var second = framer.Append(bytes.Skip(split).ToArray(), bytes.Length - split);
        Assert.Empty(first);
        Assert.Single(second);
        Assert.Equal("// Nivel Ático, año", (string)JObject.Parse(second[0])["params"]["code"]);
    }

    [Fact]
    public void A_prefix_that_ends_in_a_brace_is_not_taken_as_a_message()
    {
        var framer = new MessageFramer(Limit);
        byte[] bytes = Encoding.UTF8.GetBytes("{\"params\":{\"code\":\"x\"}");
        Assert.Empty(framer.Append(bytes, bytes.Length));
    }

    [Fact]
    public void A_message_over_the_limit_is_refused()
    {
        var framer = new MessageFramer(100);
        byte[] bytes = Encoding.UTF8.GetBytes("{\"code\":\"" + new string('x', 200));
        Assert.Throws<InvalidDataException>(() => framer.Append(bytes, bytes.Length));
    }

    [Fact]
    public void An_http_request_yields_its_request_line_before_its_body()
    {
        // A page's fetch() to 127.0.0.1 starts with HTTP lines. SocketService answers the first message that
        // is not JSON and closes the connection, so the body after the headers never runs.
        string http = "POST / HTTP/1.1\r\nHost: 127.0.0.1:8080\r\nContent-Type: text/plain\r\n\r\n" +
                      "{\"jsonrpc\":\"2.0\",\"method\":\"send_code_to_revit\",\"params\":{\"code\":\"return 1;\"},\"id\":\"x\"}";
        byte[] bytes = Encoding.UTF8.GetBytes(http);
        var messages = new MessageFramer(Limit).Append(bytes, bytes.Length);
        Assert.Equal("POST / HTTP/1.1", messages[0]);
        Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => JToken.Parse(messages[0]));
    }
}
