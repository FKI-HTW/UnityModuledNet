using System.Net;

namespace CENTIS.UnityModuledNet
{
    public abstract class SyncManagerDecorator
    {
		public SyncManagerDecorator()
		{
			SyncManager.OnAwake += Awake;
			SyncManager.OnStart += Start;
			SyncManager.OnUpdate += Update;

			SyncManager.OnSyncMessageAdded += SyncMessageAdded;
			SyncManager.OnLocalClientConnected += LocalClientConnected;
			SyncManager.OnLocalClientDisconnected += LocalClientDisconnected;
			SyncManager.OnConnectedToRoom += ConnectedToRoom;
			SyncManager.OnDisconnectedFromRoom += DisconnectedFromRoom;
			SyncManager.OnClientConnected += ClientConnected;
			SyncManager.OnClientDisconnected += ClientDisconnected;
		}

		~SyncManagerDecorator()
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
				SyncManager.OnAwake -= Awake;
				SyncManager.OnStart -= Start;
				SyncManager.OnUpdate -= Update;

				SyncManager.OnSyncMessageAdded -= SyncMessageAdded;
				SyncManager.OnLocalClientConnected -= LocalClientConnected;
				SyncManager.OnLocalClientDisconnected -= LocalClientDisconnected;
				SyncManager.OnConnectedToRoom -= ConnectedToRoom;
				SyncManager.OnDisconnectedFromRoom -= DisconnectedFromRoom;
				SyncManager.OnClientConnected -= ClientConnected;
				SyncManager.OnClientDisconnected -= ClientDisconnected;
			}
		}

		protected virtual void Awake() { }
		protected virtual void Start() { }
		protected virtual void Update() { }

		protected virtual void SyncMessageAdded() { }
		protected virtual void LocalClientConnected() { }
		protected virtual void LocalClientDisconnected() { }
		protected virtual void ConnectedToRoom() { }
		protected virtual void DisconnectedFromRoom() { }
		protected virtual void ClientConnected(IPAddress ip) { }
		protected virtual void ClientDisconnected(IPAddress ip) { }
	}
}
