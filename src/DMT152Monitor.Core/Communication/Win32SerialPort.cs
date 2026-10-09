using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DMT152Monitor.Core.Communication;

internal sealed class Win32SerialPort : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private const uint PurgeTxAbort = 0x0001;
    private const uint PurgeRxAbort = 0x0002;
    private const uint PurgeTxClear = 0x0004;
    private const uint PurgeRxClear = 0x0008;

    private const byte NoParity = 0;
    private const byte OddParity = 1;
    private const byte EvenParity = 2;
    private const byte OneStopBit = 0;
    private const byte TwoStopBits = 2;

    private SafeFileHandle? _handle;
    private bool _disposed;

    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

    public void Open(string portName, int baudRate, int dataBits, char parity, int stopBits)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpen)
            throw new InvalidOperationException("Serial port is already open.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DMT152 serial communication is supported only on Windows.");

        string normalized = portName.Trim().ToUpperInvariant();
        string path = normalized.StartsWith("\\\\.\\", StringComparison.Ordinal)
            ? normalized
            : $"\\\\.\\{normalized}";

        SafeFileHandle handle = CreateFile(
            path,
            GenericRead | GenericWrite,
            0,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open {normalized}.");
        }

        try
        {
            Configure(handle, baudRate, dataBits, parity, stopBits);
            _handle = handle;
            Purge();
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SafeFileHandle handle = GetHandle();

        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0)
            return 0;

        byte[] target;
        if (offset == 0 && count == buffer.Length)
        {
            target = buffer;
        }
        else
        {
            target = new byte[count];
        }

        if (!ReadFile(handle, target, count, out int read, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Serial read failed.");

        if (!ReferenceEquals(target, buffer) && read > 0)
            Buffer.BlockCopy(target, 0, buffer, offset, read);

        return read;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SafeFileHandle handle = GetHandle();
        if (data.Length == 0)
            return;

        byte[] bytes = data.ToArray();
        int writtenTotal = 0;
        while (writtenTotal < bytes.Length)
        {
            int remaining = bytes.Length - writtenTotal;
            byte[] chunk;
            if (writtenTotal == 0)
            {
                chunk = bytes;
            }
            else
            {
                chunk = new byte[remaining];
                Buffer.BlockCopy(bytes, writtenTotal, chunk, 0, remaining);
            }

            if (!WriteFile(handle, chunk, remaining, out int written, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Serial write failed.");
            if (written <= 0)
                throw new IOException("Serial write returned zero bytes.");

            writtenTotal += written;
        }
    }

    public void Purge()
    {
        if (!IsOpen)
            return;

        SafeFileHandle handle = GetHandle();
        if (!PurgeComm(handle, PurgeRxAbort | PurgeTxAbort | PurgeRxClear | PurgeTxClear))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not purge serial buffers.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _handle?.Dispose();
        _handle = null;
        _disposed = true;
    }

    private static void Configure(SafeFileHandle handle, int baudRate, int dataBits, char parity, int stopBits)
    {
        if (baudRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(baudRate));
        if (dataBits is not (7 or 8))
            throw new ArgumentOutOfRangeException(nameof(dataBits));
        if (stopBits is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(stopBits));

        var dcb = new Dcb
        {
            DCBlength = checked((uint)Marshal.SizeOf<Dcb>())
        };

        if (!GetCommState(handle, ref dcb))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read serial-port settings.");

        dcb.BaudRate = checked((uint)baudRate);
        dcb.ByteSize = checked((byte)dataBits);
        dcb.Parity = parity switch
        {
            'N' or 'n' => NoParity,
            'E' or 'e' => EvenParity,
            'O' or 'o' => OddParity,
            _ => throw new ArgumentOutOfRangeException(nameof(parity))
        };
        dcb.StopBits = stopBits == 1 ? OneStopBit : TwoStopBits;

        // DCB bit-field settings:
        // fBinary=1; no CTS/DSR/XON-XOFF flow control; DTR/RTS disabled.
        // The DMT152 manual specifies serial flow control = None.
        // fParity follows the selected parity mode.
        uint flags = 0;
        flags |= 1u << 0; // fBinary
        if (dcb.Parity != NoParity)
            flags |= 1u << 1; // fParity
        dcb.Flags = flags;
        dcb.XonChar = 0x11;
        dcb.XoffChar = 0x13;

        if (!SetCommState(handle, ref dcb))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure serial-port settings.");

        if (!SetupComm(handle, 4096, 4096))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure serial-port buffers.");

        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = 50,
            ReadTotalTimeoutMultiplier = 0,
            ReadTotalTimeoutConstant = 200,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 1000
        };

        if (!SetCommTimeouts(handle, ref timeouts))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure serial-port timeouts.");
    }

    private SafeFileHandle GetHandle()
    {
        return _handle is { IsInvalid: false, IsClosed: false } handle
            ? handle
            : throw new InvalidOperationException("Serial port is not open.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommState(SafeFileHandle hFile, ref Dcb lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommState(SafeFileHandle hFile, ref Dcb lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref CommTimeouts lpCommTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupComm(SafeFileHandle hFile, uint dwInQueue, uint dwOutQueue);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeComm(SafeFileHandle hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten,
        IntPtr lpOverlapped);
}
