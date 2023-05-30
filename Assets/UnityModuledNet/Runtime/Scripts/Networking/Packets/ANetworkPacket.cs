using System;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
    internal abstract class ANetworkPacket
    {
        public EPacketType Type { get; protected set; }
        public uint CRC32 { get; protected set; }
        public byte[] Bytes { get; protected set; }

        protected static byte[] GetBytesFromArray(byte[] array, int offset, int size = 0)
        {
            if (size == 0)
                size = array.Length - offset;

            byte[] bytes = new byte[size];
            Array.Copy(array, offset, bytes, 0, size);
            return bytes;
        }

        protected virtual uint CalculateChecksumBytes(byte[] packet)
        {
            if (packet == null)
                throw new Exception("The Bytes have to be initialized before a Checksum can be calculated!");

            byte[] packetData = GetBytesFromArray(packet, ModuledNetSettings.CRC32_LENGTH);

            // calculate CRC32 checksum from protocol id and packet data
            byte[] bytes = new byte[ModuledNetSettings.PROTOCOL_ID_LENGTH + packetData.Length];
            Array.Copy(BitConverter.GetBytes(ModuledNetSettings.PROTOCOL_ID), 0, bytes, 0, ModuledNetSettings.PROTOCOL_ID_LENGTH);
            Array.Copy(packetData, 0, bytes, ModuledNetSettings.PROTOCOL_ID_LENGTH, packetData.Length);

            return SyncCRC32.CRC32Bytes(bytes);
        }

        protected virtual bool CheckCRC32Checksum(byte[] packet)
        {
            return CRC32 == CalculateChecksumBytes(packet);
        }
    }
}
