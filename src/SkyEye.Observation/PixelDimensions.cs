namespace SkyEye.Observation;

public static class PixelDimensions
{
    public static void Verify(IndexedFrame frame, ReadOnlySpan<byte> bytes)
    {
        if (frame.Encoding == PixelEncoding.Gray8)
        {
            if (bytes.Length != checked(frame.Width * frame.Height)) throw new InvalidDataException("Gray payload dimensions disagree.");
            return;
        }
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
