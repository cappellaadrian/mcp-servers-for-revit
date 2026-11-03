using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Models.JsonRPC;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;

namespace revit_mcp_plugin.Core
{
    public class SocketService
    {
        // Revara fork: from mcp-servers-for-revit PR #60 (a4ada37). Far larger than any real request;
        // it only stops a misbehaving client from exhausting memory.
        private const int MaxMessageLength = 16 * 1024 * 1024;

        private static SocketService _instance;
        private TcpListener _listener;
        private Thread _listenerThread;
        private bool _isRunning;
        // Revara fork: the port is whichever one of 38080 to 38089 PortBinder bound; the connection file tells the client.
        private int _port;
        private string _token;
        private readonly Stopwatch _uptime = new Stopwatch();
        private string _revitYear;
        private UIApplication _uiApp;
        private ICommandRegistry _commandRegistry;
        private ILogger _logger;
        private CommandExecutor _commandExecutor;

        public static SocketService Instance
        {
            get
            {
                if(_instance == null)
                    _instance = new SocketService();
                return _instance;
            }
        }

        private SocketService()
        {
            _commandRegistry = new RevitCommandRegistry();
            _logger = new Logger();
        }

        public bool IsRunning => _isRunning;

        public int Port
        {
            get => _port;
            set => _port = value;
        }

        // 初始化
        // Initialization.
        public void Initialize(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _revitYear = _uiApp.Application.VersionNumber;

            // 初始化事件管理器
            // Initialize ExternalEventManager
            ExternalEventManager.Instance.Initialize(uiApp, _logger);

            // 记录当前 Revit 版本
            // Get the current Revit version.
            var versionAdapter = new RevitMCPSDK.API.Utils.RevitVersionAdapter(_uiApp.Application);
            string currentVersion = versionAdapter.GetRevitVersion();
            _logger.Info("当前 Revit 版本: {0}\nCurrent Revit version: {0}", currentVersion);



            // 创建命令执行器
            // Create CommandExecutor
            _commandExecutor = new CommandExecutor(_commandRegistry, _logger);

            // 加载配置并注册命令
            // Load configuration and register commands.
            ConfigurationManager configManager = new ConfigurationManager(_logger);
            configManager.LoadConfiguration();
            

            // Revara fork: ServiceSettings.Port is not used; the port comes from PortBinder.BindFirstFree in Start().

            // 加载命令
            // Load command.
            CommandManager commandManager = new CommandManager(
                _commandRegistry, _logger, configManager, _uiApp);
            commandManager.LoadCommands();

            _logger.Info("Socket service initialized");
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _isRunning = true;
                // Revara fork, from PR #60: loopback only, because send_code_to_revit runs arbitrary C#
                // and nothing on the network may reach this port.
                _listener = PortBinder.BindFirstFree(38080, 38089);
                _port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
                _token = SessionToken.Generate();
                _uptime.Restart();

                // The file is written only after the bind, so a client never finds a file without a listener.
                try
                {
                    ConnectionInfoFile.Write(ConnectionInfoFile.DefaultPath, _port, _token);
                }
                catch (Exception ex)
                {
                    _listener.Stop();
                    _listener = null;
                    _isRunning = false;
                    _token = null;
                    throw new InvalidOperationException(
                        "Revara MCP could not write its connection file: " + ex.Message);
                }

                _listenerThread = new Thread(ListenForClients)
                {
                    IsBackground = true
                };
                _listenerThread.Start();              
            }
            catch (Exception)
            {
                _isRunning = false;
                _listener?.Stop();
                _listener = null;

                // Revara fork, from PR #60: let the ribbon button report the failure (e.g. the port is taken by
                // another Revit) instead of claiming "Open Server". MCPServiceConnection catches it and returns
                // Result.Failed with the message.
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _isRunning = false;

                ConnectionInfoFile.DeleteIfOwned(ConnectionInfoFile.DefaultPath, _token);
                _token = null;

                _listener?.Stop();
                _listener = null;

                if(_listenerThread!=null && _listenerThread.IsAlive)
                {
                    _listenerThread.Join(1000);
                }
            }
            catch (Exception)
            {
                // log error
            }
        }

        private void ListenForClients()
        {
            try
            {
                while (_isRunning)
                {
                    TcpClient client = _listener.AcceptTcpClient();

                    Thread clientThread = new Thread(HandleClientCommunication)
                    {
                        IsBackground = true
                    };
                    clientThread.Start(client);
                }
            }
            catch (SocketException)
            {
                
            }
            catch(Exception)
            {
                // log
            }
        }

