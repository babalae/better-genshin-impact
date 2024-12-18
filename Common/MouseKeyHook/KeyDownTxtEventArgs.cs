using System;

namespace Gma.System.MouseKeyHook
{
	public class KeyDownTxtEventArgs : EventArgs
	{
		public KeyEventArgsExt KeyEvent { get; }
		public string Chars { get; }

		public KeyDownTxtEventArgs(KeyEventArgsExt keyEvent, string chars)
		{
			KeyEvent = keyEvent;
			Chars = chars ?? string.Empty;
		}
	}
}