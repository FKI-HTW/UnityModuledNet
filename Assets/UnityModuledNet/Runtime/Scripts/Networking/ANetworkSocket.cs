using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using CENTIS.UnityModuledNet.Networking.Packets;

namespace CENTIS.UnityModuledNet.Networking
{
    internal abstract class ANetworkSocket : IDisposable
    {
        #region protected fields

        protected UdpClient _udpClient;

        protected Thread _listenerThread;
        protected Thread _senderThread;

        protected int _disposeCount;

        protected readonly ConcurrentQueue<Action> _mainThreadActions = new();

        #endregion

        #region public properties

        /// <summary>
        /// Action for when connection to or creation of a server is being started.
        /// </summary>
        public event Action OnConnecting;

        /// <summary>
        /// Action for when successfully connecting to or creating a Server.
        /// </summary>
        public event Action OnConnected;

        /// <summary>
        /// Action for when disconnecting from or closing the Server.
        /// </summary>
        public event Action OnDisconnected;

        private EConnectionStatus _EConnectionStatus = EConnectionStatus.IsDisconnected;
        public EConnectionStatus EConnectionStatus
        {
            get => _EConnectionStatus;
            protected set
            {
                if (value == _EConnectionStatus)
                    return;

                _EConnectionStatus = value;
                switch (value)
                {
                    case EConnectionStatus.IsConnecting:
                        OnConnecting?.Invoke();
                        break;
                    case EConnectionStatus.IsConnected:
                        OnConnected?.Invoke();
                        break;
                    case EConnectionStatus.IsDisconnected:
                        OnDisconnected?.Invoke();
                        break;
                }
            }
        }

        public bool IsConnected => EConnectionStatus == EConnectionStatus.IsConnected;

        /// <summary>
        /// Information about the Local Server/the Server that you are connected to.
        /// </summary>
        public ServerInformation ServerInformation { get; protected set; }

        public ClientInformation ClientInformation { get; protected set; }

        public abstract ConcurrentDictionary<byte, ClientInformation> ConnectedClients { get; }

        #endregion

        #region lifecycle

        ~ANetworkSocket()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected abstract void Dispose(bool disposing);

        #endregion

        #region public methods

        public abstract void DisconnectFromServer();

        public abstract void SendDataReliable(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null);

        public abstract void SendDataReliableUnordered(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null);

        public abstract void SendDataUnreliable(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null);

        public abstract void SendDataUnreliableUnordered(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null);

        #endregion

        #region helper methods

        protected static int FindNextAvailablePort()
		{
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        private const ushort HALF_USHORT = ushort.MaxValue / 2;

        /// <summary>
        /// Checks if a packet is new by comparing the packets sequence number and the corresponding remote sequence number.
        /// Also handles ushort wrap-arounds by allowing packets that are smaller by half of the maximum value.
        /// </summary>
        /// <param name="packetSequence"></param>
        /// <param name="remoteSequence"></param>
        /// <returns>if the packet is new</returns>
        protected static bool IsNewPacket(ushort packetSequence, ushort remoteSequence)
        {
            return ((packetSequence > remoteSequence) && (packetSequence - remoteSequence <= HALF_USHORT))
                || ((packetSequence < remoteSequence) && (remoteSequence - packetSequence > HALF_USHORT));
        }

        /// <summary>
        /// Checks if a packet is next after remote sequence number.
        /// </summary>
        /// <param name="packetSequence"></param>
        /// <param name="remoteSequence"></param>
        /// <returns>if the packet is new</returns>
        protected static bool IsNextPacket(ushort packetSequence, ushort remoteSequence)
        {
            return (packetSequence == (ushort)(remoteSequence + 1))
                || (packetSequence == 0 && remoteSequence == ushort.MaxValue);
        }

        /// <summary>
        /// Returns wether or not the packet uses a reliable sequence.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        protected static bool IsReliableSequence(EPacketType type)
        {
            return type == EPacketType.ReliableData || type == EPacketType.ReliableUnorderedData
                || type == EPacketType.ClientInfo;
        }

        /// <summary>
        /// Returns wether or not the packet uses a unreliable sequence.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        protected static bool IsUnreliableSequence(EPacketType type)
        {
            return type == EPacketType.UnreliableData || type == EPacketType.UnreliableUnorderedData;
        }

        /// <summary>
        /// Returns wether or not the packet uses a ordered sequence.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        protected static bool IsOrderedSequence(EPacketType type)
        {
            return type == EPacketType.ReliableData || type == EPacketType.UnreliableData
                || type == EPacketType.ClientInfo;
        }

        /// <summary>
        /// Returns wether or not the packet uses a unordered sequence.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        protected static bool IsUnorderedSequence(EPacketType type)
        {
            return type == EPacketType.ReliableUnorderedData || type == EPacketType.UnreliableUnorderedData;
        }

        /// <summary>
        /// Returns wether or not the packet is a Data Packet.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        protected static bool IsDataPacket(EPacketType type)
        {
            return type == EPacketType.ReliableData || type == EPacketType.ReliableUnorderedData
                || type == EPacketType.UnreliableData || type == EPacketType.UnreliableUnorderedData;
        }

        /// <summary>
        /// Checks if string is ascii conform
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        protected static bool IsASCIIString(string str)
        {
            return (Encoding.UTF8.GetByteCount(str)) == str.Length;
        }

        protected static void DebugByteMessage(byte[] bytes, string msg, bool inBinary = false)
        {
            if (!ModuledNetSettings.Settings.Debug) return;

            foreach (byte d in bytes)
                msg += Convert.ToString(d, inBinary ? 2 : 16).PadLeft(inBinary ? 8 : 2, '0') + " ";
            Debug.Log(msg);
        }

        #endregion
    }
}
