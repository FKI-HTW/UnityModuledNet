using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using UnityEngine;
using CENTIS.UnityModuledNet.Networking;
using CENTIS.UnityModuledNet.Networking.ServerDiscovery;
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

        private static int _cachedIPAddressIndex = -1;
        private static string _cachedIpAddress = "";
        public static string LocalIP
        {
			get
			{
				if (_cachedIPAddressIndex != ModuledNetSettings.Settings.IPAddressIndex)
                {
                    _cachedIPAddressIndex = ModuledNetSettings.Settings.IPAddressIndex;
                    _cachedIpAddress = GetLocalIPAddress(_cachedIPAddressIndex);
                }
                return _cachedIpAddress;
            }
        }

        public static IPAddress IP => IPAddress.Parse(LocalIP);

        private static int _port;
        public static int Port => _port;

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
        public static event Action OnOpenServerListUpdated;

        /// <summary>
        /// Current Status of the Connection to the Server.
        /// </summary>
        public static EConnectionStatus EConnectionStatus
        {
            get => Socket == null ? EConnectionStatus.IsDisconnected : Socket.EConnectionStatus;
        }

        /// <summary>
        /// If the Client is connected to a Server or is a Server.
        /// </summary>
        public static bool IsConnected
        {
            get => Socket != null && Socket.IsConnected;
        }

        /// <summary>
        /// If the Client is connected to the Network and able to discover Servers.
        /// </summary>
        public static bool IsServerDiscoveryActive
        {
            get => ServerDiscoveryManager != null && ServerDiscoveryManager.IsServerDiscoveryActive;
        }

        /// <summary>
        /// A List of all available Servers.
        /// </summary>
        public static List<OpenServer> OpenServers
        {
            get => ServerDiscoveryManager?.OpenServers;
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

        private readonly static ConcurrentDictionary<byte[], ModuledNetModule> _registeredModules = new(new ByteArrayComparer());

        private readonly static ConcurrentQueue<Action> _mainThreadDispatchQueue = new();

        private static ServerDiscoveryManager _serverDiscoveryManager;
        private static ServerDiscoveryManager ServerDiscoveryManager
		{
            get => _serverDiscoveryManager;
            set
			{
                if (_serverDiscoveryManager == value) return;

                if (_serverDiscoveryManager != null)
				{
                    _serverDiscoveryManager.OnServerDiscoveryActivated -= FireServerDiscoveryActivatedEvent;
                    _serverDiscoveryManager.OnServerDiscoveryDeactivated -= FireServerDiscoveryDeactivatedEvent;
                    _serverDiscoveryManager.OnOpenServerListUpdated -= FireOpenServerListUpdatedEvent;
				}
                _serverDiscoveryManager = value;
                if (_serverDiscoveryManager != null)
				{
                    _serverDiscoveryManager.OnServerDiscoveryActivated += FireServerDiscoveryActivatedEvent;
                    _serverDiscoveryManager.OnServerDiscoveryDeactivated += FireServerDiscoveryDeactivatedEvent;
                    _serverDiscoveryManager.OnOpenServerListUpdated += FireOpenServerListUpdatedEvent;
                }
			}
		}

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
                QueueOnUpdate(() =>
                {
                    if (ModuledNetSettings.Settings.ReconnectAfterRecompile is false) return;
                    var serverInformation = ServerInformationBeforeRecompile;
                    if (serverInformation is null) return;

                    if (serverInformation.Endpoint.Equals(new IPEndPoint(IP, Port)))
                        CreateServer(ServerInformationBeforeRecompile.Servername);
                    else
                        ConnectToServer(ServerInformationBeforeRecompile.Endpoint.Address, ServerInformationBeforeRecompile.Endpoint.Port);
                });
            };
#else
			ModuledNetRuntimeManager.Instance.OnAwake += Awake;
			ModuledNetRuntimeManager.Instance.OnStart += Start;
			ModuledNetRuntimeManager.Instance.OnUpdate += Update;
