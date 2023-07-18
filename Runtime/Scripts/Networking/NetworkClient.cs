using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;
using CENTIS.UnityModuledNet.Networking.Packets;
using CENTIS.UnityModuledNet.Managing;

namespace CENTIS.UnityModuledNet.Networking
{
    internal sealed class NetworkClient : ANetworkSocket
    {
        #region private fields

        private readonly IPAddress _serverIP;
        private readonly ConcurrentDictionary<byte, ClientInformation> _connectedClients = new();

        private readonly ConcurrentQueue<ANetworkPacket> _packetsToSend = new();

        private readonly ConcurrentDictionary<ushort, ASequencedNetworkPacket> _receivedPacketsBuffer = new();
        private readonly ConcurrentDictionary<ushort, ConcurrentDictionary<ushort, DataPacket>> _receivedChunksBuffer = new();

        private readonly ConcurrentDictionary<ushort, byte[]> _sendPacketsBuffer = new();
        private readonly ConcurrentDictionary<(ushort, ushort), byte[]> _sendChunksBuffer = new();

        private ushort _unreliableLocalSequence = 0;
        private ushort _unreliableRemoteSequence = 0;
        private ushort _reliableLocalSequence = 0;
        private ushort _reliableRemoteSequence = 0;

        private string _tmpUsername;
        private Color32 _tmpColor;

        #endregion

        #region public properties

        /// <summary>
        /// Action for when a remote Client connected to the current Server and can now receive Messages.
        /// </summary>
        public event Action<byte> OnClientConnected;

        /// <summary>
        /// Action for when a remote Client disconnected from the current Server and can no longer receive any Messages.
        /// </summary>
        public event Action<byte> OnClientDisconnected;

        /// <summary>
        /// Action for when a Client was added or removed from ConnectedClients.
        /// </summary>
        public event Action OnConnectedClientListChanged;

        public override ConcurrentDictionary<byte, ClientInformation> ConnectedClients => _connectedClients;

        #endregion

        #region lifecycle

        public NetworkClient(IPAddress serverIP)
        {
            _serverIP = serverIP;
        }

        public void Connect(Action<bool> onConnectionEstablished)
        { 
            try
            {
                if (!CheckLocalIP(ModuledNetManager.LocalIP))
                {
                    Debug.LogError("No network interface possesses the given local IP!");
                    onConnectionEstablished?.Invoke(false);
                    return;
                }

                if (_serverIP == null)
                {
                    Debug.LogError("The given server IP is not a valid IP!");
                    onConnectionEstablished?.Invoke(false);
                    return;
                }

                if (ModuledNetSettings.Settings.Username.Length > 100 || !IsASCIIString(ModuledNetSettings.Settings.Username))
                {
                    Debug.LogError("The UserName must be shorter than 100 characters and be an ASCII string!");
                    onConnectionEstablished?.Invoke(false);
                    return;
                }

                _tmpUsername = ModuledNetSettings.Settings.Username;
                _tmpColor = ModuledNetSettings.Settings.Color;

                ConnectionStatus = ConnectionStatus.IsConnecting;

                _localIP = IPAddress.Parse(ModuledNetManager.LocalIP);
                _port = ModuledNetSettings.Settings.Port;
                _udpClient = new(_port);

                _listenerThread = new(() => ListenerThread(onConnectionEstablished)) { IsBackground = true };
                _listenerThread.Start();
                _senderThread = new(() => SenderThread()) { IsBackground = true };
                _senderThread.Start();

                ModuledNetManager.OnUpdate += Update;

                ModuledNetManager.AddModuledNetMessage(new("Connecting to Server..."));
                _packetsToSend.Enqueue(new ConnectionRequestPacket());
                _ = TimeoutEstablishConnection(onConnectionEstablished);
            }
            catch (Exception ex)
            {
                ConnectionStatus = ConnectionStatus.IsDisconnected;
                onConnectionEstablished?.Invoke(false);
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
                        Dispose();
                        ExceptionDispatchInfo.Capture(ex).Throw();
                        throw;
                }
                Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                if (disposing)
                {
                    ModuledNetManager.OnUpdate -= Update;
                }

                if (_listenerThread != null)
                {
                    _listenerThread.Abort();
                    _listenerThread.Join();
                }
                if (_senderThread != null)
                {
                    _senderThread.Abort();
                    _senderThread.Join();
                }

                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                }

