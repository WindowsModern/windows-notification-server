// ============================================================
// PipeListener.cs
// 后台线程里的命名管道服务端循环（逻辑与 PipeTest 一致）
// ============================================================
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace TestCenter
{
	public enum StatusLevel
	{
		Info,
		Success,
		Warning,
		Error
	}

	public class PipeStatusEventArgs: EventArgs
	{
		public string Message { get; private set; }
		public StatusLevel Level { get; private set; }

		public PipeStatusEventArgs (string message, StatusLevel level)
		{
			Message = message;
			Level = level;
		}
	}

	// PipeListener.cs —— 只需替换 PipeDataEventArgs 类
	public class PipeDataEventArgs: EventArgs
	{
		public byte [] Data { get; private set; }
		public NotifyIconData Parsed { get; private set; }
		public Exception ParseError { get; private set; }
		public DateTime ReceivedAt { get; private set; }

		public PipeDataEventArgs (byte [] data, NotifyIconData parsed, Exception parseError)
		{
			Data = data;
			Parsed = parsed;
			ParseError = parseError;
			ReceivedAt = DateTime.Now;
		}
	}

	public class PipeListener
	{
		public const string DefaultPipeName = "SidebarNotifyIconPipe";

		private readonly string _pipeName;
		private Thread _worker;
		private volatile bool _running;

		public event EventHandler<PipeStatusEventArgs> StatusChanged;
		public event EventHandler<PipeDataEventArgs> DataReceived;

		public PipeListener () : this (DefaultPipeName) { }

		public PipeListener (string pipeName)
		{
			if (string.IsNullOrEmpty (pipeName))
				throw new ArgumentNullException ("pipeName");
			_pipeName = pipeName;
		}

		public string PipeName { get { return _pipeName; } }
		public bool IsRunning { get { return _running; } }

		public void Start ()
		{
			if (_running) return;
			_running = true;

			_worker = new Thread (WorkerLoop);
			_worker.IsBackground = true;   // 后台线程：进程退出时自动结束
			_worker.Name = "PipeListener";
			_worker.Start ();
		}

		public void Stop ()
		{
			_running = false;
		}

		// ---------------- 主循环 ----------------

		private void WorkerLoop ()
		{
			while (_running)
			{
				NamedPipeServerStream server = null;
				try
				{
					server = CreateServer ();

					RaiseStatus (string.Format ("等待连接...（管道：{0}）", _pipeName), StatusLevel.Info);
					server.WaitForConnection ();

					RaiseStatus ("已连接，正在读取数据...", StatusLevel.Success);

					byte [] data = ReadFrame (server);

					if (data.Length < StrayIconParser.HeaderSize)
					{
						RaiseStatus (string.Format ("数据过短（{0} 字节），已忽略", data.Length), StatusLevel.Warning);
						continue;
					}

					RaiseStatus (string.Format ("已捕获一帧：{0} 字节", data.Length), StatusLevel.Success);

					NotifyIconData parsed = null;
					Exception parseError = null;
					try
					{
						parsed = StrayIconParser.Parse (data);
					}
					catch (Exception ex)
					{
						parseError = ex;
						RaiseStatus (string.Format ("解析异常：{0}: {1}", ex.GetType ().Name, ex.Message), StatusLevel.Error);
					}

					RaiseData (data, parsed, parseError);
				}
				catch (Exception ex)
				{
					if (_running)
						RaiseStatus (string.Format ("异常：{0}: {1}", ex.GetType ().Name, ex.Message), StatusLevel.Error);
				}
				finally
				{
					if (server != null)
					{
						try { server.Dispose (); } catch { }
					}
				}

				// 短暂延迟，避免管道名尚未完全释放时立刻重建失败
				Thread.Sleep (50);
			}

			RaiseStatus ("管道监听已停止", StatusLevel.Info);
		}

		/// <summary>
		/// 创建管道服务器，若失败则重试若干次。
		/// </summary>
		private NamedPipeServerStream CreateServer ()
		{
			const int maxRetry = 10;
			IOException lastEx = null;

			for (int i = 0; i < maxRetry; i++)
			{
				try
				{
					return new NamedPipeServerStream (
						_pipeName,
						PipeDirection.In,
						1,
						PipeTransmissionMode.Byte,
						PipeOptions.None);
				}
				catch (IOException ex)
				{
					lastEx = ex;
					RaiseStatus (string.Format ("创建管道失败，重试 {0}/{1}：{2}", i + 1, maxRetry, ex.Message), StatusLevel.Warning);
					Thread.Sleep (200);
				}
			}

			throw new IOException (string.Format ("重试 {0} 次后仍无法创建管道服务器", maxRetry), lastEx);
		}

		/// <summary>
		/// 按 dwSize 读取完整的一帧数据。
		/// </summary>
		private static byte [] ReadFrame (NamedPipeServerStream pipe)
		{
			byte [] header = new byte [StrayIconParser.HeaderSize];

			int read = 0;
			while (read < header.Length)
			{
				int r = pipe.Read (header, read, header.Length - read);
				if (r == 0) throw new EndOfStreamException ("管道在读取头部时关闭");
				read += r;
			}

			int dwSize = BitConverter.ToInt32 (header, 8);
			if (dwSize < StrayIconParser.HeaderSize)
				throw new InvalidDataException (string.Format ("无效的 dwSize: {0}", dwSize));
			if (dwSize > StrayIconParser.MaxReasonableSize)
				throw new InvalidDataException (string.Format ("dwSize 过大，可能不是有效数据: {0}", dwSize));

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

		// ---------------- 事件 ----------------

		private void RaiseStatus (string message, StatusLevel level)
		{
			var handler = StatusChanged;
			if (handler != null) handler (this, new PipeStatusEventArgs (message, level));
		}

		private void RaiseData (byte [] data, NotifyIconData parsed, Exception error)
		{
			var handler = DataReceived;
			if (handler != null) handler (this, new PipeDataEventArgs (data, parsed, error));
		}
	}
}