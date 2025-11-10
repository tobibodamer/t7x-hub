using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace GameMemoryReader
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);

        const int PROCESS_VM_READ = 0x0010;
        const int PROCESS_QUERY_INFORMATION = 0x0400;

        static IntPtr processHandle;
        static IntPtr baseAddress;
        static bool useClientAddresses = true;
        static long imageBase = 0x140000000; // Default image base for 64-bit executables

        // These are ABSOLUTE addresses from your symbols, not offsets
        // We'll calculate the actual offsets by subtracting the image base
        // Client addresses (first value)
        static readonly long ADDR_MAP_NAME_CLIENT = 0x1567D9A24;
        static readonly long ADDR_DVAR_POOL_CLIENT = 0x157AC6220;
        static readonly long ADDR_DVAR_COUNT_CLIENT = 0x157AC61CC;
        static readonly long ADDR_CLIENT_UI_ACTIVES = 0x1453D8BC0;
        static readonly long ADDR_CONNECTED_SERVER_BASE = 0x1453D8BB8;
        static readonly long ADDR_JOIN_CLIENT = 0x15574A640;

        // Dedi addresses (second value)
        static readonly long ADDR_MAP_NAME_DEDI = 0; // Not provided
        static readonly long ADDR_DVAR_POOL_DEDI = 0x14A3CB620;
        static readonly long ADDR_DVAR_COUNT_DEDI = 0x14A3CB5FC;

        static void Main(string[] args)
        {
            Console.WriteLine("Game Memory Reader");
            Console.WriteLine("==================\n");

            // Find the game process (replace with actual process name)
            Console.Write("Enter process name (e.g., 'cod' or 'game'): ");
            string processName = Console.ReadLine();

            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                Console.WriteLine($"Process '{processName}' not found!");
                return;
            }

            Process gameProcess = processes[0];
            ProcessModule module = gameProcess.Modules.OfType<ProcessModule>().First(m => m.ModuleName.Equals("BlackOps3.exe"));
            baseAddress = module.BaseAddress;
            processHandle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, gameProcess.Id);

            if (processHandle == IntPtr.Zero)
            {
                Console.WriteLine("Failed to open process!");
                return;
            }

            Console.WriteLine($"Process found! PID: {gameProcess.Id}");
            Console.WriteLine($"Base Address: 0x{baseAddress.ToInt64():X}");
            Console.WriteLine($"Module Size: 0x{module.ModuleMemorySize:X}");

            // Ask user if client or dedi
            Console.Write("\nIs this a client (C) or dedicated server (D)? [C/D]: ");
            string response = Console.ReadLine()?.ToUpper();
            useClientAddresses = response != "D";

            Console.WriteLine($"Using {(useClientAddresses ? "CLIENT" : "DEDI")} addresses");
            Console.WriteLine("\nReading memory every 1 second... (Press Ctrl+C to exit)\n");
            Thread.Sleep(2000);

            // Start reading loop
            while (true)
            {
                try
                {
                    Console.Clear();
                    Console.WriteLine($"=== Memory Read at {DateTime.Now:HH:mm:ss} ===\n");

                    // Get the appropriate addresses
                    long mapNameAddr = useClientAddresses ? ADDR_MAP_NAME_CLIENT : ADDR_MAP_NAME_DEDI;
                    long dvarPoolAddr = useClientAddresses ? ADDR_DVAR_POOL_CLIENT : ADDR_DVAR_POOL_DEDI;
                    long dvarCountAddr = useClientAddresses ? ADDR_DVAR_COUNT_CLIENT : ADDR_DVAR_COUNT_DEDI;
                    long clientUIActivesAddr = ADDR_CLIENT_UI_ACTIVES;

                    // Calculate offsets from image base and get runtime addresses
                    long mapNameFinal = baseAddress.ToInt64() + (mapNameAddr - imageBase);
                    long dvarPoolFinal = baseAddress.ToInt64() + (dvarPoolAddr - imageBase);
                    long dvarCountFinal = baseAddress.ToInt64() + (dvarCountAddr - imageBase);
                    long clientUIActivesFinal = baseAddress.ToInt64() + (clientUIActivesAddr - imageBase);

                    // Debug: Show what we're trying to read
                    Console.WriteLine($"Base Address: 0x{baseAddress.ToInt64():X}");
                    Console.WriteLine($"Image Base: 0x{imageBase:X}\n");

                    // Read map name
                    if (mapNameAddr != 0)
                    {
                        string mapName = ReadString(mapNameFinal, 64);
                        Console.WriteLine($"Map Name: '{mapName}'");
                    }

                    // Read connected server (client only)
                    if (useClientAddresses)
                    {
                        try
                        {
                            NetAddr connectedServer = GetConnectedServer();
                            Console.WriteLine($"\nConnected Server:");
                            Console.WriteLine($"  IP: {connectedServer.ipv4.a}.{connectedServer.ipv4.b}.{connectedServer.ipv4.c}.{connectedServer.ipv4.d}");
                            Console.WriteLine($"  Port: {connectedServer.port}");
                            Console.WriteLine($"  Type: {connectedServer.type}");
                            Console.WriteLine($"  LocalNetID: {connectedServer.localNetID}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\nConnected Server: Error - {ex.Message}");
                        }
                    }

                    // Read client UI state (client only)
                    if (useClientAddresses)
                    {
                        try
                        {
                            ClientUIActive uiState = ReadClientUIActive(clientUIActivesFinal);
                            Console.WriteLine($"Client UI State:");
                            Console.WriteLine($"  Flags: 0x{uiState.flags:X}");
                            Console.WriteLine($"  Key Catchers: 0x{uiState.keyCatchers:X}");
                            Console.WriteLine($"  Connection State: {uiState.connectionState}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Client UI State: Error - {ex.Message}");
                        }
                    }

                    //// Read dvar count
                    //if (dvarCountAddr != 0)
                    //{
                    //    int dvarCount = ReadInt32(dvarCountFinal);
                    //    Console.WriteLine($"\nDvar Count: {dvarCount}");

                    //    // Read dvars from pool (it's a hash table with linked lists)
                    //    if (dvarCount > 0 && dvarCount < 10000) // Sanity check
                    //    {
                    //        Console.WriteLine("\nDvar Pool (first 20 found):");
                    //        ReadDvarPoolHashTable(dvarPoolFinal, dvarCount, 20);
                    //    }
                    //    else
                    //    {
                    //        Console.WriteLine("Dvar count seems invalid (too high or zero)");
                    //    }
                    //}

                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading memory: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        }

        static NetAddr GetConnectedServer()
        {
            const int LOCAL_CLIENT_NUM = 0;
            const long MULTIPLIER = 0x25780;
            const long FINAL_OFFSET = 0x10;

            // Calculate runtime address
            long offset = ADDR_CONNECTED_SERVER_BASE - imageBase;
            long runtimeAddr = baseAddress.ToInt64() + offset;

            // Read the base pointer
            long basePtr = ReadInt64(runtimeAddr);

            // Calculate the address
            long address = basePtr + (MULTIPLIER * LOCAL_CLIENT_NUM) + FINAL_OFFSET;

            // Read the netadr_t structure
            return ReadNetAddr(address);
        }

        static ClientUIActive ReadClientUIActive(long address)
        {
            ClientUIActive uiActive = new ClientUIActive();

            uiActive.flags = ReadInt32(address + 0x00);
            uiActive.keyCatchers = ReadInt32(address + 0x04);
            uiActive.connectionState = (ConnState)ReadInt32(address + 0x08);

            return uiActive;
        }

        static void ReadDvarPoolHashTable(long poolAddress, int totalCount, int maxToDisplay)
        {
            // The dvar pool is a hash table - we need to find out the bucket count
            // Common hash table sizes are powers of 2 (256, 512, 1024, etc.)
            // Let's try to guess the bucket count or iterate through possible buckets

            int bucketsToCheck = Math.Min(512, totalCount); // Check first 512 buckets
            int found = 0;
            HashSet<long> visitedAddresses = new HashSet<long>();

            for (int bucket = 0; bucket < bucketsToCheck && found < maxToDisplay; bucket++)
            {
                try
                {
                    // Each bucket is a pointer (8 bytes)
                    long bucketAddr = poolAddress + (bucket * 8);
                    long dvarPtr = ReadInt64(bucketAddr);

                    if (dvarPtr == 0 || dvarPtr < 0x10000) continue;
                    if (visitedAddresses.Contains(dvarPtr)) continue;

                    // Traverse the linked list in this bucket
                    while (dvarPtr != 0 && found < maxToDisplay)
                    {
                        if (visitedAddresses.Contains(dvarPtr)) break;
                        visitedAddresses.Add(dvarPtr);

                        try
                        {
                            Dvar dvar = ReadDvar(dvarPtr);

                            if (!string.IsNullOrEmpty(dvar.debugName) && dvar.debugName != "(null)")
                            {
                                Console.WriteLine($"  [{found}] {dvar.debugName} = {GetDvarValueString(dvar)} (type: {dvar.type})");
                                found++;
                            }

                            // Get next dvar in linked list (hashNext pointer)
                            dvarPtr = dvar.hashNextPtr;
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    // Skip invalid buckets
                }
            }

            if (found == 0)
            {
                Console.WriteLine("  No valid dvars found. The hash table structure might be different.");
            }
        }

        static void ReadDvarPool(long poolAddress, int count)
        {
            // Legacy function - keeping for reference
            ReadDvarPoolHashTable(poolAddress, count, count);
        }

        static Dvar ReadDvar(long address)
        {
            Dvar dvar = new Dvar();

            dvar.nameHash = ReadUInt32(address + 0x00);
            dvar.debugName = ReadStringPtr(address + 0x08);
            dvar.description = ReadStringPtr(address + 0x10);
            dvar.flags = ReadUInt32(address + 0x18);
            dvar.type = (DvarType)ReadInt32(address + 0x1C);
            dvar.modified = ReadByte(address + 0x20) != 0;

            // Current value starts at offset 0x28 (after padding)
            dvar.currentValue = ReadDvarValue(address + 0x28, dvar.type);

            // hashNext pointer is at the end of the structure
            // Rough calculation: 0x00-0x27 (start fields) + 0x28-0x4F (current) + 0x50-0x77 (latched) + 0x78-0x9F (reset) + 0xA0-0xAF (limits) = ~0xB0
            // Let's read it at approximately 0xB0
            dvar.hashNextPtr = ReadInt64(address + 0xB0);

            return dvar;
        }

        static DvarValue ReadDvarValue(long address, DvarType type)
        {
            DvarValue value = new DvarValue();

            switch (type)
            {
                case DvarType.DVAR_TYPE_BOOL:
                    value.enabled = ReadByte(address) != 0;
                    break;
                case DvarType.DVAR_TYPE_INT:
                case DvarType.DVAR_TYPE_ENUM:
                    value.integer = ReadInt32(address);
                    break;
                case DvarType.DVAR_TYPE_FLOAT:
                    value.floatValue = ReadFloat(address);
                    break;
                case DvarType.DVAR_TYPE_STRING:
                    value.stringValue = ReadStringPtr(address);
                    break;
                case DvarType.DVAR_TYPE_INT64:
                    value.integer64 = ReadInt64(address);
                    break;
                default:
                    value.integer = ReadInt32(address);
                    break;
            }

            return value;
        }

        static string GetDvarValueString(Dvar dvar)
        {
            switch (dvar.type)
            {
                case DvarType.DVAR_TYPE_BOOL:
                    return dvar.currentValue.enabled.ToString();
                case DvarType.DVAR_TYPE_INT:
                case DvarType.DVAR_TYPE_ENUM:
                    return dvar.currentValue.integer.ToString();
                case DvarType.DVAR_TYPE_FLOAT:
                    return dvar.currentValue.floatValue.ToString("F2");
                case DvarType.DVAR_TYPE_STRING:
                    return dvar.currentValue.stringValue ?? "(null)";
                case DvarType.DVAR_TYPE_INT64:
                    return dvar.currentValue.integer64.ToString();
                default:
                    return $"Type {dvar.type}";
            }
        }

        static NetAddr ReadNetAddr(long address)
        {
            NetAddr addr = new NetAddr();
            byte[] buffer = ReadBytes(address, 16);

            addr.ipv4.a = buffer[0];
            addr.ipv4.b = buffer[1];
            addr.ipv4.c = buffer[2];
            addr.ipv4.d = buffer[3];
            addr.port = BitConverter.ToUInt16(buffer, 4);
            addr.type = (NetAdrType)BitConverter.ToInt32(buffer, 6);
            addr.localNetID = (NetSrc)BitConverter.ToInt32(buffer, 10);

            return addr;
        }

        static string ReadString(long address, int maxLength)
        {
            byte[] buffer = ReadBytes(address, maxLength);
            int nullIndex = Array.IndexOf(buffer, (byte)0);
            if (nullIndex >= 0)
                return Encoding.ASCII.GetString(buffer, 0, nullIndex);
            return Encoding.ASCII.GetString(buffer);
        }

        static string ReadStringPtr(long address)
        {
            long strPtr = ReadInt64(address);
            if (strPtr == 0) return null;
            return ReadString(strPtr, 256);
        }

        static byte[] ReadBytes(long address, int size)
        {
            byte[] buffer = new byte[size];
            int bytesRead;
            bool success = ReadProcessMemory(processHandle, new IntPtr(address), buffer, size, out bytesRead);

            if (!success || bytesRead == 0)
            {
                // Don't throw, just return empty buffer and log
                Console.WriteLine($"  [Warning] Failed to read {size} bytes from 0x{address:X}");
                return new byte[size];
            }

            return buffer;
        }

        static byte ReadByte(long address)
        {
            byte[] data = ReadBytes(address, 1);
            return data[0];
        }

        static int ReadInt32(long address)
        {
            byte[] data = ReadBytes(address, 4);
            if (data.All(b => b == 0)) return 0;
            return BitConverter.ToInt32(data, 0);
        }

        static uint ReadUInt32(long address)
        {
            byte[] data = ReadBytes(address, 4);
            if (data.All(b => b == 0)) return 0;
            return BitConverter.ToUInt32(data, 0);
        }

        static long ReadInt64(long address)
        {
            byte[] data = ReadBytes(address, 8);
            if (data.All(b => b == 0)) return 0;
            return BitConverter.ToInt64(data, 0);
        }

        static float ReadFloat(long address)
        {
            byte[] data = ReadBytes(address, 4);
            if (data.All(b => b == 0)) return 0f;
            return BitConverter.ToSingle(data, 0);
        }

        #region Structures

        enum ConnState
        {
            CA_DISCONNECTED = 0x0,
            CA_CINEMATIC = 0x1,
            CA_UICINEMATIC = 0x2,
            CA_LOGO = 0x3,
            CA_CONNECTING = 0x4,
            CA_CHALLENGING = 0x5,
            CA_CONFIRMLOADING = 0x6,
            CA_CONNECTED = 0x7,
            CA_SENDINGDATA = 0x8,
            CA_LOADING = 0x9,
            CA_PRIMED = 0xA,
            CA_ACTIVE = 0xB,
        }

        struct ClientUIActive
        {
            public int flags;
            public int keyCatchers;
            public ConnState connectionState;
            // unsigned char __pad0[0x106C]; - not reading padding
        }

        struct NetIPv4
        {
            public byte a, b, c, d;
        }

        enum NetAdrType
        {
            NA_BOT = 0,
            NA_BAD = 1,
            NA_LOOPBACK = 2,
            NA_RAWIP = 3,
            NA_IP = 4,
        }

        enum NetSrc
        {
            NS_NULL = -1,
            NS_CLIENT1 = 0,
            NS_CLIENT2 = 1,
            NS_CLIENT3 = 2,
            NS_CLIENT4 = 3,
            NS_SERVER = 4,
            NS_MAXCLIENTS = 4,
            NS_PACKET = 5,
        }

        struct NetAddr
        {
            public NetIPv4 ipv4;
            public ushort port;
            public NetAdrType type;
            public NetSrc localNetID;
        }

        enum DvarType
        {
            DVAR_TYPE_INVALID = 0x0,
            DVAR_TYPE_BOOL = 0x1,
            DVAR_TYPE_FLOAT = 0x2,
            DVAR_TYPE_FLOAT_2 = 0x3,
            DVAR_TYPE_FLOAT_3 = 0x4,
            DVAR_TYPE_FLOAT_4 = 0x5,
            DVAR_TYPE_INT = 0x6,
            DVAR_TYPE_ENUM = 0x7,
            DVAR_TYPE_STRING = 0x8,
            DVAR_TYPE_COLOR = 0x9,
            DVAR_TYPE_INT64 = 0xA,
            DVAR_TYPE_UINT64 = 0xB,
            DVAR_TYPE_LINEAR_COLOR_RGB = 0xC,
            DVAR_TYPE_COLOR_XYZ = 0xD,
            DVAR_TYPE_COLOR_LAB = 0xE,
            DVAR_TYPE_SESSIONMODE_BASE_DVAR = 0xF,
            DVAR_TYPE_COUNT = 0x10,
        }

        struct DvarValue
        {
            public bool enabled;
            public int integer;
            public long integer64;
            public float floatValue;
            public string stringValue;
        }

        struct Dvar
        {
            public uint nameHash;
            public string debugName;
            public string description;
            public uint flags;
            public DvarType type;
            public bool modified;
            public DvarValue currentValue;
            public long hashNextPtr; // Pointer to next dvar in hash bucket
        }

        #endregion
    }
}