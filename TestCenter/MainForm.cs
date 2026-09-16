// ============================================================
// MainForm.cs
// 顶部：工具栏；左：捕获历史列表；右上：解析信息；右下：字节集；底部：状态栏
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TestCenter
{
	public class MainForm: Form
	{
		private readonly PipeListener _listener = new PipeListener ();

		private ToolStrip _toolStrip;
		private ToolStripButton _btnReportCurrent;
		private ToolStripButton _btnReportAll;
		private ToolStripButton _btnClear;

		private SplitContainer _mainSplit;
		private SplitContainer _detailSplit;
		private ListView _captureList;
		private ListView _infoList;
		private RichTextBox _hexView;
		private StatusStrip _statusStrip;
		private ToolStripStatusLabel _statusLabel;
		private ToolStripStatusLabel _counterLabel;

		private int _frameCount;
		private bool _suspendSelection;

		public MainForm ()
		{
			_listener.StatusChanged += OnListenerStatusChanged;
			_listener.DataReceived += OnListenerDataReceived;
			BuildUi ();
		}

		// ---------------- 界面构建 ----------------

		private void BuildUi ()
		{
			SuspendLayout ();

			Text = "TestCenter - SidebarNotifyIconPipe 数据监视器";
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size (1200, 720);
			MinimumSize = new Size (900, 500);

			// ---- 工具栏 ----
			_btnReportCurrent = new ToolStripButton ("生成当前帧报告...");
			_btnReportCurrent.DisplayStyle = ToolStripItemDisplayStyle.Text;
			_btnReportCurrent.Click += OnReportCurrentClick;

			_btnReportAll = new ToolStripButton ("生成全部帧报告...");
			_btnReportAll.DisplayStyle = ToolStripItemDisplayStyle.Text;
			_btnReportAll.Click += OnReportAllClick;

			_btnClear = new ToolStripButton ("清空历史");
			_btnClear.DisplayStyle = ToolStripItemDisplayStyle.Text;
			_btnClear.Click += OnClearClick;

			_toolStrip = new ToolStrip ();
			_toolStrip.GripStyle = ToolStripGripStyle.Hidden;
			_toolStrip.Items.Add (_btnReportCurrent);
			_toolStrip.Items.Add (_btnReportAll);
			_toolStrip.Items.Add (new ToolStripSeparator ());
			_toolStrip.Items.Add (_btnClear);

			// ---- 左：捕获历史列表 ----
			_captureList = new ListView ();
			_captureList.Dock = DockStyle.Fill;
			_captureList.View = View.Details;
			_captureList.FullRowSelect = true;
			_captureList.GridLines = true;
			_captureList.HideSelection = false;
			_captureList.MultiSelect = false;
			_captureList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
			_captureList.Columns.Add ("#", 44, HorizontalAlignment.Right);
			_captureList.Columns.Add ("时间", 96);
			_captureList.Columns.Add ("消息", 96);
			_captureList.Columns.Add ("大小", 64, HorizontalAlignment.Right);
			_captureList.SelectedIndexChanged += OnCaptureSelectionChanged;

			// ---- 右上：解析信息 ----
			_infoList = new ListView ();
			_infoList.Dock = DockStyle.Fill;
			_infoList.View = View.Details;
			_infoList.FullRowSelect = true;
			_infoList.GridLines = true;
			_infoList.HideSelection = false;
			_infoList.MultiSelect = false;
			_infoList.ShowGroups = true;
			_infoList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
			_infoList.Columns.Add ("项目", 200);
			_infoList.Columns.Add ("值", 560);

			// ---- 右下：字节集 ----
			_hexView = new RichTextBox ();
			_hexView.Dock = DockStyle.Fill;
			_hexView.ReadOnly = true;
			_hexView.WordWrap = false;
			_hexView.DetectUrls = false;
			_hexView.BackColor = Color.White;
			_hexView.ForeColor = Color.FromArgb (0x20, 0x20, 0x20);
			_hexView.Font = CreateMonoFont (9f);
			_hexView.ScrollBars = RichTextBoxScrollBars.Both;
			_hexView.Text = "（尚未接收到数据）";

			// ---- 右：上下分割 ----
			_detailSplit = new SplitContainer ();
			_detailSplit.Dock = DockStyle.Fill;
			_detailSplit.Orientation = Orientation.Horizontal;
			_detailSplit.SplitterWidth = 6;
			_detailSplit.Size = new Size (800, 600);
			_detailSplit.Panel1MinSize = 120;
			_detailSplit.Panel2MinSize = 120;
			_detailSplit.Panel1.Controls.Add (_infoList);
			_detailSplit.Panel2.Controls.Add (_hexView);

			// ---- 主：左右分割 ----
			_mainSplit = new SplitContainer ();
			_mainSplit.Dock = DockStyle.Fill;
			_mainSplit.Orientation = Orientation.Vertical;
			_mainSplit.SplitterWidth = 6;
			_mainSplit.Size = new Size (1200, 720);
			_mainSplit.Panel1MinSize = 220;
			_mainSplit.Panel2MinSize = 400;
			_mainSplit.Panel1.Padding = new Padding (4, 4, 0, 4);
			_mainSplit.Panel2.Padding = new Padding (0, 4, 4, 4);
			_mainSplit.Panel1.Controls.Add (_captureList);
			_mainSplit.Panel2.Controls.Add (_detailSplit);

			// ---- 状态栏 ----
			_statusLabel = new ToolStripStatusLabel ();
			_statusLabel.Text = "正在初始化...";
			_statusLabel.Spring = true;
			_statusLabel.TextAlign = ContentAlignment.MiddleLeft;

			_counterLabel = new ToolStripStatusLabel ();
			_counterLabel.Text = "帧数：0";

			_statusStrip = new StatusStrip ();
			_statusStrip.Items.Add (_statusLabel);
			_statusStrip.Items.Add (_counterLabel);

			Controls.Add (_mainSplit);
			Controls.Add (_toolStrip);
			Controls.Add (_statusStrip);

			ResumeLayout (true);
		}

		private static Font CreateMonoFont (float size)
		{
			try
			{
				Font f = new Font ("Consolas", size);
				if (string.Equals (f.Name, "Consolas", StringComparison.OrdinalIgnoreCase))
					return f;
				f.Dispose ();
			}
			catch { }

			try { return new Font (FontFamily.GenericMonospace, size); }
			catch { return new Font ("Courier New", size); }
		}

		// ---------------- 生命周期 ----------------

		protected override void OnLoad (EventArgs e)
		{
			base.OnLoad (e);

			try
			{
				int w = _mainSplit.Width;
				if (w > 0)
				{
					int d = 300;
					int max = w - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth;
					if (max < _mainSplit.Panel1MinSize) max = _mainSplit.Panel1MinSize;
					if (d > max) d = max;
					if (d < _mainSplit.Panel1MinSize) d = _mainSplit.Panel1MinSize;
					_mainSplit.SplitterDistance = d;
				}
			}
			catch (ArgumentOutOfRangeException) { }

			try
			{
				int h = _detailSplit.Height;
				if (h > 0)
				{
					int d = (int)(h * 0.55);
					int max = h - _detailSplit.Panel2MinSize - _detailSplit.SplitterWidth;
					if (d > max) d = max;
					if (d < _detailSplit.Panel1MinSize) d = _detailSplit.Panel1MinSize;
					_detailSplit.SplitterDistance = d;
				}
			}
			catch (ArgumentOutOfRangeException) { }
		}

		protected override void OnShown (EventArgs e)
		{
			base.OnShown (e);
			SetStatus ("正在启动管道监听...", StatusLevel.Info);
			_listener.Start ();
		}

		protected override void OnFormClosing (FormClosingEventArgs e)
		{
			_listener.Stop ();
			base.OnFormClosing (e);
		}

		// ---------------- 监听事件 ----------------

		private void OnListenerStatusChanged (object sender, PipeStatusEventArgs e)
		{
			if (IsDisposed || Disposing || !IsHandleCreated) return;
			try
			{
				if (InvokeRequired)
				{
					BeginInvoke (new EventHandler<PipeStatusEventArgs> (OnListenerStatusChanged), sender, e);
					return;
				}
				SetStatus (e.Message, e.Level);
			}
			catch (ObjectDisposedException) { }
			catch (InvalidOperationException) { }
		}

		private void OnListenerDataReceived (object sender, PipeDataEventArgs e)
		{
			if (IsDisposed || Disposing || !IsHandleCreated) return;
			try
			{
				if (InvokeRequired)
				{
					BeginInvoke (new EventHandler<PipeDataEventArgs> (OnListenerDataReceived), sender, e);
					return;
				}
				AppendFrame (e);
			}
			catch (ObjectDisposedException) { }
			catch (InvalidOperationException) { }
		}

		// ---------------- 捕获历史 ----------------

		private void AppendFrame (PipeDataEventArgs e)
		{
			_frameCount++;
			_counterLabel.Text = string.Format ("帧数：{0}    本次：{1} 字节", _frameCount, e.Data.Length);

			string msgName = e.Parsed != null
				? StrayIconParser.GetMessageName (e.Parsed.Message)
				: "解析失败";

			var item = new ListViewItem (_frameCount.ToString ());
			item.SubItems.Add (e.ReceivedAt.ToString ("HH:mm:ss.fff"));
			item.SubItems.Add (msgName);
			item.SubItems.Add (e.Data.Length.ToString ());
			item.Tag = e;

			_captureList.Items.Add (item);
			item.EnsureVisible ();

			_suspendSelection = true;
			try
			{
				_captureList.SelectedItems.Clear ();
				item.Selected = true;
				item.Focused = true;
			}
			finally
			{
				_suspendSelection = false;
			}

			ShowDetail (e);
		}

		private void OnCaptureSelectionChanged (object sender, EventArgs e)
		{
			if (_suspendSelection) return;
			if (_captureList.SelectedItems.Count == 0) return;

			var data = _captureList.SelectedItems [0].Tag as PipeDataEventArgs;
			if (data != null)
				ShowDetail (data);
		}

		// ---------------- 报告 ----------------

		private void OnReportCurrentClick (object sender, EventArgs e)
		{
			if (_captureList.SelectedItems.Count == 0)
			{
				MessageBox.Show (this, "请先在左侧列表中选择一条捕获记录。", "生成报告",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			var sel = _captureList.SelectedItems [0];
			var frame = sel.Tag as PipeDataEventArgs;
			if (frame == null) return;

			int idx;
			if (!int.TryParse (sel.Text, out idx) || idx <= 0) idx = 1;

			var list = new List<PipeDataEventArgs> { frame };

			SaveReport (list, idx,
				string.Format ("当前帧 (#{0})", idx));
		}

		private void OnReportAllClick (object sender, EventArgs e)
		{
			if (_captureList.Items.Count == 0)
			{
				MessageBox.Show (this, "尚未捕获任何数据。", "生成报告",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			var list = new List<PipeDataEventArgs> (_captureList.Items.Count);
			foreach (ListViewItem item in _captureList.Items)
			{
				var frame = item.Tag as PipeDataEventArgs;
				if (frame != null) list.Add (frame);
			}

			SaveReport (list, 1,
				string.Format ("全部 {0} 帧", list.Count));
		}

		private void SaveReport (IList<PipeDataEventArgs> frames, int startIndex, string scope)
		{
			string report;
			try
			{
				report = ReportBuilder.Build (
					frames,
					_listener.PipeName,
					string.Format ("SidebarNotifyIconPipe 通知报告  ({0})", scope),
					startIndex);
			}
			catch (Exception ex)
			{
				SetStatus ("生成报告失败：" + ex.Message, StatusLevel.Error);
				MessageBox.Show (this, "生成报告失败：\n" + ex.Message, "生成报告",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			using (var dlg = new SaveFileDialog ())
			{
				dlg.Title = "保存报告";
				dlg.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
				dlg.FileName = string.Format ("SidebarNotifyIconPipe_Report_{0:yyyyMMdd_HHmmss}.txt", DateTime.Now);
				dlg.DefaultExt = "txt";
				dlg.AddExtension = true;

				if (dlg.ShowDialog (this) != DialogResult.OK)
					return;

				try
				{
					File.WriteAllText (dlg.FileName, report, new UTF8Encoding (true));
					SetStatus (string.Format ("报告已保存：{0}", dlg.FileName), StatusLevel.Success);
				}
				catch (Exception ex)
				{
					SetStatus ("报告保存失败：" + ex.Message, StatusLevel.Error);
					MessageBox.Show (this, "保存报告失败：\n" + ex.Message, "生成报告",
						MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				var result = MessageBox.Show (this,
					"报告已保存：\n" + dlg.FileName + "\n\n是否立即打开？",
					"生成报告",
					MessageBoxButtons.YesNo, MessageBoxIcon.Information);

				if (result == DialogResult.Yes)
				{
					try
					{
						Process.Start (new ProcessStartInfo (dlg.FileName) { UseShellExecute = true });
					}
					catch (Exception ex)
					{
						MessageBox.Show (this, "打开文件失败：" + ex.Message, "生成报告",
							MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				}
			}
		}

		private void OnClearClick (object sender, EventArgs e)
		{
			if (_captureList.Items.Count == 0) return;

			var result = MessageBox.Show (this,
				"确定要清空全部捕获历史吗？", "清空历史",
				MessageBoxButtons.YesNo, MessageBoxIcon.Question);

			if (result != DialogResult.Yes) return;

			_captureList.Items.Clear ();
			_frameCount = 0;
			_counterLabel.Text = "帧数：0";

			_infoList.Items.Clear ();
			_infoList.Groups.Clear ();
			_hexView.Text = "（尚未接收到数据）";

			SetStatus ("已清空捕获历史", StatusLevel.Info);
		}

		// ---------------- 显示详情 ----------------

		private void SetStatus (string message, StatusLevel level)
		{
			if (IsDisposed || Disposing) return;

			_statusLabel.Text = message;

			switch (level)
			{
				case StatusLevel.Success:
					_statusLabel.ForeColor = Color.FromArgb (0x10, 0x7C, 0x10);
					break;
				case StatusLevel.Warning:
					_statusLabel.ForeColor = Color.FromArgb (0xB0, 0x60, 0x00);
					break;
				case StatusLevel.Error:
					_statusLabel.ForeColor = Color.FromArgb (0xC0, 0x00, 0x00);
					break;
				default:
					_statusLabel.ForeColor = SystemColors.ControlText;
					break;
			}
		}

		private void ShowDetail (PipeDataEventArgs e)
		{
			_infoList.BeginUpdate ();
			try
			{
				_infoList.Items.Clear ();
				_infoList.Groups.Clear ();

				ListViewGroup gRecv = AddGroup ("接收信息");
				AddItem (gRecv, "接收时间", e.ReceivedAt.ToString ("yyyy-MM-dd HH:mm:ss.fff"));
				AddItem (gRecv, "数据长度", string.Format ("{0} 字节", e.Data.Length));

				if (e.Parsed == null)
				{
					AddItem (gRecv, "解析结果", "失败");
					AddItem (gRecv, "错误信息",
						e.ParseError == null ? "未知错误" : e.ParseError.Message);
				}
				else
				{
					NotifyIconData d = e.Parsed;

					AddItem (gRecv, "时间戳", d.TimeStamp.ToString ());
					AddItem (gRecv, "本地时间", FormatTimestamp (d.TimeStamp));
					AddItem (gRecv, "dwSize",
						string.Format ("{0} 字节（实际收到 {1} 字节）", d.DwSize, e.Data.Length));
					AddItem (gRecv, "消息类型",
						string.Format ("0x{0:X8}  {1}", d.Message, StrayIconParser.GetMessageName (d.Message)));

					ListViewGroup gHdr = AddGroup ("头部字段");
					AddItem (gHdr, "bHWndSizeOf", d.HwndSizeOf.ToString ());
					AddItem (gHdr, "bUIntSizeOf", d.UIntSizeOf.ToString ());
					AddItem (gHdr, "bHIconSizeOf", d.HIconSizeOf.ToString ());
					AddItem (gHdr, "bNone", string.Format ("0x{0:X2}", d.None));

					ListViewGroup gData = AddGroup ("NOTIFYICONDATA");
					AddItem (gData, "hWnd", FormatPointer (d.HWnd, d.HwndSizeOf));
					AddItem (gData, "uID", d.UID.ToString ());
					AddItem (gData, "uFlags",
						string.Format ("0x{0:X8}  {1}", d.UFlags, StrayIconParser.FormatNifFlags (d.UFlags)));
					AddItem (gData, "uCallbackMessage", string.Format ("0x{0:X8}", d.UCallbackMessage));
					AddItem (gData, "hIcon", FormatPointer (d.HIcon, d.HIconSizeOf));
					AddItem (gData, "dwState", string.Format ("0x{0:X8}", d.DwState));
					AddItem (gData, "dwStateMask", string.Format ("0x{0:X8}", d.DwStateMask));
					AddItem (gData, "uTimeout", d.UTimeout.ToString ());
					AddItem (gData, "uVersion", d.UVersion.ToString ());
					AddItem (gData, "dwInfoFlags",
						string.Format ("0x{0:X8}  {1}", d.DwInfoFlags, StrayIconParser.FormatNiifFlags (d.DwInfoFlags)));
					AddItem (gData, "guidItem", d.GuidItem.ToString ());
					AddItem (gData, "szTip", Escape (d.SzTip));
					AddItem (gData, "szInfo", Escape (d.SzInfo));
					AddItem (gData, "szInfoTitle", Escape (d.SzInfoTitle));
				}
			}
			finally
			{
				_infoList.EndUpdate ();
			}

			_hexView.Text = StrayIconParser.FormatHexDump (e.Data);
			_hexView.Select (0, 0);
			_hexView.ScrollToCaret ();
		}

		// ---------------- ListView 辅助 ----------------

		private ListViewGroup AddGroup (string name)
		{
			var g = new ListViewGroup (name);
			_infoList.Groups.Add (g);
			return g;
		}

		private void AddItem (ListViewGroup group, string key, string value)
		{
			if (value == null) value = string.Empty;

			var item = new ListViewItem (key);
			item.SubItems.Add (value);
			item.Group = group;
			_infoList.Items.Add (item);
		}

		// ---------------- 格式化 ----------------

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