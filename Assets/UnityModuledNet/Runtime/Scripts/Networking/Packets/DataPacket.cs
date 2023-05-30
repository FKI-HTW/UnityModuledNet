using System;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class DataPacket : ASequencedNetworkPacket
	{
		public byte ClientID { get; private set; }
		public Action<bool> Callback { get; private set; }
		public uint ModuleHash { get; private set; }
		public byte[] Data { get; private set; }

		public bool IsChunked { get; private set; }
		public ushort NumberOfSlices { get; private set; }
		public ushort SliceNumber { get; private set; }

		public DataPacket(EPacketType type, uint moduleHash, byte[] data, Action<bool> callback, byte? clientID)
		{
			Type = type;
			ClientID = clientID ?? 0;
			ModuleHash = moduleHash;
			Data = data;
			Callback = callback;

			IsChunked = false;
			NumberOfSlices = 0;
			SliceNumber = 0;
		}

		public DataPacket(EPacketType type, uint moduleHash, byte[] data, Action<bool> callback, byte? clientID,
			ushort numberOfSlices, ushort sliceNumber)
		{
			Type = type;
			ClientID = clientID ?? 0;
			ModuleHash = moduleHash;
			Data = data;
			Callback = callback;

			IsChunked = true;
			NumberOfSlices = numberOfSlices;
			SliceNumber = sliceNumber;
		}

		public DataPacket(EPacketType type, byte[] packet)
		{
			Type = type;
			Bytes = packet;
		}

		public override byte[] Serialize(ushort sequence)
		{
			int chunkedHeaderLength = IsChunked ? ModuledNetSettings.NUMBER_OF_SLICES + ModuledNetSettings.SLICE_NUMBER : 0;
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_HASH_LENGTH + Data.Length];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			Array.Copy(BitConverter.GetBytes(sequence), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH);
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength] = ClientID;
			Array.Copy(BitConverter.GetBytes(ModuleHash), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH, ModuledNetSettings.MODULE_HASH_LENGTH);
			Array.Copy(Data, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_HASH_LENGTH, Data.Length);

			if (IsChunked)
			{
				bytes[ModuledNetSettings.CRC32_LENGTH] |= 1 << 7;
				Array.Copy(BitConverter.GetBytes(NumberOfSlices), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH, ModuledNetSettings.NUMBER_OF_SLICES);
				Array.Copy(BitConverter.GetBytes(SliceNumber), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.NUMBER_OF_SLICES, ModuledNetSettings.SLICE_NUMBER);
			}

			CRC32 = CalculateChecksumBytes(bytes);
			Array.Copy(BitConverter.GetBytes(CRC32), 0, bytes, 0, ModuledNetSettings.CRC32_LENGTH);

			return bytes;
		}

		public override bool TryDeserialize()
		{
			try
			{
				CRC32 = BitConverter.ToUInt32(GetBytesFromArray(Bytes, 0, ModuledNetSettings.CRC32_LENGTH));
				if (!CheckCRC32Checksum(Bytes))
					return false;

				IsChunked = (Bytes[ModuledNetSettings.CRC32_LENGTH] & (1 << 7)) != 0;
				int chunkedHeaderLength = IsChunked ? ModuledNetSettings.NUMBER_OF_SLICES + ModuledNetSettings.SLICE_NUMBER : 0;

				Sequence = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH));
				ClientID = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength];
				ModuleHash = BitConverter.ToUInt32(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH, ModuledNetSettings.MODULE_HASH_LENGTH));
				Data = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_HASH_LENGTH);

				if (IsChunked)
				{
					NumberOfSlices = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH, ModuledNetSettings.NUMBER_OF_SLICES));
					SliceNumber = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.NUMBER_OF_SLICES, ModuledNetSettings.SLICE_NUMBER));
				}

				return true;
			}
			catch (Exception) { return false; }
		}
	}
}
