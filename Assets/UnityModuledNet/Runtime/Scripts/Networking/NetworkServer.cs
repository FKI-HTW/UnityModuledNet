using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;
using CENTIS.UnityModuledNet.Networking.Packets;
using CENTIS.UnityModuledNet.Managing;

namespace CENTIS.UnityModuledNet.Networking
{
    internal sealed class NetworkServer : ANetworkSocket
    {
        #region private fields

        private IPEndPoint _localEndpoint;

        private Thread _heartbeatThread;

        private readonly ConcurrentDictionary<IPEndPoint, byte[]> _pendingConnections = new();

        private readonly ConcurrentDictionary<byte, IPEndPoint> _idIpTable = new();
        private readonly ConcurrentDictionary<IPEndPoint, ClientInformationSocket> _connectedClients = new();

        private readonly ConcurrentQueue<(IPEndPoint, ANetworkPacket)> _packetsToSend = new();

        #endregion

        #region public properties

        public string ServerName { get; private set; }

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

        public override ConcurrentDictionary<byte, ClientInformation> ConnectedClients => new(_connectedClients.ToDictionary(k => k.Value.ID, v => (ClientInformation)v.Value));

        #endregion

        #region lifecycle

        public NetworkServer(string serverName)
        {
            ServerName = serverName;
        }

