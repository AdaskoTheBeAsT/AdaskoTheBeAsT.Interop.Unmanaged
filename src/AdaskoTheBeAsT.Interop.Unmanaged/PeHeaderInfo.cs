namespace AdaskoTheBeAsT.Interop.Unmanaged;

internal sealed class PeHeaderInfo
{
    public required ushort Machine { get; init; }

    public required bool IsPe32Plus { get; init; }

    public required uint SizeOfImage { get; init; }

    public required uint SizeOfHeaders { get; init; }

    public required int TimeDateStampFileOffset { get; init; }

    public required int CharacteristicsFileOffset { get; init; }

    public required int CheckSumFileOffset { get; init; }

    public required int DllCharacteristicsFileOffset { get; init; }

    public required int ImportDirectoryEntryFileOffset { get; init; }

    public required int SecurityDirectoryEntryFileOffset { get; init; }

    public uint SecurityTableFileOffset { get; set; }

    public uint SecurityTableSize { get; set; }

    public required uint ExportTableRva { get; init; }

    public required uint ExportTableSize { get; init; }

    public uint ImportTableRva { get; set; }

    public uint ImportTableSize { get; set; }

    public required uint DelayImportTableRva { get; init; }

    public required uint DelayImportTableSize { get; init; }

    public required PeSectionSlice[] Sections { get; init; }
}
