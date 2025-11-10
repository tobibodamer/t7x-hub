using System.Runtime.InteropServices;

namespace H2MLauncher.Core.Services;

// Struct matching netadr_s
[StructLayout(LayoutKind.Sequential)]
public struct NetAddress
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public byte[] IP;
    public ushort Port;
    public NetAddressType Type;
    public NetSrc LocalNetID;
}