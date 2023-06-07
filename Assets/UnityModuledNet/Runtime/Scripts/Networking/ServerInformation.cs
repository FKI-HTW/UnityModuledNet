
using System;
using System.Net;

namespace CENTIS.UnityModuledNet.Networking
{
    public class ServerInformation
    {
        public IPAddress IP { get; private set; }
        public string Servername { get; private set; }
        public byte MaxNumberConnectedClients { get; private set; }
        public DateTime LastHeartbeat { get; set; }

        public ServerInformation(IPAddress ip, string servername, byte maxNumberConnectedClients)
        {
            IP = ip;
            Servername = servername;
            MaxNumberConnectedClients = maxNumberConnectedClients;
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

    public class OpenServerInformation : ServerInformation
    {
        public byte NumberConnectedClients { get; private set; }
        public bool IsServerFull => NumberConnectedClients >= MaxNumberConnectedClients;

        public OpenServerInformation(IPAddress ip, string servername, byte maxNumberConnectedClients, byte numberConnectedClients)
            : base(ip, servername, maxNumberConnectedClients)
        {
            NumberConnectedClients = numberConnectedClients;
        }
    }
}
