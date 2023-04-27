using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace CENTIS.UnityModuledNet.Networking
{
    internal enum SyncPacketType : byte
    {
        ACK,
        Heartbeat,
        ReliableData,
        ReliableUnorderedData,
        UnreliableData,
        UnreliableUnorderedData
    }

    internal abstract class SyncPacket
    {
        // TODO : replace the const length with calculated MTU
        protected const int MAX_SLICE_LENGTH = 1200;

        public SyncPacketType Type { get; protected set; }
        public uint ModuleHash { get; protected set; }
        public SyncConnectedClient Client { get; protected set; }
        public byte[] Data { get; protected set; }
    }

    internal class SyncReceiverPacket : SyncPacket
    {
        public bool IsChunked { get; private set; }
        public ushort NumberOfSlices { get; private set; }
        public ConcurrentDictionary<ushort, byte[]> Slices { get; private set; }

        /// <summary>
        /// Creates a Packet that was sent by another Client.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="client"></param>
        public SyncReceiverPacket(SyncPacketType type, uint moduleHash, byte[] data, SyncConnectedClient client)
        {
            Type = type;
            ModuleHash = moduleHash;
            Client = client;
            Data = data;
            IsChunked = false;
            Slices = null;
        }

        /// <summary>
        /// Creates a chunked Packet that was sent by another Client.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="client"></param>
        /// <param name="numberOfSlices"></param>
        public SyncReceiverPacket(SyncPacketType type, uint moduleHash, SyncConnectedClient client, ushort numberOfSlices)
        {
            Type = type;
            ModuleHash = moduleHash;
            Client = client;
            Data = null;
            IsChunked = true;
            NumberOfSlices = numberOfSlices;
            Slices = new();
        }

        public bool IsPacketComplete()
        {
            return !IsChunked || Slices.Count == NumberOfSlices;
        }

        public bool ConcatenateChunkedPacket()
        {
            if (!IsPacketComplete())
                return false;

            List<byte> dataBytes = new();
            for (ushort i = 1; i <= NumberOfSlices; i++)
            {
                Slices.TryGetValue(i, out byte[] sliceData);
                dataBytes.AddRange(sliceData);
            }
            Data = dataBytes.ToArray();
            IsChunked = false;
            NumberOfSlices = 0;
            return true;
        }
    }

    internal class SyncSenderPacket : SyncPacket
    {
        public Action<bool> OnDataSend { get; private set; }
        public bool IsChunked { get; private set; }
        public ushort NumberOfSlices { get; private set; }
        public ushort CurrentSliceNumber { get; private set; }

        /// <summary>
        /// Creates packet that has yet to be sent to clients.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="data"></param>
        /// <param name="onDataSend"></param>
        /// <param name="client"></param>
        public SyncSenderPacket(SyncPacketType type, uint moduleHash, byte[] data,
            Action<bool> onDataSend, SyncConnectedClient client = null)
        {
            Type = type;
            ModuleHash = moduleHash;
            Client = client;
            Data = data;
            OnDataSend = onDataSend;
            IsChunked = data.Length > 1200;
            NumberOfSlices = (ushort)(data.Length % MAX_SLICE_LENGTH == 0
                ? data.Length / MAX_SLICE_LENGTH
                : data.Length / MAX_SLICE_LENGTH + 1);
            CurrentSliceNumber = 0;
        }

        public byte[] GetNextSlice()
        {
            if (!IsChunked)
                return Data;

            if (CurrentSliceNumber >= NumberOfSlices)
                return null;

            int sliceSize = CurrentSliceNumber < NumberOfSlices - 1
                ? MAX_SLICE_LENGTH
                : Data.Length % MAX_SLICE_LENGTH;
            byte[] data = new byte[sliceSize];
            Array.Copy(Data, CurrentSliceNumber * MAX_SLICE_LENGTH, data, 0, sliceSize);

            CurrentSliceNumber++;
            return data;
        }
    }
}
