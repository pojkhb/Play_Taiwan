using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace backend.utils
{
    public class ip
    {
        public static string getClientAndRemoteIp(HttpContext data)
        {
            IPAddress clientIpAddress = data.Connection.RemoteIpAddress;
            string sys_clientip = "";
            if (clientIpAddress != null)
            {
                // If we got an IPV6 address, then we need to ask the network for the IPV4 address 
                // This usually only happens when the browser is on the same machine as the server.
                if (clientIpAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    clientIpAddress = System.Net.Dns.GetHostEntry(clientIpAddress).AddressList.First(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                }
                sys_clientip = clientIpAddress.ToString();
            }

            IPAddress serverIpAddress = data.Connection.LocalIpAddress;
            string sys_serverip = "";
            if (serverIpAddress != null)
            {
                // If we got an IPV6 address, then we need to ask the network for the IPV4 address 
                // This usually only happens when the browser is on the same machine as the server.
                if (serverIpAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    serverIpAddress = System.Net.Dns.GetHostEntry(serverIpAddress).AddressList.First(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                }
                sys_serverip = serverIpAddress.ToString();
            }
            return @$"{sys_clientip},{sys_serverip}";
        }
    }
}