        private void HandleClientCommunication(object clientObj)
        {
            TcpClient tcpClient = (TcpClient)clientObj;
            NetworkStream stream = tcpClient.GetStream();

            try
            {
                byte[] buffer = new byte[8192];
                // Revara fork, from PR #60: one read is not one message. The framer joins reads until a
                // message is complete, so requests longer than the buffer arrive whole.
                var framer = new MessageFramer(MaxMessageLength);

                while (_isRunning && tcpClient.Connected)
                {
                    // 读取客户端消息
                    // Read client messages.
                    int bytesRead = 0;

                    try
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException)
                    {
                        // 客户端断开连接
                        // Client disconnected.
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        // 客户端断开连接
                        // Client disconnected.
                        break;
                    }

                    List<string> messages;
                    try
                    {
                        messages = framer.Append(buffer, bytesRead);
                    }
                    catch (InvalidDataException ex)
                    {
                        WriteResponse(stream, CreateErrorResponse(null, JsonRPCErrorCodes.InvalidRequest, ex.Message));
                        break;
                    }

                    foreach (string message in messages)
                    {
                        string response = ProcessJsonRPCRequest(message, out bool closeConnection);

                        // 发送响应
                        // Send response.
                        WriteResponse(stream, response);

                        if (closeConnection)
                        {
                            return;
                        }
                    }
                }
            }
            catch(Exception)
            {
                // log
            }
            finally
            {
                tcpClient.Close();
            }
        }

        private string ProcessJsonRPCRequest(string requestJson, out bool closeConnection)
        {
            closeConnection = false;
            BridgeProtocol.Outcome outcome = null;

            try
            {
                // Revara fork: the token check, request validation and the get_version and health methods are
                // decided by BridgeProtocol; only the rest reaches the command registry.
                string revitYear = _revitYear;
                outcome = BridgeProtocol.Handle(
                    requestJson,
                    _token,
                    () => BridgeProtocol.VersionResult(ReadInformationalVersion(), revitYear),
                    () => BridgeProtocol.HealthResult(
                        _uptime.ElapsedMilliseconds,
                        (_commandRegistry as RevitCommandRegistry)?.GetRegisteredCommands().Count() ?? 0));

                if (outcome.Response != null)
                {
                    closeConnection = outcome.Close;
                    return outcome.Response;
                }

                // On this path the SDK returns the id as a string; the client sends string ids.
                JsonRPCRequest request = outcome.Request.ToObject<JsonRPCRequest>();

                // 查找命令
                // Search for the command in the registry.
                if (!_commandRegistry.TryGetCommand(request.Method, out var command))
                {
                    return CreateErrorResponse(request.Id, JsonRPCErrorCodes.MethodNotFound,
                        $"Method '{request.Method}' not found");
                }

                // 执行命令
                // Execute command.
                try
                {                
                    object result = command.Execute(request.GetParamsObject(), request.Id);

                    return CreateSuccessResponse(request.Id, result);
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse(request.Id, JsonRPCErrorCodes.InternalError, ex.Message);
                }
            }
            catch (Exception ex)
            {
                // 处理请求时的其他错误
                // Catch other errors produced when processing requests.
                // The id is the request's own when the line parsed (a JSON null gives C# null).
                return CreateErrorResponse(
                    (outcome?.Id as JValue)?.Value?.ToString(),
                    JsonRPCErrorCodes.InternalError,
                    $"Internal error: {ex.Message}"
                );
            }
        }

        private static string ReadInformationalVersion()
        {
            var attribute = typeof(SocketService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attribute?.InformationalVersion;
        }

        private string CreateSuccessResponse(string id, object result)
        {
            var response = new JsonRPCSuccessResponse
            {
                Id = id,
                Result = result is JToken jToken ? jToken : JToken.FromObject(result)
            };

            return response.ToJson();
        }

        private string CreateErrorResponse(string id, int code, string message, object data = null)
        {
            var response = new JsonRPCErrorResponse
            {
                Id = id,
                Error = new JsonRPCError
                {
                    Code = code,
                    Message = message,
                    Data = data != null ? JToken.FromObject(data) : null
                }
            };

            return response.ToJson();
        }

        private static void WriteResponse(NetworkStream stream, string response)
        {
            // Revara fork, from PR #60: one response per line. JSON escapes line breaks inside strings,
            // so any raw line break is formatting and can be dropped to keep the response on one line.
            string line = response.Replace("\r", "").Replace("\n", "") + "\n";
            byte[] responseData = Encoding.UTF8.GetBytes(line);
            stream.Write(responseData, 0, responseData.Length);
        }
    }
}
