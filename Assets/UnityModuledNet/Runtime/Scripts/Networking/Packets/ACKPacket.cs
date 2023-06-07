using System;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ACKPacket : AConnectionNetworkPacket
	{
		public ushort Sequence { get; private set; }

		public bool IsChunked { get; private set; }
		public ushort SliceNumber { get; private set; }

		public ACKPacket(ushort sequence, ushort? sliceNumber = null)
		{
			Type = EPacketType.ACK;
			Sequence = sequence;
			IsChunked = sliceNumber != null;
			if (sliceNumber != null)
				SliceNumber = (ushort)sliceNumber;
		}

		public ACKPacket(byte[] packet)
		{
			Type = EPacketType.ACK;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			int sliceNumberLength = IsChunked ? ModuledNetSettings.SLICE_NUMBER : 0;
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + sliceNumberLength];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			Array.Copy(BitConverter.GetBytes(Sequence), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH);

			if (IsChunked)
			{
				bytes[ModuledNetSettings.CRC32_LENGTH] |= 1 << 7;
				Array.Copy(BitConverter.GetBytes(SliceNumber), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH, ModuledNetSettings.SLICE_NUMBER);
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

				Sequence = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH));
				IsChunked = (Bytes[ModuledNetSettings.CRC32_LENGTH] & (1 << 7)) != 0;

				if (IsChunked)
					SliceNumber = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH, ModuledNetSettings.SLICE_NUMBER));

				return true;
			}
			catch (Exception) { return false; }
		}
	}
}
