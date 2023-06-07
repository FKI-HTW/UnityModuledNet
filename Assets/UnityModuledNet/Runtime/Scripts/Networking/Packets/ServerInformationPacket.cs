using System;
using System.Text;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ServerInformationPacket : AConnectionNetworkPacket
	{
		public string Servername { get; private set; }
		public byte MaxNumberOfClients { get; private set; }
		public byte NumberOfClients { get; private set; }

		public ServerInformationPacket(string servername, byte maxNumberOfClients, byte numberOfClients)
		{
			Type = EPacketType.ServerInformation;
			Servername = servername;
			MaxNumberOfClients = maxNumberOfClients;
			NumberOfClients = numberOfClients;
		}

		public ServerInformationPacket(byte[] packet)
		{
			Type = EPacketType.ServerInformation;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] servername = Encoding.ASCII.GetBytes(Servername);

			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servername.Length + ModuledNetSettings.NUMBER_CLIENTS_LENGTH + ModuledNetSettings.NUMBER_CLIENTS_LENGTH];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH] = (byte)servername.Length;
			Array.Copy(servername, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, servername.Length);
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servername.Length] = MaxNumberOfClients;
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servername.Length + ModuledNetSettings.NUMBER_CLIENTS_LENGTH] = NumberOfClients;

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

				int servernameLength = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH];
				byte[] servername = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, servernameLength);
				Servername = Encoding.ASCII.GetString(servername);
				MaxNumberOfClients = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servernameLength];
				NumberOfClients = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servernameLength + ModuledNetSettings.NUMBER_CLIENTS_LENGTH];

				return true;
			}
			catch (Exception) { return false; }
		}
	}
}
