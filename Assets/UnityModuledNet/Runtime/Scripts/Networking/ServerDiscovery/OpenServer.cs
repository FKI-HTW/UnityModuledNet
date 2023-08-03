using System;
using System.Net;

namespace CENTIS.UnityModuledNet.Networking.ServerDiscovery
{
    public class OpenServer
    {
        public IPEndPoint Endpoint { get; private set; }
        public string Servername { get; private set; }
        public byte MaxNumberConnectedClients { get; private set; }
        public DateTime LastHeartbeat { get; set; }
        public byte NumberConnectedClients { get; private set; }
        public bool IsServerFull => NumberConnectedClients >= MaxNumberConnectedClients;

        public OpenServer(IPEndPoint endpoint, string servername, byte maxNumberConnectedClients, byte numberConnectedClients)
        {
            NumberConnectedClients = numberConnectedClients;
        }
    }
}
