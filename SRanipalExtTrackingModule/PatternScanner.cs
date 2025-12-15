using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using VRCFaceTracking;

namespace SRanipalExtTrackingModule
{
    internal class PatternScanner
    {
        public static IntPtr Scan(IntPtr hProcess, ProcessModule module, string pattern)
        {
            byte[] patternBytes = ParsePattern(pattern);

            IntPtr start = module.BaseAddress;
            IntPtr end = start + module.ModuleMemorySize;

            IntPtr current = start;

            while (current.ToInt64() < end.ToInt64())
            {
                if (VirtualQueryEx(hProcess, current, out MEMORY_BASIC_INFORMATION mbi, (IntPtr)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == IntPtr.Zero)
                {
#if DEBUG
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"VirtualQueryEx failed. Error = {error}");
#endif
                    break;
                }

                bool readable =
                    mbi.State == MEM_COMMIT &&
                    (mbi.Protect & PAGE_GUARD) == 0 &&
                    (mbi.Protect & PAGE_NOACCESS) == 0;

                if (readable)
                {
                    int size = (int)Math.Min(
                        mbi.RegionSize.ToInt64(),
                        end.ToInt64() - current.ToInt64()
                    );

                    byte[] buffer = new byte[size];
                    int bytesRead = 0;

                    if (Utils.ReadProcessMemory(
                        (int)hProcess,
                        mbi.BaseAddress,
                        buffer,
                        size,
                        ref bytesRead) && bytesRead > 0)
                    {
                        IntPtr match = ScanBuffer(
                            buffer,
                            bytesRead,
                            patternBytes,
                            mbi.BaseAddress
                        );

                        if (match != IntPtr.Zero)
                            return match;
                    }
                }

                current = mbi.BaseAddress + mbi.RegionSize;
            }

            return IntPtr.Zero;
        }

        private static IntPtr ScanBuffer(
            byte[] buffer,
            int length,
            byte[] pattern,
            IntPtr baseAddress)
        {
            for (int i = 0; i <= length - pattern.Length; i++)
            {
                bool found = true;

                for (int j = 0; j < pattern.Length; j++)
                {
                    if (pattern[j] == 0xCC)
                        continue;

                    if (buffer[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    return baseAddress + i;
            }

            return IntPtr.Zero;
        }

        private static byte[] ParsePattern(string pattern)
        {
            string[] tokens = pattern.Split(' ');
            byte[] bytes = new byte[tokens.Length];

            for (int i = 0; i < tokens.Length; i++)
            {
                bytes[i] = (tokens[i] == "?" || tokens[i] == "??")
                    ? (byte)0xCC
                    : Convert.ToByte(tokens[i], 16);
            }

            return bytes;
        }

        #region WinAPI

        private const uint MEM_COMMIT = 0x1000;
        private const uint PAGE_NOACCESS = 0x01;
        private const uint PAGE_GUARD = 0x100;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualQueryEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer,
            IntPtr dwLength
        );

        #endregion
    }
}
