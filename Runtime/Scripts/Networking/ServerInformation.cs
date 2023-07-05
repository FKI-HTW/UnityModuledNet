
using System;
using System.Net;
using UnityEngine;

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

        [Serializable]
        private class StringRepresentation
        {
            [SerializeField] private string ip;
            [SerializeField] private string servername;
            [SerializeField] private byte maxNumberConnectedClients;
            [SerializeField] private string lastHeartbeat;

            public IPAddress IP => IPAddress.Parse(ip);
            public string Servername => servername;
            public byte MaxNumberConnectedClients => maxNumberConnectedClients;
            public DateTime LastHeartbeat => DateTime.Parse(lastHeartbeat);

            public StringRepresentation(IPAddress ip, string servername, byte maxNumberConnectedClients, DateTime lastHeartbeat)
            {
                this.ip = ip.ToString();
                this.servername = servername;
                this.maxNumberConnectedClients = maxNumberConnectedClients;
                this.lastHeartbeat = lastHeartbeat.ToString();
            }

            public StringRepresentation(string ip, string servername, byte maxNumberConnectedClients, string lastHeartbeat)
            {
                this.ip = ip;
                this.servername = servername;
                this.maxNumberConnectedClients = maxNumberConnectedClients;
                this.lastHeartbeat = lastHeartbeat;
            }
        }

        public string ToJson()
        {
            var jsonObject = new StringRepresentation(IP, Servername, MaxNumberConnectedClients, LastHeartbeat);
            return JsonUtility.ToJson(jsonObject);
        }

        public static ServerInformation FromJson(string json)
        {
            var jsonObject = JsonUtility.FromJson<StringRepresentation>(json);
            return new ServerInformation(jsonObject.IP, jsonObject.Servername, jsonObject.MaxNumberConnectedClients);
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
