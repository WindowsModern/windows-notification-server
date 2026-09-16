// ============================================================
// StrayIconParser.cs
// 与 PipeTest 保持完全一致的 STRAYICONDATA 解析逻辑
// ============================================================
using System;
using System.IO;
using System.Text;

namespace TestCenter
{
	/// <summary>
	/// 解析后的 STRAYICONDATA（变长区按 PipeTest 的读取顺序展开）。
	/// </summary>
	public class NotifyIconData
	{
		// 固定头部
		public long TimeStamp;
		public int DwSize;
		public byte HwndSizeOf;
		public byte UIntSizeOf;
		public byte HIconSizeOf;
		public byte None;
		public uint Message;

		// 变长区
		public long HWnd;
		public uint UID;
		public uint UFlags;
		public uint UCallbackMessage;
		public long HIcon;
		public uint DwState;
		public uint DwStateMask;
		public uint UTimeout;
		public uint UVersion;
		public uint DwInfoFlags;
		public Guid GuidItem;
		public string SzTip = string.Empty;
		public string SzInfo = string.Empty;
		public string SzInfoTitle = string.Empty;
	}

	public static class StrayIconParser
	{
		public const int HeaderSize = 20;                       // STRAYICONDATA 头部（到 bBuffer 之前）
		public const int MaxReasonableSize = 1024 * 1024;
		public const int MaxDumpBytes = 64 * 1024;              // 十六进制视图最多显示的字节数

		// ---------------- 解析 ----------------

		public static NotifyIconData Parse (byte [] data)
		{
			if (data == null)
				throw new ArgumentNullException ("data");
			if (data.Length < HeaderSize)
				throw new InvalidDataException (string.Format ("数据长度不足：{0} < {1}", data.Length, HeaderSize));

			var d = new NotifyIconData ();

			d.TimeStamp = BitConverter.ToInt64 (data, 0);
			d.DwSize = BitConverter.ToInt32 (data, 8);
			d.HwndSizeOf = data [12];
			d.UIntSizeOf = data [13];
			d.HIconSizeOf = data [14];
			d.None = data [15];
			d.Message = BitConverter.ToUInt32 (data, 16);

			int offset = HeaderSize;

			d.HWnd = ReadIntPtr (data, ref offset, d.HwndSizeOf);
			d.UID = ReadUInt32 (data, ref offset);
			d.UFlags = ReadUInt32 (data, ref offset);
			d.UCallbackMessage = ReadUInt32 (data, ref offset);
			d.HIcon = ReadIntPtr (data, ref offset, d.HIconSizeOf);
			d.DwState = ReadUInt32 (data, ref offset);
			d.DwStateMask = ReadUInt32 (data, ref offset);
			d.UTimeout = ReadUInt32 (data, ref offset);
			d.UVersion = ReadUInt32 (data, ref offset);
			d.DwInfoFlags = ReadUInt32 (data, ref offset);
			d.GuidItem = ReadGuid (data, ref offset);

			d.SzTip = ReadStringW (data, ref offset);
			d.SzInfo = ReadStringW (data, ref offset);
			d.SzInfoTitle = ReadStringW (data, ref offset);

			return d;
		}

		// ---------------- 读取辅助 ----------------

		static long ReadIntPtr (byte [] data, ref int offset, int size)
		{
			if (size == 8)
			{
				EnsureRange (data, offset, 8);
				long val = BitConverter.ToInt64 (data, offset);
				offset += 8;
				return val;
			}
			if (size == 4)
			{
				EnsureRange (data, offset, 4);
				int val = BitConverter.ToInt32 (data, offset);
				offset += 4;
				return val;
			}
			throw new NotSupportedException (string.Format ("不支持的指针大小: {0}", size));
		}

		static uint ReadUInt32 (byte [] data, ref int offset)
		{
			EnsureRange (data, offset, 4);
			uint val = BitConverter.ToUInt32 (data, offset);
			offset += 4;
			return val;
		}

		static Guid ReadGuid (byte [] data, ref int offset)
		{
			EnsureRange (data, offset, 16);
			byte [] guidBytes = new byte [16];
			Buffer.BlockCopy (data, offset, guidBytes, 0, 16);
			offset += 16;
			return new Guid (guidBytes);
		}

