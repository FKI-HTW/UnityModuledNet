using System;
using UnityEngine;

namespace CENTIS.UnityModuledNet
{
    public abstract class ModuledNetModule
    {
		private uint _moduleHash;
		protected uint ModuleHash
		{
			get => _moduleHash;
			private set => _moduleHash = value;
		}

		public bool IsModuleRegistered
		{
			get => ModuledNetManager.IsModuleRegistered(ModuleHash);
		}

        public ModuledNetModule()
		{
			if (!RegisterModule())
			{
				Debug.LogError("The Module couldn't be registered!");
				return;
			}

			ModuledNetManager.OnAwake += Awake;
			ModuledNetManager.OnStart += Start;
			ModuledNetManager.OnUpdate += Update;
		}

		~ModuledNetModule()
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
				ModuledNetManager.OnAwake -= Awake;
				ModuledNetManager.OnStart -= Start;
				ModuledNetManager.OnUpdate -= Update;
			}

			UnregisterModule();
		}

		public void UnregisterModule()
		{
			if (IsModuleRegistered)
				ModuledNetManager.UnregisterModule(ModuleHash);
		}

		public bool RegisterModule()
		{
			if (!IsModuleRegistered)
				ModuleHash = ModuledNetManager.RegisterModule(this);
			return ModuleHash != 0;
		}

		public virtual void Awake() { }
		public virtual void Start() { }
		public virtual void Update() { }

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
		public override void SendData(byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			ModuledNetManager.SendDataReliable(ModuleHash, data, onDataSend, receiver);
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
		public override void SendData(byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			ModuledNetManager.SendDataReliableUnordered(ModuleHash, data, onDataSend, receiver);
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
		public override void SendData(byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			ModuledNetManager.SendDataUnreliable(ModuleHash, data, onDataSend, receiver);
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
		public override void SendData(byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			ModuledNetManager.SendDataUnreliableUnordered(ModuleHash, data, onDataSend, receiver);
		}
	}
}
