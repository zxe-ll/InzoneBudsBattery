namespace InzoneBudsBattery;

internal static class BudsHciProtocol
{
    private const byte ReportId = 0x02;
    private const byte CommandPacket = 0x01;
    private const ushort VendorOpcode = 0xFC00;
    private const ushort SonyKeyId = 0xC396;
    private const byte PcReceiveAddress = 0x41;
    private const byte BatteryEventId = 0x04;
    private const byte GetFlag = 0x01;
    private const int MinimumOutputReportLength = 64;

    public static byte[]? BuildBatteryGetReport(int outputReportLength, ushort transactionId)
    {
        if (outputReportLength < MinimumOutputReportLength || transactionId == 0)
        {
            return null;
        }

        var report = new byte[outputReportLength];
        report[0] = ReportId;
        report[1] = 12;
        report[2] = CommandPacket;
        report[3] = (byte)(VendorOpcode & 0xFF);
        report[4] = (byte)(VendorOpcode >> 8);
        report[5] = 8;
        report[6] = (byte)(SonyKeyId & 0xFF);
        report[7] = (byte)(SonyKeyId >> 8);
        report[8] = PcReceiveAddress;
        report[9] = BatteryEventId;
        report[10] = GetFlag;
        report[11] = (byte)transactionId;
        report[12] = (byte)(transactionId >> 8);
        report[13] = CalculateChecksum(transactionId);
        return report;
    }

    private static byte CalculateChecksum(ushort transactionId)
    {
        var sum = (byte)(SonyKeyId & 0xFF)
                  + (byte)(SonyKeyId >> 8)
                  + PcReceiveAddress
                  + BatteryEventId
                  + GetFlag
                  + (byte)transactionId
                  + (byte)(transactionId >> 8);
        return (byte)(sum & 0xFF);
    }
}
