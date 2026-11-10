using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Answers whether a process owns the IPv4 loopback listener on a port, read from the IP Helper
/// TCP owner-PID table, which names the owning process of each listening socket.
/// </summary>
internal static class TcpListener
{
    private const uint ErrorInsufficientBuffer = 122;
    private const uint AfInet = 2;
    private const uint TcpTableOwnerPidAll = 5;
    private const uint MibTcpStateListen = 2;
    private const uint LoopbackAddress = 0x7f00_0001;
    private const int TableHeaderSize = sizeof(uint);
    private const int RowSize = 6 * sizeof(uint);
    private const int StateOffset = 0;
    private const int LocalAddrOffset = 4;
    private const int LocalPortOffset = 8;
    private const int OwningPidOffset = 20;

    /// <summary>
    /// Returns false when the process instance no longer matches, so a recycled PID never claims
    /// the listener. Throws a timeout when the deadline has passed before the table is read.
    /// </summary>
    internal static bool OwnsListener(ProcessIdentity process, ushort port, Deadline deadline)
    {
        if (!ProcessTimes.MatchesInstance(process.Pid.Value, process.Instance))
        {
            return false;
        }

        if (deadline.IsExpired())
        {
            throw AdapterError.Timeout("Checking the DevTools listener owner");
        }

        uint size = 0;
        uint result = GetExtendedTcpTable(null, ref size, 0, AfInet, TcpTableOwnerPidAll, 0);
        if (result != ErrorInsufficientBuffer || size < TableHeaderSize)
        {
            throw AdapterError.FromCode(ErrorCode.Internal, "Windows could not size the TCP owner table");
        }

        byte[] buffer = new byte[size];
        result = GetExtendedTcpTable(buffer, ref size, 0, AfInet, TcpTableOwnerPidAll, 0);
        if (result != 0)
        {
            throw AdapterError.FromCode(ErrorCode.Internal, "Windows could not read the TCP owner table");
        }

        return AnyRowOwnsListener(buffer, size, process.Pid.Value, port);
    }

    /// <summary>
    /// Walks the rows bounded by the size Windows reported, so a declared entry count larger than
    /// the returned bytes cannot read past the buffer.
    /// </summary>
    private static bool AnyRowOwnsListener(byte[] buffer, uint size, uint pid, ushort port)
    {
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        int available = (int)Math.Min(size, (uint)buffer.Length);
        int capacity = Math.Max(0, (available - TableHeaderSize) / RowSize);
        int count = (int)Math.Min(declared, (uint)capacity);

        for (int index = 0; index < count; index++)
        {
            int offset = TableHeaderSize + (index * RowSize);
            uint state = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + StateOffset));
            ushort localPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset + LocalPortOffset));
            uint localAddr = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset + LocalAddrOffset));
            uint owningPid = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + OwningPidOffset));

            if (state == MibTcpStateListen
                && localPort == port
                && localAddr == LoopbackAddress
                && owningPid == pid)
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("iphlpapi.dll", EntryPoint = "GetExtendedTcpTable", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(
        byte[]? tcpTable,
        ref uint sizePointer,
        int order,
        uint addressFamily,
        uint tableClass,
        uint reserved);
}
