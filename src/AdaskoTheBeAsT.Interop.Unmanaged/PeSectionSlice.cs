using System;
using System.Runtime.InteropServices;

namespace AdaskoTheBeAsT.Interop.Unmanaged;

[StructLayout(LayoutKind.Auto)]
internal readonly struct PeSectionSlice(
    uint virtualAddress,
    uint virtualSize,
    uint pointerToRawData,
    uint sizeOfRawData)
{
    public uint VirtualAddress { get; } = virtualAddress;

    public uint VirtualSize { get; } = virtualSize;

    public uint PointerToRawData { get; } = pointerToRawData;

    public uint SizeOfRawData { get; } = sizeOfRawData;

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
