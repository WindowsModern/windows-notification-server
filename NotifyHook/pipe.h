#pragma once
#include <Windows.h>

#define PIPENAME L"\\\\.\\pipe\\SidebarNotifyIconPipe"

bool SendPipeData (const void *data, size_t size)
{
	HANDLE hPipe = INVALID_HANDLE_VALUE;
	for (short i = 0; i < 3; ++ i)
	{
		hPipe = CreateFileW (PIPENAME, GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
		if (hPipe != INVALID_HANDLE_VALUE) break;
		auto err = GetLastError ();
		if (err == ERROR_PIPE_BUSY)
		{
			if (!WaitNamedPipeW (PIPENAME, 500)) return false;
		}
		else if (err == ERROR_FILE_NOT_FOUND) return false;
		else return false;
	}
	if (hPipe == INVALID_HANDLE_VALUE) return FALSE;
	DWORD written = 0;
	bool ok = WriteFile (hPipe, data, size, &written, NULL);
	CloseHandle (hPipe);
	return ok && written == size;
}