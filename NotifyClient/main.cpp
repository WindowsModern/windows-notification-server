// Injector.cpp : NotifyHook 注入器主程序
//
#define HMODULE_MODE_EXE

#include <Windows.h>
#include <iostream>
#include <string>
#include <vector>
#include <sstream>
#include <locale>
#include <cwctype>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "kernel32.lib")

// ============================================================
// 常量
// ============================================================
#define PIPE_NAME       L"\\\\.\\pipe\\NotifyHookInjectorPipe"
#define MUTEX_NAME      L"Local\\NotifyHookInjectorMutex"
#define PIPE_BUF_SIZE   4096

// ============================================================
// 导出函数类型
// ============================================================
typedef HRESULT (*PFN_NhInstallHook)   ();
typedef HRESULT (*PFN_NhUninstallHook) ();
typedef BOOL (*PFN_NhHookExists)    ();

// ============================================================
// 全局状态
// ============================================================
static HANDLE g_hMutex = NULL;
static HANDLE g_hStopEvent = NULL;
static HANDLE g_hServerThread = NULL;

static HMODULE g_hDll = NULL;
static PFN_NhInstallHook   g_pfnInstallHook = NULL;
static PFN_NhUninstallHook g_pfnUninstallHook = NULL;
static PFN_NhHookExists    g_pfnHookExists = NULL;

static bool g_quiet = false;
static bool g_hookState = false;
static int64_t g_hookRefCount = 0;

