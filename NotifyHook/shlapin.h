#pragma once
#include <Windows.h>
#include <shellapi.h>

// 返回 true = Unicode (W)，false = ANSI (A)
inline bool IsNotifyIconDataW (DWORD cbSize)
{
	switch (cbSize)
	{
		// 32 位 Unicode
		case 152:   // V1
		case 936:   // V2
		case 952:   // V3
		case 956:   // V4
					// 64 位 Unicode
		case 168:   // V1
		case 968:   // V3
		case 976:   // V4
			return true;

			// 32 位 ANSI
		case 88:    // V1
		case 488:   // V2
		case 504:   // V3
		case 508:   // V4
					// 64 位 ANSI
		case 104:   // V1
		case 520:   // V3
		case 528:   // V4
			return false;

			// 注意：952 同时对应“32 位 Unicode V3”与“64 位 Unicode V2”，
			// 两者都是 Unicode，因此返回 true 正确；
			// 504 同时对应“32 位 ANSI V3”与“64 位 ANSI V2”，
			// 两者都是 ANSI，因此返回 false 正确。
			// 这里不会出现 A/W 混淆，是安全的。
	}
	// 回退启发：Unicode 结构通常大于 600（V2+），或等于 152/168（V1）
	return cbSize > 600 || cbSize == 152 || cbSize == 168;
}
// 返回 true = 64 位布局，false = 32 位布局
// 存在两处不可判定的重叠：952 与 504，默认按 32 位处理
inline bool IsNotifyIconData64 (DWORD cbSize)
{
	switch (cbSize)
	{
		// 明确的 64 位
		case 104:   // 64 位 A V1
		case 168:   // 64 位 W V1
		case 520:   // 64 位 A V3
		case 528:   // 64 位 A V4
		case 968:   // 64 位 W V3
		case 976:   // 64 位 W V4
			return true;

			// 明确的 32 位
		case 88:    // 32 位 A V1
		case 152:   // 32 位 W V1
		case 488:   // 32 位 A V2
		case 508:   // 32 位 A V4
		case 936:   // 32 位 W V2
		case 956:   // 32 位 W V4
			return false;

			// 不可判定（32 位 V3 与 64 位 V2 大小相同）：
			// 952 = 32 位 W V3 或 64 位 W V2
			// 504 = 32 位 A V3 或 64 位 A V2
			// 现代系统上 V3/V4 更常见，默认按 32 位处理
		case 504:
		case 952:
			return false;
	}
	// 回退：默认 32 位
	return false;
}
// 辅助：判断是否为 V1（V1 的 szTip 只有 64 字符，且没有 dwState 等字段）
inline bool IsNotifyIconDataV1 (DWORD cbSize)
{
	return cbSize == 88 || cbSize == 152 || cbSize == 104 || cbSize == 168;
}