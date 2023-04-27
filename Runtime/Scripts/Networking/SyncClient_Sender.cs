using System;
using System.Collections.Generic;
using System.Linq;
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

		private void HeartbeatThread()
		{
			byte[] username = Encoding.ASCII.GetBytes(_settings.Username);
			byte[] color = { _settings.Color.r, _settings.Color.g, _settings.Color.b };

			// prepare data with room name, username and -color
			byte[] data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH +
				ROOMNAME_FLAG_LENGTH + CurrentRoom.RoomnameBytes.Length +
				USERNAME_FLAG_LENGTH + username.Length +
				COLOR_LENGTH];
			int currentPosition = CRC32_LENGTH;
			Array.Copy(BitConverter.GetBytes((ushort)0), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
			currentPosition += SEQUENCE_ID_LENGTH;
			data[currentPosition] = (byte)SyncPacketType.Heartbeat;
			currentPosition += PACKET_TYPE_LENGTH;
			data[currentPosition] = (byte)CurrentRoom.RoomnameBytes.Length;
			currentPosition += ROOMNAME_FLAG_LENGTH;
			Array.Copy(CurrentRoom.RoomnameBytes, 0, data, currentPosition, CurrentRoom.RoomnameBytes.Length);
			currentPosition += CurrentRoom.RoomnameBytes.Length;
			data[currentPosition] = (byte)username.Length;
			currentPosition += USERNAME_FLAG_LENGTH;
			Array.Copy(username, 0, data, currentPosition, username.Length);
			currentPosition += username.Length;
			Array.Copy(color, 0, data, currentPosition, COLOR_LENGTH);
			CalculateAndAddChecksum(data);

			try
			{
				while (_disposeCount == 0)
				{
					_udpClient.Send(data, data.Length, new(IPAddress.Broadcast, _port));
					DebugByteMessage(data, "Send Heartbeat Bytes: ");
					Thread.Sleep(_settings.HeartbeatDelay);
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
						ExceptionDispatchInfo.Capture(ex).Throw();
						throw;
				}
			}
		}

		private void SenderThread()
		{
			try
			{
				while (_disposeCount == 0)
				{
					if (_packetsToSend.Count == 0 || !_packetsToSend.TryPeek(out SyncSenderPacket packet))
						continue;

					if (packet.IsChunked && IsUnreliableData(packet.Type))
					{
						Debug.LogError("Only reliable Packets can be larger than 1200 Bytes!");
						packet.OnDataSend?.Invoke(false);
						_packetsToSend.TryDequeue(out _);
						continue;
					}

					if (!packet.IsChunked)
					{   // remove packet from sender queue since it only needs to be send once
						_packetsToSend.TryDequeue(out _);
					}

					byte[] packetData = packet.GetNextSlice();
					if (packetData == null || packetData.Length == 0)
					{   // remove invalid or already completed packets from sender queue
						_packetsToSend.TryDequeue(out _);
						continue;
					}

					byte[] data;
					if (packet.IsChunked)
					{   // add packet type, slice numbers, current slice number and data to the packet
						data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH
							+ NUMBER_OF_SLICES + SLICE_NUMBER + packetData.Length];
						data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)packet.Type;
						data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] |= 1 << 7;
						Array.Copy(BitConverter.GetBytes(packet.ModuleHash), 0, data,
							CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH, MODULE_HASH_LENGTH);
						Array.Copy(BitConverter.GetBytes(packet.NumberOfSlices), 0, data,
							CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH, NUMBER_OF_SLICES);
						Array.Copy(BitConverter.GetBytes(packet.CurrentSliceNumber), 0, data,
							CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH + NUMBER_OF_SLICES, SLICE_NUMBER);
						Array.Copy(packetData, 0, data,
							CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH + NUMBER_OF_SLICES + SLICE_NUMBER, packetData.Length);
					}
					else
					{   // add packet type and data to the packet
						data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH + packetData.Length];
						data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)packet.Type;
						Array.Copy(BitConverter.GetBytes(packet.ModuleHash), 0, data, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH, MODULE_HASH_LENGTH);
						Array.Copy(packetData, 0, data, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + MODULE_HASH_LENGTH, packetData.Length);
					}


					// define receivers of the packet
					List<SyncConnectedClient> clients = new();
					if (packet.Client == null)
						clients = CurrentRoom.ConnectedClients.Values.ToList();
					else
						clients.Add(packet.Client);

					// add sequence number and checksum for each client and send packet to them
					foreach (SyncConnectedClient client in clients)
					{
						if (IsUnreliableData(packet.Type))
						{   // handle unreliable packets
							if (!packet.IsChunked || packet.CurrentSliceNumber == 1)
								client.UnreliableLocalSequence++;
							Array.Copy(BitConverter.GetBytes(client.UnreliableLocalSequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
						}
						else if (IsReliableData(packet.Type))
						{   // handle reliable packets
							if (!packet.IsChunked || packet.CurrentSliceNumber == 1)
								client.ReliableLocalSequence++;
							Array.Copy(BitConverter.GetBytes(client.ReliableLocalSequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
						}

						CalculateAndAddChecksum(data);

						_udpClient.Send(data, data.Length, new(client.IP, _port));
						if (packet.OnDataSend != null)
							packet.OnDataSend?.Invoke(true);

						if (packet.IsChunked)
						{   // save slices in buffer in case of resends
							client.SendSlicesBuffer.TryAdd((client.ReliableLocalSequence, packet.CurrentSliceNumber), data);
							_ = ResendSliceData(client, (client.ReliableLocalSequence, packet.CurrentSliceNumber));
						}
						else if (IsReliableData(packet.Type))
						{   // save reliable packets in buffer in case of resends
							client.SendPacketsBuffer.TryAdd(client.ReliableLocalSequence, data);
							_ = ResendPacketData(client, client.ReliableLocalSequence);
						}
					}
					DebugByteMessage(data, $"Send {packet.Type} Bytes: ", true);
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

		/// <summary>
		/// Retry sending a Packet Slice after a Delay and within a maximum number of retries
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="retries"></param>
		/// <returns></returns>
		private async Task ResendSliceData(SyncConnectedClient client, (ushort, ushort) sequence, int retries = 0)
		{
			// TODO : use RTTx1.25 instead of constant delay
			await Task.Delay(_settings.ResendReliablePacketsDelay);
			if (client != null && client.SendSlicesBuffer.TryGetValue(sequence, out byte[] packetData))
			{
				_udpClient.Send(packetData, packetData.Length, new(client.IP, _port));
				// TODO : handle timeout when packets dont arrive within max number of resends
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendSliceData(client, sequence, retries + 1);
			}
			return;
		}


		/// <summary>
		/// Retry sending a Packet after a Delay and within a maximum number of retries
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="retries"></param>
		/// <returns></returns>
		private async Task ResendPacketData(SyncConnectedClient client, ushort sequence, int retries = 0)
		{
			// TODO : use RTTx1.25 instead of constant delay
			await Task.Delay(_settings.ResendReliablePacketsDelay);
			if (client != null && client.SendPacketsBuffer.TryGetValue(sequence, out byte[] packetData))
			{
				_udpClient.Send(packetData, packetData.Length, new(client.IP, _port));
				// TODO : handle timeout when packets dont arrive within max number of resends
				if (retries < _settings.MaxNumberResendReliablePackets - 1)
					_ = ResendPacketData(client, sequence, retries + 1);
			}
			return;
		}

		/// <summary>
		/// Send ACK to Client, acknowledging the receival of a Packet.
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		private void SendACK(SyncConnectedClient client, ushort sequence)
		{
			byte[] data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH];
			Array.Copy(BitConverter.GetBytes(sequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
			data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)SyncPacketType.ACK;
			CalculateAndAddChecksum(data);
			_udpClient.Send(data, data.Length, new(client.IP, _port));
		}

		/// <summary>
		/// Send ACK to Client, acknowledging the receival of a Packet Slice.
		/// </summary>
		/// <param name="client"></param>
		/// <param name="sequence"></param>
		/// <param name="sliceNumber"></param>
		private void SendACK(SyncConnectedClient client, ushort sequence, ushort sliceNumber)
		{
			byte[] data = new byte[CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH + SLICE_NUMBER];
			Array.Copy(BitConverter.GetBytes(sequence), 0, data, CRC32_LENGTH, SEQUENCE_ID_LENGTH);
			data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] = (byte)SyncPacketType.ACK;
			data[CRC32_LENGTH + SEQUENCE_ID_LENGTH] |= 1 << 7;
			Array.Copy(BitConverter.GetBytes(sliceNumber), 0, data, CRC32_LENGTH + SEQUENCE_ID_LENGTH + PACKET_TYPE_LENGTH, SLICE_NUMBER);
			CalculateAndAddChecksum(data);
			_udpClient.Send(data, data.Length, new(client.IP, _port));
		}

		#endregion

		#region helper methods

		/// <summary>
		/// Add CRC32 Checksum to packet header, calculated using Protocol ID and current Roomname
		/// </summary>
		/// <param name="packet"></param>
		private void CalculateAndAddChecksum(byte[] packet)
		{
			// get packet without CRC32 bytes
			byte[] packetData = GetBytesFromArray(packet, CRC32_LENGTH);

			// calculate CRC32 checksum from protocol id, roomname and packet
			byte[] bytes = new byte[PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length + packetData.Length];
			Array.Copy(BitConverter.GetBytes(PROTOCOL_ID), 0, bytes, 0, PROTOCOL_ID_LENGTH);
			Array.Copy(CurrentRoom.RoomnameBytes, 0, bytes, PROTOCOL_ID_LENGTH, CurrentRoom.RoomnameBytes.Length);
			Array.Copy(packetData, 0, bytes, PROTOCOL_ID_LENGTH + CurrentRoom.RoomnameBytes.Length, packetData.Length);
			
			// and add it to packet
			byte[] checksumBytes = BitConverter.GetBytes(SyncCRC32.CRC32Bytes(bytes));
			Array.Copy(checksumBytes, 0, packet, 0, CRC32_LENGTH);
		}

		#endregion
	}
}
