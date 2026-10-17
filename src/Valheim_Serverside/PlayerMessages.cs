using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginConfiguration;
using UnityEngine;

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

		The middle text is also gone fast: MessageHud shows it at full strength and fades it out over 4 seconds from
		the moment it arrives, so what has several lines is half gone before the last is read. The text of a message in
		the middle is therefore sent again every second, [Server] MessageSecondsPerLine seconds for each of its lines
		(the game takes every arrival as a new message and starts the fade again). A new message to a player replaces
		the one that is being held for them, and a player who has left is dropped. Whoever sends a message that is
		followed by something that cannot wait for it to be read (a kick) is told how long it is held.
	*/
	internal static class PlayerMessages
	{
		private const float RepeatSeconds = 1f;
		private const float MaxHoldSeconds = 30f;

		// The clock of the repeats is real time, not Time.time: the client fades the text in real time, and Time.time of a busy server
		// falls behind (a frame counts at most Time.maximumDeltaTime, a third of a second: 15 s of it took 19 on a loaded test server).
		private static float Now => Time.realtimeSinceStartup;

		private class Held
		{
			public long peer;
			public string text;
			public float next;
			public float until;
		}

		private static readonly List<Held> s_held = new List<Held>();

		// Shows a message and returns the seconds it is held on the screen (0: it was shown once, as the game does).
		public static float Show(long target, MessageHud.MessageType type, string text)
		{
			int width = Configuration.messageLineLength.Value;
			if (type == MessageHud.MessageType.TopLeft)
			{
				width *= 2;
			}
			string wrapped = Wrap(text, width);
			ZRoutedRpc.instance.InvokeRoutedRPC(target, "ShowMessage", (int)type, wrapped);
			if (type != MessageHud.MessageType.Center)
			{
				return 0f;
			}
			// Whatever the new message reaches, it takes the place of the one that was being held there.
			s_held.RemoveAll(held => target == ZRoutedRpc.Everybody || held.peer == target);
			float seconds = HoldSeconds(wrapped);
			if (seconds > RepeatSeconds)
			{
				float now = Now;
				foreach (long peer in target == ZRoutedRpc.Everybody ? ZNet.instance.GetPeers().Where(p => p.IsReady()).Select(p => p.m_uid).ToList() : new List<long> { target })
				{
					s_held.Add(new Held { peer = peer, text = wrapped, next = now + RepeatSeconds, until = now + seconds });
				}
			}
			return seconds;
		}

		// The seconds a message in the middle of the screen is held: MessageSecondsPerLine for each of its lines.
		public static float HoldSeconds(string wrapped)
		{
			float perLine = Configuration.messageSecondsPerLine.Value;
			if (perLine <= 0f || string.IsNullOrWhiteSpace(wrapped))
			{
				return 0f;
			}
			return Mathf.Min(MaxHoldSeconds, perLine * (wrapped.Count(c => c == '\n') + 1));
		}

		// Every frame, from ServersidePlugin.Update: sends again what is being held.
		public static void Tick()
		{
			if (s_held.Count == 0)
			{
				return;
			}
			float now = Now;
			for (int i = s_held.Count - 1; i >= 0; i--)
			{
				Held held = s_held[i];
				if (now < held.next)
				{
					continue;
				}
				if (ZNet.instance.GetPeer(held.peer) == null)
				{
					s_held.RemoveAt(i);
					continue;
				}
				ZRoutedRpc.instance.InvokeRoutedRPC(held.peer, "ShowMessage", (int)MessageHud.MessageType.Center, held.text);
				held.next = Mathf.Max(held.next + RepeatSeconds, now);
				if (held.next >= held.until)
				{
					s_held.RemoveAt(i);
				}
			}
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

		// A line typed into the console is one line, and a panel may pass nothing but the letters of an old code page (AMP on a Western
		// Windows replaces č ř š ž ů ě by c r s z u e before the server sees them): `\n` is a line break, `\u010d` the letter with that code,
		// `\\` a backslash. Any other backslash stays as it was typed.
		public static string Unescape(string text)
		{
			if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0)
			{
				return text;
			}
			StringBuilder result = new StringBuilder(text.Length);
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c == '\\' && i + 1 < text.Length)
				{
					char next = text[i + 1];
					if (next == 'n')
					{
						result.Append('\n');
						i++;
						continue;
					}
					if (next == '\\')
					{
						result.Append('\\');
						i++;
						continue;
					}
					if (next == 'u' && i + 6 <= text.Length && int.TryParse(text.Substring(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
					{
						result.Append((char)code);
						i += 5;
						continue;
					}
				}
				result.Append(c);
			}
			return result.ToString();
		}
	}
}
