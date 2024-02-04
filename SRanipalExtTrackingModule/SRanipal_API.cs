//========= Copyright 2019, HTC Corporation. All rights reserved. ===========
using System;
using System.Runtime.InteropServices;

namespace ViveSR
{
    namespace anipal
    {
        public static class SRanipal_API
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr LoadLibrary(string dllToLoad);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool FreeLibrary(IntPtr hModule);

            private static IntPtr module = IntPtr.Zero;

            public static void InitialRuntime()
            {
                if (module != IntPtr.Zero) 
                    ReleaseRuntime();
                module = LoadLibrary("SRanipal.dll");
            }

            public static void ReleaseRuntime()
            {
                if (!FreeLibrary(module)) // ideally should never happen.
                    throw new Exception($"Failed to release Lip module DLL.");
                module = IntPtr.Zero;
            }

            [DllImport("SRanipal.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern Error Initial(int anipalType, IntPtr config);
        }
    }
}