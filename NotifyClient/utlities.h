#pragma once
#include <string>

std::wstring GetHex (void *p)
{
	wchar_t buf [33] = { 0 };;
	swprintf_s (buf, 33, L"0x%p", p);
	return buf;
}