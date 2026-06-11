using System;
using System.Runtime.InteropServices;

namespace NurMarketKassa.Services
{
    public static class RawPrinterHelper
    {
        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int Level, ref DOC_INFOA docInfo);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static bool SendBytesToPrinter(string printerName, byte[] bytes)
        {
            if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
                return false;

            var docInfo = new DOC_INFOA { pDocName = "Receipt", pDataType = "RAW" };
            bool success = StartDocPrinter(hPrinter, 1, ref docInfo) && StartPagePrinter(hPrinter);

            if (success)
            {
                IntPtr unmanagedBytes = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, unmanagedBytes, bytes.Length);
                WritePrinter(hPrinter, unmanagedBytes, bytes.Length, out _);
                Marshal.FreeHGlobal(unmanagedBytes);
                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
            }

            ClosePrinter(hPrinter);
            return success;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DOC_INFOA
        {
            public string pDocName;
            public string pOutputFile;
            public string pDataType;
        }
    }
}