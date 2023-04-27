using System;

namespace CENTIS.UnityModuledNet
{
	public struct SyncMessage
	{
		public string Message { get; private set; }
		public SyncMessageSeverity Severity { get; private set; }
		public DateTime Timestamp { get; private set; }

		public SyncMessage(string message, SyncMessageSeverity severity = SyncMessageSeverity.Log)
		{
			Message = message;
			Severity = severity;
			Timestamp = DateTime.Now;
		}
	}
}
