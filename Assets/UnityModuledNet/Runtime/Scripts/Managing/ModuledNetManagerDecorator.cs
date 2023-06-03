using System.Net;

namespace CENTIS.UnityModuledNet.Managing
{
    public abstract class ModuledNetManagerDecorator
    {
		public ModuledNetManagerDecorator()
		{
			ModuledNetManager.OnAwake += Awake;
			ModuledNetManager.OnStart += Start;
			ModuledNetManager.OnUpdate += Update;

			ModuledNetManager.OnSyncMessageAdded += SyncMessageAdded;
			ModuledNetManager.OnServerDiscoveryActivated += ServerDiscoveryActivated;
			ModuledNetManager.OnServerDiscoveryDeactivated += ServerDiscoveryDeactivated;
			ModuledNetManager.OnConnected += Connected;
			ModuledNetManager.OnDisconnected += Disconnected;
			ModuledNetManager.OnClientConnected += ClientConnected;
			ModuledNetManager.OnClientDisconnected += ClientDisconnected;
		}

		~ModuledNetManagerDecorator()
		{
			Dispose(false);
		}

		public virtual void Dispose()
		{
			Dispose(true);
		}

		private void Dispose(bool disposing)
		{
			if (disposing)
			{
				ModuledNetManager.OnAwake -= Awake;
				ModuledNetManager.OnStart -= Start;
				ModuledNetManager.OnUpdate -= Update;

				ModuledNetManager.OnSyncMessageAdded -= SyncMessageAdded;
				ModuledNetManager.OnServerDiscoveryActivated -= ServerDiscoveryActivated;
				ModuledNetManager.OnServerDiscoveryDeactivated -= ServerDiscoveryDeactivated;
				ModuledNetManager.OnConnected -= Connected;
				ModuledNetManager.OnDisconnected -= Disconnected;
				ModuledNetManager.OnClientConnected -= ClientConnected;
				ModuledNetManager.OnClientDisconnected -= ClientDisconnected;
			}
		}

		protected virtual void Awake() { }
		protected virtual void Start() { }
		protected virtual void Update() { }

		protected virtual void SyncMessageAdded() { }
		protected virtual void ServerDiscoveryActivated() { }
		protected virtual void ServerDiscoveryDeactivated() { }
		protected virtual void Connected() { }
		protected virtual void Disconnected() { }
		protected virtual void ClientConnected(byte id) { }
		protected virtual void ClientDisconnected(byte id) { }
	}
}
