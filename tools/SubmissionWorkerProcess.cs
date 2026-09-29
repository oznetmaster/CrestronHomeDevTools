// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CrestronHomeDevTools.WorkerHosting
{
    // The launcher is the sole owner of this non-inheritable job handle. Windows
    // closes it even if Task Scheduler forcibly terminates the launcher.
    public sealed class SubmissionWorkerProcess : IDisposable
    {
        private SafeFileHandle job;
        public Process Process { get; private set; }

        public SubmissionWorkerProcess(string executable, string arguments, string directory)
        {
            if (String.IsNullOrWhiteSpace(executable) || executable.Contains("\""))
                throw new ArgumentException("Invalid worker executable.", nameof(executable));
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            PROCESS_INFORMATION child = new PROCESS_INFORMATION();
            bool started = false;
            IntPtr attributes = IntPtr.Zero, jobValue = IntPtr.Zero;
            bool attributesInitialized = false;
            try
            {
                var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                limits.BasicLimitInformation.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
                int size = Marshal.SizeOf(limits);
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(limits, buffer, false);
                    if (!SetInformationJobObject(job, 9, buffer, (uint)size))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { Marshal.FreeHGlobal(buffer); }

                // Associate the job atomically at creation (Windows 10+). Assigning
                // an already-created process leaves an orphan window if the parent dies.
                IntPtr attributeSize = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
                attributes = Marshal.AllocHGlobal(attributeSize);
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref attributeSize))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                attributesInitialized = true;
                jobValue = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(jobValue, job.DangerousGetHandle());
                if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x2000D), jobValue,
                    new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var startup = new STARTUPINFOEX();
                startup.StartupInfo.cb = Marshal.SizeOf(startup);
                startup.AttributeList = attributes;
                var command = new StringBuilder("\"" + executable + "\" " + arguments);
                // Suspend BEFORE any worker code runs; assignment cannot race with
                // the worker launching children or the launcher being stopped.
                if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, false,
                    0x00000004 | 0x08000000 | 0x00080000, IntPtr.Zero, directory, ref startup, out child))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                started = true;
                Process = Process.GetProcessById((int)child.dwProcessId);
                // Acquire the managed handle before resume, including short-lived workers.
                var handle = Process.Handle;
                if (ResumeThread(child.hThread) == UInt32.MaxValue)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch
            {
                // Assignment may fail (for example an incompatible containing job).
                // The child is still suspended and must never be left behind.
                if (started) TerminateProcess(child.hProcess, 1);
                Dispose();
                throw;
            }
            finally
            {
                if (child.hThread != IntPtr.Zero) CloseHandle(child.hThread);
                if (child.hProcess != IntPtr.Zero) CloseHandle(child.hProcess);
                if (attributesInitialized) DeleteProcThreadAttributeList(attributes);
                if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                if (jobValue != IntPtr.Zero) Marshal.FreeHGlobal(jobValue);
            }
        }

        public void Dispose()
        {
            if (job != null) { job.Dispose(); job = null; }
            if (Process != null) { Process.Dispose(); Process = null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public ushort wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public uint dwProcessId, dwThreadId;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr AttributeList;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute,
            IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);
        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
            ref STARTUPINFOEX startup, out PROCESS_INFORMATION process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
