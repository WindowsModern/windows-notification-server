#pragma once
#include <Windows.h>
#include <shellapi.h>
#include <string>
std::wstring DbgSafeStr (LPCWSTR s)
{
	return s ? s : L"(null)";
}
std::wstring DbgNifName (UINT uFlags)
{
	static WCHAR buf [256];
	buf [0] = L'\0';
	auto append = [&] (LPCWSTR name)
	{
		if (buf [0]) wcscat_s (buf, L"|");
		wcscat_s (buf, name);
	};
	if (uFlags & 0x00000001) append (L"NIF_MESSAGE");
	if (uFlags & 0x00000002) append (L"NIF_ICON");
	if (uFlags & 0x00000004) append (L"NIF_TIP");
	if (uFlags & 0x00000008) append (L"NIF_STATE");
	if (uFlags & 0x00000010) append (L"NIF_INFO");
	if (uFlags & 0x00000020) append (L"NIF_GUID");
	if (uFlags & 0x00000040) append (L"NIF_REALTIME");
	if (uFlags & 0x00000080) append (L"NIF_SHOWTIP");
	if (!buf [0]) wcscpy_s (buf, L"0");
	return buf;
}
std::wstring DbgNiifName (DWORD dwInfoFlags)
{
	switch (dwInfoFlags & 0x0F)
	{
		case 0x00: return L"NIIF_NONE";
		case 0x01: return L"NIIF_INFO";
		case 0x02: return L"NIIF_WARNING";
		case 0x03: return L"NIIF_ERROR";
		case 0x04: return L"NIIF_USER";
		default:   return L"NIIF_?";
	}
}