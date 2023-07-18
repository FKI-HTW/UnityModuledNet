using System;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class DataPacket : ASequencedNetworkPacket
	{
		public byte ClientID { get; private set; }
		public Action<bool> Callback { get; private set; }
		public byte[] ModuleID { get; private set; }
		public byte[] Data { get; private set; }

		public bool IsChunked { get; private set; }
		public ushort NumberOfSlices { get; private set; }
		public ushort SliceNumber { get; set; }

		public DataPacket(EPacketType type, byte[] moduleID, byte[] data, Action<bool> callback, byte? clientID)
		{
			Type = type;
			ClientID = clientID ?? 0;
			ModuleID = moduleID;
			Data = data;
			Callback = callback;

			var mtu = ModuledNetSettings.Settings.MTU;
            if (data.Length > mtu)
			{   // if data is larger than MTU split packet into slices and send individually
				IsChunked = true;
				NumberOfSlices = (ushort)(data.Length % mtu == 0
					? data.Length / mtu
					: data.Length / mtu + 1);
				SliceNumber = 0;
			}
			else
			{
				IsChunked = false;
				NumberOfSlices = 0;
				SliceNumber = 0;
			}
		}

		public DataPacket(EPacketType type, byte[] packet)
		{
			Type = type;
			Bytes = packet;
		}

		public override byte[] Serialize(ushort sequence)
		{
            var mtu = ModuledNetSettings.Settings.MTU;
            int chunkedHeaderLength = IsChunked ? ModuledNetSettings.NUMBER_OF_SLICES + ModuledNetSettings.SLICE_NUMBER : 0;
			int sliceSize = IsChunked ? (SliceNumber < NumberOfSlices - 1 ? mtu : Data.Length % mtu) : Data.Length;

			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_ID_LENGTH + sliceSize];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			Array.Copy(BitConverter.GetBytes(sequence), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH);
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength] = ClientID;
			Array.Copy(ModuleID, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH, ModuledNetSettings.MODULE_ID_LENGTH);
			Array.Copy(Data, SliceNumber * mtu, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_ID_LENGTH, sliceSize);

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
				ModuleID = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH, ModuledNetSettings.MODULE_ID_LENGTH);
				Data = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + chunkedHeaderLength + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.MODULE_ID_LENGTH);

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
