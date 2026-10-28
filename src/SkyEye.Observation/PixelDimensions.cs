namespace SkyEye.Observation;

public static class PixelDimensions
{
    public static void Verify(IndexedFrame frame, ReadOnlySpan<byte> bytes)
    {
        switch (frame.Encoding)
        {
            case PixelEncoding.Gray8:
                if (bytes.Length != checked(frame.Width * frame.Height)) throw new InvalidDataException("Gray payload dimensions disagree.");
                return;
            case PixelEncoding.Png:
                VerifyPng(frame, bytes);
                return;
            case PixelEncoding.Jpeg:
                VerifyJpeg(frame, bytes);
                return;
            default:
                throw new InvalidDataException("Unsupported pixel encoding.");
        }
    }

    static void VerifyPng(IndexedFrame frame, ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature) || ReadBigEndianUInt32(bytes.Slice(8, 4)) != 13
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Expected PNG payload with an IHDR header.");
        uint width = ReadBigEndianUInt32(bytes.Slice(16, 4));
        uint height = ReadBigEndianUInt32(bytes.Slice(20, 4));
        if (width != frame.Width || height != frame.Height)
            throw new InvalidDataException("PNG dimensions disagree with the paired receipt.");
    }

    static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    static void VerifyJpeg(IndexedFrame frame, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8) throw new InvalidDataException("Expected JPEG payload.");
        int offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset++] != 0xff) throw new InvalidDataException("Invalid JPEG marker.");
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) break;
            byte marker = bytes[offset++];
            if (marker is 0xd9 or 0xda) break;
            if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
            if (offset + 2 > bytes.Length) break;
            int length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || offset + length > bytes.Length) throw new InvalidDataException("Truncated JPEG segment.");
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                if (length < 8) throw new InvalidDataException("Invalid JPEG frame header.");
                int height = (bytes[offset + 3] << 8) | bytes[offset + 4], width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                if (width != frame.Width || height != frame.Height) throw new InvalidDataException("JPEG dimensions disagree with the paired receipt.");
                return;
            }
            offset += length;
        }
        throw new InvalidDataException("JPEG dimensions are unavailable.");
    }
}
