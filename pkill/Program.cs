using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using Shell32;
using static pkill.NativeMethods;

// ReSharper disable InconsistentNaming

namespace pkill
{
    /*
     * Pkill is for terminating processes. A comma-separated list of image names and/or process id's can be provided
     * as a command-line argument, or the -gui switch can be used to terminate all process that have a window open.
     * The parent console of pkill.exe will never be terminated.
     */
    internal class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length != 1 || string.IsNullOrWhiteSpace(args.First()))
                    throw new ArgumentException("usage: pkill [,<process name | process id> | -gui]");

                var argument = args.First();
                var processes = new List<Process>();
                var closeAllWindows = argument.ToLower().Substring(1) == "gui";

                if (closeAllWindows)
                    processes.AddRange(from p in Process.GetProcesses()
                                       where p.MainWindowHandle != IntPtr.Zero && p.ProcessName != "explorer"
                                                                               && p.Handle !=
                                                                               Process.GetCurrentProcess().Handle
                                       select p);
                else
                {
                    var split = argument.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in split)
                        if (s.All(char.IsDigit))
                            processes.Add(Process.GetProcessById(int.Parse(s)));
                        else
                        {
                            var processName = Path.GetFileNameWithoutExtension(s);
                            if (processName.ToLower() == "explorer")
                                CloseExplorerWindows();
                            else
                                processes.AddRange(Process.GetProcessesByName(processName));
                        }
                }

                // Most likely this application will be ran from a console window. In that case, we do not want to allow
                // the console to be closed, so we try to find the handle value of the console using the NtQueryInformationProcess
                // kernel function.
                var dontCloseParent = TryGetCurrentConsoleHostProcessId(out var parentId);

                foreach (var process in processes)
                {
                    if (dontCloseParent && process.Id == parentId)
                        continue;
                    StopProcess(process);
                }

                if (closeAllWindows)
                    CloseExplorerWindows();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message + " " + e.InnerException?.Message);
            }

#if DEBUG
            if (!Debugger.IsAttached)
                return;
            Console.Write("Press any key to exit...");
            Console.ReadKey(true);
#endif
        }

        private static void StopProcess(Process process)
        {
            // We have to save these values to variables because once the process object has been
            // disposed, the ProcessName and Id properties will throw an InvalidOperationException.
            var (processname, id) = (process.ProcessName, process.Id);
            process.Kill();
            process.Dispose();

            Console.Out.WriteLine($"Terminated process \"{processname}\" (ID: {id}).");
        }

        private static void CloseExplorerWindows()
        {
            // All File Explorer windows are hosted withing a single instance of explorer.exe. Since
            // explorer also manages the Task Bar, we don't want to actually kill the process, just
            // close the windows.
            var shell = new Shell();
            Shell shellApplication = shell.Application;

            foreach (var window in shellApplication.Windows())
                if (window.Name == "File Explorer")
                {
                    long hwnd = window.HWND;
                    window.Quit();
                    Console.Out.WriteLine($"Closed File Explorer window (HWND: {hwnd}).");
                }
        }

        private static bool TryGetCurrentConsoleHostProcessId(out int id)
        {
            id = default;
            try
            {
                id = GetCurrentConsoleHostProcessId();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int GetCurrentConsoleHostProcessId()
        {
            var parent = GetOwningProcess(Process.GetCurrentProcess());

            // Work our way up the process tree to find the hosting cmd.exe process.
            while (parent.ProcessName != "cmd")
                parent = GetOwningProcess(parent);

            using (parent) return parent.Id;
        }

        private static Process GetOwningProcess(Process child)
        {
            var returnSize = 0;
            var size = Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION));
            var buffer = Marshal.AllocHGlobal(size);

            if (NtQueryInformationProcess(child.Handle, 0, buffer, size, ref returnSize) != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var pbi = Marshal.PtrToStructure<PROCESS_BASIC_INFORMATION>(buffer);

            Marshal.FreeHGlobal(buffer);
            child.Dispose();

            return Process.GetProcessById((int) pbi.InheritedFromUniqueProcessId);
        }
    }

    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
    internal class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public UIntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [DllImport("ntdll", SetLastError = true, CharSet = CharSet.Auto)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        public static extern int NtQueryInformationProcess(
            IntPtr ProcessHandle,
            int ProcessInformationClass,
            IntPtr ProcessInformation,
            int ProcessInformationLength,
            ref int ReturnLength);
    }
}
