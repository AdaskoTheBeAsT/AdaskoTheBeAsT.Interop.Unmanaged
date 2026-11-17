using System;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

[StructLayout(LayoutKind.Auto)]
internal readonly struct PeSectionSlice
{
    public PeSectionSlice(
        uint virtualAddress,
        uint virtualSize,
        uint pointerToRawData,
        uint sizeOfRawData)
    {
        VirtualAddress = virtualAddress;
        VirtualSize = virtualSize;
        PointerToRawData = pointerToRawData;
        SizeOfRawData = sizeOfRawData;
    }

    public uint VirtualAddress { get; }

    public uint VirtualSize { get; }

    public uint PointerToRawData { get; }

    public uint SizeOfRawData { get; }

    public ulong FileBackedSpanLength
    {
        get
        {
            if (SizeOfRawData == 0 || PointerToRawData == 0)
            {
                return 0;
            }

            if (VirtualSize == 0)
            {
                return SizeOfRawData;
            }

            return Math.Min(VirtualSize, SizeOfRawData);
        }
    }
}
