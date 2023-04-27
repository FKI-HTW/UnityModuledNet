using CENTIS.UnityModuledNet.Networking;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet
{
	public static class SyncManager
	{
		#region public properties

		public static bool IsDebug = false;
		public static string IP = GetLocalIPAddress();

		public static Action OnAwake;
		public static Action OnStart;
		public static Action OnUpdate;
		
		public static Action OnSyncMessageAdded;

		/// <summary>
		/// Action for when the local Client successfully connected to the Network and is able to discover and join Rooms.
		/// </summary>
		public static Action OnLocalClientConnected;

		/// <summary>
		/// Action for when the local Client was disconnected from the Network and is now unable to discover and join Rooms.
		/// </summary>
		public static Action OnLocalClientDisconnected;

		/// <summary>
		/// Action for when the local Client connected to a Room and can now send Messages.
		/// </summary>
		public static Action OnConnectedToRoom;

		/// <summary>
		/// Action for when the local Client disconnected from a Room and can no longer send any Messages.
		/// </summary>
		public static Action OnDisconnectedFromRoom;

		/// <summary>
		/// Action for when a Client connected to the current Room and can now receive Messages.
		/// </summary>
		public static Action<IPAddress> OnClientConnected;

		/// <summary>
		/// Action for when a Client disconnected from the current Room and can no longer receive any Messages.
		/// </summary>
		public static Action<IPAddress> OnClientDisconnected;

		/// <summary>
		/// If the Client is connected to the Network and able to discover Rooms.
		/// </summary>
		public static bool IsClientActive 
		{ 
			get => _client != null && _client.IsClientActive; 
		}
		
		/// <summary>
		/// If the Client is connected to a Room and able to send Messages.
		/// </summary>
		public static bool IsConnectedToRoom 
		{ 
			get => _client != null && _client.IsConnectedToRoom; 
		}
		
		/// <summary>
		/// A List of all available Rooms.
		/// </summary>
		public static List<SyncOpenRoom> OpenRooms 
		{ 
			get => IsClientActive 
				? _client.OpenRooms.Values.ToList() 
				: null; 
		}
		
		/// <summary>
		/// The Room to which the Client is currently connected to.
		/// </summary>
		public static SyncOpenRoom CurrentRoom 
		{ 
			get => IsClientActive 
				? _client.CurrentRoom 
				: null; 
		}

		/// <summary>
		/// A List of remote Clients connected to the same Room as the local Client.
		/// </summary>
		public static List<SyncConnectedClient> ConnectedClients 
		{
			get => IsClientActive 
				? _client.CurrentRoom.ConnectedClients.Values.ToList()
				: null;
		}

		/// <summary>
		/// If the Client created the Room that they are currently connected to.
		/// </summary>
		public static bool IsRoomOwner 
		{
			get => IsClientActive && _client.IsRoomOwner;
		}

		public static readonly List<SyncMessage> SyncMessages = new();

		#endregion

		#region internal fields

		internal static Action<uint, SyncConnectedClient, byte[]> DataReceived;

		#endregion

		#region private fields

		private static SyncClient _client;

		private readonly static Dictionary<uint, SyncModule> _registeredModules = new();

		private static readonly ConcurrentQueue<Action> _mainThreadActions = new();

		#endregion

		#region unity lifecycle

		static SyncManager()
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
			_client = new();
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

		private static void OnDataReceived(uint moduleHash, SyncConnectedClient client, byte[] data)
		{
			if (_registeredModules.TryGetValue(moduleHash, out SyncModule module))
			{
				_mainThreadActions.Enqueue(() => module.OnReceiveData(client, data));
			}
		}

		#endregion

		#region internal methods

		internal static uint RegisterModule(SyncModule module)
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
			uint hash = SyncCRC32.CRC32Bytes(Encoding.ASCII.GetBytes(moduleValues));

			if (_registeredModules.TryGetValue(hash, out _))
			{
				Debug.LogError("Only one Module of each type can be registered!");
				return 0;
			}

			_registeredModules.Add(hash, module);
			return hash;
		}

		internal static void UnregisterModule(uint moduleHash)
		{
			if (!_registeredModules.Remove(moduleHash))
				Debug.LogWarning($"The given Module could not be found!");
		}

		internal static bool IsModuleRegistered(uint moduleHash)
		{
			return _registeredModules.TryGetValue(moduleHash, out _);
		}

		internal static void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				Debug.LogError("The Client must be active and connected to a Room before Data can be send!");
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataReliable(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				Debug.LogError("The Client must be active and connected to a Room before Data can be send!");
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataReliableUnordered(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				Debug.LogError("The Client must be active and connected to a Room before Data can be send!");
				onDataSend?.Invoke(false);
				return;
			}

			if (data.Length > 1200)
			{
				Debug.LogError("Only reliable Packets can be larger than 1200 Bytes!");
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataUnreliable(moduleHash, data, onDataSend, receiver);
		}

		internal static void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			if (!IsClientActive || !IsConnectedToRoom)
			{
				Debug.LogError("The Client must be active and connected to a Room before Data can be send!");
				onDataSend?.Invoke(false);
				return;
			}

			if (data.Length > 1200)
			{
				Debug.LogError("Only reliable Packets can be larger than 1200 Bytes!");
				onDataSend?.Invoke(false);
				return;
			}

			_client.SendDataUnreliableUnordered(moduleHash, data, onDataSend, receiver);
		}

		#endregion

		#region public methods

		/// <summary>
		/// Retrieves the Settings used for the Sync Client.
		/// </summary>
		/// <returns></returns>
		public static SyncSettings GetSyncSettings()
		{
			return SyncSettings.GetOrCreateSettings();
		}

		/// <summary>
		/// Resets the Client. Use this, when Exceptions ocurred or the Socket was closed, to reconnect to the network.
		/// </summary>
		/// <returns><see langword="true"/> if the Client is Active after the Reset</returns>
		public static bool ResetClient()
		{
			if (_client != null)
			{
				_client.Dispose();
			}

			_client = new();
			return IsClientActive;
		}

		/// <summary>
		/// Connect to a Room or create a new one. Membership in a Room is required before Data can be send.
		/// </summary>
		/// <param name="roomname">Name of the Room</param>
		/// <param name="createroom">Wether an existing Room should be joined or a new Room should be created.</param>
		/// <returns><see langword="true"/> if the Client successfully connected to the Room</returns>
		public static bool ConnectToRoom(string roomname, bool createroom = false)
		{
			if (_client == null)
			{
				_client = new();
			}

			return _client.ConnectToRoom(roomname, createroom);
		}

		/// <summary>
		/// Disconnects the Client from the Room
		/// </summary>
		public static void DisconnectFromRoom()
		{
			if (_client == null)
			{
				_client = new();
				return;
			}

			_client.DisconnectFromRoom();
		}

		public static void AddSyncMessage(SyncMessage message)
		{
			_mainThreadActions.Enqueue(() => {
				SyncMessages.Add(message);
				OnSyncMessageAdded?.Invoke();
			});
		}

		#endregion
	}
}
