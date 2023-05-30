using System;

namespace CENTIS.UnityModuledNet
{
	public struct ModuledNetMessage
	{
		public string Message { get; private set; }
		public ModuledNetMessageSeverity Severity { get; private set; }
		public DateTime Timestamp { get; private set; }

		public ModuledNetMessage(string message, ModuledNetMessageSeverity severity = ModuledNetMessageSeverity.Log)
		{
			Message = message;
			Severity = severity;
			Timestamp = DateTime.Now;
		}
	}
}