#endif

            DataReceived += OnDataReceived;

            QueueOnUpdate(() => StartServerDiscovery());
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

            while (_mainThreadDispatchQueue.Count > 0)
                if (_mainThreadDispatchQueue.TryDequeue(out Action action))
                    action?.Invoke();
        }

        public static void QueueOnUpdate(Action updateAction)
        {
            _mainThreadDispatchQueue.Enqueue(updateAction);
        }

        #endregion

        #region private methods

        private static void FireConnectingEvent() => OnConnecting?.Invoke();
        private static void FireConnectedEvent() => OnConnected?.Invoke();
        private static void FireDisconnectedEvent() => OnDisconnected?.Invoke();
        private static void FireClientConnectedEvent(byte id) => OnClientConnected?.Invoke(id);
        private static void FireClientDisconnectedEvent(byte id) => OnClientDisconnected?.Invoke(id);
        private static void FireConnectedClientListChangedEvent() => OnConnectedClientListChanged?.Invoke();
        private static void FireServerDiscoveryActivatedEvent() => OnServerDiscoveryActivated?.Invoke();
        private static void FireServerDiscoveryDeactivatedEvent() => OnServerDiscoveryDeactivated?.Invoke();
        private static void FireOpenServerListUpdatedEvent() => OnOpenServerListUpdated?.Invoke();

        private static string GetLocalIPAddress(int index = 0, bool checkForGatewayAddress = true)
        {
            var ipAdresses = GetLocalIPAddresses(checkForGatewayAddress);
            
            if (ipAdresses == null || ipAdresses.Count == 0)
                return string.Empty;

            return ipAdresses[Mathf.Clamp(index, 0, ipAdresses.Count - 1)];
        }

        public static List<string> GetLocalIPAddresses(bool checkForGatewayAddress = true)
        {
            // taken from https://stackoverflow.com/a/24814027
            List<string> ipAddresses = new();
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.OperationalStatus == OperationalStatus.Up)
                {   // Fetch the properties of this adapter
                    IPInterfaceProperties adapterProperties = item.GetIPProperties();
                    // Check if the gateway adress exist, if not its most likley a virtual network or smth
                    if (checkForGatewayAddress == false || adapterProperties.GatewayAddresses.FirstOrDefault() != null)
                    {   // Iterate over each available unicast adresses
                        foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
                        {   // If the IP is a local IPv4 adress
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                ipAddresses.Add(ip.Address.ToString());
                                break;
                            }
                        }
                    }
                }
            }
            return ipAddresses;
        }

        private static void OnDataReceived(byte[] moduleID, byte client, byte[] data)
        {
            if (_registeredModules.TryGetValue(moduleID, out ModuledNetModule module))
            {
                module.OnReceiveData(client, data);
            }
            else
            {
                Debug.LogError($"Received data for an unregistered module! Module ID: {moduleID} by client '{client}', " +
                    $"There are {_registeredModules.Count} modules registered right now.");
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

        #region public

        /// <summary>
        /// Starts the Server Discovery.
        /// </summary>
        /// <returns><see langword="true"> if the Server Discovery is already active or was successfully started</returns>
        public static bool StartServerDiscovery()
		{
            if (ServerDiscoveryManager == null)
                ServerDiscoveryManager = new();
            return ServerDiscoveryManager.StartServerDiscovery();

        }

        /// <summary>
        /// Ends the Server Discovery.
        /// </summary>
        public static void EndServerDiscovery()
		{
            if (ServerDiscoveryManager != null)
                ServerDiscoveryManager.EndServerDiscovery();
        }

        /// <summary>
        /// Resets the Server Discovery. Use this when Exceptions ocurred or the Service Discovery was closed.
        /// </summary>
        /// <returns><see langword="true"/> if the Server Discovery is Active after the Reset</returns>
        public static bool RestartServerDiscovery()
        {
            if (ServerDiscoveryManager == null)
                return StartServerDiscovery();
            else
                return ServerDiscoveryManager.RestartServerDiscovery();
        }

        /// <summary>
        /// Connect to a given Server. Membership in a Server is required before Data can be send.
        /// </summary>
        /// <param name="serverIP">IP of the Server.</param>
        /// <param name="onConnectionEstablished">Invoked once the connection was successfully established or failed to.</param>
        public static void ConnectToServer(IPAddress serverIP, int serverPort, Action<bool> onConnectionEstablished = null)
        {
            if (EConnectionStatus != EConnectionStatus.IsDisconnected)
            {
                Debug.LogWarning("The local Client is already connected or connecting to a Server!");
                onConnectionEstablished?.Invoke(false);
                return;
            }

            _port = FindNextAvailablePort();

            NetworkClient networkClient = new();
            Socket = networkClient;
            networkClient.Connect(IP, _port, serverIP, serverPort, onConnectionEstablished);
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

            _port = FindNextAvailablePort();

            NetworkServer networkServer = new();
            Socket = networkServer;
            networkServer.StartServer(IP, _port, servername, onConnectionEstablished);
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
            QueueOnUpdate(() =>
            {
                ModuledNetMessages.Add(message);
                OnSyncMessageAdded?.Invoke();
            });
        }

        #endregion

        #region helper methods

        private static int FindNextAvailablePort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

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
