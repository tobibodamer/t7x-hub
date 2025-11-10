using System.Diagnostics;

using H2MLauncher.Core.Utilities;

namespace H2MLauncher.Core.Services;

public class T7GameMemory : IDisposable
{
    // Memory offsets for game variables
    const nint ADDR_MAP_NAME_CLIENT = 0x167D9A24;
    const nint ADDR_DVAR_POOL_CLIENT = 0x17AC6220;
    const nint ADDR_DVAR_COUNT_CLIENT = 0x17AC61CC;
    const nint ADDR_CLIENT_UI_ACTIVES = 0x53D8BC0;
    const nint ADDR_JOIN_CLIENT = 0x1574A640;
    const nint ADDR_CONNECTED_SERVER_BASE = 0x53D8BB8;

    public Process Process { get; }

    private readonly IntPtr _processHandle;
    private readonly IntPtr _moduleBaseAddress;

    public T7GameMemory(Process process, string moduleName = Constants.GAME_EXECUTABLE_NAME)
    {
        var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => m.ModuleName.Equals(moduleName));
        if (module is null)
        {
            throw new Exception("Game module not found in process");
        }

        _moduleBaseAddress = module.BaseAddress;
        _processHandle = ProcessMemory.OpenProcess(process);
        Process = process;
    }

    public void Dispose()
    {
        ProcessMemory.CloseProcess(_processHandle);
    }


    public NetAddress? GetConnectedServer()
    {
        const int LOCAL_CLIENT_NUM = 0;
        const long MULTIPLIER = 0x25780;
        const long FINAL_OFFSET = 0x10;

        // Calculate runtime address
        nint runtimeAddr = _moduleBaseAddress + ADDR_CONNECTED_SERVER_BASE;

        // Read the base pointer
        if (!ProcessMemory.ReadPointerFromMemory(_processHandle, runtimeAddr, out nint basePtr))
        {
            return null;
        }

        // Calculate the address
        nint address = new(basePtr + (MULTIPLIER * LOCAL_CLIENT_NUM) + FINAL_OFFSET);

        // Read the netadr structure
        if (ProcessMemory.ReadStructFromMemory(_processHandle, address, out NetAddress netAddress))
        {
            return netAddress;
        }

        return null;
    }

    public ConnectionState? GetConnectionState()
    {
        nint connectionStateAddr = _moduleBaseAddress + ADDR_CLIENT_UI_ACTIVES + 8;
        if (ProcessMemory.ReadProcessMemoryInt(_processHandle, connectionStateAddr, out int connectionState))
        {
            return (ConnectionState)connectionState;
        }

        return null;
    }

    public int GetUsermapsCount()
    {
        nint addr = _moduleBaseAddress + 0x167B3580;
        if (ProcessMemory.ReadProcessMemoryUInt(_processHandle, addr, out uint count))
        {
            return (int)count;
        }

        return 0;
    }
}
