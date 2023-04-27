using System.Net;
using System.Text;
using System.Collections.Concurrent;

namespace CENTIS.UnityModuledNet.Networking
{
    public class SyncOpenRoom
    {
        public string Roomname { get; private set; }
        public byte[] RoomnameBytes { get; private set; }
        public ConcurrentDictionary<IPAddress, SyncConnectedClient> ConnectedClients { get; private set; }

        public SyncOpenRoom(string roomname)
		{
            Roomname = roomname;
            RoomnameBytes = Encoding.ASCII.GetBytes(roomname);
            ConnectedClients = new();
		}

		public override bool Equals(object obj)
		{
            if ((obj == null) || !this.GetType().Equals(obj.GetType()))
            {
                return false;
            }
            else
			{
                SyncOpenRoom room = (SyncOpenRoom)obj;
                return room.Roomname.Equals(Roomname);
			}
        }

		public override int GetHashCode()
		{
			return Roomname.GetHashCode();
		}
	}
}
