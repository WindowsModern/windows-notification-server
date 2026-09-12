using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace PipeTest
{
	class Program
	{
		private const string PipeName = "SidebarNotifyIconPipe";
		private const int HeaderSize = 20;      // STRAYICONDATA 头部（到 bBuffer 之前）
		private const int MaxReasonableSize = 1024 * 1024;

		static void Main (string [] args)
		{
			Console.WriteLine ("SidebarNotifyIconPipe 测试程序");
			Console.WriteLine ("等待管道连接... (按 Ctrl+C 退出)");
			Console.WriteLine ();

			while (true)
			{
				NamedPipeServerStream pipeServer = null;
				try
				{
					pipeServer = CreatePipeServer ();

					Console.WriteLine ($"[{DateTime.Now:HH:mm:ss}] 等待连接...");
					pipeServer.WaitForConnection ();
					Console.WriteLine ($"[{DateTime.Now:HH:mm:ss}] 已连接");

					byte [] data = ReadAllData (pipeServer);
					if (data == null || data.Length < HeaderSize)
					{
						Console.WriteLine ($"[警告] 数据过短 ({data?.Length ?? 0} 字节)");
						continue;
					}

					Console.WriteLine ($"[{DateTime.Now:HH:mm:ss}] 收到 {data.Length} 字节");

					try
					{
						ParseAndPrint (data);
					}
					catch (Exception pex)
					{
						Console.WriteLine ($"[解析异常] {pex.GetType ().Name}: {pex.Message}");
						DumpBytes (data);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine ($"[连接异常] {ex.GetType ().Name}: {ex.Message}");
				}
				finally
				{
					if (pipeServer != null)
					{
						try { pipeServer.Dispose (); } catch { }
					}
				}

				// 短暂延迟，避免管道名尚未完全释放时立刻重建失败
				Thread.Sleep (50);
			}
		}

		/// <summary>
		/// 创建管道服务器，若失败则重试若干次。
		/// </summary>
		private static NamedPipeServerStream CreatePipeServer ()
		{
			const int maxRetry = 10;
			IOException lastEx = null;
			for (int i = 0; i < maxRetry; i++)
			{
				try
				{
					return new NamedPipeServerStream (
						PipeName,
						PipeDirection.In,
						1,
						PipeTransmissionMode.Byte,
						PipeOptions.None);
				}
				catch (IOException ex)
				{
					lastEx = ex;
					Console.WriteLine ($"[重试 {i + 1}/{maxRetry}] 创建管道失败: {ex.Message}");
					Thread.Sleep (200);
				}
			}
			throw new IOException ($"重试 {maxRetry} 次后仍无法创建管道服务器", lastEx);
		}

		/// <summary>
		/// 按 dwSize 读取完整的一帧数据。
		/// </summary>
		static byte [] ReadAllData (NamedPipeServerStream pipe)
		{
			// 头部固定 20 字节；偏移 8 处为 dwSize
			byte [] header = new byte [HeaderSize];
			int read = 0;
			while (read < header.Length)
			{
				int r = pipe.Read (header, read, header.Length - read);
				if (r == 0) throw new EndOfStreamException ("管道在读取头部时关闭");
				read += r;
			}

			int dwSize = BitConverter.ToInt32 (header, 8);
			if (dwSize < HeaderSize)
				throw new InvalidDataException ($"无效的 dwSize: {dwSize}");
			if (dwSize > MaxReasonableSize)
				throw new InvalidDataException ($"dwSize 过大，可能不是有效数据: {dwSize}");

			byte [] fullData = new byte [dwSize];
			Buffer.BlockCopy (header, 0, fullData, 0, header.Length);

			int totalRead = header.Length;
			while (totalRead < dwSize)
			{
				int r = pipe.Read (fullData, totalRead, dwSize - totalRead);
				if (r == 0) throw new EndOfStreamException ("管道在读取数据时关闭");
				totalRead += r;
			}

			return fullData;
		}

		/// <summary>
		/// 解析 STRAYICONDATA 并打印。
		/// </summary>
		static void ParseAndPrint (byte [] data)
		{
			// ---- 固定头部 ----
			long timeStamp = BitConverter.ToInt64 (data, 0);
			int dwSize = BitConverter.ToInt32 (data, 8);
			byte bHWndSizeOf = data [12];
			byte bUIntSizeOf = data [13];
			byte bHIconSizeOf = data [14];
			byte bNone = data [15];
			uint dwMessage = BitConverter.ToUInt32 (data, 16);

			Console.WriteLine ("========================================");

			try
			{
				var epoch = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				var localTime = epoch.AddSeconds (timeStamp).ToLocalTime ();
				Console.WriteLine ($"时间戳 (UTC): {timeStamp} -> {localTime:yyyy-MM-dd HH:mm:ss}");
			}
			catch
			{
				Console.WriteLine ($"时间戳 (UTC): {timeStamp} (无效)");
			}

			Console.WriteLine ($"数据总大小: {dwSize} 字节 (实际收到: {data.Length})");
			Console.WriteLine ($"HWND 大小: {bHWndSizeOf}, UINT 大小: {bUIntSizeOf}, HICON 大小: {bHIconSizeOf}, 标记: {(char)bNone}");
			Console.WriteLine ($"消息类型: 0x{dwMessage:X8} ({GetMessageName (dwMessage)})");

			// ---- 变长区：bBuffer 从偏移 20 开始 ----
			int offset = HeaderSize;

			IntPtr hWnd = (IntPtr)ReadIntPtr (data, ref offset, bHWndSizeOf);
			uint uID = ReadUInt32 (data, ref offset);
			uint uFlags = ReadUInt32 (data, ref offset);
			uint uCallbackMessage = ReadUInt32 (data, ref offset);
			IntPtr hIcon = (IntPtr)ReadIntPtr (data, ref offset, bHIconSizeOf);
			uint dwState = ReadUInt32 (data, ref offset);
			uint dwStateMask = ReadUInt32 (data, ref offset);
			uint uTimeout = ReadUInt32 (data, ref offset);
			uint uVersion = ReadUInt32 (data, ref offset);
			uint dwInfoFlags = ReadUInt32 (data, ref offset);
			Guid guidItem = ReadGuid (data, ref offset);

			Console.WriteLine ($"hWnd: 0x{hWnd.ToInt64 ():X}");
			Console.WriteLine ($"uID: {uID}");
			Console.WriteLine ($"uFlags: 0x{uFlags:X8} ({FormatNifFlags (uFlags)})");
			Console.WriteLine ($"uCallbackMessage: 0x{uCallbackMessage:X8}");
			Console.WriteLine ($"hIcon: 0x{hIcon.ToInt64 ():X}");
			Console.WriteLine ($"dwState: 0x{dwState:X8}");
			Console.WriteLine ($"dwStateMask: 0x{dwStateMask:X8}");
			Console.WriteLine ($"uTimeout: {uTimeout}");
			Console.WriteLine ($"uVersion: {uVersion}");
			Console.WriteLine ($"dwInfoFlags: 0x{dwInfoFlags:X8} ({FormatNiifFlags (dwInfoFlags)})");
			Console.WriteLine ($"guidItem: {guidItem}");

			string szTip = ReadStringW (data, ref offset);
			string szInfo = ReadStringW (data, ref offset);
			string szInfoTitle = ReadStringW (data, ref offset);

			Console.WriteLine ($"szTip: {szTip}");
			Console.WriteLine ($"szInfo: {szInfo}");
			Console.WriteLine ($"szInfoTitle: {szInfoTitle}");
			Console.WriteLine ("========================================\n");
		}

		// ---------------- 辅助读取 ----------------

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
			throw new NotSupportedException ($"不支持的指针大小: {size}");
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
				throw new InvalidDataException ($"越界读取：offset={offset}, size={size}, total={data.Length}");
		}

		// ---------------- 格式化辅助 ----------------

		static string GetMessageName (uint msg)
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

		static string FormatNifFlags (uint f)
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

		static string FormatNiifFlags (uint f)
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

		static void DumpBytes (byte [] data)
		{
			if (data == null) return;
			const int batch = 512;
			for (int i = 0; i < data.Length; i += batch)
			{
				int n = Math.Min (batch, data.Length - i);
				var sb = new StringBuilder (n * 3 + 2);
				sb.Append ('{');
				for (int j = 0; j < n; j++)
				{
					if (j > 0) sb.Append (' ');
					sb.Append (data [i + j].ToString ("X2"));
				}
				sb.Append ('}');
				Console.WriteLine (sb.ToString ());
			}
		}
	}
}