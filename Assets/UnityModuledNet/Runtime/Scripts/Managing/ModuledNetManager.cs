using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;
using CENTIS.UnityModuledNet.Networking;
using CENTIS.UnityModuledNet.Networking.Packets;
using CENTIS.UnityModuledNet.Modules;
using CENTIS.UnityModuledNet.Serialiser;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet.Managing
{
    public static class ModuledNetManager
    {
        #region public properties

        public static bool IsDebug = false;

        public static string LocalIP
        {
            get => _localIP;
            set
            {
                if (!IsConnected)
                    _localIP = value;
            }
        }

        public static IPAddress IP => IPAddress.Parse(LocalIP);

        public static event Action OnAwake;
        public static event Action OnStart;
        public static event Action OnUpdate;

        public static event Action OnSyncMessageAdded;

        /// <summary>
        /// Action for when the Server Discovery was activated.
        /// </summary>
        public static event Action OnServerDiscoveryActivated;

        /// <summary>
        /// Action for when the Server Discovery was deactivated.
        /// </summary>
        public static event Action OnServerDiscoveryDeactivated;

        /// <summary>
        /// Action for when connection to or creation of a server is being started.
        /// </summary>
        public static event Action OnConnecting;

        /// <summary>
        /// Action for when successfully connecting to or creating a Server.
        /// </summary>
        public static event Action OnConnected;

        /// <summary>
        /// Action for when disconnecting from or closing the Server.
        /// </summary>
        public static event Action OnDisconnected;

        /// <summary>
        /// Action for when a remote Client connected to the current Server and can now receive Messages.
        /// </summary>
        public static event Action<byte> OnClientConnected;

        /// <summary>
        /// Action for when a remote Client disconnected from the current Server and can no longer receive any Messages.
        /// </summary>
        public static event Action<byte> OnClientDisconnected;

        /// <summary>
        /// Action for when a Client was added or removed from ConnectedClients.
        /// </summary>
        public static event Action OnConnectedClientListChanged;

        /// <summary>
        /// Action for when a Server was added or removed from the OpenServers.
        /// </summary>
        public static event Action OnServerListChanged;

        /// <summary>
        /// If the Client is connected to the Network and able to discover Servers.
        /// </summary>
        public static bool IsServerDiscoveryActive
        {
            get => _isServerDiscoveryActive; private set => _isServerDiscoveryActive = value;
        }

        public static ConnectionStatus ConnectionStatus
        {
            get => Socket == null ? ConnectionStatus.IsDisconnected : Socket.ConnectionStatus;
        }

        /// <summary>
        /// If the Client is connected to a Server or is a Server.
        /// </summary>
        public static bool IsConnected
        {
            get => Socket != null && Socket.IsConnected;
        }

        /// <summary>
        /// A List of all available Servers.
        /// </summary>
        public static List<OpenServerInformation> OpenServers
        {
            get => IsServerDiscoveryActive
                ? _openServers.Values.ToList()
                : null;
        }

        /// <summary>
        /// Information on the Server to which the local Client is currently connected to.
        /// </summary>
        public static ServerInformation CurrentServer
        {
            get => IsConnected
                ? Socket.ServerInformation
                : null;
        }

        /// <summary>
        /// Information on the local Client.
        /// </summary>
        public static ClientInformation LocalClient
        {
            get => IsConnected
                ? Socket.ClientInformation
                : null;
        }

        /// <summary>
        /// A List of remote Clients connected to the same Server as the local Client.
        /// </summary>
        public static ConcurrentDictionary<byte, ClientInformation> ConnectedClients
        {
            get => IsConnected
                ? Socket.ConnectedClients
                : null;
        }

        /// <summary>
        /// If the local Client acts as a Server that remote Clients can connect to.
        /// </summary>
        public static bool IsHost
        {
            get => IsConnected && LocalClient != null && LocalClient.IsHost;
        }

        public static readonly List<ModuledNetMessage> ModuledNetMessages = new();

        #endregion

        #region internal fields

        internal static Action<byte[], byte, byte[]> DataReceived;

        #endregion

        #region private fields

        private static string _localIP = GetLocalIPAddress();

        private readonly static ModuledNetSettings _settings = ModuledNetSettings.GetOrCreateSettings();

        private readonly static ConcurrentDictionary<IPAddress, OpenServerInformation> _openServers = new();

        private readonly static ConcurrentDictionary<byte[], ModuledNetModule> _registeredModules = new(new ByteArrayComparer());

        private readonly static ConcurrentQueue<Action> _mainThreadActions = new();

        private static UdpClient _udpClient;

        private static Thread _discoveryThread;

        private static ANetworkSocket _socket;
        private static ANetworkSocket Socket
        {
            get => _socket;
            set
            {
                if (_socket == value) return;

                if (_socket != null)
                {
                    _socket.OnConnecting -= FireConnectingEvent;
                    _socket.OnConnected -= FireConnectedEvent;
                    _socket.OnDisconnected -= FireDisconnectedEvent;

                    if (_socket is NetworkServer networkServer)
                    {
                        networkServer.OnClientConnected -= FireClientConnectedEvent;
                        networkServer.OnClientDisconnected -= FireClientDisconnectedEvent;
                        networkServer.OnConnectedClientListChanged -= FireConnectedClientListChangedEvent;
                    }
                    else if (_socket is NetworkClient networkClient)
                    {
                        networkClient.OnClientConnected -= FireClientConnectedEvent;
                        networkClient.OnClientDisconnected -= FireClientDisconnectedEvent;
                        networkClient.OnConnectedClientListChanged -= FireConnectedClientListChangedEvent;
                    }
                }
                _socket = value;
                if (_socket != null)
                {
                    _socket.OnConnecting += FireConnectingEvent;
                    _socket.OnConnected += FireConnectedEvent;
                    _socket.OnDisconnected += FireDisconnectedEvent;

                    if (_socket is NetworkServer networkServer)
                    {
                        networkServer.OnClientConnected += FireClientConnectedEvent;
                        networkServer.OnClientDisconnected += FireClientDisconnectedEvent;
                        networkServer.OnConnectedClientListChanged += FireConnectedClientListChangedEvent;
                    }
                    else if (_socket is NetworkClient networkClient)
                    {
                        networkClient.OnClientConnected += FireClientConnectedEvent;
                        networkClient.OnClientDisconnected += FireClientDisconnectedEvent;
                        networkClient.OnConnectedClientListChanged += FireConnectedClientListChangedEvent;
                    }
                }
            }
        }

        private static bool _isServerDiscoveryActive;

        private static ServerInformation ServerInformationBeforeRecompile
        {
            get => PlayerPrefs.HasKey(nameof(ServerInformationBeforeRecompile)) ?
                ServerInformation.FromJson(PlayerPrefs.GetString(nameof(ServerInformationBeforeRecompile))) : null;
            set
            {
                if (value != null) PlayerPrefs.SetString(nameof(ServerInformationBeforeRecompile), value.ToJson());
                else PlayerPrefs.DeleteKey(nameof(ServerInformationBeforeRecompile));
            }
        }

        #endregion

        #region unity lifecycle

        static ModuledNetManager()
        {
#if UNITY_EDITOR
            Awake();
            Start();
            EditorApplication.update += Update;

            // Disconnect Server on recompile
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (IsConnected is false)
                {
                    ServerInformationBeforeRecompile = null;
                    return;
                }
                ServerInformationBeforeRecompile = CurrentServer;

                DisconnectFromServer();
            };
            // Reconnect server on recompile
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                if (_settings.ReconnectAfterRecompile is false) return;
                var serverInformation = ServerInformationBeforeRecompile;
                if (serverInformation is null) return;

                if (serverInformation.IP.ToString().Equals(LocalIP))
                    CreateServer(ServerInformationBeforeRecompile.Servername);
                else
                    ConnectToServer(ServerInformationBeforeRecompile.IP);
            };
#else
			ModuledNetRuntimeManager.Instance.OnAwake += Awake;
			ModuledNetRuntimeManager.Instance.OnStart += Start;
			ModuledNetRuntimeManager.Instance.OnUpdate += Update;
#endif

            DataReceived += OnDataReceived;

            ResetServerDiscovery();
        }

        public static void Init() { }

        private static void Awake()
        {
            OnAwake?.Invoke();
        }

        private static void Start()
        {
            OnStart?.Invoke();
        }

        private static void Update()
        {
            OnUpdate?.Invoke();

            while (_mainThreadActions.Count > 0)
            {
                if (_mainThreadActions.TryDequeue(out Action action))
                    action?.Invoke();
            }
        }

        #endregion

        #region private methods

        private static void FireConnectingEvent() => OnConnecting?.Invoke();
        private static void FireConnectedEvent() => OnConnected?.Invoke();
        private static void FireDisconnectedEvent() => OnDisconnected?.Invoke();
        private static void FireClientConnectedEvent(byte id) => OnClientConnected?.Invoke(id);
        private static void FireClientDisconnectedEvent(byte id) => OnClientDisconnected?.Invoke(id);
        private static void FireConnectedClientListChangedEvent() => OnConnectedClientListChanged?.Invoke();

        private static string GetLocalIPAddress()
        {
            // taken from https://stackoverflow.com/a/24814027
            string ipAddress = "";
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.OperationalStatus == OperationalStatus.Up)
                {   // Fetch the properties of this adapter
                    IPInterfaceProperties adapterProperties = item.GetIPProperties();
                    // Check if the gateway adress exist, if not its most likley a virtual network or smth
                    if (adapterProperties.GatewayAddresses.FirstOrDefault() != null)
                    {   // Iterate over each available unicast adresses
                        foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
                        {   // If the IP is a local IPv4 adress
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                ipAddress = ip.Address.ToString();
                                break;
                            }
                        }
                    }
                }
                if (ipAddress != "") { break; }
            }
            return ipAddress;
        }

        private static void DiscoveryThread()
        {
            IPEndPoint receiveEndpoint = new(IPAddress.Any, _settings.DiscoveryPort);

            while (true)
            {
                try
                {
                    // get packet ip headers
                    byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
                    IPAddress sender = receiveEndpoint.Address;
                    if (sender.Equals(IP))
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

                    OpenServerInformation newServer = new(sender, heartbeat.Servername, heartbeat.MaxNumberOfClients, heartbeat.NumberOfClients);
                    if (!_openServers.TryGetValue(sender, out OpenServerInformation _))
                        _ = TimeoutServer(sender);

                    // add new values or update server with new values
                    _openServers.AddOrUpdate(sender, newServer, (key, value) => value = newServer);

                    _mainThreadActions.Enqueue(() => OnServerListChanged?.Invoke());
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
                            Debug.LogError("An Error occurred in the Server Discovery. Reset Server Discovery!");
                            IsServerDiscoveryActive = false;
                            return;
                    }
                }
            }
        }

        private static async Task TimeoutServer(IPAddress serverIP)
        {
            await Task.Delay(_settings.ServerDiscoveryTimeout);
            if (_openServers.TryGetValue(serverIP, out OpenServerInformation server))
            {   // timeout and remove servers that haven't been updated for longer than the timeout value
                if ((DateTime.Now - server.LastHeartbeat).TotalMilliseconds > _settings.ServerDiscoveryTimeout)
                {
                    _openServers.TryRemove(serverIP, out _);
                    _mainThreadActions.Enqueue(() => OnServerListChanged?.Invoke());
                    return;
                }

                _ = TimeoutServer(serverIP);
            }
        }

        private static void OnDataReceived(byte[] moduleID, byte client, byte[] data)
        {
            if (_registeredModules.TryGetValue(moduleID, out ModuledNetModule module))
            {
                module.OnReceiveData(client, data);
            }
        }

        #endregion

        #region internal methods

        internal static bool RegisterModule(ModuledNetModule module)
        {
            if (IsModuleRegistered(module.ModuleIDBytes))
            {
                Debug.LogError("Only one Module of each type can be registered!");
                return false;
            }

            return _registeredModules.TryAdd(module.ModuleIDBytes, module);
        }

        internal static void UnregisterModule(byte[] moduleID)
        {
            if (!_registeredModules.TryRemove(moduleID, out _))
                Debug.LogWarning($"The given Module was already unregistered!");
        }

        internal static bool IsModuleRegistered(byte[] moduleID)
        {
            return _registeredModules.TryGetValue(moduleID, out _);
        }

        internal static void SendDataReliable(byte[] moduleID, byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
        {
            if (!IsEligibleForSending(onDataSend))
                return;

            Socket.SendDataReliable(moduleID, data, onDataSend, receiver);
        }

        internal static void SendDataReliableUnordered(byte[] moduleID, byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
        {
            if (!IsEligibleForSending(onDataSend))
                return;

            Socket.SendDataReliableUnordered(moduleID, data, onDataSend, receiver);
        }

        internal static void SendDataUnreliable(byte[] moduleID, byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
        {
            if (!IsEligibleForSending(onDataSend))
                return;

            Socket.SendDataUnreliable(moduleID, data, onDataSend, receiver);
        }

        internal static void SendDataUnreliableUnordered(byte[] moduleID, byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
        {
            if (!IsEligibleForSending(onDataSend))
                return;

            Socket.SendDataUnreliableUnordered(moduleID, data, onDataSend, receiver);
        }

        #endregion

        #region public methods

        /// <summary>
        /// Retrieves the Settings used for the Sync Client.
        /// </summary>
        /// <returns></returns>
        public static ModuledNetSettings GetModuledNetSettings()
        {
            return ModuledNetSettings.GetOrCreateSettings();
        }

        /// <summary>
        /// Resets the Server Discovery. Use this when Exceptions ocurred or the Service Discovery was closed.
        /// </summary>
        /// <returns><see langword="true"/> if the Server Discovery is Active after the Reset</returns>
        public static bool ResetServerDiscovery()
        {
            try
            {
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }
                if (_discoveryThread != null)
                {
                    _discoveryThread.Abort();
                    _discoveryThread.Join();
                }

                UpdateIPAddress();

                _udpClient = new();
                _udpClient.EnableBroadcast = true;
                _udpClient.ExclusiveAddressUse = false;
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _settings.DiscoveryPort));

                _discoveryThread = new(() => DiscoveryThread()) { IsBackground = true };
                _discoveryThread.Start();

                return IsServerDiscoveryActive = true;
            }
            catch (Exception ex)
            {
                switch (ex)
                {
                    case SocketException:
                        Debug.LogError("An Error ocurred when accessing the socket. "
                            + "Make sure the port is not occupied by another process!");
                        break;
                    case ArgumentOutOfRangeException:
                        Debug.LogError("The Given Port is outside the possible Range!");
                        break;
                    case ArgumentNullException:
                        Debug.LogError("The local IP can't be null!");
                        break;
                    case FormatException:
                        Debug.LogError("The local IP is not a valid IP Address!");
                        break;
                    case ThreadStartException:
                        Debug.LogError("An Error ocurred when starting the Threads. Please try again later!");
                        break;
                    case OutOfMemoryException:
                        Debug.LogError("Not enough memory available to start the Threads!");
                        break;
                    default:
                        IsServerDiscoveryActive = false;
                        ExceptionDispatchInfo.Capture(ex).Throw();
                        throw;
                }
                return IsServerDiscoveryActive = false;
            }
        }

        public static void UpdateIPAddress()
        {
            LocalIP = GetLocalIPAddress();
        }

        /// <summary>
        /// Connect to a given Server. Membership in a Server is required before Data can be send.
        /// </summary>
        /// <param name="serverIP">IP of the Server.</param>
        /// <param name="onConnectionEstablished">Invoked once the connection was successfully established or failed to.</param>
        public static void ConnectToServer(IPAddress serverIP, Action<bool> onConnectionEstablished = null)
        {
            if (ConnectionStatus != ConnectionStatus.IsDisconnected)
            {
                Debug.LogWarning("The local Client is already connected or connecting to a Server!");
                onConnectionEstablished?.Invoke(false);
                return;
            }

            var networkClient = new NetworkClient(serverIP); // ToDo: Handle this in a way that the networkCLient can be reused to connect to a different server ip
            // 1. Make sure there is a valid network client
            // 2. Close other connections. Own server or existing connection.
            // 3. Connect to wanted ip
            Socket = networkClient;
            networkClient.Connect(onConnectionEstablished);
        }

        /// <summary>
        /// Connect to a given Server. Membership in a Server is required before Data can be send.
        /// </summary>
        /// <param name="servername">Name of the local Server that is to be created.</param>
        /// <param name="onConnectionEstablished">Invoked once the connection was successfully established or failed to.</param>
        public static void CreateServer(string servername, Action<bool> onConnectionEstablished = null)
        {
            if (IsConnected)
            {
                Debug.LogWarning("The local Client is already connected to a Server!");
                onConnectionEstablished?.Invoke(false);
                return;
            }

            var networkServer = new NetworkServer(servername);
            Socket = networkServer;
            networkServer.StartServer(onConnectionEstablished);
        }

        /// <summary>
        /// Disconnects the Client from the Server
        /// </summary>
        public static void DisconnectFromServer()
        {
            if (Socket == null)
            {
                Debug.LogWarning("The local Client is not currently connected to a Server!");
                return;
            }

            Socket.DisconnectFromServer();
        }

        public static void AddModuledNetMessage(ModuledNetMessage message)
        {
            _mainThreadActions.Enqueue(() =>
            {
                ModuledNetMessages.Add(message);
                OnSyncMessageAdded?.Invoke();
            });
        }

        #endregion

        #region helper methods

        private static bool IsEligibleForSending(Action<bool> onDataSend)
        {
            if (!IsConnected)
            {
                Debug.LogError("The local Client is currently not connected to a Server!");
                onDataSend?.Invoke(false);
                return false;
            }
            return true;
        }

        #endregion
    }
}
