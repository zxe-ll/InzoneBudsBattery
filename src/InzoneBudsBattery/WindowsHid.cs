using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InzoneBudsBattery;

internal sealed record WindowsHidDevice(
    string DevicePath,
    int InputReportLength,
    int OutputReportLength,
    ushort UsagePage,
    ushort Usage)
{
    public FileStream OpenRead()
        => Open(WindowsHidNative.GenericRead, FileAccess.Read);

    public FileStream OpenReadWrite()
        => Open(
            WindowsHidNative.GenericRead | WindowsHidNative.GenericWrite,
            FileAccess.ReadWrite);

    private FileStream Open(uint desiredAccess, FileAccess access)
    {
        var handle = WindowsHidNative.CreateFile(
            DevicePath,
            desiredAccess,
            WindowsHidNative.FileShareRead | WindowsHidNative.FileShareWrite,
            IntPtr.Zero,
            WindowsHidNative.OpenExisting,
            WindowsHidNative.FileAttributeNormal | WindowsHidNative.FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open HID interface: {DevicePath}");
        }

        return new FileStream(
            handle,
            access,
            Math.Max(64, Math.Max(InputReportLength, OutputReportLength)),
            isAsync: true);
    }
}

internal static class WindowsHid
{
    private const int ErrorNoMoreItems = 259;

    public static IReadOnlyList<WindowsHidDevice> Enumerate(int vendorId, int productId)
    {
        WindowsHidNative.HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = WindowsHidNative.SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            WindowsHidNative.DigcfPresent | WindowsHidNative.DigcfDeviceInterface);

        if (deviceInfoSet == WindowsHidNative.InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed.");
        }

        try
        {
            var devices = new List<WindowsHidDevice>();
            for (uint index = 0; ; index++)
            {
                var interfaceData = new WindowsHidNative.SpDeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<WindowsHidNative.SpDeviceInterfaceData>(),
                };

                if (!WindowsHidNative.SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed.");
                }

                _ = WindowsHidNative.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    IntPtr.Zero);

                if (requiredSize == 0)
                {
                    continue;
                }

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!WindowsHidNative.SetupDiGetDeviceInterfaceDetail(
                            deviceInfoSet,
                            ref interfaceData,
                            detailBuffer,
                            requiredSize,
                            out _,
                            IntPtr.Zero))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetDeviceInterfaceDetail failed.");
                    }

                    var path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, sizeof(uint)));
                    if (string.IsNullOrWhiteSpace(path)
                        || !MatchesVidPid(path, vendorId, productId))
                    {
                        continue;
                    }

                    var capabilities = GetCapabilities(path);
                    devices.Add(
                        new WindowsHidDevice(
                            path,
                            capabilities.InputReportLength,
                            capabilities.OutputReportLength,
                            capabilities.UsagePage,
                            capabilities.Usage));
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }

            return devices;
        }
        finally
        {
            _ = WindowsHidNative.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public static bool TryGetInputReport(
        string devicePath,
        int reportLength,
        byte reportId,
        out byte[] report,
        out int errorCode)
    {
        report = new byte[Math.Max(20, reportLength)];
        report[0] = reportId;

        using var handle = WindowsHidNative.CreateFile(
            devicePath,
            WindowsHidNative.GenericRead,
            WindowsHidNative.FileShareRead | WindowsHidNative.FileShareWrite,
            IntPtr.Zero,
            WindowsHidNative.OpenExisting,
            WindowsHidNative.FileAttributeNormal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        if (!WindowsHidNative.HidD_GetInputReport(handle, report, report.Length))
        {
            errorCode = Marshal.GetLastWin32Error();
            return false;
        }

        errorCode = 0;
        return true;
    }

    private static bool MatchesVidPid(string path, int vendorId, int productId)
    {
        var expected = $"vid_{vendorId:x4}&pid_{productId:x4}";
        return path.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static HidCapabilities GetCapabilities(string path)
    {
        using var handle = WindowsHidNative.CreateFile(
            path,
            0,
            WindowsHidNative.FileShareRead | WindowsHidNative.FileShareWrite,
            IntPtr.Zero,
            WindowsHidNative.OpenExisting,
            WindowsHidNative.FileAttributeNormal,
            IntPtr.Zero);

        if (handle.IsInvalid
            || !WindowsHidNative.HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return default;
        }

        try
        {
            if (WindowsHidNative.HidP_GetCaps(preparsedData, out var capabilities)
                != WindowsHidNative.HidpStatusSuccess)
            {
                return default;
            }

            return new HidCapabilities(
                capabilities.InputReportByteLength,
                capabilities.OutputReportByteLength,
                capabilities.UsagePage,
                capabilities.Usage);
        }
        finally
        {
            _ = WindowsHidNative.HidD_FreePreparsedData(preparsedData);
        }
    }

    private readonly record struct HidCapabilities(
        int InputReportLength,
        int OutputReportLength,
        ushort UsagePage,
        ushort Usage);
}

internal static partial class WindowsHidNative
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint OpenExisting = 3;
    public const uint FileAttributeNormal = 0x00000080;
    public const uint FileFlagOverlapped = 0x40000000;
    public const uint DigcfPresent = 0x00000002;
    public const uint DigcfDeviceInterface = 0x00000010;
    public const int HidpStatusSuccess = 0x00110000;
    public static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [LibraryImport("hid.dll")]
    internal static partial void HidD_GetHidGuid(out Guid hidGuid);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool HidD_GetInputReport(
        SafeFileHandle device,
        [Out] byte[] reportBuffer,
        int reportBufferLength);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    internal static partial IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr parentWindow,
        uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