                ConnectionStatus = ConnectionStatus.IsDisconnected;
                ServerInformation = null;
                ClientInformation = null;
            }
        }

        public void Update()
        {
            while (_mainThreadActions.Count > 0)
            {
                if (_mainThreadActions.TryDequeue(out Action action))
                    action?.Invoke();
            }
        }


        #endregion

        #region public methods

        // TODO : add reason
        public override void DisconnectFromServer()
        {
            if (!IsConnected)
                return;

            ConnectionClosedPacket connectionClosed = new();
            byte[] data = connectionClosed.Serialize();
            _udpClient.Send(data, data.Length, new(_serverIP, _port));

            ModuledNetManager.AddModuledNetMessage(new("Disconnected from Server!"));
            _mainThreadActions.Enqueue(() => Dispose());
        }

        public override void SendDataReliable(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null)
        {
            if (!CheckIfEligibleForSending(onDataSend, receiver))
                return;

            CreateDataSenderPackets(EPacketType.ReliableData, moduleID, data, onDataSend, receiver);
        }

        public override void SendDataReliableUnordered(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null)
        {
            if (!CheckIfEligibleForSending(onDataSend, receiver))
                return;

            CreateDataSenderPackets(EPacketType.ReliableUnorderedData, moduleID, data, onDataSend, receiver);
        }

        public override void SendDataUnreliable(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null)
        {
            if (!CheckIfEligibleForSending(onDataSend, receiver))
                return;

            CreateDataSenderPackets(EPacketType.UnreliableData, moduleID, data, onDataSend, receiver);
        }

        public override void SendDataUnreliableUnordered(byte[] moduleID, byte[] data, Action<bool> onDataSend, byte? receiver = null)
        {
            if (!CheckIfEligibleForSending(onDataSend, receiver))
                return;

            CreateDataSenderPackets(EPacketType.UnreliableUnorderedData, moduleID, data, onDataSend, receiver);
        }

        #endregion

        #region listener logic

        private void ListenerThread(Action<bool> onConnectionEstablished)
        {
            while (_disposeCount == 0)
            {
                try
                {   // get packet ip headers
                    IPEndPoint receiveEndpoint = new(IPAddress.Any, _port);
                    byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
                    IPAddress sender = receiveEndpoint.Address;
                    if (!sender.Equals(_serverIP))
                        continue;

                    // get packet type without chunked packet bit
                    byte typeBytes = receivedBytes[ModuledNetSettings.CRC32_LENGTH];
                    byte mask = 1 << 7;
                    typeBytes &= (byte)~mask;
                    EPacketType packetType = (EPacketType)typeBytes;

                    // handle individual packet types
                    switch (packetType)
                    {
                        case EPacketType.ServerInformation:
                            HandleServerInformationPacket(receivedBytes);
                            break;
                        case EPacketType.ACK:
                            HandleACKPacket(receivedBytes);
                            break;
                        case EPacketType.ConnectionChallenge:
                            HandleConnectionChallengePacket(receivedBytes);
                            break;
                        case EPacketType.ConnectionAccepted:
                            HandleConnectionAcceptedPacket(receivedBytes, onConnectionEstablished);
                            break;
                        case EPacketType.ConnectionDenied:
                            HandleConnectionDeniedPacket(receivedBytes, onConnectionEstablished);
                            break;
                        case EPacketType.ConnectionClosed:
                            HandleConnectionClosedPacket(receivedBytes);
                            break;
                        case EPacketType.ClientDisconnected:
                            HandleClientDisconnectedPacket(receivedBytes);
                            break;
                        case EPacketType.ReliableData:
                        case EPacketType.ReliableUnorderedData:
                        case EPacketType.UnreliableData:
                        case EPacketType.UnreliableUnorderedData:
                            if (!IsConnected)
                                break;

                            ServerInformation.LastHeartbeat = DateTime.Now;
                            DataPacket dataPacket = new(packetType, receivedBytes);
                            if (!dataPacket.TryDeserialize())
                                break;

                            if (dataPacket.IsChunked)
                            {
                                HandleChunkedDataPacket(dataPacket);
                                break;
                            }

                            HandleSequencedPacket(dataPacket);
                            break;
                        case EPacketType.ClientInfo:
                            if (!IsConnected)
                                break;

                            ServerInformation.LastHeartbeat = DateTime.Now;
                            ClientInfoPacket clientInfoPacket = new(receivedBytes);
                            if (!clientInfoPacket.TryDeserialize())
                                break;

                            HandleSequencedPacket(clientInfoPacket);
                            break;
                        default: break;
                    }
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
                        case SocketException:
                        case ObjectDisposedException:
                            Debug.LogError(ex.ToString());
                            _mainThreadActions.Enqueue(() => Dispose());
                            return;
                        default:
                            _mainThreadActions.Enqueue(() => Dispose());
                            ExceptionDispatchInfo.Capture(ex).Throw();
                            throw;
                    }
                }
            }
        }

        private void HandleServerInformationPacket(byte[] packet)
        {
            if (!IsConnected)
                return;

            ServerInformationPacket serverInformation = new(packet);
            if (!serverInformation.TryDeserialize())
                return;

            ServerInformation = new(_serverIP, serverInformation.Servername, serverInformation.MaxNumberOfClients);
        }

        private void HandleACKPacket(byte[] packet)
        {
            if (!IsConnected)
                return;

            ServerInformation.LastHeartbeat = DateTime.Now;
            ACKPacket ack = new(packet);
            if (!ack.TryDeserialize())
                return;

            if (ack.IsChunked)
                _sendChunksBuffer.TryRemove((ack.Sequence, ack.SliceNumber), out _);
            else
                _sendPacketsBuffer.TryRemove(ack.Sequence, out _);
        }

        private void HandleConnectionChallengePacket(byte[] packet)
        {
            if (ConnectionStatus != ConnectionStatus.IsConnecting)
                return;

            ConnectionChallengePacket connectionChallenge = new(packet);
            if (!connectionChallenge.TryDeserialize())
                return;

            ChallengeAnswerPacket challengeAnswer = new(connectionChallenge.Challenge, _tmpUsername, _tmpColor);
            _packetsToSend.Enqueue(challengeAnswer);
        }

        private void HandleConnectionAcceptedPacket(byte[] packet, Action<bool> onConnectionEstablished)
        {
            if (ConnectionStatus != ConnectionStatus.IsConnecting)
                return;

            ConnectionAcceptedPacket connectionAccepted = new(packet);
            if (!connectionAccepted.TryDeserialize())
                return;

            ClientInformation = new(connectionAccepted.ClientID, _tmpUsername, _tmpColor);
            ServerInformation = new(_serverIP, connectionAccepted.Servername, connectionAccepted.MaxNumberConnectedClients);

            _mainThreadActions.Enqueue(() => onConnectionEstablished?.Invoke(true));
            _mainThreadActions.Enqueue(() => ConnectionStatus = ConnectionStatus.IsConnected);

            ModuledNetManager.AddModuledNetMessage(new("Connected to Server!"));
        }

        private void HandleConnectionDeniedPacket(byte[] packet, Action<bool> onConnectionEstablished)
        {
            if (ConnectionStatus != ConnectionStatus.IsConnecting)
                return;

            ConnectionDeniedPacket connectionDenied = new(packet);
            if (!connectionDenied.TryDeserialize())
                return;

            _mainThreadActions.Enqueue(() => onConnectionEstablished?.Invoke(false));
            _mainThreadActions.Enqueue(() => Dispose());

            ModuledNetManager.AddModuledNetMessage(new("Connection has been denied!"));
        }

        private void HandleConnectionClosedPacket(byte[] packet)
        {
            if (!IsConnected)
                return;

            ConnectionClosedPacket connectionClosed = new(packet);
            if (!connectionClosed.TryDeserialize())
                return;

            _mainThreadActions.Enqueue(() => Dispose());

            ModuledNetManager.AddModuledNetMessage(new("Connection was closed..."));
        }

        private void HandleClientDisconnectedPacket(byte[] packet)
        {
            if (!IsConnected)
                return;

            ClientDisconnectedPacket clientDisconnected = new(packet);
            if (!clientDisconnected.TryDeserialize())
                return;

            if (!_connectedClients.TryRemove(clientDisconnected.ClientID, out ClientInformation client))
                return;

            _mainThreadActions.Enqueue(() => OnClientDisconnected?.Invoke(clientDisconnected.ClientID));
            _mainThreadActions.Enqueue(() => OnConnectedClientListChanged.Invoke());

            ModuledNetManager.AddModuledNetMessage(new($"Client {client} disconnected!"));
        }

        private void HandleSequencedPacket(ASequencedNetworkPacket packet)
        {   // unreliable packet sequence
            if (IsUnreliableSequence(packet.Type))
            {   // ignore old packets unless they are unordered
                if (!IsNewPacket(packet.Sequence, _unreliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                    return;

                // update sequence and consume packet
                _unreliableRemoteSequence = packet.Sequence;
                ConsumeSequencedPacket(packet);
                return;
            }

            // reliable packet sequence
            {
                // send ACK for reliable sequence
                _packetsToSend.Enqueue(new ACKPacket(packet.Sequence));

                // ignore old packets unless they are unordered
                if (!IsNewPacket(packet.Sequence, _reliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                    return;

                if (!IsNextPacket(packet.Sequence, _reliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                {   // if a packet is missing in the sequence keep it in the buffer
                    _receivedPacketsBuffer.TryAdd(packet.Sequence, packet);
                    return;
                }

                // update sequence and consume packet
                _reliableRemoteSequence = packet.Sequence;
                ConsumeSequencedPacket(packet);

                // apply all packets from that senders buffer that are now next in the sequence
                ushort sequence = packet.Sequence;
                while (_receivedPacketsBuffer.Count > 0)
                {
                    sequence++;
                    if (!_receivedPacketsBuffer.TryRemove(sequence, out ASequencedNetworkPacket bufferedPacket))
                        break;

                    // update sequence and consume packet
                    _reliableRemoteSequence = sequence;
                    ConsumeSequencedPacket(bufferedPacket);
                }
            }
        }

        private void HandleChunkedDataPacket(DataPacket packet)
        {   // send ACK
            _packetsToSend.Enqueue(new ACKPacket(packet.Sequence, packet.SliceNumber));

            // ignore old packets unless they are unordered
            if (!IsNewPacket(packet.Sequence, _reliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                return;

            if (!_receivedChunksBuffer.TryGetValue(packet.Sequence, out ConcurrentDictionary<ushort, DataPacket> bufferedChunk))
            {   // create chunked packet if it doesn't exist yet
                bufferedChunk = new();
                _receivedChunksBuffer.TryAdd(packet.Sequence, bufferedChunk);
            }

            // add slice to chunk and return if chunk is not complete
            bufferedChunk.AddOrUpdate(packet.SliceNumber, packet, (key, oldValue) => oldValue = packet);
            if (bufferedChunk.Count != packet.NumberOfSlices)
                return;

            // concatenate slices to complete packet and remove it from chunk buffer
            List<byte> dataBytes = new();
            for (ushort i = 0; i < packet.NumberOfSlices; i++)
            {
                if (!bufferedChunk.TryGetValue(i, out DataPacket sliceData))
                    return;
                dataBytes.AddRange(sliceData.Data);
            }
            byte[] data = dataBytes.ToArray();
            DataPacket dataPacket = new(packet.Type, packet.ModuleID, data, null, packet.ClientID);
            _receivedChunksBuffer.TryRemove(packet.Sequence, out _);

            if (!IsNextPacket(packet.Sequence, _reliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
            {   // if a packet is missing in the sequence keep it in the buffer
                _receivedPacketsBuffer.TryAdd(packet.Sequence, dataPacket);
                return;
            }

            // update sequence and consume packet
            _reliableRemoteSequence = packet.Sequence;
            ConsumeSequencedPacket(dataPacket);
        }

        private void ConsumeSequencedPacket(ASequencedNetworkPacket packet)
        {
            switch (packet)
            {
                case DataPacket dataPacket:
                    // notify manager of received data, consuming the packet
                    _mainThreadActions.Enqueue(() => ModuledNetManager.DataReceived?.Invoke(dataPacket.ModuleID, dataPacket.ClientID, dataPacket.Data));
                    break;
                case ClientInfoPacket clientInfoPacket:
                    // add or update connected client
                    ClientInformation newClient = new(clientInfoPacket.ClientID, clientInfoPacket.Username, clientInfoPacket.Color);

                    if (_connectedClients.TryGetValue(clientInfoPacket.ClientID, out ClientInformation oldClient))
                    {
                        _connectedClients.TryUpdate(clientInfoPacket.ClientID, oldClient, newClient);
                    }
                    else
                    {
                        _connectedClients.TryAdd(clientInfoPacket.ClientID, newClient);
                        _mainThreadActions.Enqueue(() => OnClientConnected?.Invoke(clientInfoPacket.ClientID));
                        ModuledNetManager.AddModuledNetMessage(new($"Client {newClient} connected!"));
                    }

                    _mainThreadActions.Enqueue(() => OnConnectedClientListChanged?.Invoke());
                    break;
                default: break;
            }
        }

        #endregion

        #region sender logic

        private void SenderThread()
        {
            while (_disposeCount == 0)
            {
                try
                {
                    if (_packetsToSend.Count == 0 || !_packetsToSend.TryDequeue(out ANetworkPacket packet))
                        continue;

                    byte[] data;
                    switch (packet)
                    {
                        case AConnectionNetworkPacket c:
                            data = c.Serialize();
                            _udpClient.Send(data, data.Length, new(_serverIP, _port));
                            continue;
                        case ASequencedNetworkPacket s:
                            // serialize with unreliable sequence
                            if (IsUnreliableSequence(s.Type))
                            {
                                _unreliableLocalSequence++;
                                data = s.Serialize(_unreliableLocalSequence);
                                _udpClient.Send(data, data.Length, new(_serverIP, _port));

                                if (s is DataPacket ud)
                                {   // invoke callback once it was send
                                    ud.Callback?.Invoke(true);
                                }

                                continue;
                            }

                            // serialize with reliable sequence
                            {
                                _reliableLocalSequence++;
                                if (s is DataPacket rd)
                                {
                                    if (rd.IsChunked)
                                    {   // send slices individually
                                        for (ushort i = 0; i < rd.NumberOfSlices; i++)
                                        {
                                            rd.SliceNumber = i;
                                            data = s.Serialize(_reliableLocalSequence);
                                            _udpClient.Send(data, data.Length, new(_serverIP, _port));
                                            _sendChunksBuffer.TryAdd((_reliableLocalSequence, rd.SliceNumber), data);
                                            _ = ResendSliceData((_reliableLocalSequence, rd.SliceNumber));
                                        }
                                        rd.Callback?.Invoke(true);
                                        continue;
                                    }
                                    else
                                    {   // send data packet as one
                                        data = s.Serialize(_reliableLocalSequence);
                                        _udpClient.Send(data, data.Length, new(_serverIP, _port));
                                        _sendPacketsBuffer.TryAdd(_reliableLocalSequence, data);
                                        _ = ResendPacketData(_reliableLocalSequence);
                                        rd.Callback?.Invoke(true);
                                        continue;
                                    }
                                }

                                // send sequenced packet
                                data = s.Serialize(_reliableLocalSequence);
                                _udpClient.Send(data, data.Length, new(_serverIP, _port));
                                _sendPacketsBuffer.TryAdd(_reliableLocalSequence, data);
                                _ = ResendPacketData(_reliableLocalSequence);
                                continue;
                            }
                    }
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
                        case SocketException:
                        case ObjectDisposedException:
                            Debug.LogError(ex.ToString());
                            _mainThreadActions.Enqueue(() => Dispose());
                            return;
                        default:
                            _mainThreadActions.Enqueue(() => Dispose());
                            ExceptionDispatchInfo.Capture(ex).Throw();
                            throw;
                    }
                }
            }
        }

        /// <summary>
        /// Retry sending a Packet Slice after a Delay and within a maximum number of retries
        /// </summary>
        /// <param name="client"></param>
        /// <param name="sequence"></param>
        /// <param name="retries"></param>
        /// <returns></returns>
        private async Task ResendSliceData((ushort, ushort) sequence, int retries = 0)
        {
            await Task.Delay((int)(ModuledNetSettings.Settings.RTT * 1.25f));
            if (_sendChunksBuffer.TryGetValue(sequence, out byte[] data))
            {
                _udpClient.Send(data, data.Length, new(_serverIP, _port));
                if (retries < ModuledNetSettings.Settings.MaxNumberResendReliablePackets)
                    _ = ResendSliceData(sequence, retries + 1);
                else
                    DisconnectFromServer();
            }
        }


        /// <summary>
        /// Retry sending a Packet after a Delay and within a maximum number of retries
        /// </summary>
        /// <param name="client"></param>
        /// <param name="sequence"></param>
        /// <param name="retries"></param>
        /// <returns></returns>
        private async Task ResendPacketData(ushort sequence, int retries = 0)
        {
            await Task.Delay((int)(ModuledNetSettings.Settings.RTT * 1.25f));
            if (_sendPacketsBuffer.TryGetValue(sequence, out byte[] data))
            {
                _udpClient.Send(data, data.Length, new(_serverIP, _port));
                if (retries < ModuledNetSettings.Settings.MaxNumberResendReliablePackets)
                    _ = ResendPacketData(sequence, retries + 1);
                else
                    DisconnectFromServer();
            }
        }

        #endregion

        #region helper methods

        /// <summary>
        /// Stops the process of establishing a Connection, if it did not success within a given timeout.
        /// </summary>
        /// <returns></returns>
        private async Task TimeoutEstablishConnection(Action<bool> onConnectionEstablished)
        {
            await Task.Delay(ModuledNetSettings.Settings.ServerConnectionTimeout);
            if (!IsConnected)
            {
                Dispose();
                onConnectionEstablished?.Invoke(false);
            }
        }

        /// <summary>
        /// Checks if the given Receiver exists or is a multicast.
        /// </summary>
        /// <param name="dataCallback"></param>
        /// <param name="receiver"></param>
        /// <returns></returns>
        private bool CheckIfEligibleForSending(Action<bool> dataCallback, byte? receiver = null)
        {
            if (!IsConnected)
            {
                Debug.LogError("The local Client is currently not connected to a Server!");
                dataCallback?.Invoke(false);
                return false;
            }

            if (receiver != null && !_connectedClients.TryGetValue((byte)receiver, out ClientInformation _))
            {
                Debug.LogError("The given Receiver does not exist in the Server!");
                dataCallback?.Invoke(false);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates a Data Packet send by the Server to the given Receiver or all connected Clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="moduleHash"></param>
        /// <param name="data"></param>
        /// <param name="dataCallback"></param>
        /// <param name="receiver"></param>
        private void CreateDataSenderPackets(EPacketType type, byte[] moduleID, byte[] data, Action<bool> dataCallback, byte? receiver = null)
        {
            if (!IsDataPacket(type))
            {
                dataCallback?.Invoke(false);
                throw new Exception("This function only supports Data Packets!");
            }

            int mtu = ModuledNetSettings.Settings.MTU;
            if (data.Length > mtu && !IsReliableSequence(type))
            {
                Debug.LogError($"Only Reliable Packets can be larger than the MTU ({mtu} Bytes)!");
                dataCallback?.Invoke(false);
                return;
            }

            _packetsToSend.Enqueue(new DataPacket(type, moduleID, data, dataCallback, receiver));
        }

        private static bool CompareByteArrays(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            return a.SequenceEqual(b);
        }

        #endregion
    }
}
