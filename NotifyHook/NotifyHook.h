// 下列 ifdef 块是创建使从 DLL 导出更简单的
// 宏的标准方法。此 DLL 中的所有文件都是用命令行上定义的 NOTIFYHOOK_EXPORTS
// 符号编译的。在使用此 DLL 的
// 任何其他项目上不应定义此符号。这样，源文件中包含此文件的任何其他项目都会将
// NOTIFYHOOK_API 函数视为是从 DLL 导入的，而此 DLL 则将用此宏定义的
// 符号视为是被导出的。
#ifdef NOTIFYHOOK_EXPORTS
#define NOTIFYHOOK_API __declspec(dllexport)
#else
#define NOTIFYHOOK_API __declspec(dllimport)
#endif

#define NHAPI NOTIFYHOOK_API

#ifndef EXTERN_C
#define EXTERN_C extern "C"
#endif

EXTERN_C NHAPI HRESULT NhInstallHook ();
EXTERN_C NHAPI HRESULT NhUninstallHook ();
EXTERN_C NHAPI BOOL NhHookExists ();
EXTERN_C NHAPI void NhForceReset ();
EXTERN_C NHAPI BOOL NhIsHookAlive ();

#ifndef DEFAULT_VALUE
#if __cplusplus
#define DEFAULT_VALUE(_dflt_value_) = (_dflt_value_)
#else
#define DEFAULT_VALUE(_dflt_value_)
#endif
#endif
typedef struct _STRAYICONDATA
{
	INT64 llTimeStampUtc DEFAULT_VALUE (0);
	DWORD dwSize DEFAULT_VALUE (sizeof (_STRAYICONDATA));
	BYTE bHWndSizeOf DEFAULT_VALUE (sizeof (HWND));
	BYTE bUIntSizeOf DEFAULT_VALUE (sizeof (UINT));
	BYTE bHIconSizeOf DEFAULT_VALUE (sizeof (HICON));
	BYTE bNone DEFAULT_VALUE ('N');
	DWORD dwMessage DEFAULT_VALUE (0);
	/*
	hWnd | uId | uFlags | uCallbackMsg |
	hIcon | dwState | dwStateMask | 
	uTimeout | uVersion | dwInfoFlags | guidItem |
	szTip | szInfo | szInfoTitle
	*/
	BYTE bBuffer [4];
} STRAYICONDATA, *PSTRAYICONDATA;
#define SidebarNotifyInfoGetDataSize(_PSTRAYICONDATA_) ((_PSTRAYICONDATA_) ? (_PSTRAYICONDATA_)->dwSize : 0)
#define SidebarNotifyInfoGetTimeStampUtc(_PSTRAYICONDATA_) ((_PSTRAYICONDATA_) ? (_PSTRAYICONDATA_)->llTimeStampUtc : 0)
#define SidebarNotifyInfoGetMessage(_PSTRAYICONDATA_) ((_PSTRAYICONDATA_) ? (_PSTRAYICONDATA_)->dwMessage : 0)
#ifdef DEFAULT_VALUE
#undef DEFAULT_VALUE
#endif