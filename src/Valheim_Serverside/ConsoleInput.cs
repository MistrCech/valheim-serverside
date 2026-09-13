using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Valheim_Serverside
{
	/*
		Reads the server's standard input line by line as bytes and turns each line into text.
		What encoding a panel writes to the process is its business: AMP on Windows was seen to
		send a single-byte code page while Unity's Mono reads standard input as UTF-8, so every
		accented letter became U+FFFD. A line that is valid UTF-8 is taken as such; anything else
		is decoded with the configured code page. Mono ships without the I18N code page tables
		on the server, so the two Central European ones are built in.
	*/
	public static class ConsoleInput
	{
		private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
		private const string Cp1250 = "€�‚�„…†‡�‰Š‹ŚŤŽŹ�‘’“”•–—�™š›śťžź ˇ˘Ł¤Ą¦§¨©Ş«¬­®Ż°±˛ł´µ¶·¸ąş»Ľ˝ľżŔÁÂĂÄĹĆÇČÉĘËĚÍÎĎĐŃŇÓÔŐÖ×ŘŮÚŰÜÝŢßŕáâăäĺćçčéęëěíîďđńňóôőö÷řůúűüýţ˙";
		private const string Cp852 = "ÇüéâäůćçłëŐőîŹÄĆÉĹĺôöĽľŚśÖÜŤťŁ×čáíóúĄąŽžĘę¬źČş«»░▒▓│┤ÁÂĚŞ╣║╗╝Żż┐└┴┬├─┼Ăă╚╔╩╦╠═╬¤đĐĎËďŇÍÎě┘┌█▄ŢŮ▀ÓßÔŃńňŠšŔÚŕŰýÝţ´­˝˛ˇ˘§÷¸°¨˙űŘř■ ";

		public delegate void LineHandler(string line, string notice);

		// Blocks until standard input ends. Calls the handler for every line; a notice is set the first time a line was not UTF-8.
		public static void ReadLines(Func<int> codePage, LineHandler handler)
		{
			bool noticed = false;
			using (Stream input = System.Console.OpenStandardInput())
			{
				byte[] buffer = new byte[4096];
				List<byte> line = new List<byte>();
				int count;
				while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					for (int i = 0; i < count; i++)
					{
						if (buffer[i] == (byte)'\n')
						{
							Emit(line, codePage(), handler, ref noticed);
							line.Clear();
						}
						else
						{
							line.Add(buffer[i]);
						}
					}
				}
				Emit(line, codePage(), handler, ref noticed);
			}
		}

		private static void Emit(List<byte> line, int codePage, LineHandler handler, ref bool noticed)
		{
			if (line.Count > 0 && line[line.Count - 1] == (byte)'\r')
			{
				line.RemoveAt(line.Count - 1);
			}
			if (line.Count == 0)
			{
				return;
			}
			byte[] data = line.ToArray();
			string notice = null;
			string text = Decode(data, codePage, out bool fellBack);
			if (fellBack && !noticed)
			{
				noticed = true;
				notice = $"Console input is not UTF-8 (bytes: {BitConverter.ToString(data).Replace("-", " ")}); decoded with code page {codePage}."
					+ " If accents come out wrong, set [Server] ConsoleInputCodePage to what the panel sends (1250, 852, 1252 or 65001).";
			}
			handler(text, notice);
		}

		public static string Decode(byte[] data, int codePage, out bool fellBack)
		{
			fellBack = false;
			bool ascii = true;
			foreach (byte b in data)
			{
				if (b >= 0x80)
				{
					ascii = false;
					break;
				}
			}
			if (ascii)
			{
				return Encoding.ASCII.GetString(data);
			}
			if (codePage != 65001)
			{
				try
				{
					return s_utf8.GetString(data);
				}
				catch (DecoderFallbackException)
				{
					fellBack = true;
				}
			}
			return SingleByte(data, codePage);
		}

		private static string SingleByte(byte[] data, int codePage)
		{
			string upper = codePage == 852 ? Cp852 : codePage == 1250 ? Cp1250 : null;
			char[] chars = new char[data.Length];
			for (int i = 0; i < data.Length; i++)
			{
				byte b = data[i];
				// Below 0x80 every code page here is ASCII; above, the table, or Latin-1 for the rest (1252 differs only in 0x80-0x9F).
				chars[i] = b < 0x80 ? (char)b : upper != null ? upper[b - 0x80] : (char)b;
			}
			return new string(chars);
		}
	}
}
