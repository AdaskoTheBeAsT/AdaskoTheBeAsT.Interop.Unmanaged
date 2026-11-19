using System;
using System.Globalization;
#if NET8_0_OR_GREATER
using System.IO;
using System.Reflection.PortableExecutable;
#endif

namespace AdaskoTheBeAsT.Interop.Unmanaged;

#pragma warning disable MA0012 // BadImageFormatException is the documented public exception contract for PE validation failures.

internal static class PeHeaderParser
{
    private const ushort DosSignature = 0x5A4D;
    private const uint PeSignature = 0x00004550;
    private const ushort Pe32Magic = 0x010B;
    private const ushort Pe32PlusMagic = 0x020B;
    private const ushort ImageFileDllFlag = 0x2000;
    private const int DosHeaderSize = 64;
    private const int CoffHeaderSize = 20;
    private const int SectionHeaderSize = 40;
    private const int MaxSections = 96;
    private const uint MaxDataDirectories = 64;

    public static PeHeaderInfo Parse(
        byte[] buffer,
        string imageLabel,
        PeImageValidationOptions validation)
    {
        ValidateExpectedLength(buffer, imageLabel, validation);
        var peOffset = ReadAndValidatePeSignatureOffset(buffer, imageLabel);
        var coffOffset = peOffset + 4;
        var machine = ReadUInt16(buffer, coffOffset);
        var sectionCount = ReadUInt16(buffer, coffOffset + 2);
        var optionalHeaderSize = ReadUInt16(buffer, coffOffset + 16);
        var characteristics = ReadUInt16(buffer, coffOffset + 18);

        ValidateCoffFields(machine, sectionCount, characteristics, imageLabel, validation);
        return ParseOptionalHeaderAndSections(
            buffer,
            coffOffset,
            machine,
            sectionCount,
            optionalHeaderSize,
            imageLabel,
            validation);
    }