// ============================================================
// 输出辅助
// ============================================================
static std::wstring GetLocalTimeString ()
{
	SYSTEMTIME st;
	GetLocalTime (&st);
	WCHAR buf [64] = { 0 };
	swprintf_s (buf, _countof (buf),
		L"%04u-%02u-%02u %02u:%02u:%02u.%03u",
		st.wYear, st.wMonth, st.wDay,
		st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
	return buf;
}

static void Output (const std::wstring &msg)
{
	if (g_quiet) return;
	if (msg.empty ()) return;
	std::wcout << L"[" << GetLocalTimeString () << L"] " << msg << L"\r\n";
	std::wcout.flush ();
}

static void OutputError (const std::wstring &msg)
{
	if (g_quiet) return;
	if (msg.empty ()) return;
	std::wcerr << L"[" << GetLocalTimeString () << L"] ERROR: " << msg << L"\r\n";
	std::wcerr.flush ();
}

// ============================================================
// 清屏
// ============================================================
static void ClearConsole ()
{
	HANDLE hOut = GetStdHandle (STD_OUTPUT_HANDLE);
	if (hOut == INVALID_HANDLE_VALUE || hOut == NULL) return;
	CONSOLE_SCREEN_BUFFER_INFO csbi;
	if (!GetConsoleScreenBufferInfo (hOut, &csbi)) return;
	DWORD cellCount = csbi.dwSize.X * csbi.dwSize.Y;
	COORD home = { 0, 0 };
	DWORD written = 0;
	FillConsoleOutputCharacterW (hOut, L' ', cellCount, home, &written);
	FillConsoleOutputAttribute (hOut, csbi.wAttributes, cellCount, home, &written);
	SetConsoleCursorPosition (hOut, home);
}

// ============================================================
// 路径辅助
// ============================================================
static std::wstring GetExeDirectory ()
{
	WCHAR path [MAX_PATH] = { 0 };
	DWORD len = GetModuleFileNameW (NULL, path, MAX_PATH);
	if (len == 0) return L".";
	std::wstring s (path, len);
	size_t pos = s.find_last_of (L"\\/");
	return (pos == std::wstring::npos) ? s : s.substr (0, pos);
}

static std::wstring GetHookDllPath ()
{
	return GetExeDirectory () + L"\\NotifyHook.dll";
}

// ============================================================
// 十六进制格式化
// ============================================================
static std::wstring ToHex (HRESULT hr)
{
	WCHAR buf [16] = { 0 };
	swprintf_s (buf, L"%08X", (unsigned)hr);
	return buf;
}

// ============================================================
// DLL 加载
// ============================================================
static bool LoadHookDll ()
{
	if (g_hDll) return true;

	std::wstring dllPath = GetHookDllPath ();
	g_hDll = LoadLibraryW (dllPath.c_str ());
	if (!g_hDll)
	{
		OutputError (L"Failed to load NotifyHook.dll (err=" +
			std::to_wstring (GetLastError ()) + L", path=" + dllPath + L")");
		return false;
	}

	g_pfnInstallHook = (PFN_NhInstallHook)GetProcAddress (g_hDll, "NhInstallHook");
	g_pfnUninstallHook = (PFN_NhUninstallHook)GetProcAddress (g_hDll, "NhUninstallHook");
	g_pfnHookExists = (PFN_NhHookExists)GetProcAddress (g_hDll, "NhHookExists");

	if (!g_pfnInstallHook || !g_pfnUninstallHook || !g_pfnHookExists)
	{
		OutputError (L"Failed to resolve exports in NotifyHook.dll");
		FreeLibrary (g_hDll);
		g_hDll = NULL;
		return false;
	}
	return true;
}

// ============================================================
// 命令实现
// ============================================================
static std::wstring CommandRegister ()
{
	if (!LoadHookDll ()) return L"Failed to load NotifyHook.dll";

	if (g_pfnHookExists ())
		return L"Hook is already registered";

	HRESULT hr = g_pfnInstallHook ();
	if (SUCCEEDED (hr))
	{
		g_hookState = true;
		if (g_hookRefCount <= 0) g_hookRefCount = 1;
		return L"Hook registered successfully. HRESULT=0x" + ToHex (hr);
	}
	return L"Failed to register hook. HRESULT=0x" + ToHex (hr);
}

static std::wstring CommandUnregister ()
{
	if (!g_hDll)
		return L"DLL not loaded; hook is not registered";

	if (!g_pfnHookExists ())
		return L"Hook is not registered";

	HRESULT hr = g_pfnUninstallHook ();
	if (SUCCEEDED (hr))
	{
		g_hookState = false;
		g_hookRefCount = 0;
		return L"Hook unregistered successfully. HRESULT=0x" + ToHex (hr);
	}
	return L"Failed to unregister hook. HRESULT=0x" + ToHex (hr);
}
// 计次注册：+1；钩子未安装时才真正安装
static std::wstring CommandRegisterCounted ()
{
	if (!LoadHookDll ()) return L"Failed to load NotifyHook.dll";

	if (!g_pfnHookExists ())
	{
		HRESULT hr = g_pfnInstallHook ();
		if (FAILED (hr))
			return L"Failed to register hook. HRESULT=0x" + ToHex (hr);
		g_hookState = true;
	}

	++ g_hookRefCount;
	return L"Hook ref-count registered. RefCount=" + std::to_wstring (g_hookRefCount);
}

// 计次反注册：-1；计数降到 0 时才真正卸载
static std::wstring CommandUnregisterCounted ()
{
	if (!g_hDll)
		return L"DLL not loaded; hook is not registered";

	if (g_hookRefCount <= 0)
	{
		g_hookRefCount = 0;
		// 若钩子不知为何仍存在，一并清掉
		if (g_pfnHookExists && g_pfnHookExists ())
		{
			g_pfnUninstallHook ();
			g_hookState = false;
		}
		return L"Hook ref count is already 0; nothing to unregister";
	}

	-- g_hookRefCount;

	if (g_hookRefCount == 0)
	{
		if (g_pfnHookExists && g_pfnHookExists ())
		{
			HRESULT hr = g_pfnUninstallHook ();
			if (FAILED (hr))
			{
				++ g_hookRefCount;  // 回滚
				return L"Failed to unregister hook. HRESULT=0x" + ToHex (hr);
			}
		}
		g_hookState = false;
		return L"Hook ref count reached 0; hook unregistered";
	}

	return L"Hook ref-count unregistered. RefCount=" + std::to_wstring (g_hookRefCount);
}

static std::wstring CommandShow ()
{
	std::wstringstream ss;
	ss << L"Instance PID: " << GetCurrentProcessId () << L"\r\n";
	ss << L"DLL path: " << GetHookDllPath () << L"\r\n";
	if (g_hDll && g_pfnHookExists)
	{
		BOOL exists = g_pfnHookExists ();
		ss << L"Hook state: " << (exists ? L"REGISTERED" : L"NOT REGISTERED") << L"\r\n";
		ss << L"Hook ref count: " << g_hookRefCount;
	}
	else
	{
		ss << L"Hook state: DLL NOT LOADED" << L"\r\n";
		ss << L"Hook ref count: " << g_hookRefCount;
	}
	return ss.str ();
}

static std::wstring CommandHelp ()
{
	return
		L"Available commands (prefix '/' or '-', case-insensitive):\r\n"
		L"  register        - Register the Shell_NotifyIcon hook (non-counted)\r\n"
		L"  unregister      - Unregister the hook (force uninstall, reset ref count)\r\n"
		L"  register-count  - Register the hook with reference counting (+1)\r\n"
		L"  unregister-count- Unregister the hook with reference counting (-1)\r\n"
		L"  show            - Show current hook state and ref count\r\n"
		L"  help            - Show this help\r\n"
		L"  clear           - Clear the console screen\r\n"
		L"  quiet           - Disable output (differentiated)\r\n"
		L"  verbose         - Enable output (default, differentiated)\r\n"
		L"  exit            - Shut down the primary instance (aliases: quit, shutdown)\r\n"
		L"\r\n"
		L"No argument:  Become primary instance if none exists, otherwise query state.";
}

// clear：清屏后输出一句
static std::wstring CommandClear ()
{
	ClearConsole ();
	Output (L"Console cleared.");
	return L"";
}

// quiet：差异化，已经是 quiet 则什么都不做
static std::wstring CommandQuiet ()
{
	if (g_quiet) return L"";
	Output (L"Output disabled (quiet mode).");
	g_quiet = true;
	return L"";
}

// verbose：差异化，已经是 verbose 则什么都不做
static std::wstring CommandVerbose ()
{
	if (!g_quiet) return L"";
	g_quiet = false;
	Output (L"Output enabled (verbose mode).");
	return L"";
}

// exit：请求主实例退出
static std::wstring CommandExit ()
{
	if (g_hStopEvent)
		SetEvent (g_hStopEvent);
	return L"Injector shutdown requested. Goodbye!";
}

// ============================================================
// 命令归一化与分发
// ============================================================
static std::wstring NormalizeCommand (const std::wstring &arg)
{
	std::wstring s = arg;
	if (!s.empty () && (s [0] == L'/' || s [0] == L'-'))
		s = s.substr (1);
	for (auto &c : s) c = (wchar_t)towlower (c);
	while (!s.empty () && (s.back () == L' ' || s.back () == L'\t' ||
		s.back () == L'\r' || s.back () == L'\n'))
		s.pop_back ();
	return s;
}

static std::wstring ProcessCommand (const std::wstring &rawCmd)
{
	std::wstring cmd = NormalizeCommand (rawCmd);
	if (cmd == L"register" || cmd == L"reg")                     return CommandRegister ();
	if (cmd == L"unregister" || cmd == L"unreg")                   return CommandUnregister ();
	if (cmd == L"register-count"
		|| cmd == L"regcount"
		|| cmd == L"rreg"
		|| cmd == L"register-ref")                 return CommandRegisterCounted ();
	if (cmd == L"unregister-count"
		|| cmd == L"unregcount"
		|| cmd == L"runreg"
		|| cmd == L"rureg"
		|| cmd == L"unregister-ref")               return CommandUnregisterCounted ();
	if (cmd == L"show")                         return CommandShow ();
	if (cmd == L"help")                         return CommandHelp ();
	if (cmd == L"clear")                        return CommandClear ();
	if (cmd == L"quiet")                        return CommandQuiet ();
	if (cmd == L"verbose")                      return CommandVerbose ();
	if (cmd == L"exit" || cmd == L"quit" || cmd == L"shutdown")
		return CommandExit ();
	return L"Unknown command: " + rawCmd + L"\r\n" + CommandHelp ();
}

static bool IsExitCommand (const std::wstring &rawCmd)
{
	std::wstring cmd = NormalizeCommand (rawCmd);
	return cmd == L"exit" || cmd == L"quit" || cmd == L"shutdown";
}

// ============================================================
// 管道 IPC：客户端
// ============================================================
static bool SendCommandToInstance (const std::wstring &cmd, std::wstring &outReply)
{
	HANDLE hPipe = INVALID_HANDLE_VALUE;
	for (int i = 0; i < 5; ++ i)
	{
		hPipe = CreateFileW (PIPE_NAME, GENERIC_READ | GENERIC_WRITE,
			0, NULL, OPEN_EXISTING, 0, NULL);
		if (hPipe != INVALID_HANDLE_VALUE) break;

		DWORD err = GetLastError ();
		if (err == ERROR_PIPE_BUSY)
		{
			if (!WaitNamedPipeW (PIPE_NAME, 1000))
				return false;
		}
		else
		{
			return false;
		}
	}
	if (hPipe == INVALID_HANDLE_VALUE) return false;

	DWORD mode = PIPE_READMODE_MESSAGE;
	SetNamedPipeHandleState (hPipe, &mode, NULL, NULL);

	DWORD written = 0;
	if (!WriteFile (hPipe, cmd.c_str (),
		(DWORD)(cmd.length () * sizeof (WCHAR)), &written, NULL))
	{
		CloseHandle (hPipe);
		return false;
	}

	WCHAR buf [PIPE_BUF_SIZE / sizeof (WCHAR)] = { 0 };
	DWORD read = 0;
	if (!ReadFile (hPipe, buf, sizeof (buf) - sizeof (WCHAR), &read, NULL))
	{
		CloseHandle (hPipe);
		return false;
	}
	buf [read / sizeof (WCHAR)] = L'\0';
	outReply = buf;

	CloseHandle (hPipe);
	return true;
}

// ============================================================
// 管道 IPC：服务器线程
// ============================================================
static DWORD WINAPI PipeServerThread (LPVOID)
{
	while (WaitForSingleObject (g_hStopEvent, 0) != WAIT_OBJECT_0)
	{
		HANDLE hPipe = CreateNamedPipeW (
			PIPE_NAME,
			PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
			PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
			1, PIPE_BUF_SIZE, PIPE_BUF_SIZE, 0, NULL);

		if (hPipe == INVALID_HANDLE_VALUE)
		{
			Sleep (500);
			continue;
		}

		OVERLAPPED ov = { 0 };
		ov.hEvent = CreateEventW (NULL, TRUE, FALSE, NULL);

		BOOL ok = ConnectNamedPipe (hPipe, &ov);
		DWORD err = GetLastError ();
		if (!ok && err == ERROR_IO_PENDING)
		{
			HANDLE handles [2] = { ov.hEvent, g_hStopEvent };
			DWORD w = WaitForMultipleObjects (2, handles, FALSE, INFINITE);
			if (w == WAIT_OBJECT_0 + 1)
			{
				CancelIoEx (hPipe, &ov);
				CloseHandle (ov.hEvent);
				CloseHandle (hPipe);
				break;
			}
		}
		else if (!ok && err != ERROR_PIPE_CONNECTED)
		{
			CloseHandle (ov.hEvent);
			CloseHandle (hPipe);
			continue;
		}
		CloseHandle (ov.hEvent);

		WCHAR buf [PIPE_BUF_SIZE / sizeof (WCHAR)] = { 0 };
		DWORD read = 0;
		if (ReadFile (hPipe, buf, sizeof (buf) - sizeof (WCHAR), &read, NULL) && read > 0)
		{
			buf [read / sizeof (WCHAR)] = L'\0';
			std::wstring rawCmd = buf;

			std::wstring reply = ProcessCommand (rawCmd);
			if (!reply.empty ())
				Output (reply);

			// 先把回复发给对方，再决定是否退出，避免丢回复
			DWORD written = 0;
			WriteFile (hPipe, reply.c_str (),
				(DWORD)(reply.length () * sizeof (WCHAR)), &written, NULL);
			FlushFileBuffers (hPipe);

			if (IsExitCommand (rawCmd))
			{
				// 回复已送达，主线程会在 WaitForSingleObject 醒来并清理
				SetEvent (g_hStopEvent);
			}
		}

		DisconnectNamedPipe (hPipe);
		CloseHandle (hPipe);
	}
	return 0;
}

// ============================================================
// Ctrl+C 处理器
// ============================================================
static BOOL WINAPI ConsoleCtrlHandler (DWORD ctrlType)
{
	switch (ctrlType)
	{
		case CTRL_C_EVENT:
		case CTRL_BREAK_EVENT:
		case CTRL_CLOSE_EVENT:
		case CTRL_LOGOFF_EVENT:
		case CTRL_SHUTDOWN_EVENT:
			if (g_hStopEvent) SetEvent (g_hStopEvent);
			return TRUE;
	}
	return FALSE;
}

// ============================================================
// 主实例
// ============================================================
static int RunAsPrimary (const std::vector<std::wstring> &commands)
{
	Output (L"NotifyHook injector started as PRIMARY instance (PID=" +
		std::to_wstring (GetCurrentProcessId ()) + L")");

	// 加载 DLL 并检查当前状态
	LoadHookDll ();
	if (g_hDll && g_pfnHookExists && g_pfnHookExists ())
	{
		g_hookState = true;
		Output (L"Hook is currently REGISTERED");
	}
	else
	{
		Output (L"Hook is currently NOT REGISTERED");
	}

	// 启动管道服务器线程
	g_hStopEvent = CreateEventW (NULL, TRUE, FALSE, NULL);
	g_hServerThread = CreateThread (NULL, 0, PipeServerThread, NULL, 0, NULL);
	if (!g_hServerThread)
	{
		OutputError (L"Failed to start pipe server thread");
		return 1;
	}

	SetConsoleCtrlHandler (ConsoleCtrlHandler, TRUE);

	// 唯一实例启动时有参数，直接执行这些命令
	bool exitRequested = false;
	for (const auto &c : commands)
	{
		std::wstring reply = ProcessCommand (c);
		if (!reply.empty ())
			Output (reply);
		if (IsExitCommand (c))
		{
			exitRequested = true;
			break;
		}
	}

	if (!exitRequested)
		Output (L"Waiting for commands from other instances... Press Ctrl+C to exit.");

	// 阻塞等待停止事件
	WaitForSingleObject (g_hStopEvent, INFINITE);

	Output (L"Shutting down...");

	// 退出时取消注册
	if (g_hookState && g_pfnUninstallHook)
	{
		Output (L"Unregistering hook on exit...");
		HRESULT hr = g_pfnUninstallHook ();
		if (SUCCEEDED (hr))
			Output (L"Hook unregistered on exit. HRESULT=0x" + ToHex (hr));
		else
			Output (L"Failed to unregister hook on exit. HRESULT=0x" + ToHex (hr));
		g_hookState = false;
	}
	g_hookRefCount = 0;

	// 收尾
	SetEvent (g_hStopEvent);
	if (g_hServerThread)
	{
		WaitForSingleObject (g_hServerThread, 3000);
		CloseHandle (g_hServerThread);
		g_hServerThread = NULL;
	}
	if (g_hStopEvent) { CloseHandle (g_hStopEvent); g_hStopEvent = NULL; }
	if (g_hDll) { FreeLibrary (g_hDll);       g_hDll = NULL; }

	Output (L"Injector terminated.");
	return 0;
}

// ============================================================
// 辅助实例
// ============================================================
static int RunAsSecondary (const std::vector<std::wstring> &commands)
{
	Output (L"Another instance is already running. Forwarding request...");

	if (commands.empty ())
	{
		std::wstring reply;
		if (SendCommandToInstance (L"show", reply))
		{
			Output (L"--- Remote instance state ---");
			Output (reply);
			Output (L"-----------------------------");
		}
		else
		{
			OutputError (L"Failed to contact the running instance.");
		}
		return 0;
	}

	for (const auto &c : commands)
	{
		std::wstring reply;
		if (SendCommandToInstance (c, reply))
		{
			if (!reply.empty ())
				Output (reply);
		}
		else
		{
			OutputError (L"Failed to forward command: " + c);
		}
	}
	return 0;
}

// ============================================================
// 入口
// ============================================================
int wmain (int argc, wchar_t **argv)
{
	setlocale (LC_ALL, "");
	std::wcout.imbue (std::locale ("", LC_CTYPE));

	std::vector<std::wstring> commands;

	for (int i = 1; i < argc; ++ i)
	{
		std::wstring arg = argv [i];
		if (arg.empty ()) continue;
		if (arg [0] != L'/' && arg [0] != L'-') continue;

		std::wstring norm = NormalizeCommand (arg);
		if (!norm.empty ())
			commands.push_back (norm);
	}

	g_hMutex = CreateMutexW (NULL, FALSE, MUTEX_NAME);
	if (!g_hMutex)
	{
		OutputError (L"Failed to create mutex. err=" + std::to_wstring (GetLastError ()));
		return 1;
	}
	bool isFirst = (GetLastError () != ERROR_ALREADY_EXISTS);

	int ret;
	if (isFirst)
		ret = RunAsPrimary (commands);
	else
		ret = RunAsSecondary (commands);

	if (g_hMutex) { CloseHandle (g_hMutex); g_hMutex = NULL; }
	return ret;
}