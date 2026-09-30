# OpenAL Soft for Windows (x64, x86)

`win-x64/soft_oal.dll` and `win-x86/soft_oal.dll` are the official OpenAL Soft 1.23.1 binaries,
unchanged (bin/Win64 and bin/Win32 of `openal-soft-1.23.1-bin.zip` from
https://github.com/kcat/openal-soft/releases/tag/1.23.1, sha256
`ea8bc36fd7fa05f64e13400d20886de753a227202a4ea3781913489a26b36fc6` of the zip). They are MinGW builds that need only Windows' own `msvcrt.dll`, not the
Microsoft Visual C++ runtime that Silk.NET.OpenAL.Soft.Native's copies need (VCRUNTIME140.dll,
VCRUNTIME140_1.dll, MSVCP140.dll). Without that runtime installed, Silk's DLL fails to load and the
game is silent.

`Directory.Build.targets` swaps these in for Silk's on those two platforms, in builds and
publishes alike. There is no official ARM64 build, so win-arm64 keeps Silk's.

OpenAL Soft is LGPL-2.0-or-later (`COPYING-openal-soft.txt`); the source is at
https://github.com/kcat/openal-soft/tree/1.23.1.