		/// <summary>
		/// 读取以 UTF-16 null 结尾的字符串；到达数据末尾则停止。
		/// </summary>
		static string ReadStringW (byte [] data, ref int offset)
		{
			if (offset < 0 || offset + 1 >= data.Length) return string.Empty;

			int start = offset;
			while (offset + 1 < data.Length)
			{
				if (data [offset] == 0 && data [offset + 1] == 0)
					break;
				offset += 2;
			}

			int charCount = (offset - start) / 2;
			string str = charCount > 0
				? Encoding.Unicode.GetString (data, start, charCount * 2)
				: string.Empty;

			// 跳过 null 终止符（如果存在）
			if (offset + 1 < data.Length && data [offset] == 0 && data [offset + 1] == 0)
				offset += 2;

			return str;
		}

		static void EnsureRange (byte [] data, int offset, int size)
		{
			if (offset < 0 || offset + size > data.Length)
				throw new InvalidDataException (string.Format ("越界读取：offset={0}, size={1}, total={2}", offset, size, data.Length));
		}

		// ---------------- 格式化辅助 ----------------

		public static string GetMessageName (uint msg)
		{
			switch (msg)
			{
				case 0x00000000: return "NIM_ADD";
				case 0x00000001: return "NIM_MODIFY";
				case 0x00000002: return "NIM_DELETE";
				case 0x00000003: return "NIM_SETFOCUS";
				case 0x00000004: return "NIM_SETVERSION";
				default: return "未知";
			}
		}

		public static string FormatNifFlags (uint f)
		{
			if (f == 0) return "0";
			var sb = new StringBuilder ();
			AppendFlag (sb, f, 0x00000001, "NIF_MESSAGE");
			AppendFlag (sb, f, 0x00000002, "NIF_ICON");
			AppendFlag (sb, f, 0x00000004, "NIF_TIP");
			AppendFlag (sb, f, 0x00000008, "NIF_STATE");
			AppendFlag (sb, f, 0x00000010, "NIF_INFO");
			AppendFlag (sb, f, 0x00000020, "NIF_GUID");
			AppendFlag (sb, f, 0x00000040, "NIF_REALTIME");
			AppendFlag (sb, f, 0x00000080, "NIF_SHOWTIP");
			return sb.Length > 0 ? sb.ToString () : "?";
		}

		static void AppendFlag (StringBuilder sb, uint flags, uint bit, string name)
		{
			if ((flags & bit) != 0)
			{
				if (sb.Length > 0) sb.Append ('|');
				sb.Append (name);
			}
		}

		public static string FormatNiifFlags (uint f)
		{
			switch (f & 0x0F)
			{
				case 0x00: return "NIIF_NONE";
				case 0x01: return "NIIF_INFO";
				case 0x02: return "NIIF_WARNING";
				case 0x03: return "NIIF_ERROR";
				case 0x04: return "NIIF_USER";
				default: return "NIIF_?";
			}
		}

		/// <summary>
		/// 生成类似 WinHex 的十六进制视图（偏移 + 16 字节 + ASCII）。
		/// </summary>
		public static string FormatHexDump (byte [] data)
		{
			if (data == null || data.Length == 0)
				return "（空数据）";

			int count = Math.Min (data.Length, MaxDumpBytes);
			StringBuilder sb = new StringBuilder (count * 4 + 128);

			for (int i = 0; i < count; i += 16)
			{
				sb.Append (i.ToString ("X8")).Append ("  ");

				int n = Math.Min (16, count - i);

				for (int j = 0; j < 16; j++)
				{
					if (j < n)
						sb.Append (data [i + j].ToString ("X2")).Append (' ');
					else
						sb.Append ("   ");

					if (j == 7) sb.Append (' ');
				}

				sb.Append (" |");
				for (int j = 0; j < n; j++)
				{
					byte b = data [i + j];
					sb.Append (b >= 0x20 && b < 0x7F ? (char)b : '.');
				}
				sb.Append ('|');
				sb.AppendLine ();
			}

			if (count < data.Length)
			{
				sb.AppendLine ();
				sb.AppendFormat ("... 剩余 {0} 字节未显示（上限 {1} 字节）...", data.Length - count, MaxDumpBytes);
				sb.AppendLine ();
			}

			return sb.ToString ();
		}
	}
}