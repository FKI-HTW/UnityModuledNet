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
using System.Text;
using System.Reflection;
using UnityEngine;
using CENTIS.UnityModuledNet.Networking;
using CENTIS.UnityModuledNet.Networking.Packets;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet
{
	public static class ModuledNetManager
	{
		#region public properties

		public static bool IsDebug = false;
		public static string LocalIP = GetLocalIPAddress();

		public static Action OnAwake;
		public static Action OnStart;
		public static Action OnUpdate;
		
		public static Action OnSyncMessageAdded;

		/// <summary>
		/// Action for when the Server Discovery was activated.
		/// </summary>
		public static Action OnServerDiscoveryActivated;

		/// <summary>
		/// Action for when the Server Discovery was deactivated.
		/// </summary>
		public static Action OnServerDiscoveryDeactivated;

		/// <summary>
		/// Action for when successfully connecting to or creating a Server.
		/// </summary>
		public static Action OnConnected;

		/// <summary>
		/// Action for when connection to or creation of a server is being started.
		/// </summary>
		public static Action OnConnecting;

		/// <summary>
		/// Action for when disconnecting from or closing the Server.
		/// </summary>
		public static Action OnDisconnected;

		/// <summary>
		/// Action for when a remote Client connected to the current Server and can now receive Messages.
		/// </summary>
		public static Action<byte> OnClientConnected;

		/// <summary>
		/// Action for when a remote Client disconnected from the current Server and can no longer receive any Messages.
		/// </summary>
		public static Action<byte> OnClientDisconnected;

		/// <summary>
		/// Action for when a Server was added or removed from the OpenServers.
		/// </summary>
		public static Action OnServerListChanged;

		/// <summary>
		/// If the Client is connected to the Network and able to discover Servers.
		/// </summary>
		public static bool IsServerDiscoveryActive
		{
			get => _isServerDiscoveryActive; private set => _isServerDiscoveryActive = value;
		}

		public static ConnectionStatus ConnectionStatus
		{
			get => _socket == null ? ConnectionStatus.IsDisconnected : _socket.ConnectionStatus;
		}
		
		/// <summary>
		/// If the Client is connected to a Server or is a Server.
		/// </summary>
		public static bool IsConnected
		{
			get => _socket != null && _socket.IsConnected;
		}
		
		/// <summary>
		/// A List of all available Servers.
		/// </summary>
		public static List<ServerInformation> OpenServers
		{ 
			get => IsServerDiscoveryActive
				? _openServers.Values.Select(x => x.Item1).ToList()
				: null; 
		}
		
		/// <summary>
		/// Information on the Server to which the local Client is currently connected to.
		/// </summary>
		public static ServerInformation CurrentServer 
		{ 
			get => IsConnected
				? _socket.ServerInformation
				: null; 
		}

		/// <summary>
		/// Information on the local Client.
		/// </summary>
		public static ClientInformation LocalClient
		{
			get => IsConnected
				? _socket.ClientInformation
				: null;
		}

		/// <summary>
		/// A List of remote Clients connected to the same Server as the local Client.
		/// </summary>
		public static ConcurrentDictionary<byte, ClientInformation> ConnectedClients 
		{
			get => IsConnected
				? _socket.ConnectedClients
				: null;
		}

		/// <summary>
		/// If the local Client acts as a Server that remote Clients can connect to.
		/// </summary>
		public static bool IsHost
		{
			get => IsConnected && LocalClient.IsHost;
		}

		public static readonly List<ModuledNetMessage> SyncMessages = new();

		#endregion

		#region internal fields

		internal static Action<uint, byte, byte[]> DataReceived;

		#endregion

		#region private fields

		private readonly static ModuledNetSettings _settings = ModuledNetSettings.GetOrCreateSettings();
		
		private readonly static ConcurrentDictionary<IPAddress, (ServerInformation, DateTime)> _openServers = new();

		private readonly static ConcurrentDictionary<uint, ModuledNetModule> _registeredModules = new();

		private readonly static ConcurrentQueue<Action> _mainThreadActions = new();

		private static IPAddress _ip;
		private static UdpClient _udpClient;

		private static Thread _discoveryThread;

		private static ANetworkSocket _socket;
		private static bool _isServerDiscoveryActive;

		#endregion

		#region unity lifecycle

		static ModuledNetManager()
		{
#if UNITY_EDITOR
			Awake();
			Start();
			EditorApplication.update += Update;
#else
			SyncRuntimeManager.Instance.OnAwake += Awake;
			SyncRuntimeManager.Instance.OnStart += Start;
			SyncRuntimeManager.Instance.OnUpdate += Update;
#endif

			DataReceived += OnDataReceived;

			ResetClient();
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
					if (sender.Equals(_ip))
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

					ServerInformation newServer = new(sender, heartbeat.Servername, heartbeat.MaxNumberOfClients, heartbeat.NumberOfClients);
					if (_openServers.TryGetValue(sender, out (ServerInformation, DateTime) server))
					{   // update server with new values
						server.Item1 = newServer;
						server.Item2 = DateTime.Now;
					}
					else
					{   // add server to dict and create timeout
						_openServers.TryAdd(sender, (newServer, DateTime.Now));
						_ = TimeoutServer(sender);
					}

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
			if (_openServers.TryGetValue(serverIP, out (ServerInformation, DateTime) server))
			{	// timeout and remove servers that haven't been updated for longer than the timeout value
				if ((DateTime.Now - server.Item2).TotalMilliseconds > _settings.ServerDiscoveryTimeout)
				{
					_openServers.TryRemove(serverIP, out _);
					_mainThreadActions.Enqueue(() => OnServerListChanged?.Invoke());
					return;
				}

				_ = TimeoutServer(serverIP);
			}
		}

		private static void OnDataReceived(uint moduleHash, byte client, byte[] data)
		{
			if (_registeredModules.TryGetValue(moduleHash, out ModuledNetModule module))
			{
				_mainThreadActions.Enqueue(() => module.OnReceiveData(client, data));
			}
		}

		#endregion

		#region internal methods

		internal static uint RegisterModule(ModuledNetModule module)
		{
			string moduleValues = module.GetType().Name;
			BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			foreach (var f in module.GetType().GetMembers(flags))
			{
				if (f.MemberType == MemberTypes.Field || (f.MemberType & MemberTypes.Property) != 0)
					moduleValues += f.Name;

				if (f is FieldInfo fi)
					moduleValues += fi.FieldType.Name;

				if (f.MemberType == MemberTypes.Method)
					moduleValues += f.Name;
			}
			uint hash = CRC32Hash.CRC32Bytes(Encoding.ASCII.GetBytes(moduleValues));

			if (_registeredModules.TryGetValue(hash, out _))
			{
				Debug.LogError("Only one Module of each type can be registered!");
				return 0;
			}

			_registeredModules.TryAdd(hash, module);
			return hash;
		}

		internal static void UnregisterModule(uint moduleHash)
		{
			if (!_registeredModules.TryRemove(moduleHash, out _))
				Debug.LogWarning($"The given Module could not be found!");
		}

		internal static bool IsModuleRegistered(uint moduleHash)
		{
			return _registeredModules.TryGetValue(moduleHash, out _);
		}

		internal static void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataReliable(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataReliableUnordered(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataUnreliable(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!IsEligibleForSending(onDataSend))
				return;

			_socket.SendDataUnreliableUnordered(moduleHash, data, onDataSend, receiver);
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
		/// Resets the Client. Use this when Exceptions ocurred or the Service Discovery was closed.
		/// </summary>
		/// <returns><see langword="true"/> if the Client is Active after the Reset</returns>
		public static bool ResetClient()
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

				_ip = IPAddress.Parse(LocalIP);

				_udpClient = new();
				_udpClient.EnableBroadcast = true;
				_udpClient.ExclusiveAddressUse = false;
				_udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				_udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _settings.DiscoveryPort));

				_discoveryThread = new(() => DiscoveryThread()) { IsBackground = true };
				_discoveryThread.Start();

				IsServerDiscoveryActive = true;
				return true;
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
				IsServerDiscoveryActive = false;
				return false;
			}
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

			_socket = new NetworkClient(serverIP, onConnectionEstablished);
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

			_socket = new NetworkServer(servername, onConnectionEstablished);
		}

		/// <summary>
		/// Disconnects the Client from the Server
		/// </summary>
		public static void DisconnectFromServer()
		{
			if (_socket == null)
			{
				Debug.LogWarning("The local Client is not currently connected to a Server!");
				return;
			}

			_socket.DisconnectFromServer();
		}

		public static void AddSyncMessage(ModuledNetMessage message)
		{
			_mainThreadActions.Enqueue(() => {
				SyncMessages.Add(message);
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
