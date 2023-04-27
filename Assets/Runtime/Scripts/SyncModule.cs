using CENTIS.UnityModuledNet.Networking;
using System;
using UnityEngine;

namespace CENTIS.UnityModuledNet
{
    public abstract class SyncModule
    {
		private uint _moduleHash;
		protected uint ModuleHash
		{
			get => _moduleHash;
			private set => _moduleHash = value;
		}

		public bool IsModuleRegistered
		{
			get => SyncManager.IsModuleRegistered(ModuleHash);
		}

        public SyncModule()
		{
			if (!RegisterModule())
			{
				Debug.LogError("The Module couldn't be registered!");
				return;
			}

			SyncManager.OnAwake += Awake;
			SyncManager.OnStart += Start;
			SyncManager.OnUpdate += Update;
		}

		~SyncModule()
		{
			Dispose(false);
		}

		public virtual void Dispose()
		{
			Dispose(true);
		}

		private void Dispose(bool isDisposing)
		{
			if (isDisposing)
			{
				SyncManager.OnAwake -= Awake;
				SyncManager.OnStart -= Start;
				SyncManager.OnUpdate -= Update;
				UnregisterModule();
			}
		}

		public void UnregisterModule()
		{
			if (IsModuleRegistered)
				SyncManager.UnregisterModule(ModuleHash);
		}

		public bool RegisterModule()
		{
			if (!IsModuleRegistered)
			{
				ModuleHash = SyncManager.RegisterModule(this);
				return ModuleHash != 0;
			}
			return false;
		}

		public virtual void Awake() { }
		public virtual void Start() { }
		public virtual void Update() { }

        public abstract void OnReceiveData(SyncConnectedClient sender, byte[] data);

        public abstract void SendData(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null);
    }

	public abstract class SyncReliableModule : SyncModule
	{
		/// <summary>
		/// Sends Data over Reliable UDP. This guarantees the arrival of all Data Packets in the correct Order.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			SyncManager.SendDataReliable(ModuleHash, data, onDataSend, receiver);
		}
	}

	public abstract class SyncReliableUnorderedModule : SyncModule
	{
		/// <summary>
		/// Sends Data over Reliable UDP. This guarantees the arrival of all Data Packets without taking the Order into account.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			SyncManager.SendDataReliableUnordered(ModuleHash, data, onDataSend, receiver);
		}
	}

	public abstract class SyncUnreliableModule : SyncModule
	{
		/// <summary>
		/// Sends Data over Unreliable UDP. 
		/// This ensures that old Packets don't overwrite new Updates, but it doesn't guarantee that all Packets actually arrive.
		/// The size of the Data array must be below 1200.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			SyncManager.SendDataUnreliable(ModuleHash, data, onDataSend, receiver);
		}
	}

	public abstract class SyncUnreliableUnorderedModule : SyncModule
	{
		/// <summary>
		/// Sends Data over Unreliable UDP. 
		/// This ignores the Order that Packets were send in and doesn't guarantee that all Packets actually arrive.
		/// The size of the Data array must be below 1200.
		/// </summary>
		/// <param name="data">The serialized Data that should be send.</param>
		/// <param name="onDataSend">This Action will be invoked once the Data failed to-/or was successfully send.</param>
		/// <param name="receiver">
		/// A Client in the same Room that should receive the Packet. If the Receiver is null, all Clients receive the Packet.
		/// </param>
		public override void SendData(byte[] data, Action<bool> onDataSend, SyncConnectedClient receiver = null)
		{
			SyncManager.SendDataUnreliableUnordered(ModuleHash, data, onDataSend, receiver);
		}
	}
}
