using System.Text;
using PluginConfiguration;

namespace Valheim_Serverside
{
	/*
		What the server shows on players' screens: MessageHud's routed RPC "ShowMessage". A vanilla client puts the
		text into a TextMeshPro that never wraps (MessageHud.ShowMessage; Averia Serif at size 40 in the middle of the
		screen, 20 in the top left corner, overflow on), so a long message ran off both sides of the screen: measured
		with the game's own fonts on a dedicated server (17 Oct 2026), the 72 characters of the portal lock message take
		1200 units in the middle, the 218 of the anti-cheat notice 3921, and the screen is 1920 units wide
		at the default GUI scale (GuiScaler: the smaller of width/1920 and height/1080, times the GUI scale). A line
		break in the text is kept, so lines are broken here, at spaces: [Server] MessageLineLength characters in the
		middle, twice as many in the top left corner, where the letters are half as big.
	*/
	internal static class PlayerMessages
	{
		public static void Show(long target, MessageHud.MessageType type, string text)
		{
			int width = Configuration.messageLineLength.Value;
			if (type == MessageHud.MessageType.TopLeft)
			{
				width *= 2;
			}
			ZRoutedRpc.instance.InvokeRoutedRPC(target, "ShowMessage", (int)type, Wrap(text, width));
		}

		// Lines of at most `width` characters, broken at spaces; a word longer than that stands on a line of its own.
		public static string Wrap(string text, int width)
		{
			if (width <= 0 || string.IsNullOrEmpty(text) || (text.Length <= width && text.IndexOf('\n') < 0))
			{
				return text;
			}
			StringBuilder wrapped = new StringBuilder(text.Length + 8);
			string[] lines = text.Replace("\r\n", "\n").Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				if (i > 0)
				{
					wrapped.Append('\n');
				}
				int length = 0;
				foreach (string word in lines[i].Split(' '))
				{
					if (word.Length == 0)
					{
						continue;
					}
					if (length > 0 && length + 1 + word.Length > width)
					{
						wrapped.Append('\n');
						length = 0;
					}
					else if (length > 0)
					{
						wrapped.Append(' ');
						length++;
					}
					wrapped.Append(word);
					length += word.Length;
				}
			}
			return wrapped.ToString();
		}
	}
}
