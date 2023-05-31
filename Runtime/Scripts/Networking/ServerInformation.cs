
using System;
using System.Net;

namespace CENTIS.UnityModuledNet.Networking
{
    public class ServerInformation
    {
        public IPAddress IP { get; private set; }
        public string Servername { get; private set; }
        public byte MaxNumberConnectedClients { get; set; }
        public byte NumberConnectedClients { get; set; }
        public readonly DateTime LastHeartbeat;

        public ServerInformation(IPAddress ip, string servername, byte maxNumberConnectedClients, byte? numberOfConnectedClients = null)
        {
            IP = ip;
            Servername = servername;
            MaxNumberConnectedClients = maxNumberConnectedClients;
            NumberConnectedClients = numberOfConnectedClients ?? 1;
            LastHeartbeat = DateTime.Now;
        }

        public override bool Equals(object obj)
        {
            if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            {
                return false;
            }
            else
            {
                ServerInformation server = (ServerInformation)obj;
                return server.IP.Equals(IP);
            }
        }

        public override int GetHashCode()
        {
            return IP.GetHashCode();
        }
    }
}
