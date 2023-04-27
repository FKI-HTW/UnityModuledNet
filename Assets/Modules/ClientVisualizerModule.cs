using CENTIS.UnityModuledNet.Networking;
using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet.Modules
{
    public class ClientVisualiserModule : SyncUnreliableModule
    {
		private static readonly SyncSettings _settings = SyncManager.GetSyncSettings();

		private readonly Dictionary<SyncConnectedClient, SyncClientVisualiser> _visualisers = new();

		private Vector3 _lastCameraPosition;
		private Vector3 _lastCameraRotation;
		private int _clientVisualiserDelay = _settings.ClientVisualiserDelay;

		private Transform _visualiserParent;

		#region lifecycle

		public ClientVisualiserModule()
		{
			SyncManager.OnClientDisconnected += RemoveConnectedClient;

			_visualiserParent = new GameObject().transform;
			_visualiserParent.name = "SceneSync Visualiser Parent";
			// TODO : cant hide the object if it's not automatically deleted, cuz a user won't be able to delete it neither
			//_visualiserParent.gameObject.hideFlags = HideFlags.HideInHierarchy;
		}

		public override void Dispose()
		{
			SyncManager.OnClientDisconnected -= RemoveConnectedClient;
#if UNITY_EDITOR
			GameObject.DestroyImmediate(_visualiserParent.gameObject);
#else
			GameObject.Destroy(_visualiserParent.gameObject);
#endif
			base.Dispose();
		}

		public override void Update()
		{
			_clientVisualiserDelay++;
#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView.camera.transform.hasChanged)
				CurrentCameraMoved(SceneView.lastActiveSceneView.camera.transform);
#else
			if (Camera.current.transform && Camera.current.transform.hasChanged)
				CurrentCameraMoved(Camera.current.transform);
#endif
		}

		#endregion

		#region receive data

		public void RemoveConnectedClient(IPAddress ip)
		{
			foreach (KeyValuePair<SyncConnectedClient, SyncClientVisualiser> client in _visualisers)
			{
				if (client.Key.IP.Equals(ip))
				{
					_visualisers.Remove(client.Key);
#if UNITY_EDITOR
					GameObject.DestroyImmediate(client.Value.gameObject);
#else
					GameObject.Destroy(client.Value.gameObject);
#endif
					break;
				}
			}
		}

		public override void OnReceiveData(SyncConnectedClient sender, byte[] data)
		{
			if (SyncManager.IsDebug)
				Debug.Log($"Received Camera Update from {sender.IP}");

			int size = sizeof(float);
			byte[] positionX = GetBytesFromArray(data, 0 * size, size);
			byte[] positionY = GetBytesFromArray(data, 1 * size, size);
			byte[] positionZ = GetBytesFromArray(data, 2 * size, size);
			byte[] rotationX = GetBytesFromArray(data, 3 * size, size);
			byte[] rotationY = GetBytesFromArray(data, 4 * size, size);
			byte[] rotationZ = GetBytesFromArray(data, 5 * size, size);

			sender.LastHeartbeat = DateTime.Now;
			if (!_visualisers.TryGetValue(sender, out SyncClientVisualiser visualiser))
			{
				GameObject obj = GameObject.Instantiate(_settings.ClientVisualiser.gameObject, _visualiserParent);
				visualiser = obj.GetComponent<SyncClientVisualiser>();
				visualiser.UpdateVisualiser(sender.IPString, sender.Username, sender.Color);
				_visualisers.Add(sender, visualiser);
			}

			visualiser.transform.position = new Vector3(BitConverter.ToSingle(positionX),
				BitConverter.ToSingle(positionY),
				BitConverter.ToSingle(positionZ));
			visualiser.transform.eulerAngles = new Vector3(BitConverter.ToSingle(rotationX),
				BitConverter.ToSingle(rotationY),
				BitConverter.ToSingle(rotationZ));
			if (!visualiser.gameObject.activeSelf)
				visualiser.gameObject.SetActive(true);
		}

		#endregion

		#region send data

		private void CurrentCameraMoved(Transform camera)
		{
			// limit camera syncs
			if (_clientVisualiserDelay < _settings.ClientVisualiserDelay)
			{
				camera.hasChanged = false;
				return;
			}

			if (_lastCameraPosition != null
				&& camera.position.Equals(_lastCameraPosition)
				&& camera.eulerAngles.Equals(_lastCameraRotation))
			{
				camera.hasChanged = false;
				return;
			}

			// TODO : combine de-/serializer
			// TODO : optimize this
			byte[] data = new byte[sizeof(float) * 6];
			Array.Copy(BitConverter.GetBytes(camera.position.x), 0, data, 0 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.position.y), 0, data, 1 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.position.z), 0, data, 2 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.x), 0, data, 3 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.y), 0, data, 4 * sizeof(float), sizeof(float));
			Array.Copy(BitConverter.GetBytes(camera.eulerAngles.z), 0, data, 5 * sizeof(float), sizeof(float));
			SendData(data, null);

			_lastCameraPosition = camera.position;
			_lastCameraRotation = camera.eulerAngles;
			camera.hasChanged = false;
			_clientVisualiserDelay = 0;
		}

		private static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
		{
			if (size == 0)
				size = array.Length - offset;

			byte[] bytes = new byte[size];
			Array.Copy(array, offset, bytes, 0, size);
			return bytes;
		}

		#endregion
	}
}
