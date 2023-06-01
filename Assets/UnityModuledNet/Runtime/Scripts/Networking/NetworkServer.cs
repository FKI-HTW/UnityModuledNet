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

namespace CENTIS.UnityModuledNet.Networking
{
	internal sealed class NetworkServer : ANetworkSocket
	{
		#region private fields

		private readonly Thread _heartbeatThread;

		private readonly ConcurrentDictionary<IPAddress, byte[]> _pendingConnections = new();

		private readonly ConcurrentDictionary<byte, IPAddress> _idIpTable = new();
		private readonly ConcurrentDictionary<IPAddress, ClientInformationSocket> _connectedClients = new();

		private readonly ConcurrentQueue<(IPAddress, ANetworkPacket)> _packetsToSend = new();

		#endregion

		#region public properties

		public override ConcurrentDictionary<byte, ClientInformation> ConnectedClients => new(_connectedClients.ToDictionary(k => k.Value.ID, v => (ClientInformation)v.Value));

		#endregion

		#region lifecycle

		public NetworkServer(string servername, Action<bool> onConnectionEstablished)
		{
			try
			{
				if (!CheckLocalIP(ModuledNetManager.LocalIP))
				{
					Debug.LogError("No network interface possesses the given local IP!");
					onConnectionEstablished?.Invoke(false);
					return;
				}

				if (servername.Length > 100 || _settings.Username.Length > 100)
				{
					Debug.LogError($"The Servername and Username must be shorter than 100 Characters!");
					onConnectionEstablished?.Invoke(false);
					return;
				}

				if (!IsASCIIString(servername) || !IsASCIIString(_settings.Username))
				{
					Debug.LogError($"The Servername or Username contains a non-ASCII Character!");
					onConnectionEstablished?.Invoke(false);
					return;
				}

				ConnectionStatus = ConnectionStatus.IsConnecting;

				_localIP = IPAddress.Parse(ModuledNetManager.LocalIP);
				_port = _settings.Port;
				_udpClient = new(_port);

				ServerInformation = new(_localIP, servername, _settings.MaxNumberClients);
				ClientInformation = new(1, _settings.Username, _settings.Color);

				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();
				_heartbeatThread = new(() => HeartbeatThread()) { IsBackground = true };
				_heartbeatThread.Start();
				_senderThread = new(() => SenderThread()) { IsBackground = true };
				_senderThread.Start();

				ModuledNetManager.OnUpdate += Update;

				ModuledNetManager.AddSyncMessage(new("Server has been opened!"));
				ConnectionStatus = ConnectionStatus.IsConnected;
				onConnectionEstablished?.Invoke(true);
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

				ConnectionStatus = ConnectionStatus.IsDisconnected;
				ServerInformation = null;
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
		
		public override void DisconnectFromServer()
		{
			if (!IsConnected)
				return;

			ConnectionClosedPacket connectionClosed = new();
			byte[] data = connectionClosed.Serialize();
			foreach (ClientInformationSocket client in _connectedClients.Values)
				_udpClient.Send(data, data.Length, new(client.IP, _port));

			ModuledNetManager.AddSyncMessage(new("Disconnected from Server!"));
			Dispose();
		}

		public override void SendDataReliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(onDataSend, receiver))
				return;

			CreateDataSenderPackets(EPacketType.ReliableData, moduleHash, data, onDataSend, receiver);
		}

		public override void SendDataReliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(onDataSend, receiver))
				return;

