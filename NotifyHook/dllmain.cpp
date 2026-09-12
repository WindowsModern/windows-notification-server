// dllmain.cpp : 定义 DLL 应用程序的入口点。
#include "stdafx.h"
#include "NotifyHook.h"

#define HMODULE_MODE_DLL2

#include "module.h"
#include "encoding.h"
#include "clrstr.h"
#include "utilities.h"
#include "pipe.h"
#include "shlapin.h"
#include "debug.h"
#include "debugni.h"

#define UNREG_SUBCLASSMSGSTR L"SidebarNotificationProcessSubClass"

#pragma data_seg(".SIDEBARNOTIFYHOOKSHARE")
HHOOK g_hHook = nullptr;
HWND g_hWndTray = nullptr;
bool g_bHasSubclass = false;
UINT g_uUnregSubclassMsg = 0;
#pragma data_seg()
#pragma comment(linker, "/section:.SIDEBARNOTIFYHOOKSHARE,rws")

// 从原始字节构造 STRAYICONDATA
// pNidRaw 指向 WM_COPYDATA 中 base + 0x08 处（NOTIFYICONDATA 起始）
// cbSize 为 pNidRaw 首 4 字节读出的结构体大小
PSTRAYICONDATA BuildCommonNotifyData (DWORD dwMessage, const BYTE *pNidRaw, DWORD cbSize)
{
	if (!pNidRaw) return nullptr;
	if (cbSize < 4 || cbSize > 4096) return nullptr;

	const bool isW = IsNotifyIconDataW (cbSize);
	const bool is64 = IsNotifyIconData64 (cbSize);
	const bool isV1 = IsNotifyIconDataV1 (cbSize);
	const size_t charSize = isW ? sizeof (WCHAR) : sizeof (CHAR);

	// ---- 头字段偏移（只取决于 32/64 位，与 A/W 无关）----
	const size_t hWndOff = is64 ? 8 : 4;
	const size_t uIDOff = is64 ? 16 : 8;
	const size_t uFlagsOff = is64 ? 20 : 12;
	const size_t uCallbackOff = is64 ? 24 : 16;
	const size_t hIconOff = is64 ? 32 : 20;
	const size_t szTipOff = is64 ? 40 : 24;

	// V1 的 szTip 为 64 字符，V2+ 为 128 字符
	const size_t szTipChars = isV1 ? 64 : 128;
	const size_t szTipBytes = szTipChars * charSize;
	const size_t szTipEnd = szTipOff + szTipBytes;

	// ---- 读取头字段 ----
	HWND  hWnd = *(const HWND  *)(pNidRaw + hWndOff);
	UINT  uID = *(const UINT  *)(pNidRaw + uIDOff);
	UINT  uFlags = *(const UINT  *)(pNidRaw + uFlagsOff);
	UINT  uCallback = *(const UINT  *)(pNidRaw + uCallbackOff);
	HICON hIcon = *(const HICON *)(pNidRaw + hIconOff);

	// ---- 通用字符串读取（按 A/W 自适应）----
	auto readStr = [&] (size_t off, size_t maxChars) -> std::wstring {
		std::wstring out;
		if (off + maxChars * charSize > cbSize && off >= cbSize) return out;

		if (isW)
		{
			const WCHAR *p = (const WCHAR *)(pNidRaw + off);
			size_t len = 0;
			while (len < maxChars && p [len] != L'\0') ++ len;
			out.assign (p, len);
		}
		else
		{
			const CHAR *p = (const CHAR *)(pNidRaw + off);
			size_t len = 0;
			while (len < maxChars && p [len] != '\0') ++ len;
			if (len > 0)
			{
				int wlen = MultiByteToWideChar (CP_ACP, 0, p, (int)len, nullptr, 0);
				if (wlen > 0)
				{
					out.resize (wlen);
					MultiByteToWideChar (CP_ACP, 0, p, (int)len, &out [0], wlen);
				}
			}
		}
		return out;
	};

	std::wstring tip = readStr (szTipOff, szTipChars);

	// ---- V2+ 才有 dwState / szInfo / szInfoTitle / guidItem 等 ----
	DWORD dwState = 0, dwStateMask = 0;
	UINT  uTimeout = 0;
	DWORD dwInfoFlags = 0;
	GUID  guidItem = { 0 };
	std::wstring info, infoTitle;

	if (!isV1)
	{
		const size_t dwStateOff = szTipEnd;
		const size_t dwStateMaskOff = dwStateOff + 4;
		const size_t szInfoOff = dwStateMaskOff + 4;
		const size_t szInfoChars = 256;
		const size_t szInfoBytes = szInfoChars * charSize;
		const size_t uTimeoutOff = szInfoOff + szInfoBytes;
		const size_t szInfoTitleOff = uTimeoutOff + 4;
		const size_t szInfoTitleChars = 64;
		const size_t szInfoTitleBytes = szInfoTitleChars * charSize;
		const size_t dwInfoFlagsOff = szInfoTitleOff + szInfoTitleBytes;
		const size_t guidItemOff = dwInfoFlagsOff + 4;

		// 逐字段越界检查，防止畸形 cbSize 造成越界读取
		if (dwStateOff + 4 <= cbSize) dwState = *(const DWORD *)(pNidRaw + dwStateOff);
		if (dwStateMaskOff + 4 <= cbSize) dwStateMask = *(const DWORD *)(pNidRaw + dwStateMaskOff);
		if (uTimeoutOff + 4 <= cbSize) uTimeout = *(const UINT  *)(pNidRaw + uTimeoutOff);
		if (dwInfoFlagsOff + 4 <= cbSize) dwInfoFlags = *(const DWORD *)(pNidRaw + dwInfoFlagsOff);
		if (guidItemOff + sizeof (GUID) <= cbSize)
			memcpy (&guidItem, pNidRaw + guidItemOff, sizeof (GUID));

		if (szInfoOff + szInfoBytes <= cbSize)        info = readStr (szInfoOff, szInfoChars);
		if (szInfoTitleOff + szInfoTitleBytes <= cbSize) infoTitle = readStr (szInfoTitleOff, szInfoTitleChars);
	}

	// ---- 构造 STRAYICONDATA（与原实现一致）----
	size_t totallen = offsetof (STRAYICONDATA, bBuffer) +
		sizeof (HWND) + sizeof (UINT) * 3 +
		sizeof (HICON) + sizeof (DWORD) * 2 +
		sizeof (UINT) * 2 + sizeof (DWORD) +
		sizeof (GUID);
	totallen += (tip.length () + info.length () + infoTitle.length () + 3) * sizeof (WCHAR);

	auto ptid = (PSTRAYICONDATA)malloc (totallen);
	if (!ptid) return nullptr;
	ZeroMemory (ptid, totallen);

	ptid->dwSize = (DWORD)totallen;
	ptid->llTimeStampUtc = GetUtcTimestampSec ();
	ptid->bHWndSizeOf = sizeof (HWND);
	ptid->bUIntSizeOf = sizeof (UINT);
	ptid->bHIconSizeOf = sizeof (HICON);
	ptid->bNone = 'T';
	ptid->dwMessage = dwMessage;

	auto headptr = (BYTE *)&ptid->bBuffer;
	*(HWND *)headptr = hWnd;             headptr += sizeof (HWND);
	((UINT *)headptr) [0] = uID;
	((UINT *)headptr) [1] = uFlags;
	((UINT *)headptr) [2] = uCallback;   headptr += sizeof (UINT) * 3;
	*(HICON *)headptr = hIcon;           headptr += sizeof (HICON);
	((DWORD *)headptr) [0] = dwState;
	((DWORD *)headptr) [1] = dwStateMask; headptr += sizeof (DWORD) * 2;
	((UINT *)headptr) [0] = uTimeout;
	((UINT *)headptr) [1] = 0;           headptr += sizeof (UINT) * 2; // uVersion
	*(DWORD *)headptr = dwInfoFlags;     headptr += sizeof (DWORD);
	*(GUID *)headptr = guidItem;         headptr += sizeof (GUID);
	auto wstrheader = (WCHAR *)headptr;
	wcscpy (wstrheader, tip.c_str ());       wstrheader += tip.length () + 1;
	wcscpy (wstrheader, info.c_str ());      wstrheader += info.length () + 1;
	wcscpy (wstrheader, infoTitle.c_str ());
	return ptid;
}
void DestroyCommonNotifyData (PSTRAYICONDATA lpData) { if (lpData) free (lpData); }
void DebugOutputNotifyDataInfo (PSTRAYICONDATA lpData)
{
	if (!lpData)
	{
		DebugOutputString (L"[NotifyData] <null>");
		return;
	}

	// 基本大小校验，防止越界
	if (lpData->dwSize < offsetof (STRAYICONDATA, bBuffer) + 4)
	{
		DebugOutputString (L"[NotifyData] <invalid size>");
		return;
	}

	const BYTE *p = (const BYTE *)&lpData->bBuffer;
	const BYTE *pEnd = (const BYTE *)lpData + lpData->dwSize;

	// 辅助：确保剩余字节足够
	auto ensure = [&] (size_t need) -> bool {
		return (size_t)(pEnd - p) >= need;
	};

	// ---- hWnd ----
	if (!ensure (lpData->bHWndSizeOf)) { DebugOutputString (L"[NotifyData] <truncated hWnd>"); return; }
	HWND hWnd = *(const HWND *)p;
	p += lpData->bHWndSizeOf;

	// ---- uID / uFlags / uCallbackMessage ----
	if (!ensure (lpData->bUIntSizeOf * 3)) { DebugOutputString (L"[NotifyData] <truncated UINTs>"); return; }
	UINT uID = ((const UINT *)p) [0];
	UINT uFlags = ((const UINT *)p) [1];
	UINT uCallbackMessage = ((const UINT *)p) [2];
	p += lpData->bUIntSizeOf * 3;

	// ---- hIcon ----
	if (!ensure (lpData->bHIconSizeOf)) { DebugOutputString (L"[NotifyData] <truncated hIcon>"); return; }
	HICON hIcon = *(const HICON *)p;
	p += lpData->bHIconSizeOf;

	// ---- dwState / dwStateMask ----
	if (!ensure (sizeof (DWORD) * 2)) { DebugOutputString (L"[NotifyData] <truncated DWORDs>"); return; }
	DWORD dwState = ((const DWORD *)p) [0];
	DWORD dwStateMask = ((const DWORD *)p) [1];
	p += sizeof (DWORD) * 2;

	// ---- uTimeout / uVersion ----
	if (!ensure (lpData->bUIntSizeOf * 2)) { DebugOutputString (L"[NotifyData] <truncated timeout>"); return; }
	UINT uTimeout = ((const UINT *)p) [0];
	UINT uVersion = ((const UINT *)p) [1];
	p += lpData->bUIntSizeOf * 2;

	// ---- dwInfoFlags ----
	if (!ensure (sizeof (DWORD))) { DebugOutputString (L"[NotifyData] <truncated infoFlags>"); return; }
	DWORD dwInfoFlags = *(const DWORD *)p;
	p += sizeof (DWORD);

	// ---- guidItem ----
	if (!ensure (sizeof (GUID))) { DebugOutputString (L"[NotifyData] <truncated guid>"); return; }
	GUID guidItem = *(const GUID *)p;
	p += sizeof (GUID);

	// ---- 字符串区域 ----
	// 安全读取以 null 结尾的宽字符串，并返回读取后的指针
	auto readWStr = [&] (LPCWSTR &outStr) -> bool {
		if ((size_t)(pEnd - p) < sizeof (WCHAR)) return false;
		const WCHAR *start = (const WCHAR *)p;
		const WCHAR *end = (const WCHAR *)pEnd;
		const WCHAR *q = start;
		while (q < end && *q != L'\0') ++ q;
		if (q >= end) return false; // 未找到结尾
		outStr = start;
		p = (const BYTE *)(q + 1);
		return true;
	};

	LPCWSTR szTip = L"";
	LPCWSTR szInfo = L"";
	LPCWSTR szInfoTitle = L"";
	if (!readWStr (szTip)) { DebugOutputString (L"[NotifyData] <truncated tip>"); return; }
	if (!readWStr (szInfo)) { DebugOutputString (L"[NotifyData] <truncated info>"); return; }
	if (!readWStr (szInfoTitle)) { DebugOutputString (L"[NotifyData] <truncated infoTitle>"); return; }

	// ---- 输出 ----
	WCHAR buf [2048] = { 0 };
	swprintf_s (buf, _countof (buf),
		L"[NotifyData] "
		L"size=%u ts=%lld msg=0x%08X "
		L"hwnd=0x%p uid=%u flags=0x%08X(%s) cbMsg=0x%08X "
		L"hIcon=0x%p state=0x%08X stateMask=0x%08X "
		L"timeout=%u version=%u infoFlags=0x%08X(%s) "
		L"guid={%08X-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X} "
		L"tip={%s} info={%s} infoTitle={%s}",
		lpData->dwSize, lpData->llTimeStampUtc, lpData->dwMessage,
		(void *)hWnd, uID, uFlags, DbgNifName (uFlags), uCallbackMessage,
		(void *)hIcon, dwState, dwStateMask,
		uTimeout, uVersion, dwInfoFlags, DbgNiifName (dwInfoFlags),
		guidItem.Data1, guidItem.Data2, guidItem.Data3,
		guidItem.Data4 [0], guidItem.Data4 [1], guidItem.Data4 [2], guidItem.Data4 [3],
		guidItem.Data4 [4], guidItem.Data4 [5], guidItem.Data4 [6], guidItem.Data4 [7],
		DbgSafeStr (szTip).c_str (), DbgSafeStr (szInfo).c_str (), DbgSafeStr (szInfoTitle).c_str ());
	DebugOutputString (buf);
}
void DebugOutputBytes (const void *data, size_t size)
{
	if (!data && size != 0)
	{
		DebugOutputString (L"(null)");
		return;
	}
	if (size == 0)
	{
		DebugOutputString (L"{}");
		return;
	}
	const BYTE *p = (const BYTE *)data;
	const LPCWSTR HEX = L"0123456789ABCDEF";
	const size_t BATCH = 512;
	std::wstring line;
	line.reserve (BATCH * 3 + 2);
	for (size_t i = 0; i < size; ++ i)
	{
		if (i % BATCH == 0)
		{
			if (i != 0)
			{
				DebugOutputString (line.c_str ());
			}
			line.clear ();
			line.push_back (L'{');
		}
		else
		{
			line.push_back (L' ');
		}
		line.push_back (HEX [(p [i] >> 4) & 0x0F]);
		line.push_back (HEX [p [i] & 0x0F]);
	}
	line.push_back (L'}');
	DebugOutputString (line.c_str ());
}
LRESULT CALLBACK SubclassWndProc (HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam, UINT_PTR, DWORD_PTR)
{
	if (hwnd == g_hWndTray && msg == WM_COPYDATA)
	{
		switch (msg)
		{
			case WM_COPYDATA:
			{
				auto cds = reinterpret_cast <COPYDATASTRUCT *> (lParam);
				if (cds && cds->dwData == 1) // dwData == 1 表示是 Shell_NotifyIcon 的数据
				{
					auto base = (BYTE *)cds->lpData;
					auto signature = *(DWORD *)base;
					if (signature != 0x34753423) break;
					auto dwMessage = *(DWORD *)(base + 0x04);
					{
						const BYTE *pNid = base + 0x08;
						DWORD cbSize = *(const DWORD *)pNid;   // cbSize 始终在偏移 0，安全
						DebugOutputBytes (pNid, cbSize);
						const bool is64 = IsNotifyIconData64 (cbSize);
						const size_t uFlagsOff = is64 ? 20 : 12;
						UINT uFlags = *(const UINT *)(pNid + uFlagsOff);
						if (!(uFlags & NIF_INFO)) break;
						auto data = BuildCommonNotifyData (dwMessage, pNid, cbSize);
						//DebugOutputNotifyDataInfo (data);
						if (data) SendPipeData (data, data->dwSize);
						DestroyCommonNotifyData (data);
						*(UINT *)(pNid + uFlagsOff) &= ~NIF_INFO;
					}
				}
			}
			break;
		}
	}
	return DefSubclassProc (hwnd, msg, wParam, lParam);
}
LRESULT CALLBACK CallWndProc (INT iCode, WPARAM wParam, LPARAM lParam)
{
	auto cpw = reinterpret_cast <CWPSTRUCT *> (lParam);
	if (iCode >= 0 && cpw && cpw->hwnd == g_hWndTray)
	{
		if (cpw->message == g_uUnregSubclassMsg)
		{
			if (g_bHasSubclass)
			{
				g_bHasSubclass = RemoveWindowSubclass (g_hWndTray, SubclassWndProc, 1);
			}
		}
		else if (!g_bHasSubclass)
		{
			g_bHasSubclass = SetWindowSubclass (g_hWndTray, SubclassWndProc, 1, NULL);
		}
	}
	return CallNextHookEx (g_hHook, iCode, wParam, lParam);
}
HRESULT NhInstallHook ()
{
	DebugOutputString ("[NhInstallHook] Entered");
	if (g_hHook) return S_OK;
	g_hWndTray = FindWindowW (L"Shell_TrayWnd", nullptr);
	if (!g_hWndTray) return HRESULT_FROM_WIN32 (ERROR_INVALID_WINDOW_HANDLE);
	DWORD dwProcessId = 0;
	auto dwThreadId = GetWindowThreadProcessId (g_hWndTray, &dwProcessId);
	HANDLE hCurrentProcess = GetCurrentProcess ();
	HANDLE hTrayProcess = OpenProcess (PROCESS_QUERY_INFORMATION, FALSE, dwProcessId);
	if (hTrayProcess)
	{
		bool bCurrentIs64 = IsProcess64Bit (hCurrentProcess);
		bool bTrayIs64 = IsProcess64Bit (hTrayProcess);
		CloseHandle (hTrayProcess);
		if (bCurrentIs64 != bTrayIs64)
		{
			return HRESULT_FROM_WIN32 (ERROR_BAD_EXE_FORMAT);
		}
	}
	g_uUnregSubclassMsg = RegisterWindowMessageW (UNREG_SUBCLASSMSGSTR);
	g_hHook = SetWindowsHookExW (WH_CALLWNDPROC, &CallWndProc, DEFAULT_HMODULE, dwThreadId);
	if (!g_hHook) return HRESULT_FROM_WIN32 (GetLastError ());
	DebugOutputString ("[NhInstallHook] Succeeded!");
	return S_OK;
}
HRESULT NhUninstallHook ()
{
	DebugOutputString ("[NhInstallHook] NhUninstallHook");
	if (!g_hHook) return S_OK;
	SendMessageW (g_hWndTray, g_uUnregSubclassMsg, 0, 0);
	g_uUnregSubclassMsg;
	auto bResult = UnhookWindowsHookEx (g_hHook);
	if (bResult)
	{
		g_hHook = nullptr;
		return S_OK;
	}
	DebugOutputString ("[NhInstallHook] Failed!");
	return HRESULT_FROM_WIN32 (GetLastError ());
}
BOOL NhHookExists () { return g_hHook != nullptr; }

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
					 )
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

