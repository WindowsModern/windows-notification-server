#pragma once
#include <Windows.h>
#include <string>
#include "encoding.h"
#ifndef DBG_CPP_WSTRING 
#define DBG_CPP_WSTRING std::wstring
#endif
#ifndef DBG_CPP_STRING
#define DBG_CPP_STRING std::string
#endif
#ifndef _DEBUG_IO
#ifdef _DEBUG
#define _DEBUG_IO
#endif
#endif
#define debug_wstr(_wsptr_) ((_wsptr_) ? (_wsptr_) : L"")
#define debug_str(_str_) ((_str_) ? (_str_) : "")

#ifdef _DEBUG_IO
void DebugOutputStringW (LPCWSTR lpString) { OutputDebugStringW (debug_wstr (lpString)); }
void DebugOutputStringW (const DBG_CPP_WSTRING &swString) { DebugOutputStringW (swString.c_str ()); }
void DebugOutputStringA (const DBG_CPP_STRING &szString) { DebugOutputStringW (StringToWString (szString)); }
void DebugOutputStringA (LPCSTR lpString) { DebugOutputStringA (DBG_CPP_STRING (debug_str (lpString))); }
void DebugOutputString (LPCWSTR lpWString) { DebugOutputStringW (lpWString); }
void DebugOutputString (const DBG_CPP_WSTRING &swString) { DebugOutputStringW (swString); }
void DebugOutputString (LPCSTR lpString) { DebugOutputStringA (lpString); }
void DebugOutputString (const DBG_CPP_STRING &szString) { DebugOutputStringA (szString); }
#else
#define DebugOutputStringW(_not_debug_mode_) /* Please enable the _DEBUG_IO macro (it is automatically defined when _DEBUG is defined), and ensure that the language is C++ before using this API. Then use a tool such as DebugView to capture the output. */
#define DebugOutputStringA(_not_debug_mode_) /* Please enable the _DEBUG_IO macro (it is automatically defined when _DEBUG is defined), and ensure that the language is C++ before using this API. Then use a tool such as DebugView to capture the output. */
#define DebugOutputString(_not_debug_mode_) /* Please enable the _DEBUG_IO macro (it is automatically defined when _DEBUG is defined), and ensure that the language is C++ before using this API. Then use a tool such as DebugView to capture the output. */
#endif

#undef debug_wstr
#undef debug_str
#undef _DEBUG_IO
#undef DBG_CPP_WSTRING
#undef DBG_CPP_STRING