    internal static ushort ReadUInt16(
        byte[] buffer,
        int offset)
    {
        return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    internal static uint ReadUInt32(
        byte[] buffer,
        int offset)
    {
        return (uint)(buffer[offset]
                      | (buffer[offset + 1] << 8)
                      | (buffer[offset + 2] << 16)
                      | (buffer[offset + 3] << 24));
    }

    internal static ulong ReadUInt64(
        byte[] buffer,
        int offset)
    {
        var low = ReadUInt32(buffer, offset);
        var high = ReadUInt32(buffer, offset + 4);
        return low | ((ulong)high << 32);
    }

    internal static void WriteUInt16(
        byte[] buffer,
        int offset,
        ushort value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    internal static void WriteUInt32(
        byte[] buffer,
        int offset,
        uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    internal static BadImageFormatException CreateBadImage(
        string imageLabel,
        string detail)
    {
        return new BadImageFormatException($"Invalid PE image '{imageLabel}': {detail}");
    }

    private static PeHeaderInfo ParseOptionalHeaderAndSections(
        byte[] buffer,
        int coffOffset,
        ushort machine,
        ushort sectionCount,
        ushort optionalHeaderSize,
        string imageLabel,
        PeImageValidationOptions validation)
    {
        var optOffset = coffOffset + CoffHeaderSize;
        if ((long)optOffset + optionalHeaderSize > buffer.Length
            || optionalHeaderSize < 96)
        {
            throw CreateBadImage(imageLabel, "Optional header is truncated or exceeds file bounds.");
        }

        var isPe32Plus = ResolveMagic(ReadUInt16(buffer, optOffset), machine, optionalHeaderSize, imageLabel);
        var sizeOfImage = ReadUInt32(buffer, optOffset + 56);
        var sizeOfHeaders = ReadUInt32(buffer, optOffset + 60);
        var numRvaOffset = isPe32Plus ? optOffset + 108 : optOffset + 92;
        var dataDirOffset = isPe32Plus ? optOffset + 112 : optOffset + 96;
        var numDataDirs = ReadUInt32(buffer, numRvaOffset);
        if (numDataDirs > MaxDataDirectories
            || (long)dataDirOffset + ((long)numDataDirs * 8L) > (long)optOffset + optionalHeaderSize)
        {
            throw CreateBadImage(imageLabel, "Data directory count exceeds optional header bounds.");
        }

        var sectionTableOffset = optOffset + optionalHeaderSize;
        var sectionTableEnd = (long)sectionTableOffset + ((long)sectionCount * SectionHeaderSize);
        if (sizeOfHeaders > buffer.Length
            || sectionTableEnd > buffer.Length
            || sectionTableEnd > sizeOfHeaders
            || sizeOfImage < sizeOfHeaders)
        {
            throw CreateBadImage(imageLabel, "Section table or header sizes exceed PE image bounds.");
        }

        var sections = ParseSections(
            buffer,
            sectionTableOffset,
            sectionCount,
            sectionTableEnd,
            sizeOfHeaders,
            sizeOfImage,
            imageLabel);
#if NET8_0_OR_GREATER
        CrossValidateWithPeReader(buffer, imageLabel, validation, machine, isPe32Plus, sectionCount);
#endif
        return BuildHeaderInfo(
            buffer,
            coffOffset,
            optOffset,
            dataDirOffset,
            numDataDirs,
            machine,
            isPe32Plus,
            sizeOfImage,
            sizeOfHeaders,
            sections,
            imageLabel,
            validation);
    }

#pragma warning disable S107
    private static PeHeaderInfo BuildHeaderInfo(
        byte[] buffer,
        int coffOffset,
        int optOffset,
        int dataDirOffset,
        uint numDataDirs,
        ushort machine,
        bool isPe32Plus,
        uint sizeOfImage,
        uint sizeOfHeaders,
        PeSectionSlice[] sections,
        string imageLabel,
        PeImageValidationOptions validation)
#pragma warning restore S107
    {
        ReadDataDirectory(buffer, dataDirOffset, numDataDirs, index: 0, out var exportRva, out var exportSize);
        ReadDataDirectory(buffer, dataDirOffset, numDataDirs, index: 1, out var importRva, out var importSize);
        ReadDataDirectory(buffer, dataDirOffset, numDataDirs, index: 4, out var securityOffset, out var securitySize);
        ReadDataDirectory(
            buffer,
            dataDirOffset,
            numDataDirs,
            index: 13,
            out var delayImportRva,
            out var delayImportSize);
        ReadDataDirectory(buffer, dataDirOffset, numDataDirs, index: 14, out var corHeaderRva, out var corHeaderSize);

        if (validation.RequireUnmanagedImage
            && (corHeaderRva != 0 || corHeaderSize != 0))
        {
            throw CreateBadImage(
                imageLabel,
                "Managed CLI/CLR assemblies are not supported; expected an unmanaged native PE image.");
        }

        ValidateSecurityDirectory(buffer.Length, sizeOfHeaders, securityOffset, securitySize, imageLabel);
        return new PeHeaderInfo
        {
            Machine = machine,
            IsPe32Plus = isPe32Plus,
            SizeOfImage = sizeOfImage,
            SizeOfHeaders = sizeOfHeaders,
            TimeDateStampFileOffset = coffOffset + 4,
            CharacteristicsFileOffset = coffOffset + 18,
            CheckSumFileOffset = optOffset + 64,
            DllCharacteristicsFileOffset = optOffset + 70,
            ImportDirectoryEntryFileOffset = numDataDirs > 1 ? dataDirOffset + 8 : -1,
            SecurityDirectoryEntryFileOffset = numDataDirs > 4 ? dataDirOffset + 32 : -1,
            SecurityTableFileOffset = securityOffset,
            SecurityTableSize = securitySize,
            ExportTableRva = exportRva,
            ExportTableSize = exportSize,
            ImportTableRva = importRva,
            ImportTableSize = importSize,
            DelayImportTableRva = delayImportRva,
            DelayImportTableSize = delayImportSize,
            Sections = sections,
        };
    }

    private static void ValidateExpectedLength(
        byte[] buffer,
        string imageLabel,
        PeImageValidationOptions validation)
    {
        if (validation.ExpectedFileLength.HasValue
            && buffer.LongLength != validation.ExpectedFileLength.Value)
        {
            throw CreateBadImage(
                imageLabel,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Expected file length {0} bytes, but found {1} bytes.",
                    validation.ExpectedFileLength.Value,
                    buffer.LongLength));
        }
    }

    private static int ReadAndValidatePeSignatureOffset(
        byte[] buffer,
        string imageLabel)
    {
        if (buffer.Length < DosHeaderSize
            || ReadUInt16(buffer, 0) != DosSignature)
        {
            throw CreateBadImage(imageLabel, "Missing DOS 'MZ' header.");
        }

        var peOffset = unchecked((int)ReadUInt32(buffer, 0x3C));
        if (peOffset < DosHeaderSize
            || (long)peOffset + 4L + CoffHeaderSize > buffer.Length)
        {
            throw CreateBadImage(imageLabel, "Invalid or out-of-range e_lfanew PE header offset.");
        }

        if (ReadUInt32(buffer, peOffset) != PeSignature)
        {
            throw CreateBadImage(imageLabel, "Missing 'PE\\0\\0' signature.");
        }

        return peOffset;
    }

    private static void ValidateCoffFields(
        ushort machine,
        ushort sectionCount,
        ushort characteristics,
        string imageLabel,
        PeImageValidationOptions validation)
    {
        if (machine is not (PeMachine.I386 or PeMachine.Amd64 or PeMachine.Arm64))
        {
            throw CreateBadImage(
                imageLabel,
                string.Format(CultureInfo.InvariantCulture, "Unsupported COFF machine 0x{0:X4}.", machine));
        }

        if (validation.ExpectedMachine.HasValue
            && machine != validation.ExpectedMachine.Value)
        {
            throw CreateBadImage(
                imageLabel,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Expected COFF machine 0x{0:X4}, but found 0x{1:X4}.",
                    validation.ExpectedMachine.Value,
                    machine));
        }

        if (sectionCount == 0
            || sectionCount > MaxSections)
        {
            throw CreateBadImage(
                imageLabel,
                string.Format(CultureInfo.InvariantCulture, "Unsupported section count {0}.", sectionCount));
        }

        if (validation.RequireDll
            && (characteristics & ImageFileDllFlag) == 0)
        {
            throw CreateBadImage(imageLabel, "Image does not have the IMAGE_FILE_DLL characteristic set.");
        }
    }

    private static bool ResolveMagic(
        ushort magic,
        ushort machine,
        ushort optionalHeaderSize,
        string imageLabel)
    {
        if (magic == Pe32Magic)
        {
            if (machine != PeMachine.I386)
            {
                throw CreateBadImage(imageLabel, "PE32 optional header requires an I386 COFF machine.");
            }

            return false;
        }

        if (magic == Pe32PlusMagic)
        {
            if (optionalHeaderSize < 112)
            {
                throw CreateBadImage(imageLabel, "PE32+ optional header is truncated.");
            }

            if (machine == PeMachine.I386)
            {
                throw CreateBadImage(imageLabel, "PE32+ optional header is incompatible with I386 COFF machine.");
            }

            return true;
        }

        throw CreateBadImage(
            imageLabel,
            string.Format(CultureInfo.InvariantCulture, "Unsupported optional header magic 0x{0:X4}.", magic));
    }

    private static void ReadDataDirectory(
        byte[] buffer,
        int dataDirOffset,
        uint numDataDirs,
        uint index,
        out uint rva,
        out uint size)
    {
        if (index >= numDataDirs)
        {
            rva = 0;
            size = 0;
            return;
        }

        var entryOffset = dataDirOffset + ((int)index * 8);
        rva = ReadUInt32(buffer, entryOffset);
        size = ReadUInt32(buffer, entryOffset + 4);
    }

    private static void ValidateSecurityDirectory(
        int fileLength,
        uint sizeOfHeaders,
        uint securityOffset,
        uint securitySize,
        string imageLabel)
    {
        if (securityOffset == 0
            && securitySize == 0)
        {
            return;
        }

        if (securityOffset < sizeOfHeaders
            || securitySize == 0
            || (ulong)securityOffset + securitySize > (ulong)fileLength)
        {
            throw CreateBadImage(
                imageLabel,
                "Certificate table (IMAGE_DIRECTORY_ENTRY_SECURITY) is out of file bounds.");
        }
    }

    private static PeSectionSlice[] ParseSections(
        byte[] buffer,
        int sectionTableOffset,
        ushort sectionCount,
        long sectionTableEnd,
        uint sizeOfHeaders,
        uint sizeOfImage,
        string imageLabel)
    {
        var sections = new PeSectionSlice[sectionCount];
        for (var i = 0; i < sectionCount; i++)
        {
            var offset = sectionTableOffset + (i * SectionHeaderSize);
            var virtualSize = ReadUInt32(buffer, offset + 8);
            var virtualAddress = ReadUInt32(buffer, offset + 12);
            var sizeOfRawData = ReadUInt32(buffer, offset + 16);
            var pointerToRawData = ReadUInt32(buffer, offset + 20);

            if (virtualAddress < sizeOfHeaders
                || virtualAddress >= sizeOfImage
                || (ulong)virtualAddress + virtualSize > sizeOfImage)
            {
                throw CreateBadImage(
                    imageLabel,
                    string.Format(CultureInfo.InvariantCulture, "Section #{0} virtual range exceeds SizeOfImage.", i));
            }

            if (sizeOfRawData > 0
                && (pointerToRawData < (ulong)sectionTableEnd
                    || (ulong)pointerToRawData + sizeOfRawData > (ulong)buffer.Length))
            {
                throw CreateBadImage(
                    imageLabel,
                    string.Format(CultureInfo.InvariantCulture, "Section #{0} raw data range exceeds file bounds.", i));
            }

            sections[i] = new PeSectionSlice(virtualAddress, virtualSize, pointerToRawData, sizeOfRawData);
        }

        return sections;
    }

#if NET8_0_OR_GREATER
    private static void CrossValidateWithPeReader(
        byte[] buffer,
        string imageLabel,
        PeImageValidationOptions validation,
        ushort expectedMachine,
        bool expectedPe32Plus,
        ushort expectedSectionCount)
    {
        try
        {
            using var stream = new MemoryStream(buffer, writable: false);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var headers = reader.PEHeaders;
            if (headers.PEHeader is null)
            {
                throw CreateBadImage(imageLabel, "Missing PE optional header.");
            }

            if ((ushort)headers.CoffHeader.Machine != expectedMachine
                || (headers.PEHeader.Magic == PEMagic.PE32Plus) != expectedPe32Plus
                || headers.SectionHeaders.Length != expectedSectionCount)
            {
                throw CreateBadImage(imageLabel, "PEHeaders metadata disagreed with raw COFF header layout.");
            }

            if (validation.RequireDll
                && !headers.IsDll)
            {
                throw CreateBadImage(imageLabel, "Image is not marked as a dynamic-link library.");
            }

            if (validation.RequireUnmanagedImage
                && headers.CorHeader is not null)
            {
                throw CreateBadImage(imageLabel, "Managed CLI/CLR assemblies are not supported.");
            }
        }
        catch (BadImageFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            throw new BadImageFormatException($"Invalid PE image '{imageLabel}': {ex.Message}", ex);
        }
    }
#endif
}
