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
		private readonly Action<bool> _onConnectionEstablished;
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

		private readonly string _tmpUsername;
		private readonly Color32 _tmpColor;

		#endregion

		#region public properties

		public override ConcurrentDictionary<byte, ClientInformation> ConnectedClients => _connectedClients;

		#endregion

		#region lifecycle

		public NetworkClient(IPAddress serverIP, Action<bool> onConnectionEstablished)
		{
			try
			{
				if (!CheckLocalIP(ModuledNetManager.LocalIP))
				{
					Debug.LogError("No network interface possesses the given local IP!");
					onConnectionEstablished?.Invoke(false);
					return;
				}

				if (serverIP == null)
				{
					Debug.LogError("The given server IP is not a valid IP!");
					onConnectionEstablished?.Invoke(false);
					return;
				}

				if (_settings.Username.Length > 100 || !IsASCIIString(_settings.Username))
				{
					Debug.LogError("The Username must be shorter than 100 characters and be an ASCII string!");
					onConnectionEstablished?.Invoke(false);
					return;
				}

				_tmpUsername = _settings.Username;
				_tmpColor = _settings.Color;

				ConnectionStatus = ConnectionStatus.IsConnecting;

				_localIP = IPAddress.Parse(ModuledNetManager.LocalIP);
				_port = _settings.Port;
				_udpClient = new(_port);
				_serverIP = serverIP;
				_onConnectionEstablished = onConnectionEstablished;
				
				_listenerThread = new(() => ListenerThread()) { IsBackground = true };
				_listenerThread.Start();
				_senderThread = new(() => SenderThread()) { IsBackground = true };
				_senderThread.Start();
				
				ModuledNetManager.OnUpdate += Update;

				_packetsToSend.Enqueue(new ConnectionRequestPacket());
				_ = TimeoutEstablishConnection();
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

			ModuledNetManager.AddSyncMessage(new("Disconnected from Server!"));
			_mainThreadActions.Enqueue(() => Dispose());
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
				{   // get packet ip headers
					IPEndPoint receiveEndpoint = new(IPAddress.Any, _settings.Port);
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
							HandleConnectionAcceptedPacket(receivedBytes);
							break;
						case EPacketType.ConnectionDenied:
							HandleConnectionDeniedPacket(receivedBytes);
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

		private void HandleConnectionAcceptedPacket(byte[] packet)
		{
			if (ConnectionStatus != ConnectionStatus.IsConnecting)
				return;

			ConnectionAcceptedPacket connectionAccepted = new(packet);
			if (!connectionAccepted.TryDeserialize())
				return;

			ClientInformation = new(connectionAccepted.ClientID, _tmpUsername, _tmpColor);
			ServerInformation = new(_serverIP, connectionAccepted.Servername, connectionAccepted.MaxNumberConnectedClients);

			_mainThreadActions.Enqueue(() => _onConnectionEstablished?.Invoke(true));
			_mainThreadActions.Enqueue(() => ConnectionStatus = ConnectionStatus.IsConnected);
		}

		private void HandleConnectionDeniedPacket(byte[] packet)
		{
			if (ConnectionStatus != ConnectionStatus.IsConnecting)
				return;

			ConnectionDeniedPacket connectionDenied = new(packet);
			if (!connectionDenied.TryDeserialize())
				return;

			_mainThreadActions.Enqueue(() => _onConnectionEstablished?.Invoke(false));
			_mainThreadActions.Enqueue(() => Dispose());
		}

		private void HandleConnectionClosedPacket(byte[] packet)
		{
			if (!IsConnected)
				return;

			ConnectionClosedPacket connectionClosed = new(packet);
			if (!connectionClosed.TryDeserialize())
				return;

			_mainThreadActions.Enqueue(() => Dispose());
		}

		private void HandleClientDisconnectedPacket(byte[] packet)
		{
			if (!IsConnected)
				return;

			ClientDisconnectedPacket clientDisconnected = new(packet);
			if (!clientDisconnected.TryDeserialize())
				return;

			if (!_connectedClients.TryRemove(clientDisconnected.ClientID, out _))
				return;

			_mainThreadActions.Enqueue(() => ModuledNetManager.OnClientDisconnected?.Invoke(clientDisconnected.ClientID));
			_mainThreadActions.Enqueue(() => ModuledNetManager.OnConnectedClientListChanged.Invoke());
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
			for (ushort i = 1; i <= packet.NumberOfSlices; i++)
			{
				if (!bufferedChunk.TryGetValue(i, out DataPacket sliceData))
					return;
				dataBytes.AddRange(sliceData.Data);
			}
			byte[] data = dataBytes.ToArray();
			DataPacket dataPacket = new(packet.Type, packet.ModuleHash, data, null, packet.ClientID);
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
					_mainThreadActions.Enqueue(() => ModuledNetManager.DataReceived?.Invoke(dataPacket.ModuleHash, dataPacket.ClientID, dataPacket.Data));
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
						_mainThreadActions.Enqueue(() => ModuledNetManager.OnClientConnected?.Invoke(clientInfoPacket.ClientID));
					}

					_mainThreadActions.Enqueue(() => ModuledNetManager.OnConnectedClientListChanged?.Invoke());
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
								data = s.Serialize(_reliableLocalSequence);
								_udpClient.Send(data, data.Length, new(_serverIP, _port));

								if (s is DataPacket rd)
								{   // invoke callback once it was send
									if (rd.IsChunked)
									{   // save slices in buffer in case of resends
										_sendChunksBuffer.TryAdd((_reliableLocalSequence, rd.SliceNumber), data);
										_ = ResendSliceData((_reliableLocalSequence, rd.SliceNumber));

										if (rd.SliceNumber == rd.NumberOfSlices - 1)
										{   // only invoke callback once all slices were send
											rd.Callback?.Invoke(true);
										}
										continue;
									}
									rd.Callback?.Invoke(true);
								}

								// save packets in buffer in case of resends
								_sendPacketsBuffer.TryAdd(_reliableLocalSequence, data);
								_ = ResendPacketData(_reliableLocalSequence);
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
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (_sendChunksBuffer.TryGetValue(sequence, out byte[] data))
			{
				_udpClient.Send(data, data.Length, new(_serverIP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets)
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
			await Task.Delay((int)(_settings.RTT * 1.25f));
			if (_sendPacketsBuffer.TryGetValue(sequence, out byte[] data))
			{
				_udpClient.Send(data, data.Length, new(_serverIP, _port));
				if (retries < _settings.MaxNumberResendReliablePackets)
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
		private async Task TimeoutEstablishConnection()
		{
			await Task.Delay(_settings.ServerConnectionTimeout);
			if (!IsConnected)
			{
				Dispose();
				_onConnectionEstablished?.Invoke(false);
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
		private void CreateDataSenderPackets(EPacketType type, uint moduleHash, byte[] data, Action<bool> dataCallback, byte? receiver = null)
		{
			if (!IsDataPacket(type))
			{
				dataCallback?.Invoke(false);
				throw new Exception("This function only supports Data Packets!");
			}

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
					_packetsToSend.Enqueue(new DataPacket(type, moduleHash, sliceData, dataCallback, receiver, numberOfSlices, sliceNumber));
				}
				return;
			}

			_packetsToSend.Enqueue(new DataPacket(type, moduleHash, data, dataCallback, receiver));
		}

		private static bool CompareByteArrays(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
		{
			return a.SequenceEqual(b);
		}

		#endregion
	}
}
