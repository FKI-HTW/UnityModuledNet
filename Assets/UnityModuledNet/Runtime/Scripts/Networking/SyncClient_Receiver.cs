using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Networking
{
	internal sealed partial class SyncClient
	{
		#region threads

		private void ListenerThread()
		{
			try
			{
				while (_disposeCount == 0)
				{
					try
					{
						//----- Handle valid/relevant Internal Packets -----

						// get packet ip headers
						IPEndPoint receiveEndpoint = new(IPAddress.Any, _settings.Port);
						byte[] receivedBytes = _udpClient.Receive(ref receiveEndpoint);
						IPAddress sender = receiveEndpoint.Address;
						if (sender.Equals(_ip))
							continue;

						// get bytes used for crc32 check
						byte[] checksumBytes = GetBytesFromArray(receivedBytes, 0, CRC32_LENGTH);
						byte[] remainder = GetBytesFromArray(receivedBytes, CRC32_LENGTH);

						// get relevant header bytes
						byte[] sequenceBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
						ushort sequence = BitConverter.ToUInt16(sequenceBytes);
						bool isChunked = (receivedBytes[CRC32_LENGTH + SEQUENCE_ID_LENGTH] & (1 << 7)) != 0;
						SyncPacketType packetType = GetOriginalType(receivedBytes[CRC32_LENGTH + SEQUENCE_ID_LENGTH]);

						if (packetType == SyncPacketType.Heartbeat)
						{
							UpdateHeartbeat(sender, checksumBytes, remainder);
							continue;
						}

						if (!IsConnectedToRoom)
							continue;

						if (!CheckPacketCRC32(checksumBytes, remainder, CurrentRoom.RoomnameBytes))
							continue;

						if (!CurrentRoom.ConnectedClients.TryGetValue(sender, out SyncConnectedClient client))
							continue;

						if (packetType == SyncPacketType.ACK)
						{   // remove ACK'd packets from buffer
							if (isChunked)
							{
								byte[] sliceNumberBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH);
								ushort sliceNumber = BitConverter.ToUInt16(sliceNumberBytes);
								client.SendSlicesBuffer.TryRemove((sequence, sliceNumber), out _);
							}
							else
							{
								client.SendPacketsBuffer.TryRemove(sequence, out _);
							}
							continue;
						}

						//----- Handle Data Packets -----

						byte[] moduleHashBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH);
						uint moduleHash = BitConverter.ToUInt32(moduleHashBytes);
						byte[] data = isChunked
							? GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH + NUMBER_OF_SLICES + SLICE_NUMBER)
							: GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH);

						if (isChunked)
						{   // handle chunked packet by checking sequence and only consuming once its complete and relevant
							byte[] sliceNumberBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH + NUMBER_OF_SLICES, SLICE_NUMBER);
							ushort sliceNumber = BitConverter.ToUInt16(sliceNumberBytes);

							SendACK(client, sequence, sliceNumber);

							// ignore old packets unless they are unordered
							if (!IsNewPacket(sequence, client.ReliableRemoteSequence)
								&& packetType != SyncPacketType.ReliableUnorderedData)
								continue;

							if (!client.ReceivedPacketsBuffer.TryGetValue(sequence, out SyncReceiverPacket packet))
							{   // create chunked packet if it doesn't exist yet
								byte[] numberOfSlicesBytes = GetBytesFromArray(receivedBytes, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH, NUMBER_OF_SLICES);
								ushort numberOfSlices = BitConverter.ToUInt16(numberOfSlicesBytes);
								packet = new(packetType, moduleHash, client, numberOfSlices);
								client.ReceivedPacketsBuffer.TryAdd(sequence, packet);
							}
							packet.Slices.TryAdd(sliceNumber, data);

							// if packet is not complete or a packet is missing in the sequence keep it in the buffer
							if (!packet.IsPacketComplete() || (!IsNextPacket(sequence, client.ReliableRemoteSequence)
								&& packetType != SyncPacketType.ReliableUnorderedData))
								continue;

							client.ReceivedPacketsBuffer.TryRemove(sequence, out _);
							client.ReliableRemoteSequence = sequence;
							packet.ConcatenateChunkedPacket();
							SyncManager.DataReceived?.Invoke(packet.ModuleHash, packet.Client, packet.Data);
						}
						else
						{   // handle packet by checking sequence and consume packet if it is relevant
							if (IsUnreliableData(packetType))
							{   // unreliable packet sequences

								// ignore old packets unless they are unordered
								if (!IsNewPacket(sequence, client.UnreliableRemoteSequence)
									&& packetType != SyncPacketType.UnreliableUnorderedData)
									continue;

								client.UnreliableRemoteSequence = sequence;
								SyncManager.DataReceived?.Invoke(moduleHash, client, data);
							}
							else if (IsReliableData(packetType))
							{   // reliable packet sequences
								SendACK(client, sequence);

								// ignore old packets unless they are unordered
								if (!IsNewPacket(sequence, client.ReliableRemoteSequence)
									&& packetType != SyncPacketType.ReliableUnorderedData)
									continue;

								if (!IsNextPacket(sequence, client.ReliableRemoteSequence)
									&& packetType != SyncPacketType.ReliableUnorderedData)
								{   // if a packet is missing in the sequence keep it in the buffer
									client.ReceivedPacketsBuffer.TryAdd(sequence, new(packetType, moduleHash, data, client));
									// TODO : if not received within RTTx1.25 timeframe timeout client
									continue;
								}

								client.ReliableRemoteSequence = sequence;
								SyncManager.DataReceived?.Invoke(moduleHash, client, data);
							}
						}

						if (IsReliableData(packetType))
						{   // apply all packets in buffer that are now next in line and complete
							while (client.ReceivedPacketsBuffer.Count > 0)
							{
								sequence++;
								if (!client.ReceivedPacketsBuffer.TryGetValue(sequence, out SyncReceiverPacket packet))
									break;

								// only consume chunked packet if it is complete
								if (packet.IsChunked && !packet.ConcatenateChunkedPacket())
									break;

								client.ReceivedPacketsBuffer.TryRemove(sequence, out _);
								client.ReliableRemoteSequence = sequence;
								SyncManager.DataReceived?.Invoke(packet.ModuleHash, packet.Client, packet.Data);
							}
						}
					}
					catch (OverflowException) { continue; }
				}
			}
			catch (Exception ex)
			{
				switch (ex)
				{
					case ThreadAbortException:
						break;
					case SocketException:
					case ObjectDisposedException:
						Debug.LogError(ex.ToString());
						_mainThreadActions.Enqueue(() => IsClientActive = false);
						break;
					default:
						_mainThreadActions.Enqueue(() => IsClientActive = false);
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
			}
		}

		#endregion

		#region packet receiving

		private void UpdateHeartbeat(IPAddress sender, byte[] checksum, byte[] data)
		{
			// read roomname and control crc32 checksum
			int currentPosition = SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH;
			int roomnameLength = data[currentPosition];
			currentPosition += ROOMNAME_FLAG_LENGTH;
			byte[] roomnameBytes = GetBytesFromArray(data, currentPosition, roomnameLength);
			if (!CheckPacketCRC32(checksum, data, roomnameBytes))
				return;

			DebugByteMessage(data, "Received Heartbeat Bytes: ");

			// get username and -color from data
			string roomname = Encoding.ASCII.GetString(roomnameBytes);
			currentPosition += roomnameLength;
			int usernameLength = data[currentPosition];
			currentPosition += USERNAME_FLAG_LENGTH;
			byte[] usernameBytes = GetBytesFromArray(data, currentPosition, usernameLength);
			string username = Encoding.ASCII.GetString(usernameBytes);
			currentPosition += usernameLength;
			Color32 color = new(data[currentPosition], data[currentPosition + 1], data[currentPosition + 2], 255);

			// create room/client and add it to dictionary if it doesn't exist yet
			if (!OpenRooms.TryGetValue(roomname, out SyncOpenRoom room))
			{
				room = new(roomname);
				OpenRooms.TryAdd(roomname, room);
			}
			if (!room.ConnectedClients.TryGetValue(sender, out SyncConnectedClient client))
			{
				client = new(sender, username, color);
				room.ConnectedClients.TryAdd(sender, client);
				_ = TimeoutClient(client, room);
				if (room.Equals(CurrentRoom))
					_mainThreadActions.Enqueue(() => SyncManager.OnClientConnected?.Invoke(sender));
			}

			// update client values
			client.Color = color;
			client.LastHeartbeat = DateTime.Now;
		}

		/// <summary>
		/// Check if a Client has been inactive for longer than the maximum timeout delay.
		/// </summary>
		/// <param name="client"></param>
		/// <param name="room"></param>
		/// <returns></returns>
		private async Task TimeoutClient(SyncConnectedClient client, SyncOpenRoom room)
		{
			while (_disposeCount == 0)
			{
				if ((DateTime.Now - client.LastHeartbeat).TotalMilliseconds > _settings.ClientTimeoutDelay)
				{
					room.ConnectedClients.TryRemove(client.IP, out _);
					if (room.Equals(CurrentRoom))
						_mainThreadActions.Enqueue(() => SyncManager.OnClientDisconnected?.Invoke(client.IP));
					if (room.ConnectedClients.Count == 0 && !room.Equals(CurrentRoom))
						OpenRooms.TryRemove(room.Roomname, out _);
					return;
				}
				await Task.Delay(_settings.ClientTimeoutDelay);
			}
		}

		#endregion

		#region helper methods

		/// <summary>
		/// Resets the most significant Bit (IsChunked-Bit) in the Packet Type Header to 0.
		/// </summary>
		/// <param name="typeBytes"></param>
		/// <returns></returns>
		private static SyncPacketType GetOriginalType(byte typeBytes)
		{
			byte mask = 1 << 7;
			typeBytes &= (byte)~mask;
			return (SyncPacketType)typeBytes;
		}

		/// <summary>
		/// Check a Packets CRC32 Checksum against a newly calculated one using the Packet Bytes and a Roomname.
		/// </summary>
		/// <param name="checksum"></param>
		/// <param name="packet"></param>
		/// <param name="roomname"></param>
		/// <returns></returns>
		private static bool CheckPacketCRC32(byte[] checksum, byte[] packet, byte[] roomname)
		{  
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + roomname.Length + packet.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(roomname, 0, bytes, PROTOCOL_ID_LENGTH, roomname.Length);
			Array.Copy(packet, 0, bytes, PROTOCOL_ID_LENGTH + roomname.Length, packet.Length);
			return BitConverter.ToUInt32(checksum) == SyncCRC32.CRC32Bytes(bytes);
		}

		/// <summary>
		/// Checks if a packet is new by comparing the packets sequence number and the corresponding remote sequence number.
		/// Also handles ushort wrap-arounds by allowing packets that are smaller by half of the maximum value.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		private static bool IsNewPacket(ushort packetSequence, ushort remoteSequence)
		{
			return ((packetSequence > remoteSequence) && (packetSequence - remoteSequence <= 32768))
				|| ((packetSequence < remoteSequence) && (remoteSequence - packetSequence > 32768));
		}

		/// <summary>
		/// Checks if a packet is next after remote sequence number.
		/// </summary>
		/// <param name="packetSequence"></param>
		/// <param name="remoteSequence"></param>
		/// <returns>if the packet is new</returns>
		private static bool IsNextPacket(ushort packetSequence, ushort remoteSequence)
		{
			return (packetSequence == (ushort)(remoteSequence + 1))
				|| (packetSequence == 0 && remoteSequence == 65535);
		}

		#endregion
	}
}