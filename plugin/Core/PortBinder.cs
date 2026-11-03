// Revara fork.

using System;
using System.Net;
using System.Net.Sockets;

namespace revit_mcp_plugin.Core
{
    public static class PortBinder
    {
        /// <summary>
        /// Starts a loopback listener on the first free port from first to last. Throws when none is free.
        /// </summary>
        public static TcpListener BindFirstFree(int first, int last)
        {
            for (int port = first; port <= last; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                try
                {
                    listener.Start();
                    return listener;
                }
                catch (SocketException)
                {
                    try { listener.Stop(); } catch (Exception) { }
                }
            }

            throw new InvalidOperationException(
                "No free port on 127.0.0.1 from " + first + " to " + last + ".");
        }
    }
}
