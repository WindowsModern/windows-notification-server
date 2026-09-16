// ============================================================
// ReportBuilder.cs
// 生成通知帧报告：接收信息 + 解析结果 + 完整字节集
// ============================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace TestCenter
{
	public static class ReportBuilder
	{
		public static string Build (IList<PipeDataEventArgs> frames, string pipeName, string title)
		{
			return Build (frames, pipeName, title, 1);
		}

		public static string Build (IList<PipeDataEventArgs> frames, string pipeName, string title, int startIndex)
		{
			if (frames == null) throw new ArgumentNullException ("frames");

			var sb = new StringBuilder ();

			sb.AppendLine (new string ('=', 78));
			sb.AppendLine (title);
			sb.AppendLine (new string ('=', 78));
			sb.AppendLine (string.Format ("生成时间：{0:yyyy-MM-dd HH:mm:ss.fff}", DateTime.Now));
			sb.AppendLine (string.Format ("管道名称：{0}", pipeName ?? string.Empty));
			sb.AppendLine (string.Format ("报告帧数：{0}", frames.Count));
			sb.AppendLine ();

			for (int i = 0; i < frames.Count; i++)
			{
				AppendFrame (sb, frames [i], startIndex + i);
			}

			return sb.ToString ();
		}

		// ---------------- 单帧 ----------------

		private static void AppendFrame (StringBuilder sb, PipeDataEventArgs e, int index)
		{
			sb.AppendLine ();
			sb.AppendLine (new string ('-', 78));
			sb.AppendLine (string.Format ("第 {0} 帧", index));
			sb.AppendLine (new string ('-', 78));

			sb.AppendLine (string.Format ("接收时间：{0:yyyy-MM-dd HH:mm:ss.fff}", e.ReceivedAt));
			sb.AppendLine (string.Format ("数据长度：{0} 字节", e.Data.Length));

			if (e.Parsed == null)
			{
				sb.AppendLine ("解析结果：失败");
				if (e.ParseError != null)
					sb.AppendLine (string.Format ("错误信息：{0}: {1}",
						e.ParseError.GetType ().Name, e.ParseError.Message));
			}
			else
			{
				AppendParsed (sb, e.Parsed);
			}

			AppendHex (sb, e.Data);
			sb.AppendLine ();
		}

		// ---------------- 解析内容 ----------------

		private static void AppendParsed (StringBuilder sb, NotifyIconData d)
		{
			sb.AppendLine ();
			sb.AppendLine ("【解析结果】");
			sb.AppendLine (string.Format ("  时间戳 (UTC)：{0}", d.TimeStamp));
			sb.AppendLine (string.Format ("  本地时间：{0}", FormatTimestamp (d.TimeStamp)));
			sb.AppendLine (string.Format ("  dwSize：{0} 字节", d.DwSize));
			sb.AppendLine (string.Format ("  消息类型：0x{0:X8}  {1}",
				d.Message, StrayIconParser.GetMessageName (d.Message)));

			sb.AppendLine ();
			sb.AppendLine ("  -- 头部字段 --");
			sb.AppendLine (string.Format ("  bHWndSizeOf：{0}", d.HwndSizeOf));
			sb.AppendLine (string.Format ("  bUIntSizeOf：{0}", d.UIntSizeOf));
			sb.AppendLine (string.Format ("  bHIconSizeOf：{0}", d.HIconSizeOf));
			sb.AppendLine (string.Format ("  bNone：0x{0:X2}", d.None));

			sb.AppendLine ();
			sb.AppendLine ("  -- NOTIFYICONDATA --");
			sb.AppendLine (string.Format ("  hWnd：{0}", FormatPointer (d.HWnd, d.HwndSizeOf)));
			sb.AppendLine (string.Format ("  uID：{0}", d.UID));
			sb.AppendLine (string.Format ("  uFlags：0x{0:X8}  {1}",
				d.UFlags, StrayIconParser.FormatNifFlags (d.UFlags)));
			sb.AppendLine (string.Format ("  uCallbackMessage：0x{0:X8}", d.UCallbackMessage));
			sb.AppendLine (string.Format ("  hIcon：{0}", FormatPointer (d.HIcon, d.HIconSizeOf)));
			sb.AppendLine (string.Format ("  dwState：0x{0:X8}", d.DwState));
			sb.AppendLine (string.Format ("  dwStateMask：0x{0:X8}", d.DwStateMask));
			sb.AppendLine (string.Format ("  uTimeout：{0}", d.UTimeout));
			sb.AppendLine (string.Format ("  uVersion：{0}", d.UVersion));
			sb.AppendLine (string.Format ("  dwInfoFlags：0x{0:X8}  {1}",
				d.DwInfoFlags, StrayIconParser.FormatNiifFlags (d.DwInfoFlags)));
			sb.AppendLine (string.Format ("  guidItem：{0}", d.GuidItem));
			sb.AppendLine (string.Format ("  szTip：{0}", Escape (d.SzTip)));
			sb.AppendLine (string.Format ("  szInfo：{0}", Escape (d.SzInfo)));
			sb.AppendLine (string.Format ("  szInfoTitle：{0}", Escape (d.SzInfoTitle)));
		}

		// ---------------- 完整字节集 ----------------

		private static void AppendHex (StringBuilder sb, byte [] data)
		{
			sb.AppendLine ();
			sb.AppendLine ("【原始字节集】");

			if (data == null || data.Length == 0)
			{
				sb.AppendLine ("  （空数据）");
				return;
			}

			for (int i = 0; i < data.Length; i += 16)
			{
				sb.Append ("  ");
				sb.Append (i.ToString ("X8")).Append ("  ");

				int n = Math.Min (16, data.Length - i);

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
		}

		// ---------------- 格式化辅助 ----------------

		private static string FormatTimestamp (long seconds)
		{
			try
			{
				DateTime epoch = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
				return epoch.AddSeconds (seconds).ToLocalTime ().ToString ("yyyy-MM-dd HH:mm:ss");
			}
			catch
			{
				return "(无效)";
			}
		}

		private static string FormatPointer (long value, int size)
		{
			if (size == 4) return "0x" + ((uint)value).ToString ("X8");
			if (size == 8) return "0x" + unchecked((ulong)value).ToString ("X16");
			return "0x" + value.ToString ("X");
		}

		private static string Escape (string s)
		{
			if (string.IsNullOrEmpty (s)) return string.Empty;
			return s.Replace ("\r", "\\r").Replace ("\n", "\\n").Replace ("\t", "\\t");
		}
	}
}