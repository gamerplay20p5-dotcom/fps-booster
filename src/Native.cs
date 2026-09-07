using System;
using System.Runtime.InteropServices;

namespace F26Boost
{
    // Camada P/Invoke. Tudo aqui e API publica do Windows.
    // Nada de driver, nada de patch de kernel, nada de escrita em memoria de terceiros.
    internal static class Native
    {
        // ===================================================== MEMORIA
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static MEMORYSTATUSEX GetMemory()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref m)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return m;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr min, IntPtr max, uint flags);

        // ===================================================== PRIVILEGIOS
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }
        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privilege; }

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool LookupPrivilegeValue(string system, string name, out LUID luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int len, IntPtr prev, IntPtr retLen);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;

        public static bool EnablePrivilege(string name)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) return false;
            try
            {
                LUID luid;
                if (!LookupPrivilegeValue(null, name, out luid)) return false;
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privilege = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };
                if (!AdjustTokenPrivileges(token, false, ref tp, Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero)) return false;
                return Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(token); }
        }

        public const string SE_PROFILE_SINGLE_PROCESS = "SeProfileSingleProcessPrivilege";
        public const string SE_INCREASE_QUOTA = "SeIncreaseQuotaPrivilege";
        public const string SE_DEBUG = "SeDebugPrivilege";

        // ===================================================== PROCESSO
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        public const uint PROCESS_SET_INFORMATION = 0x0200;
        public const uint PROCESS_SET_QUOTA = 0x0100;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

        public const int ProcessPowerThrottling = 4;
        public const uint PT_VERSION = 1;
        public const uint PT_EXECUTION_SPEED = 0x1;

        // EcoQoS: com eco=true o Thread Director do Alder Lake empurra o processo
        // para os E-cores, liberando os P-cores para o jogo.
        // Com eco=false o processo vai para HighQoS: nunca e freado (usado no jogo).
        // Para voltar ao padrao do sistema use ClearQoS, nao eco=false.
        public static bool SetEcoQoS(int pid, bool eco)
        {
            IntPtr h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                var st = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PT_VERSION,
                    ControlMask = PT_EXECUTION_SPEED,
                    StateMask = eco ? PT_EXECUTION_SPEED : 0u
                };
                return SetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
            }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// Devolve o processo ao gerenciamento padrao do Windows — nem Eco, nem High.
        /// ControlMask = 0 significa "o sistema decide", que e o estado de fabrica.
        /// </summary>
        public static bool ClearQoS(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                var st = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PT_VERSION,
                    ControlMask = 0,
                    StateMask = 0
                };
                return SetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
            }
            finally { CloseHandle(h); }
        }

        public static bool TrimProcess(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return SetProcessWorkingSetSizeEx(h, (IntPtr)(-1), (IntPtr)(-1), 0); }
            finally { CloseHandle(h); }
        }

        // ===================================================== TIMER
        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtSetTimerResolution(uint desired, bool set, out uint current);
        [DllImport("ntdll.dll", SetLastError = true)]
        public static extern int NtQueryTimerResolution(out uint min, out uint max, out uint current);

        // ===================================================== ENERGIA
        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint esFlags);
        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_DISPLAY_REQUIRED = 0x00000002;
    }
}
