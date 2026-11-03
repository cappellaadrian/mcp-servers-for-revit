// Revara fork. The "auth" member is adapted from mcp-servers-for-revit PR #60 (commit a4ada37), MIT License.
// See LICENSE and NOTICE.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// What the socket layer decides about one request line before any command runs: the session token check,
    /// request validation and the two methods it answers itself (get_version and health). No Revit types here.
    /// </summary>
    public static class BridgeProtocol
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int InternalError = -32603;
        public const int Unauthorized = -32001;

        public const string UnauthorizedMessage = "Unauthorized: the request did not include this Revit session's token.";

        public class Outcome
        {
            /// <summary>The serialized response line, or null when the request must go to the command registry.</summary>
            public string Response { get; set; }

            /// <summary>True when the connection must be closed after sending Response.</summary>
            public bool Close { get; set; }

            /// <summary>The parsed request object, or null when the line was not a JSON object.</summary>
            public JObject Request { get; set; }

            /// <summary>The request's "id" when it is a JSON value, else null.</summary>
            public JToken Id { get; set; }
        }

        public static JObject VersionResult(string version, string revit)
        {
            var result = new JObject();
            result["name"] = "revara-mcp";
            result["version"] = version;
            result["revit"] = revit;
            result["protocol"] = 1;
            return result;
        }

        public static JObject HealthResult(long uptimeMs, int commands)
        {
            var result = new JObject();
            result["status"] = "ok";
            result["uptime_ms"] = uptimeMs;
            result["commands"] = commands;
            return result;
        }

        /// <summary>
        /// Never throws.
        /// </summary>
        public static Outcome Handle(string line, string token, Func<JObject> versionResult, Func<JObject> healthResult)
        {
            var outcome = new Outcome();
            bool authProven = false;

            try
            {
                JObject obj;
                try
                {
                    obj = JToken.Parse(line) as JObject;
                }
                catch (JsonException)
                {
                    obj = null;
                }

                if (obj == null)
                {
                    outcome.Response = Error(null, ParseError, "Invalid JSON");
                    outcome.Close = true;
                    return outcome;
                }

                outcome.Request = obj;
                var idValue = obj["id"] as JValue;
                outcome.Id = idValue == null ? null : idValue.DeepClone();

                string presented = (obj["auth"] as JValue)?.Value as string;
                if (!SessionToken.Matches(token, presented))
                {
                    outcome.Response = Error(outcome.Id, Unauthorized, UnauthorizedMessage);
                    outcome.Close = true;
                    return outcome;
                }
                authProven = true;

                string jsonrpc = (obj["jsonrpc"] as JValue)?.Value as string;
                string method = (obj["method"] as JValue)?.Value as string;
                if (jsonrpc != "2.0" || string.IsNullOrEmpty(method))
                {
                    outcome.Response = Error(outcome.Id, InvalidRequest, "Invalid JSON-RPC request");
                    return outcome;
                }

                if (method == "get_version")
                {
                    outcome.Response = Success(outcome.Id, versionResult());
                    return outcome;
                }
                if (method == "health")
                {
                    outcome.Response = Success(outcome.Id, healthResult());
                    return outcome;
                }

                return outcome;
            }
            catch (Exception ex)
            {
                if (!authProven)
                {
                    outcome.Response = Error(outcome.Id, Unauthorized, UnauthorizedMessage);
                    outcome.Close = true;
                }
                else
                {
                    outcome.Response = Error(outcome.Id, InternalError, "Internal error: " + ex.Message);
                    outcome.Close = false;
                }
                return outcome;
            }
        }

        private static string Success(JToken id, JToken result)
        {
            var response = new JObject();
            response["jsonrpc"] = "2.0";
            response["id"] = id == null ? JValue.CreateNull() : id.DeepClone();
            response["result"] = result;
            return response.ToString(Formatting.None);
        }

        private static string Error(JToken id, int code, string message)
        {
            var error = new JObject();
            error["code"] = code;
            error["message"] = message;

            var response = new JObject();
            response["jsonrpc"] = "2.0";
            response["id"] = id == null ? JValue.CreateNull() : id.DeepClone();
            response["error"] = error;
            return response.ToString(Formatting.None);
        }
    }
}