			CreateDataSenderPackets(EPacketType.ReliableUnorderedData, moduleHash, data, onDataSend, receiver);
		}

		public override void SendDataUnreliable(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(onDataSend, receiver))
				return;

			CreateDataSenderPackets(EPacketType.UnreliableData, moduleHash, data, onDataSend, receiver);
		}

		public override void SendDataUnreliableUnordered(uint moduleHash, byte[] data, Action<bool> onDataSend, byte? receiver = null)
		{
			if (!CheckIfEligibleForSending(onDataSend, receiver))
				return;

			CreateDataSenderPackets(EPacketType.UnreliableUnorderedData, moduleHash, data, onDataSend, receiver);
		}

		#endregion

		#region listener logic

		private void ListenerThread()
		{
			while (_disposeCount == 0)
			{
				try
				{	// get packet ip headers
					IPEndPoint receiveEndpoint = new(IPAddress.Any, _settings.Port);
					byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
					IPAddress sender = receiveEndpoint.Address;
					if (sender.Equals(_localIP))
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
							HandleConnectionRequestPacket(sender, receivedBytes);
							break;
						case EPacketType.ChallengeAnswer:
							HandleChallengeAnswerPacket(sender, receivedBytes);
							break;
						case EPacketType.ACK:
							{
								if (!_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
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
								if (!_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
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
								if (!_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
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

		private void HandleConnectionRequestPacket(IPAddress sender, byte[] packet)
		{
			ConnectionRequestPacket connectionRequest = new(packet);
			if (!connectionRequest.TryDeserialize())
				return;

			if (_connectedClients.TryGetValue(sender, out ClientInformationSocket client))
			{   // resend client id in case of client not receiving theirs
				_packetsToSend.Enqueue((client.IP, new ConnectionAcceptedPacket(client.ID, ServerInformation.Servername, ServerInformation.MaxNumberConnectedClients)));
				return;
			}

			if (_connectedClients.Count >= ServerInformation.MaxNumberConnectedClients)
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

		private void HandleChallengeAnswerPacket(IPAddress sender, byte[] packet)
		{
			ChallengeAnswerPacket challengeAnswer = new(packet);
			if (!challengeAnswer.TryDeserialize())
				return;

			if (!_pendingConnections.TryGetValue(sender, out byte[] value))
				return;

			if (!CompareByteArrays(value, challengeAnswer.ChallengeAnswer) || _connectedClients.Count >= ServerInformation.MaxNumberConnectedClients)
			{   // send connection denied packet if no space available or challenge answer is incorrect
				_packetsToSend.Enqueue((sender, new ConnectionDeniedPacket()));
				return;
			}

			AddClient(sender);
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
				_packetsToSend.Enqueue((sender.IP, new ACKPacket(packet.Sequence)));

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
		{	// send ACK
			_packetsToSend.Enqueue((sender.IP, new ACKPacket(packet.Sequence, packet.SliceNumber)));

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
			for (ushort i = 1; i <= packet.NumberOfSlices; i++)
			{
				if (!bufferedChunk.TryGetValue(i, out DataPacket sliceData))
					return;
				dataBytes.AddRange(sliceData.Data);
			}
			byte[] data = dataBytes.ToArray();
			DataPacket dataPacket = new(packet.Type, packet.ModuleHash, data, null, sender.ID);
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
						if (GetClientById((byte)dataPacket.ClientID, out ClientInformationSocket targetClient))
							CreateDataSenderPackets(dataPacket.Type, dataPacket.ModuleHash, dataPacket.Data, null, targetClient.IP, sender.ID);
						return;
					}

					// forward packet to all other clients before consuming
					if (dataPacket.ClientID == 0)
						foreach (ClientInformationSocket targetClient in _connectedClients.Values)
							CreateDataSenderPackets(dataPacket.Type, dataPacket.ModuleHash, dataPacket.Data, null, targetClient.IP, sender.ID);

					// notify manager of received data, consuming the packet
					ModuledNetManager.DataReceived?.Invoke(dataPacket.ModuleHash, sender.ID, dataPacket.Data);
					break;
				case ClientInfoPacket clientInfoPacket:
					sender.Username = clientInfoPacket.Username;
					sender.Color = clientInfoPacket.Color;

					ClientInfoPacket info = new(sender.ID, sender.Username, sender.Color);
					foreach (ClientInformationSocket client in _connectedClients.Values)
						if (sender.ID != client.ID)
							_packetsToSend.Enqueue((client.IP, info));
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
				int discoveryPort = _settings.DiscoveryPort;
				UdpClient heartbeatClient = new();
				heartbeatClient.EnableBroadcast = true;
				heartbeatClient.ExclusiveAddressUse = false;
				heartbeatClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				heartbeatClient.Client.Bind(new IPEndPoint(_localIP, discoveryPort));
				IPEndPoint remoteEndpoint = new(IPAddress.Broadcast, discoveryPort);

				while (_disposeCount == 0)
				{   // send heartbeat used for discovery until server is closed
					// TODO : only recalculate if connected clients changed
					ServerInformationPacket heartbeat = new(ServerInformation.Servername, ServerInformation.MaxNumberConnectedClients, (byte)_connectedClients.Count);
					byte[] heartbeatBytes = heartbeat.Serialize();
					heartbeatClient.Send(heartbeatBytes, heartbeatBytes.Length, remoteEndpoint);
					Thread.Sleep(_settings.ServerHeartbeatDelay);
				}
			}
			catch (Exception ex)
			{
				switch (ex)
				{
					case ThreadAbortException:
						return;
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
					if (_packetsToSend.Count == 0 || !_packetsToSend.TryDequeue(out (IPAddress, ANetworkPacket) packet))
						continue;

					byte[] data;
					switch (packet.Item2)
					{
						case AConnectionNetworkPacket c:
							data = c.Serialize();
							_udpClient.Send(data, data.Length, new(packet.Item1, _port));
							continue;
						case ASequencedNetworkPacket s:
							if (!_connectedClients.TryGetValue(packet.Item1, out ClientInformationSocket client))
								continue;

							// serialize with unreliable sequence
							if (IsUnreliableSequence(s.Type))
							{
								client.UnreliableLocalSequence++;
								data = s.Serialize(client.UnreliableLocalSequence);
								_udpClient.Send(data, data.Length, new(packet.Item1, _port));

								if (s is DataPacket ud)
								{	// invoke callback once it was send
									ud.Callback?.Invoke(true);
								}

								continue;
							}

							// serialize with reliable sequence
							{
								client.ReliableLocalSequence++;
								data = s.Serialize(client.ReliableLocalSequence);
								_udpClient.Send(data, data.Length, new(packet.Item1, _port));

								if (s is DataPacket rd)
								{	// invoke callback once it was send
									if (rd.IsChunked)
									{   // save slices in buffer in case of resends
										client.SendChunksBuffer.TryAdd((client.ReliableLocalSequence, rd.SliceNumber), data);
										_ = ResendSliceData(packet.Item1, (client.ReliableLocalSequence, rd.SliceNumber));

										if (rd.SliceNumber == rd.NumberOfSlices - 1)
										{   // only invoke callback once all slices were send
											rd.Callback?.Invoke(true);
										}
										continue;
									}
									rd.Callback?.Invoke(true);
								}

								// save packets in buffer in case of resends
								client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, data);
								_ = ResendPacketData(packet.Item1, client.ReliableLocalSequence);
							}
							break;
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
		private async Task ResendSliceData(IPAddress clientIP, (ushort, ushort) sequence, int retries = 0)
		{
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (_connectedClients.TryGetValue(clientIP, out ClientInformationSocket client) 
				&& client.SendChunksBuffer.TryGetValue(sequence, out byte[] data))
			{
				_udpClient.Send(data, data.Length, new(client.IP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets)
					_ = ResendSliceData(clientIP, sequence, retries + 1);
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
		private async Task ResendPacketData(IPAddress clientIP, ushort sequence, int retries = 0)
		{
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (_connectedClients.TryGetValue(clientIP, out ClientInformationSocket client)
				&& client.SendPacketsBuffer.TryGetValue(sequence, out byte[] data))
			{
				_udpClient.Send(data, data.Length, new(client.IP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets)
					_ = ResendPacketData(clientIP, sequence, retries + 1);
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
			if (!_idIpTable.TryGetValue(clientID, out IPAddress clientIP)
				|| !_connectedClients.TryGetValue(clientIP, out ClientInformationSocket foundClient))
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
		private ClientInformationSocket AddClient(IPAddress ip)
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
			ClientInformationSocket newClient = new(newID, ip);
			if (!_connectedClients.TryAdd(ip, newClient) || !_idIpTable.TryAdd(newID, ip) || !_pendingConnections.TryRemove(ip, out _))
				throw new Exception("Something went wrong creating the Client!");

			// send server info to client
			_packetsToSend.Enqueue((ip, new ConnectionAcceptedPacket(newID, ServerInformation.Servername, ServerInformation.MaxNumberConnectedClients)));
			_packetsToSend.Enqueue((ip, new ClientInfoPacket(ClientInformation.ID, ClientInformation.Username, ClientInformation.Color)));

			ClientInfoPacket info = new(newID, newClient.Username, newClient.Color);
			foreach (ClientInformationSocket client in _connectedClients.Values)
			{
				if (client.ID != newID)
				{	// notify all other clients of new client
					_packetsToSend.Enqueue((client.IP, info));

					// send data of all other clients to new client
					_packetsToSend.Enqueue((ip, new ClientInfoPacket(client.ID, client.Username, client.Color)));
				}
			}

			_mainThreadActions.Enqueue(() => ModuledNetManager.OnClientConnected?.Invoke(newID));

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

			_packetsToSend.Enqueue((client.IP, new ConnectionClosedPacket()));
			_connectedClients.TryRemove(client.IP, out _);
			_mainThreadActions.Enqueue(() => ModuledNetManager.OnClientConnected?.Invoke(clientID));

			foreach (ClientInformationSocket remainingClient in _connectedClients.Values)
				_packetsToSend.Enqueue((remainingClient.IP, new ClientDisconnectedPacket(clientID)));
			
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
		private void CreateDataSenderPackets(EPacketType type, uint moduleHash, byte[] data, Action<bool> dataCallback, byte? receiver = null)
		{
			if (receiver != null)
			{
				if (GetClientById((byte)receiver, out ClientInformationSocket client))
					CreateDataSenderPackets(type, moduleHash, data, dataCallback, client.IP, 1);
				else
					dataCallback?.Invoke(false);
				return;
			}

			foreach (ClientInformationSocket client in _connectedClients.Values)
				CreateDataSenderPackets(type, moduleHash, data, dataCallback, client.IP, 1);
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
		private void CreateDataSenderPackets(EPacketType type, uint moduleHash, byte[] data, Action<bool> dataCallback, IPAddress receiver, byte sender)
		{
			if (!IsDataPacket(type))
				throw new Exception("This function only supports Data Packets!");

			int mtu = _settings.MTU;
			if (data.Length > mtu && !IsReliableSequence(type))
			{
				Debug.LogError($"Only Reliable Packets can be larger than the MTU ({mtu} Bytes)!");
				dataCallback?.Invoke(false);
				return;
			}

			if (data.Length > mtu)
			{   // if data is larger than MTU split packet into slices and send individually
				ushort numberOfSlices = (ushort)(data.Length % mtu == 0
					? data.Length / mtu
					: data.Length / mtu + 1);

				for (ushort sliceNumber = 1; sliceNumber <= numberOfSlices; sliceNumber++)
				{
					int sliceSize = sliceNumber < numberOfSlices - 1 ? mtu : data.Length % mtu;
					byte[] sliceData = new byte[sliceSize];
					Array.Copy(data, sliceNumber * mtu, sliceData, 0, sliceSize);
					_packetsToSend.Enqueue((receiver, new DataPacket(type, moduleHash, sliceData, dataCallback, sender, numberOfSlices, sliceNumber)));
				}
				return;
			}

			_packetsToSend.Enqueue((receiver, new DataPacket(type, moduleHash, data, dataCallback, sender)));
		}

		private static bool CompareByteArrays(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
		{
			return a.SequenceEqual(b);
		}

		#endregion
	}
}
