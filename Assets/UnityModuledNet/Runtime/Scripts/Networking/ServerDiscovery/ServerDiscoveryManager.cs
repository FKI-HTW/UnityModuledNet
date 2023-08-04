using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;
using CENTIS.UnityModuledNet.Managing;
using CENTIS.UnityModuledNet.Networking.Packets;

namespace CENTIS.UnityModuledNet.Networking.ServerDiscovery
{
    public class ServerDiscoveryManager
    {
		#region properties

		public bool IsServerDiscoveryActive { get; private set; }
        public List<OpenServer> OpenServers => _openServers.Values.ToList();

        public Action OnServerDiscoveryActivated;
        public Action OnServerDiscoveryDeactivated;
        public Action OnOpenServerListUpdated;

		#endregion

		#region fields

		private IPAddress _localIP;
		private IPAddress _multicastIP;
        private int _discoveryPort;
        private UdpClient _udpClient;
        private Thread _discoveryThread;

        private readonly ConcurrentDictionary<IPEndPoint, OpenServer> _openServers = new();

		#endregion

		#region public methods

		public bool StartServerDiscovery()
        {
            if (IsServerDiscoveryActive)
                return true;

            try
            {
                _localIP = ModuledNetManager.IP;
                _multicastIP = IPAddress.Parse(ModuledNetSettings.Settings.MulticastAddress);
                _discoveryPort = ModuledNetSettings.Settings.DiscoveryPort;

                _udpClient = new();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                _udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(_multicastIP, _localIP));
                _udpClient.Client.Bind(new IPEndPoint(_localIP, _discoveryPort));

                _discoveryThread = new(() => DiscoveryThread()) { IsBackground = true };
                _discoveryThread.Start();

                OnServerDiscoveryActivated?.Invoke();
                return IsServerDiscoveryActive = true;
            }
            catch (Exception ex)
            {
                switch (ex)
                {
                    case FormatException:
                        Debug.LogError("The Server Discovery Multicast IP is not a valid Address!");
                        break;
                    case SocketException:
                        Debug.LogError("An Error ocurred when accessing the socket. Make sure the port is not occupied by another process!");
                        break;
                    case ArgumentOutOfRangeException:
                        Debug.LogError("The Given Port is outside the possible Range!");
                        break;
                    case ArgumentNullException:
                        Debug.LogError("The local IP can't be null!");
                        break;
                    case ThreadStartException:
                        Debug.LogError("An Error ocurred when starting the Threads. Please try again later!");
                        break;
                    case OutOfMemoryException:
                        Debug.LogError("Not enough memory available to start the Threads!");
                        break;
                    default:
                        OnServerDiscoveryDeactivated?.Invoke();
                        IsServerDiscoveryActive = false;
                        ExceptionDispatchInfo.Capture(ex).Throw();
                        throw;
                }
                OnServerDiscoveryDeactivated?.Invoke();
                return IsServerDiscoveryActive = false;
            }
        }

        public void EndServerDiscovery()
		{
            if (!IsServerDiscoveryActive)
                return;

            if (_udpClient != null)
            {
                _udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(_multicastIP, _localIP));
                _udpClient.Close();
                _udpClient.Dispose();
            }
            if (_discoveryThread != null)
            {
                _discoveryThread.Abort();
                _discoveryThread.Join();
            }

            IsServerDiscoveryActive = false;
            OnServerDiscoveryDeactivated?.Invoke();
        }

        public bool RestartServerDiscovery()
		{
            EndServerDiscovery();
            return StartServerDiscovery();
		}

		#endregion

		#region private methods

		private void DiscoveryThread()
        {
            while (true)
            {
                try
                {
                    // get packet ip headers
                    IPEndPoint receiveEndpoint = new(1, 1);
                    byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
                    // TODO : also check for port
                    if (receiveEndpoint.Address.Equals(_localIP) && receiveEndpoint.Port == ModuledNetManager.Port
                        || receiveEndpoint.Address.Equals(_localIP) && !ModuledNetSettings.Settings.AllowLocalConnection)
                        continue;

                    // get packet type without chunked packet bit
                    byte typeBytes = receivedBytes[ModuledNetSettings.CRC32_LENGTH];
                    byte mask = 1 << 7;
                    typeBytes &= (byte)~mask;
                    EPacketType packetType = (EPacketType)typeBytes;
                    if (packetType != EPacketType.ServerInformation)
                        continue;

                    ServerInformationPacket heartbeat = new(receivedBytes);
                    if (!heartbeat.TryDeserialize())
                        continue;

                    OpenServer newServer = new(receiveEndpoint, heartbeat.Servername, heartbeat.MaxNumberOfClients, heartbeat.NumberOfClients);
                    if (!_openServers.TryGetValue(receiveEndpoint, out OpenServer _))
                        _ = TimeoutServer(receiveEndpoint);

                    // add new values or update server with new values
                    _openServers.AddOrUpdate(receiveEndpoint, newServer, (key, value) => value = newServer);

                    ModuledNetManager.QueueOnUpdate(() => OnOpenServerListUpdated?.Invoke());
                }
                catch (Exception ex)
                {
                    switch (ex)
                    {
                        case IndexOutOfRangeException:
                        case ArgumentException:
                            continue;
                        case ThreadAbortException:
                            IsServerDiscoveryActive = false;
                            return;
                        default:
                            Debug.LogError("An Error occurred in the Server Discovery!");
                            IsServerDiscoveryActive = false;
                            return;
                    }
                }
            }
        }

        private async Task TimeoutServer(IPEndPoint serverEndpoint)
        {
            await Task.Delay(ModuledNetSettings.Settings.ServerDiscoveryTimeout);
            if (_openServers.TryGetValue(serverEndpoint, out OpenServer server))
            {   // timeout and remove servers that haven't been updated for longer than the timeout value
                if ((DateTime.Now - server.LastHeartbeat).TotalMilliseconds > ModuledNetSettings.Settings.ServerDiscoveryTimeout)
                {
                    _openServers.TryRemove(serverEndpoint, out _);
                    ModuledNetManager.QueueOnUpdate(() => OnOpenServerListUpdated?.Invoke());
                    return;
                }

                _ = TimeoutServer(serverEndpoint);
            }
        }

        #endregion
    }
}