        public void StartServer(Action<bool> onConnectionEstablished)
        { 
            try
            {
                if (!ModuledNetManager.GetLocalIPAddresses(false).Contains(ModuledNetManager.LocalIP))
                {
                    Debug.LogError("No network interface possesses the given local IP!");
                    onConnectionEstablished?.Invoke(false);
                    return;
                }

                if (ServerName.Length > 100 || ModuledNetSettings.Settings.Username.Length > 100)
                {
                    Debug.LogError($"The Servername and UserName must be shorter than 100 Characters!");
                    onConnectionEstablished?.Invoke(false);
                    return;
                }

                if (!IsASCIIString(ServerName) || !IsASCIIString(ModuledNetSettings.Settings.Username))
                {
                    Debug.LogError($"The Servername and UserName must be ASCII Strings!");
                    onConnectionEstablished?.Invoke(false);
                    return;
                }

                EConnectionStatus = EConnectionStatus.IsConnecting;

                IPAddress localAddress = IPAddress.Parse(ModuledNetManager.LocalIP);
                int localPort = FindNextAvailablePort();
                _localEndpoint = new(localAddress, localPort);
                _udpClient = new();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(_localEndpoint);

                ServerInformation = new(_localEndpoint, ServerName, ModuledNetSettings.Settings.MaxNumberClients);
                ClientInformation = new(1, ModuledNetSettings.Settings.Username, ModuledNetSettings.Settings.Color);

                _listenerThread = new(() => ListenerThread()) { IsBackground = true };
                _listenerThread.Start();
                _heartbeatThread = new(() => HeartbeatThread()) { IsBackground = true };
                _heartbeatThread.Start();
                _senderThread = new(() => SenderThread()) { IsBackground = true };
                _senderThread.Start();

                ModuledNetManager.OnUpdate += Update;

                ModuledNetManager.AddModuledNetMessage(new("Server has been opened!"));
                EConnectionStatus = EConnectionStatus.IsConnected;
                onConnectionEstablished?.Invoke(true);
            }
            catch (Exception ex)
            {
                EConnectionStatus = EConnectionStatus.IsDisconnected;
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
                if (_heartbeatThread != null)
                {
                    _heartbeatThread.Abort();
                    _heartbeatThread.Join();
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

                EConnectionStatus = EConnectionStatus.IsDisconnected;
                ServerInformation = null;
                ClientInformation = null;
            }
        }

        public void Update()
        {
            while (_mainThreadActions.Count > 0)
                if (_mainThreadActions.TryDequeue(out Action action))
                    action?.Invoke();
        }

        #endregion

        #region public methods

        public override void DisconnectFromServer()
        {
            if (!IsConnected)
                return;

            ConnectionClosedPacket connectionClosed = new();
            byte[] data = connectionClosed.Serialize();
            foreach (ClientInformationSocket client in _connectedClients.Values)
                _udpClient.Send(data, data.Length, client.Endpoint);

            ModuledNetManager.AddModuledNetMessage(new("Closed Server!"));
            Dispose();
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

        private void ListenerThread()
        {
            while (_disposeCount == 0)
            {
                try
                {   // get packet ip headers
                    IPEndPoint receiveEndpoint = new(1, 1);
                    byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
                    if (receiveEndpoint.Equals(_localEndpoint))
                        continue;

                    // get packet type without chunked packet bit
                    byte typeBytes = receivedBytes[ModuledNetSettings.CRC32_LENGTH];
                    byte mask = 1 << 7;
                    typeBytes &= (byte)~mask;
                    EPacketType packetType = (EPacketType)typeBytes;

                    // TODO : send connectionclosed when forcefully disconnected client sends packet
                    // TODO : otherwise refresh sequence numbers

                    // handle individual packet types
                    switch (packetType)
                    {
                        case EPacketType.ConnectionRequest:
                            HandleConnectionRequestPacket(receiveEndpoint, receivedBytes);
                            break;
                        case EPacketType.ChallengeAnswer:
                            HandleChallengeAnswerPacket(receiveEndpoint, receivedBytes);
                            break;
                        case EPacketType.ConnectionClosed:
                            HandleConnectionClosedPacket(receiveEndpoint, receivedBytes);
                            break;
                        case EPacketType.ACK:
                            {
                                if (!_connectedClients.TryGetValue(receiveEndpoint, out ClientInformationSocket client))
                                    break;
                                client.LastHeartbeat = DateTime.Now;

                                HandleACKPacket(client, receivedBytes);
                                break;
                            }
                        case EPacketType.ReliableData:
                        case EPacketType.ReliableUnorderedData:
                        case EPacketType.UnreliableData:
                        case EPacketType.UnreliableUnorderedData:
                            {
                                if (!_connectedClients.TryGetValue(receiveEndpoint, out ClientInformationSocket client))
                                    break;
                                client.LastHeartbeat = DateTime.Now;

                                DataPacket dataPacket = new(packetType, receivedBytes);
                                if (!dataPacket.TryDeserialize())
                                    break;

                                if (dataPacket.IsChunked)
                                {
                                    HandleChunkedDataPacket(client, dataPacket);
                                    break;
                                }

                                HandleSequencedPacket(client, dataPacket);
                                break;
                            }
                        case EPacketType.ClientInfo:
                            {
                                if (!_connectedClients.TryGetValue(receiveEndpoint, out ClientInformationSocket client))
                                    break;
                                client.LastHeartbeat = DateTime.Now;

                                ClientInfoPacket clientInfoPacket = new(receivedBytes);
                                if (!clientInfoPacket.TryDeserialize())
                                    break;

                                HandleSequencedPacket(client, clientInfoPacket);
                                break;
                            }
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
                            break;
                        default:
                            _mainThreadActions.Enqueue(() => Dispose());
                            ExceptionDispatchInfo.Capture(ex).Throw();
                            throw;
                    }
                }
            }
        }

        private void HandleConnectionRequestPacket(IPEndPoint sender, byte[] packet)
        {
            ConnectionRequestPacket connectionRequest = new(packet);
            if (!connectionRequest.TryDeserialize())
                return;

            if (_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
            {   // resend client id in case of client not receiving theirs
                _packetsToSend.Enqueue((client.Endpoint, new ConnectionAcceptedPacket(client.ID, ServerInformation.Servername, ServerInformation.MaxNumberConnectedClients)));
                return;
            }

            if (_connectedClients.Count + 1 >= ServerInformation.MaxNumberConnectedClients)
            {   // send connection denied packet if no space available
                _packetsToSend.Enqueue((sender, new ConnectionDeniedPacket()));
                return;
            }

            // create challenge packet and send it if enough space available
            ConnectionChallengePacket connectionChallenge = new();
            using (SHA256 h = SHA256.Create())
            {   // save sha256 hash of challenge nonce for comparing answer from client
                byte[] hashedChallenge = h.ComputeHash(BitConverter.GetBytes(connectionChallenge.Challenge));
                _pendingConnections.AddOrUpdate(sender, hashedChallenge, (key, oldValue) => oldValue = hashedChallenge);
            }
            _packetsToSend.Enqueue((sender, connectionChallenge));
        }

        private void HandleChallengeAnswerPacket(IPEndPoint sender, byte[] packet)
        {
            ChallengeAnswerPacket challengeAnswer = new(packet);
            if (!challengeAnswer.TryDeserialize())
                return;

            if (!_pendingConnections.TryGetValue(sender, out byte[] value))
                return;

            if (!CompareByteArrays(value, challengeAnswer.ChallengeAnswer) || _connectedClients.Count + 1 >= ServerInformation.MaxNumberConnectedClients)
            {   // send connection denied packet if no space available or challenge answer is incorrect
                _packetsToSend.Enqueue((sender, new ConnectionDeniedPacket()));
                return;
            }

            AddClient(sender, challengeAnswer.Username, challengeAnswer.Color);
        }

        private void HandleConnectionClosedPacket(IPEndPoint sender, byte[] packet)
        {
            ConnectionClosedPacket connectionClosed = new(packet);
            if (!connectionClosed.TryDeserialize())
                return;

            if (!_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
                return;

            _connectedClients.TryRemove(sender, out _);

            foreach (ClientInformationSocket remainingClient in _connectedClients.Values)
                _packetsToSend.Enqueue((remainingClient.Endpoint, new ClientDisconnectedPacket(client.ID)));

            _mainThreadActions.Enqueue(() => OnClientDisconnected?.Invoke(client.ID));
            _mainThreadActions.Enqueue(() => OnConnectedClientListChanged?.Invoke());
        }

        private void HandleACKPacket(ClientInformationSocket sender, byte[] packet)
        {
            ACKPacket ack = new(packet);
            if (!ack.TryDeserialize())
                return;

            if (ack.IsChunked)
                sender.SendChunksBuffer.TryRemove((ack.Sequence, ack.SliceNumber), out _);
            else
                sender.SendPacketsBuffer.TryRemove(ack.Sequence, out _);
        }

        private void HandleSequencedPacket(ClientInformationSocket sender, ASequencedNetworkPacket packet)
        {   // unreliable packet sequence
            if (IsUnreliableSequence(packet.Type))
            {   // ignore old packets unless they are unordered
                if (!IsNewPacket(packet.Sequence, sender.UnreliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                    return;

                // update sequence and consume packet
                sender.UnreliableRemoteSequence = packet.Sequence;
                ConsumeSequencedPacket(sender, packet);
                return;
            }

            // reliable packet sequence
            {
                // send ACK for reliable sequence
                _packetsToSend.Enqueue((sender.Endpoint, new ACKPacket(packet.Sequence)));

                // ignore old packets unless they are unordered
                if (!IsNewPacket(packet.Sequence, sender.ReliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                    return;

                if (!IsNextPacket(packet.Sequence, sender.ReliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                {   // if a packet is missing in the sequence keep it in the buffer
                    sender.ReceivedPacketsBuffer.TryAdd(packet.Sequence, packet);
                    return;
                }

                // update sequence and consume packet
                sender.ReliableRemoteSequence = packet.Sequence;
                ConsumeSequencedPacket(sender, packet);

                // apply all packets from that senders buffer that are now next in the sequence
                ushort sequence = packet.Sequence;
                while (sender.ReceivedPacketsBuffer.Count > 0)
                {
                    sequence++;
                    if (!sender.ReceivedPacketsBuffer.TryRemove(sequence, out ASequencedNetworkPacket bufferedPacket))
                        break;

                    // update sequence and consume packet
                    sender.ReliableRemoteSequence = sequence;
                    ConsumeSequencedPacket(sender, bufferedPacket);
                }
            }
        }

        private void HandleChunkedDataPacket(ClientInformationSocket sender, DataPacket packet)
        {   // send ACK
            _packetsToSend.Enqueue((sender.Endpoint, new ACKPacket(packet.Sequence, packet.SliceNumber)));

            // ignore old packets unless they are unordered
            if (!IsNewPacket(packet.Sequence, sender.ReliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
                return;

            if (!sender.ReceivedChunksBuffer.TryGetValue(packet.Sequence, out ConcurrentDictionary<ushort, DataPacket> bufferedChunk))
            {   // create chunked packet if it doesn't exist yet
                bufferedChunk = new();
                sender.ReceivedChunksBuffer.TryAdd(packet.Sequence, bufferedChunk);
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
            sender.ReceivedChunksBuffer.TryRemove(packet.Sequence, out _);

            if (!IsNextPacket(packet.Sequence, sender.ReliableRemoteSequence) && !IsUnorderedSequence(packet.Type))
            {   // if a packet is missing in the sequence keep it in the buffer
                sender.ReceivedPacketsBuffer.TryAdd(packet.Sequence, dataPacket);
                return;
            }

            // update sequence and consume packet
            sender.ReliableRemoteSequence = packet.Sequence;
            ConsumeSequencedPacket(sender, dataPacket);
        }

        private void ConsumeSequencedPacket(ClientInformationSocket sender, ASequencedNetworkPacket packet)
        {
            switch (packet)
            {
                case DataPacket dataPacket:
                    if (dataPacket.ClientID > 1)
                    {   // forward packet to specified client
                        if (GetClientById(dataPacket.ClientID, out ClientInformationSocket targetClient))
                            CreateDataSenderPackets(dataPacket.Type, dataPacket.ModuleID, dataPacket.Data, null, targetClient.Endpoint, sender.ID);
                        else
                            _packetsToSend.Enqueue((sender.Endpoint, new ClientDisconnectedPacket(dataPacket.ClientID)));
                        return;
                    }

                    // forward packet to all other clients before consuming
                    if (dataPacket.ClientID == 0)
                    {
                        foreach (ClientInformationSocket targetClient in _connectedClients.Values)
                            if (targetClient.ID != sender.ID)
                                CreateDataSenderPackets(dataPacket.Type, dataPacket.ModuleID, dataPacket.Data, null, targetClient.Endpoint, sender.ID);
                    }

                    // notify manager of received data, consuming the packet
                    _mainThreadActions.Enqueue(() => ModuledNetManager.DataReceived?.Invoke(dataPacket.ModuleID, sender.ID, dataPacket.Data));
                    break;
                case ClientInfoPacket clientInfoPacket:
                    sender.Username = clientInfoPacket.Username;
                    sender.Color = clientInfoPacket.Color;

                    ClientInfoPacket info = new(sender.ID, sender.Username, sender.Color);
                    foreach (ClientInformationSocket client in _connectedClients.Values)
                        if (sender.ID != client.ID)
                            _packetsToSend.Enqueue((client.Endpoint, info));

                    _mainThreadActions.Enqueue(() => OnConnectedClientListChanged?.Invoke());
                    break;
                default: break;
            }
        }

        #endregion

        #region sender logic

        private void HeartbeatThread()
        {
            try
            {
                IPAddress multicastIP = IPAddress.Parse(ModuledNetSettings.Settings.MulticastAddress);
                IPEndPoint remoteEP = new(multicastIP, ModuledNetSettings.Settings.DiscoveryPort);

                UdpClient heartbeatClient = new();
                heartbeatClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                heartbeatClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                heartbeatClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                heartbeatClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(multicastIP, _localEndpoint.Address));
                heartbeatClient.Client.Bind(_localEndpoint);
                heartbeatClient.Connect(remoteEP);

                while (_disposeCount == 0)
                {   // send heartbeat used for discovery until server is closed
                    // TODO : only recalculate if connected clients changed
                    ServerInformationPacket heartbeat = new(ServerInformation.Servername, ServerInformation.MaxNumberConnectedClients, (byte)(_connectedClients.Count + 1));
                    byte[] heartbeatBytes = heartbeat.Serialize();
                    heartbeatClient.Send(heartbeatBytes, heartbeatBytes.Length);
                    Thread.Sleep(ModuledNetSettings.Settings.ServerHeartbeatDelay);
                }
            }
            catch (Exception ex)
            {
                switch (ex)
                {
                    case ThreadAbortException:
                        return;
                    case FormatException:
                        Debug.Log("The Server Discovery Multicast IP is not a valid Address!");
                        _mainThreadActions.Enqueue(() => Dispose());
                        break;
                    default:
                        _mainThreadActions.Enqueue(() => Dispose());
                        ExceptionDispatchInfo.Capture(ex).Throw();
                        throw;
                }
            }
        }

        private void SenderThread()
        {
            while (_disposeCount == 0)
            {
                try
                {
                    if (_packetsToSend.Count == 0 || !_packetsToSend.TryDequeue(out (IPEndPoint, ANetworkPacket) packet))
                        continue;

                    byte[] data;
                    switch (packet.Item2)
                    {
                        case AConnectionNetworkPacket c:
                            data = c.Serialize();
                            _udpClient.Send(data, data.Length, packet.Item1);
                            continue;
                        case ASequencedNetworkPacket s:
                            if (!_connectedClients.TryGetValue(packet.Item1, out ClientInformationSocket client))
                                continue;

                            // serialize with unreliable sequence
                            if (IsUnreliableSequence(s.Type))
                            {
                                client.UnreliableLocalSequence++;
                                data = s.Serialize(client.UnreliableLocalSequence);
                                _udpClient.Send(data, data.Length, packet.Item1);

                                if (s is DataPacket ud)
                                {   // invoke callback once it was send
                                    ud.Callback?.Invoke(true);
                                }

                                continue;
                            }

                            // serialize with reliable sequence
                            {
                                client.ReliableLocalSequence++;
                                if (s is DataPacket rd)
                                {
                                    if (rd.IsChunked)
                                    {   // send slices individually
                                        for (ushort i = 0; i < rd.NumberOfSlices; i++)
                                        {
                                            rd.SliceNumber = i;
                                            data = s.Serialize(client.ReliableLocalSequence);
                                            _udpClient.Send(data, data.Length, packet.Item1);
                                            client.SendChunksBuffer.TryAdd((client.ReliableLocalSequence, rd.SliceNumber), data);
                                            _ = ResendSliceData(packet.Item1, (client.ReliableLocalSequence, rd.SliceNumber));
                                        }
                                        rd.Callback?.Invoke(true);
                                        continue;
                                    }
                                    else
                                    {   // send data packet as one
                                        data = s.Serialize(client.ReliableLocalSequence);
                                        _udpClient.Send(data, data.Length, packet.Item1);
                                        client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, data);
                                        _ = ResendPacketData(packet.Item1, client.ReliableLocalSequence);
                                        rd.Callback?.Invoke(true);
                                        continue;
                                    }
                                }

                                // send sequenced packet
                                data = s.Serialize(client.ReliableLocalSequence);
                                _udpClient.Send(data, data.Length, packet.Item1);
                                client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, data);
                                _ = ResendPacketData(packet.Item1, client.ReliableLocalSequence);
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
                            break;
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
        private async Task ResendSliceData(IPEndPoint clientEndpoint, (ushort, ushort) sequence, int retries = 0)
        {
            await Task.Delay((int)(ModuledNetSettings.Settings.RTT * 1.25f));
            if (_connectedClients.TryGetValue(clientEndpoint, out ClientInformationSocket client)
                && client.SendChunksBuffer.TryGetValue(sequence, out byte[] data))
            {
                _udpClient.Send(data, data.Length, clientEndpoint);
                if (retries < ModuledNetSettings.Settings.MaxNumberResendReliablePackets)
                    _ = ResendSliceData(clientEndpoint, sequence, retries + 1);
                else
                    RemoveClient(client.ID, true);
            }
        }


        /// <summary>
        /// Retry sending a Packet after a Delay and within a maximum number of retries
        /// </summary>
        /// <param name="client"></param>
        /// <param name="sequence"></param>
        /// <param name="retries"></param>
        /// <returns></returns>
        private async Task ResendPacketData(IPEndPoint clientEndpoint, ushort sequence, int retries = 0)
        {
            await Task.Delay((int)(ModuledNetSettings.Settings.RTT * 1.25f));
            if (_connectedClients.TryGetValue(clientEndpoint, out ClientInformationSocket client)
                && client.SendPacketsBuffer.TryGetValue(sequence, out byte[] data))
            {
                _udpClient.Send(data, data.Length, clientEndpoint);
                if (retries < ModuledNetSettings.Settings.MaxNumberResendReliablePackets)
                    _ = ResendPacketData(clientEndpoint, sequence, retries + 1);
                else
                    RemoveClient(client.ID, true);
            }
        }

        #endregion

        #region helper methods

        /// <summary>
        /// Translates Client ID into Client Information, if it exists.
        /// </summary>
        /// <param name="clientID"></param>
        /// <param name="client"></param>
        /// <returns></returns>
        private bool GetClientById(byte clientID, out ClientInformationSocket client)
        {
            if (!_idIpTable.TryGetValue(clientID, out IPEndPoint clientEndpoint)
                || !_connectedClients.TryGetValue(clientEndpoint, out ClientInformationSocket foundClient))
            {
                client = null;
                return false;
            }

            client = foundClient;
            return true;
        }

        /// <summary>
        /// Creates and adds new Client to all relevant collections and notifies the Manager and other connected Clients.
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="username"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        private ClientInformationSocket AddClient(IPEndPoint endpoint, string username, Color32 color)
        {
            // find next available client id
            byte newID = 0;
            for (byte i = 2; i <= ServerInformation.MaxNumberConnectedClients; i++)
            {
                if (!_idIpTable.TryGetValue(i, out _))
                {
                    newID = i;
                    break;
                }
            }

            if (newID == 0)
                throw new Exception("Something went wrong assigning the Client ID!");

            // create client
            ClientInformationSocket newClient = new(newID, endpoint, username, color);
            if (!_connectedClients.TryAdd(endpoint, newClient) || !_idIpTable.TryAdd(newID, endpoint) || !_pendingConnections.TryRemove(endpoint, out _))
                throw new Exception("Something went wrong creating the Client!");

            // send server info to client
            _packetsToSend.Enqueue((endpoint, new ConnectionAcceptedPacket(newID, ServerInformation.Servername, ServerInformation.MaxNumberConnectedClients)));
            _packetsToSend.Enqueue((endpoint, new ClientInfoPacket(ClientInformation.ID, ClientInformation.Username, ClientInformation.Color)));

            ClientInfoPacket info = new(newID, newClient.Username, newClient.Color);
            foreach (ClientInformationSocket client in _connectedClients.Values)
            {
                if (client.ID != newID)
                {   // notify all other clients of new client
                    _packetsToSend.Enqueue((client.Endpoint, info));

                    // send data of all other clients to new client
                    _packetsToSend.Enqueue((endpoint, new ClientInfoPacket(client.ID, client.Username, client.Color)));
                }
            }

            _mainThreadActions.Enqueue(() => OnClientConnected?.Invoke(newID));
            _mainThreadActions.Enqueue(() => OnConnectedClientListChanged?.Invoke());

            ModuledNetManager.AddModuledNetMessage(new($"Client {newClient} connected!"));
            return newClient;
        }

        // TODO : add reason
        /// <summary>
        /// Removes Client from all relevant collections and notifies the Manager and other connected Clients.
        /// </summary>
        /// <param name="clientID"></param>
        /// <param name="saveClient"></param>
        /// <returns></returns>
        private bool RemoveClient(byte clientID, bool saveClient)
        {
            if (!GetClientById(clientID, out ClientInformationSocket client))
                return false;

            _packetsToSend.Enqueue((client.Endpoint, new ConnectionClosedPacket()));
            _connectedClients.TryRemove(client.Endpoint, out _);

            foreach (ClientInformationSocket remainingClient in _connectedClients.Values)
                _packetsToSend.Enqueue((remainingClient.Endpoint, new ClientDisconnectedPacket(clientID)));

            _mainThreadActions.Enqueue(() => OnClientDisconnected?.Invoke(clientID));
            _mainThreadActions.Enqueue(() => OnConnectedClientListChanged?.Invoke());

            ModuledNetManager.AddModuledNetMessage(new($"Client {client} disconnected!"));

            if (saveClient)
            {
                // TODO : save disconnected clients in buffer unless forcefully disconnected
            }
            return true;
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

            if (receiver != null && !GetClientById((byte)receiver, out ClientInformationSocket _))
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
            if (receiver != null)
            {
                if (GetClientById((byte)receiver, out ClientInformationSocket client))
                    CreateDataSenderPackets(type, moduleID, data, dataCallback, client.Endpoint, 1);
                else
                    dataCallback?.Invoke(false);
                return;
            }

            foreach (ClientInformationSocket client in _connectedClients.Values)
                CreateDataSenderPackets(type, moduleID, data, dataCallback, client.Endpoint, 1);
        }

        /// <summary>
        /// Creates a Data Packet send by the given Sender to a given Receiver.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="moduleHash"></param>
        /// <param name="data"></param>
        /// <param name="dataCallback"></param>
        /// <param name="receiver"></param>
        /// <param name="sender"></param>
        private void CreateDataSenderPackets(EPacketType type, byte[] moduleID, byte[] data, Action<bool> dataCallback, IPEndPoint receiver, byte sender)
        {
            if (!IsDataPacket(type))
                throw new Exception("This function only supports Data Packets!");

            var mtu = ModuledNetSettings.Settings.MTU;
            if (data.Length > mtu && !IsReliableSequence(type))
            {
                Debug.LogError($"Only Reliable Packets can be larger than the MTU ({mtu} Bytes)!");
                dataCallback?.Invoke(false);
                return;
            }

            _packetsToSend.Enqueue((receiver, new DataPacket(type, moduleID, data, dataCallback, sender)));
        }

        private static bool CompareByteArrays(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            return a.SequenceEqual(b);
        }

        #endregion
    }
}
