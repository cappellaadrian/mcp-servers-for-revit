// Revara fork: tests of the session token, the connection file, the port binder and the request checks.
// Written in C# 7.3 because the plugin files they cover also build for net48.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Core;
using Xunit;
using Xunit.Abstractions;

// No namespace, like MessageFramerTests.cs: a namespace called MessageFramer would hide the class of that name.
    [CollectionDefinition("ports", DisableParallelization = true)]
    public class PortsCollection
    {
    }

    public class SessionTokenTests
    {
        private static readonly string T0 =
            "0123456789ABCDEF" + "FEDCBA9876543210" + "00112233445566FF" + "AABBCCDDEEFF0099";

        [Fact]
        public void P1_Generate_is_64_uppercase_hex_and_differs_each_call()
        {
            string a = SessionToken.Generate();
            string b = SessionToken.Generate();
            Assert.Matches("^[0-9A-F]{64}$", a);
            Assert.Matches("^[0-9A-F]{64}$", b);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void P2_Matches_accepts_only_the_exact_token()
        {
            Assert.Equal(64, T0.Length);
            Assert.True(SessionToken.Matches(T0, T0));
            Assert.False(SessionToken.Matches(null, null));
            Assert.False(SessionToken.Matches(T0, null));
            Assert.False(SessionToken.Matches(null, T0));
            Assert.False(SessionToken.Matches(T0, ""));
            Assert.False(SessionToken.Matches(T0, T0.Substring(0, 63)));
            Assert.False(SessionToken.Matches(T0, "1" + T0.Substring(1)));
            Assert.False(SessionToken.Matches(T0, T0.Substring(0, 63) + "0"));
            Assert.False(SessionToken.Matches(T0, T0.ToLowerInvariant()));
        }
    }

    public class ConnectionInfoFileTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "revara-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (Exception)
            {
            }
        }

        private static string Token(char c)
        {
            return new string(c, 64);
        }

        [Fact]
        public void P3_Write_creates_the_folder_and_a_BOM_free_two_member_file()
        {
            string folder = Path.Combine(_root, "not", "there", "yet");
            string path = Path.Combine(folder, "connection.json");
            Assert.False(Directory.Exists(folder));

            ConnectionInfoFile.Write(path, 38081, Token('A'));

            Assert.True(Directory.Exists(folder));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'{', bytes[0]);

            JObject obj = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            Assert.Equal(2, obj.Properties().Count());
            Assert.Equal(JTokenType.Integer, obj["port"].Type);
            Assert.Equal(38081, (int)obj["port"]);
            Assert.Equal(JTokenType.String, obj["token"].Type);
            Assert.Equal(Token('A'), (string)obj["token"]);
            Assert.False(File.Exists(path + ".tmp"));

            ConnectionInfoFile.Write(path, 38085, Token('B'));

            JObject second = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(2, second.Properties().Count());
            Assert.Equal(38085, (int)second["port"]);
            Assert.Equal(Token('B'), (string)second["token"]);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void P3b_DefaultPath_is_LocalAppData_Revara_connection_json()
        {
            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Revara", "connection.json");
            Assert.Equal(expected, ConnectionInfoFile.DefaultPath);
        }

        [Fact]
        public void P4_DeleteIfOwned_deletes_only_its_own_file_and_never_throws()
        {
            Directory.CreateDirectory(_root);
            string path = Path.Combine(_root, "connection.json");

            ConnectionInfoFile.Write(path, 38080, Token('A'));
            ConnectionInfoFile.DeleteIfOwned(path, Token('A'));
            Assert.False(File.Exists(path));

            ConnectionInfoFile.Write(path, 38080, Token('B'));
            ConnectionInfoFile.DeleteIfOwned(path, Token('A'));
            Assert.True(File.Exists(path));

            File.Delete(path);
            ConnectionInfoFile.DeleteIfOwned(path, Token('A'));
            Assert.False(File.Exists(path));

            File.WriteAllText(path, "this is not json");
            ConnectionInfoFile.DeleteIfOwned(path, Token('A'));
            Assert.True(File.Exists(path));

            File.WriteAllText(path, "{\"port\":1}");
            ConnectionInfoFile.DeleteIfOwned(path, null);
            Assert.True(File.Exists(path));
        }
    }

    [Collection("ports")]
    public class PortBinderTests
    {
        private const int First = 38080;
        private const int Last = 38089;

        private readonly ITestOutputHelper _output;

        public PortBinderTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static int PortOf(TcpListener listener)
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static bool CanBind(int port)
        {
            var probe = new TcpListener(IPAddress.Loopback, port);
            try
            {
                probe.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                try { probe.Stop(); } catch (Exception) { }
            }
        }

        [Fact]
        public void P5_BindFirstFree_returns_a_started_loopback_listener_in_the_range()
        {
            TcpListener listener = PortBinder.BindFirstFree(First, Last);
            try
            {
                int port = PortOf(listener);
                Assert.InRange(port, First, Last);
                Assert.Equal(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Address);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void P5_BindFirstFree_skips_a_port_that_is_taken()
        {
            TcpListener held = null;
            TcpListener bound = null;
            try
            {
                int heldPort = 0;
                for (int p = First; p <= Last && held == null; p++)
                {
                    var candidate = new TcpListener(IPAddress.Loopback, p);
                    try
                    {
                        candidate.Start();
                        held = candidate;
                        heldPort = p;
                    }
                    catch (SocketException)
                    {
                    }
                }
                Assert.NotNull(held);

                int expected = -1;
                for (int p = heldPort + 1; p <= Last; p++)
                {
                    if (CanBind(p))
                    {
                        expected = p;
                        break;
                    }
                }
                Assert.True(expected > 0, "no other port of the range could be bound by the test");

                bound = PortBinder.BindFirstFree(First, Last);
                int got = PortOf(bound);
                Assert.InRange(got, First, Last);
                Assert.NotEqual(heldPort, got);
                Assert.Equal(expected, got);
            }
            finally
            {
                if (bound != null) bound.Stop();
                if (held != null) held.Stop();
            }
        }

        [Fact]
        public void P5_BindFirstFree_throws_naming_the_range_when_every_port_is_taken()
        {
            var held = new System.Collections.Generic.List<TcpListener>();
            try
            {
                for (int p = First; p <= Last; p++)
                {
                    var candidate = new TcpListener(IPAddress.Loopback, p);
                    try
                    {
                        candidate.Start();
                        held.Add(candidate);
                    }
                    catch (SocketException)
                    {
                    }
                }
                _output.WriteLine("ports held by the test: " + held.Count);

                var ex = Assert.Throws<InvalidOperationException>(() => PortBinder.BindFirstFree(First, Last));
                Assert.Contains("38080", ex.Message);
                Assert.Contains("38089", ex.Message);
            }
            finally
            {
                foreach (TcpListener l in held)
                {
                    l.Stop();
                }
            }
        }
    }

    public class BridgeProtocolHandleTests
    {
        private static readonly string T0 =
            "0123456789ABCDEF" + "FEDCBA9876543210" + "00112233445566FF" + "AABBCCDDEEFF0099";

        private static readonly JObject Version = BridgeProtocol.VersionResult("9.9.9", "2025");
        private static readonly JObject Health = BridgeProtocol.HealthResult(42, 7);

        private static BridgeProtocol.Outcome Run(string line, string token)
        {
            return BridgeProtocol.Handle(line, token, () => Version, () => Health);
        }

        private static JToken ErrorResponse(JToken id, int code, string message)
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id ?? JValue.CreateNull(),
                ["error"] = new JObject { ["code"] = code, ["message"] = message },
            };
        }

        private static JToken Unauthorized(JToken id)
        {
            return ErrorResponse(id, -32001,
                "Unauthorized: the request did not include this Revit session's token.");
        }

        private static void AssertResponse(JToken expected, BridgeProtocol.Outcome outcome, bool close)
        {
            Assert.NotNull(outcome.Response);
            JToken actual = JToken.Parse(outcome.Response);
            Assert.True(JToken.DeepEquals(expected, actual),
                "expected " + expected.ToString(Newtonsoft.Json.Formatting.None) + " but got " + outcome.Response);
            Assert.Equal(close, outcome.Close);
            Assert.DoesNotContain("\n", outcome.Response);
        }

        private static string Req(string extra)
        {
            return "{\"jsonrpc\":\"2.0\"," + extra + "}";
        }

        [Fact]
        public void P6_no_auth_is_unauthorized_and_closes()
        {
            AssertResponse(Unauthorized(new JValue("17")),
                Run(Req("\"method\":\"say_hello\",\"params\":{},\"id\":\"17\""), T0), true);
        }

        [Fact]
        public void P6_wrong_auth_of_the_right_length_is_unauthorized()
        {
            string wrong = T0.Substring(0, 63) + "0";
            AssertResponse(Unauthorized(new JValue("17")),
                Run(Req("\"method\":\"say_hello\",\"params\":{},\"id\":\"17\",\"auth\":\"" + wrong + "\""), T0), true);
        }

        [Fact]
        public void P6_service_not_started_rejects_even_the_right_auth()
        {
            string line = Req("\"method\":\"say_hello\",\"id\":\"17\",\"auth\":\"" + T0 + "\"");
            AssertResponse(Unauthorized(new JValue("17")), Run(line, null), true);
        }

        [Fact]
        public void P6_token_null_and_no_auth_is_unauthorized()
        {
            AssertResponse(Unauthorized(new JValue("17")),
                Run(Req("\"method\":\"say_hello\",\"id\":\"17\""), null), true);
        }

        [Fact]
        public void P6_builtins_need_auth_first()
        {
            AssertResponse(Unauthorized(new JValue("17")),
                Run(Req("\"method\":\"get_version\",\"id\":\"17\""), T0), true);
            AssertResponse(Unauthorized(new JValue("17")),
                Run(Req("\"method\":\"health\",\"id\":\"17\""), T0), true);
        }

        [Fact]
        public void P6_builtins_answer_with_auth()
        {
            string auth = "\"auth\":\"" + T0 + "\"";

            BridgeProtocol.Outcome v = Run(Req("\"method\":\"get_version\",\"params\":{},\"id\":\"17\"," + auth), T0);
            AssertResponse(new JObject { ["jsonrpc"] = "2.0", ["id"] = "17", ["result"] = Version.DeepClone() }, v, false);

            BridgeProtocol.Outcome h = Run(Req("\"method\":\"health\",\"id\":\"18\"," + auth), T0);
            AssertResponse(new JObject { ["jsonrpc"] = "2.0", ["id"] = "18", ["result"] = Health.DeepClone() }, h, false);
        }

        [Fact]
        public void P6_missing_method_is_InvalidRequest_with_the_id()
        {
            string line = "{\"jsonrpc\":\"2.0\",\"id\":\"17\",\"auth\":\"" + T0 + "\"}";
            AssertResponse(ErrorResponse(new JValue("17"), -32600, "Invalid JSON-RPC request"), Run(line, T0), false);
        }

        [Fact]
        public void P6_wrong_jsonrpc_or_empty_method_is_InvalidRequest_with_the_id()
        {
            string auth = ",\"auth\":\"" + T0 + "\"";
            AssertResponse(ErrorResponse(new JValue("17"), -32600, "Invalid JSON-RPC request"),
                Run("{\"jsonrpc\":\"1.0\",\"method\":\"say_hello\",\"id\":\"17\"" + auth + "}", T0), false);
            AssertResponse(ErrorResponse(new JValue("17"), -32600, "Invalid JSON-RPC request"),
                Run(Req("\"method\":\"\",\"id\":\"17\"" + auth), T0), false);
        }

        [Fact]
        public void P6_other_methods_go_to_the_registry()
        {
            string line = Req("\"method\":\"say_hello\",\"params\":{},\"id\":\"17\",\"auth\":\"" + T0 + "\"");
            BridgeProtocol.Outcome outcome = Run(line, T0);
            Assert.Null(outcome.Response);
            Assert.False(outcome.Close);
            Assert.NotNull(outcome.Request);
            Assert.Equal("say_hello", (string)outcome.Request["method"]);
            Assert.Equal("17", (string)outcome.Id);
        }

        [Fact]
        public void P6_not_JSON_and_non_object_JSON_are_ParseError_with_null_id()
        {
            JToken parseError = ErrorResponse(null, -32700, "Invalid JSON");
            AssertResponse(parseError, Run("not json", T0), true);
            AssertResponse(parseError, Run("[1,2]", T0), true);
        }

        [Fact]
        public void P6_auth_comes_before_validation()
        {
            AssertResponse(Unauthorized(null), Run("{\"jsonrpc\":\"2.0\",\"method\":\"say_hello\"}", T0), true);
            AssertResponse(Unauthorized(new JValue("17")), Run("{\"id\":\"17\"}", T0), true);
        }

        [Fact]
        public void P6_a_numeric_id_comes_back_as_a_number()
        {
            AssertResponse(Unauthorized(new JValue(17)),
                Run(Req("\"method\":\"say_hello\",\"id\":17"), T0), true);
        }

        [Fact]
        public void P6_auth_that_is_not_a_string_is_unauthorized_and_does_not_throw()
        {
            AssertResponse(Unauthorized(new JValue("17")),
                Run(Req("\"method\":\"say_hello\",\"id\":\"17\",\"auth\":123"), T0), true);
            AssertResponse(Unauthorized(new JValue("17")),
                Run(Req("\"method\":\"say_hello\",\"id\":\"17\",\"auth\":{}"), T0), true);
        }
    }

    public class ResultShapeTests
    {
        [Fact]
        public void P7_VersionResult_has_exactly_the_agreed_shape()
        {
            JObject actual = BridgeProtocol.VersionResult("1.0.0-revara.3", "2025");
            JToken expected = JToken.Parse(
                "{\"name\":\"revara-mcp\",\"version\":\"1.0.0-revara.3\",\"revit\":\"2025\",\"protocol\":1}");
            Assert.True(JToken.DeepEquals(expected, actual), actual.ToString());
            Assert.Equal(JTokenType.Integer, actual["protocol"].Type);
            Assert.Equal(JTokenType.String, actual["revit"].Type);
        }

        [Fact]
        public void P7_HealthResult_has_exactly_the_agreed_shape()
        {
            JObject actual = BridgeProtocol.HealthResult(1234, 5);
            JToken expected = JToken.Parse("{\"status\":\"ok\",\"uptime_ms\":1234,\"commands\":5}");
            Assert.True(JToken.DeepEquals(expected, actual), actual.ToString());
            Assert.Equal(JTokenType.Integer, actual["uptime_ms"].Type);
            Assert.Equal(JTokenType.Integer, actual["commands"].Type);
        }
    }
