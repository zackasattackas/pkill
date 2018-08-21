# pkill
I made a console application for killing processes. This was born out of the following frustrations with the `taskkill` command in Windows.

*	Complexity of command line arguments. Typing out `taskkill.exe /im <process name> /f` is annoying.
    * Pkill only has two possible arguments which are mutually exclusive (see below).
*	Inability to gracefully close File Explorer windows.
    * If you run `taskkill.exe /im explorer /f`, the explorer.exe process itself will be killed, which will also kill your task bar. Since all File Explorer windows are hosted within a single explorer.exe instance, Pkill instead uses the Windows Shell API to only close the File Explorer windows, and not terminate the process.
* The command `taskkill.exe /im cmd /f` closes the console window you currently have open.
    * Pkill uses a call to the Windows kernel to get the process ID of the parent console window and will not terminate that process if it is still running. (Windows does not maintain an actual process hierarchy like Unix-based systems, but you can still get the ID of the parent using the NtQueryInformationProcess function exported in ntdll.dll. This is also how Sysinternals ProcExp is able to display processes in Windows as a hierarchy.)

The command-line syntax is `pkill [,<process name | process id> | -gui]`. 
You can provide a comma-separated list of process names (with or without the file extension) and/or process ID’s, or you can use the `–gui` switch to close all applications that have an open Window. 

## Examples

* Terminate all processes with the name "notepad"
```
pkill notepad
```

* Close all open File Explorer windows and terminate a process with a specific ID
```
pkill Explorer,10728
```

* Terminate all processes with a window handle and close all File Explorer windows
```
pkill -gui
```
