using System.Text;

namespace Zest.Core.Office;

/// <summary>
/// Recognizes password-encrypted OOXML files (xlsx, pptx, docx). Encrypting turns the zip package into a Compound File
/// (CFB) with an <c>EncryptionInfo</c> stream. Used to tell "wrong password" from "corrupt" where Office reports both
/// with the same error (Excel: 0x800A03EC).
/// </summary>
static class OfficeEncryption
{
    static readonly byte[] CfbSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    static readonly byte[] StreamName = Encoding.Unicode.GetBytes("EncryptionInfo\0");
    const int EntrySize = 128;  // CFB directory entries are 128 bytes, and every sector starts on a 128-byte boundary
    const int NameLengthOffset = 64;

    public static bool IsEncryptedPackage(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            var entry = new byte[EntrySize];
            if (file.ReadAtLeast(entry, EntrySize, throwOnEndOfStream: false) < EntrySize) return false;
            if (!entry.AsSpan(0, CfbSignature.Length).SequenceEqual(CfbSignature)) return false;
            // A directory entry names its stream in UTF-16 with the name's byte length (terminator included) at 64.
            while (file.ReadAtLeast(entry, EntrySize, throwOnEndOfStream: false) == EntrySize)
            {
                if (entry.AsSpan(0, StreamName.Length).SequenceEqual(StreamName)
                    && BitConverter.ToUInt16(entry, NameLengthOffset) == StreamName.Length)
                    return true;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
