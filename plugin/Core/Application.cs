using System;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;



namespace revit_mcp_plugin.Core
{
    public class Application : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            RibbonPanel mcpPanel = application.CreateRibbonPanel("Revara MCP");

            PushButtonData pushButtonData = new PushButtonData("ID_EXCMD_TOGGLE_REVARA_MCP", "Revit MCP\r\n Switch",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.MCPServiceConnection");
            pushButtonData.ToolTip = "Open / Close mcp server";
            pushButtonData.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/icon-16.png", UriKind.RelativeOrAbsolute));
            pushButtonData.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/icon-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(pushButtonData);

            PushButtonData mcp_settings_pushButtonData = new PushButtonData("ID_EXCMD_REVARA_MCP_SETTINGS", "Settings",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.Settings");
            mcp_settings_pushButtonData.ToolTip = "MCP Settings";
            mcp_settings_pushButtonData.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-16.png", UriKind.RelativeOrAbsolute));
            mcp_settings_pushButtonData.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(mcp_settings_pushButtonData);

            // Revara fork: start the service once Revit has finished starting, unless autoStart is false.
            // The handler only logs on failure: it must never throw or show a dialog during Revit's startup.
            application.ControlledApplication.ApplicationInitialized += (sender, args) =>
            {
                var logger = new Logger();
                try
                {
                    // A bare "Application" here is this plugin's own class, so the Revit type is written in full.
                    var uiApp = new UIApplication(sender as Autodesk.Revit.ApplicationServices.Application);

                    bool autoStart = PluginSettingsFile.ReadAutoStart(PluginSettingsFile.DefaultPath);

                    if (autoStart && !SocketService.Instance.IsRunning)
                    {
                        SocketService.Instance.Initialize(uiApp);
                        SocketService.Instance.Start();
                    }
                }
                catch (Exception ex)
                {
                    try { logger.Error("Revara MCP auto-start failed: {0}", ex.Message); } catch (Exception) { }
                }
            };

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                if (SocketService.Instance.IsRunning)
                {
                    SocketService.Instance.Stop();
                }
            }
            catch { }

            return Result.Succeeded;
        }
    }
}
