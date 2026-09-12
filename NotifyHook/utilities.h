#pragma once
#include <Windows.h>
bool IsProcess64Bit (HANDLE hProcess)
{
	SYSTEM_INFO si;
	GetNativeSystemInfo (&si);
	if (si.wProcessorArchitecture != PROCESSOR_ARCHITECTURE_AMD64 &&
		si.wProcessorArchitecture != PROCESSOR_ARCHITECTURE_IA64 &&
		si.wProcessorArchitecture != 12)
	{
		return false;
	}
	typedef BOOL (WINAPI *LPFN_ISWOW64PROCESS)(HANDLE, PBOOL);
	static LPFN_ISWOW64PROCESS fnIsWow64Process = NULL;
	if (!fnIsWow64Process)
	{
		HMODULE hKernel32 = GetModuleHandleW (L"kernel32.dll");
		if (hKernel32)
		{
			fnIsWow64Process = (LPFN_ISWOW64PROCESS)GetProcAddress (hKernel32, "IsWow64Process");
		}
	}
	if (fnIsWow64Process)
	{
		BOOL bIsWow64 = FALSE;
		if (fnIsWow64Process (hProcess, &bIsWow64))
		{
			return !bIsWow64;
		}
	}
	return false;
}
INT64 GetUtcTimestampSec ()
{
	FILETIME ft = { 0 };
	GetSystemTimeAsFileTime (&ft); 
	ULARGE_INTEGER uli;
	uli.LowPart = ft.dwLowDateTime;
	uli.HighPart = ft.dwHighDateTime;
	#define EPOCH_DIFF_100NS 116444736000000000ULL
	if (uli.QuadPart < EPOCH_DIFF_100NS) return 0;
	return static_cast <INT64> ((uli.QuadPart - EPOCH_DIFF_100NS) / 10000000ULL);
}