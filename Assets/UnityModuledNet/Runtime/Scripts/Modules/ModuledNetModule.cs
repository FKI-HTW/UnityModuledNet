using System;
using System.Text;
using UnityEngine;
using CENTIS.UnityModuledNet.Managing;

namespace CENTIS.UnityModuledNet.Modules
{
    public abstract class ModuledNetModule : IDisposable
    {
		/// <summary>
		/// The ID used to differentiate between different Modules. This ID can't be longer than 30 characters and must use 
		/// ASCII Encoding.
		/// </summary>
		public abstract string ModuleID { get; }
		
		public byte[] ModuleIDBytes { get; private set; }
		
		public bool IsModuleRegistered => ModuledNetManager.IsModuleRegistered(ModuleIDBytes);

		public ModuledNetModule()
		{
			if (ModuleID.Length > ModuledNetSettings.MODULE_ID_LENGTH || Encoding.UTF8.GetByteCount(ModuleID) != ModuleID.Length)
			{
				throw new Exception("The Module ID has to be shorter than 30 characters and use ASCII Encoding!");
			}

			ModuleIDBytes = Encoding.ASCII.GetBytes(ModuleID.PadRight(ModuledNetSettings.MODULE_ID_LENGTH));

			if (!RegisterModule())
			{
				Debug.LogError("The Module couldn't be registered!");
				return;
			}

			ModuledNetManager.OnAwake += Awake;
			ModuledNetManager.OnStart += Start;
			ModuledNetManager.OnUpdate += Update;
			ModuledNetManager.OnServerDiscoveryActivated += ServerDiscoveryActivated;
			ModuledNetManager.OnServerDiscoveryDeactivated += ServerDiscoveryDeactivated;
			ModuledNetManager.OnConnected += Connected;
			ModuledNetManager.OnConnecting += Connecting;
			ModuledNetManager.OnDisconnected += Disconnected;
			ModuledNetManager.OnClientConnected += ClientConnected;
			ModuledNetManager.OnClientDisconnected += ClientDisconnected;
			ModuledNetManager.OnConnectedClientListChanged += ConnectedClientListChanged;
			ModuledNetManager.OnOpenServerListChanged += OpenServerListChanged;
		}

		~ModuledNetModule()
		{
			Dispose(false);
		}

		/// <summary>
		/// IDisposable implementation for the Module. Can be overriden in child with custom Dispose method.
		/// If overriden, it is recommended to call base.Dispose() to ensure event listeners and the module are unregistered.
		/// </summary>
		public virtual void Dispose()
		{
			Dispose(true);
		}

		private void Dispose(bool isDisposing)
		{
			if (isDisposing)
			{
				ModuledNetManager.OnAwake -= Awake;
				ModuledNetManager.OnStart -= Start;
				ModuledNetManager.OnUpdate -= Update;
				ModuledNetManager.OnServerDiscoveryActivated -= ServerDiscoveryActivated;
				ModuledNetManager.OnServerDiscoveryDeactivated -= ServerDiscoveryDeactivated;
				ModuledNetManager.OnConnected -= Connected;
				ModuledNetManager.OnConnecting -= Connecting;
				ModuledNetManager.OnDisconnected -= Disconnected;
				ModuledNetManager.OnClientConnected -= ClientConnected;
				ModuledNetManager.OnClientDisconnected -= ClientDisconnected;
				ModuledNetManager.OnConnectedClientListChanged -= ConnectedClientListChanged;
				ModuledNetManager.OnOpenServerListChanged -= OpenServerListChanged;
			}

			UnregisterModule();
		}

		/// <summary>
		/// Unregisters the Module if it is already registered.
		/// </summary>
		public void UnregisterModule()
		{
			if (IsModuleRegistered)
				ModuledNetManager.UnregisterModule(ModuleIDBytes);
		}

		/// <summary>
		/// Registers the Module with the ModuledNet Manager.
		/// </summary>
		/// <returns><see langword="true"/> if the module was successfully registered or was already registered</returns>
		public bool RegisterModule()
		{
			if (!IsModuleRegistered)
				return ModuledNetManager.RegisterModule(this);
			return IsModuleRegistered;
		}

		public virtual void Awake() { }
		public virtual void Start() { }
		public virtual void Update() { }
		public virtual void ServerDiscoveryActivated() { }
		public virtual void ServerDiscoveryDeactivated() { }
		public virtual void Connected() { }
		public virtual void Connecting() { }
		public virtual void Disconnected() { }
		public virtual void ClientConnected(byte clientID) { }
		public virtual void ClientDisconnected(byte clientID) { }
		public virtual void ConnectedClientListChanged() { }
		public virtual void OpenServerListChanged() { }

        public abstract void OnReceiveData(byte sender, byte[] data);
        public abstract void SendData(byte[] data, Action<bool> onDataSend, byte? receiver = null);
    }

	public abstract class ReliableModule : ModuledNetModule
	{
		/// <summary>
		/// Sends Data over Reliable UDP. This guarantees the arrival of all Data Packets in the correct Order.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Server that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
		{
			if (ModuledNetManager.IsConnected && IsModuleRegistered)
				ModuledNetManager.SendDataReliable(ModuleIDBytes, data, onDataSend, receiver);
		}
	}

	public abstract class ReliableUnorderedModule : ModuledNetModule
	{
		/// <summary>
		/// Sends Data over Reliable UDP. This guarantees the arrival of all Data Packets without taking the Order into account.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Server that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
		{
			if (ModuledNetManager.IsConnected && IsModuleRegistered)
				ModuledNetManager.SendDataReliableUnordered(ModuleIDBytes, data, onDataSend, receiver);
		}
	}

	public abstract class UnreliableModule : ModuledNetModule
	{
		/// <summary>
		/// Sends Data over Unreliable UDP. 
		/// This ensures that old Packets don't overwrite new Updates, but it doesn't guarantee that all Packets actually arrive.
		/// The size of the Data array must be below the MTU.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Server that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
		{
			if (ModuledNetManager.IsConnected && IsModuleRegistered)
				ModuledNetManager.SendDataUnreliable(ModuleIDBytes, data, onDataSend, receiver);
		}
	}

	public abstract class UnreliableUnorderedModule : ModuledNetModule
	{
		/// <summary>
		/// Sends Data over Unreliable UDP. 
		/// This ignores the Order that Packets were send in and doesn't guarantee that all Packets actually arrive.
		/// The size of the Data array must be below the MTU.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Server that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend = null, byte? receiver = null)
		{
			if (ModuledNetManager.IsConnected && IsModuleRegistered)
				ModuledNetManager.SendDataUnreliableUnordered(ModuleIDBytes, data, onDataSend, receiver);
		}
	}
}
