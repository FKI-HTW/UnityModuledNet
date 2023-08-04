using CENTIS.UnityModuledNet.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;
using CENTIS.UnityModuledNet.Managing;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet.Modules
{
    public class ClientVisualiserModule : UnreliableModule
    {
        public override string ModuleID => "ClientVisualiserModule";

        private const string PARENT_NAME = "ClientVisualiser Parent";
        private readonly Dictionary<byte, ClientVisualiser> _visualisers = new();

        private Vector3 _lastCameraPosition;
        private Vector3 _lastCameraRotation;
        private int _clientVisualiserDelay = ClientVisualiserSettings.Settings.SyncDelay;

        private Transform _visualiserParent;

        #region lifecycle

        public ClientVisualiserModule()
        {
            ModuledNetManager.OnConnected += CreateVisualiserParent;
            ModuledNetManager.OnDisconnected += DestroyVisualiserParent;
            ModuledNetManager.OnClientDisconnected += RemoveConnectedClient;
        }

        public override void Dispose()
        {
            ModuledNetManager.OnConnected -= CreateVisualiserParent;
            ModuledNetManager.OnDisconnected -= DestroyVisualiserParent;
            ModuledNetManager.OnClientDisconnected -= RemoveConnectedClient;

            if (_visualiserParent != null)
                DestroyVisualiserParent();
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

        private void CreateVisualiserParent()
        {
            GameObject parent = GameObject.Find(PARENT_NAME);
            _visualiserParent = parent == null ? new GameObject().transform : parent.transform;
            _visualiserParent.name = PARENT_NAME;
        }

        private void DestroyVisualiserParent()
        {
            if (_visualiserParent == null) return;

#if UNITY_EDITOR
            GameObject.DestroyImmediate(_visualiserParent?.gameObject);
#else
			GameObject.Destroy(_visualiserParent?.gameObject);
#endif
            _visualiserParent = null;
        }

        public void FixOrphanedVisualiser()
        {
            if (_visualiserParent != null) DestroyVisualiserParent();
            else
            {
                _visualiserParent = GameObject.FindObjectOfType<ClientVisualiser>()?.transform.parent;
                DestroyVisualiserParent();
            }
        }

        private void RemoveConnectedClient(byte id)
        {
            if (!_visualisers.Remove(id, out ClientVisualiser visualiser))
                return;

            if (visualiser == null) return;
#if UNITY_EDITOR
            GameObject.DestroyImmediate(visualiser.gameObject);
#else
			GameObject.Destroy(visualiser.gameObject);
#endif
            visualiser = null;
        }

        #endregion

        #region receive data

        public override void OnReceiveData(byte sender, byte[] data)
        {
            if (!ModuledNetManager.ConnectedClients.TryGetValue(sender, out ClientInformation client))
                return;

            if (ModuledNetSettings.Settings.Debug)
                Debug.Log($"Received Camera Update from {client}");

            int size = sizeof(float);
            byte[] positionX = GetBytesFromArray(data, 0 * size, size);
            byte[] positionY = GetBytesFromArray(data, 1 * size, size);
            byte[] positionZ = GetBytesFromArray(data, 2 * size, size);
            byte[] rotationX = GetBytesFromArray(data, 3 * size, size);
            byte[] rotationY = GetBytesFromArray(data, 4 * size, size);
            byte[] rotationZ = GetBytesFromArray(data, 5 * size, size);

            if (!_visualisers.TryGetValue(sender, out ClientVisualiser visualiser))
            {
                GameObject obj = GameObject.Instantiate(ClientVisualiserSettings.Settings.ClientVisualiser.gameObject, _visualiserParent);
                visualiser = obj.GetComponent<ClientVisualiser>();
                visualiser.UpdateVisualiser(client.ID, client.Username, client.Color);
                _visualisers.Add(sender, visualiser);
            }

            if (visualiser)
            {
                var position = new Vector3(BitConverter.ToSingle(positionX),
                    BitConverter.ToSingle(positionY),
                    BitConverter.ToSingle(positionZ));
                var rotation = new Vector3(BitConverter.ToSingle(rotationX),
                    BitConverter.ToSingle(rotationY),
                    BitConverter.ToSingle(rotationZ));
                visualiser.SetTransform(position, rotation, ClientVisualiserSettings.Settings.Lerp);

                if (!visualiser.gameObject.activeSelf)
                    visualiser.gameObject.SetActive(true);
            }
        }

        #endregion

        #region send data

        private void CurrentCameraMoved(Transform camera)
        {
            // limit camera syncs
            if (_clientVisualiserDelay < ClientVisualiserSettings.Settings.SyncDelay)
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
