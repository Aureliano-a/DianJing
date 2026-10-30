namespace NexusClick.Engine;

public static class KeyboardInputFlags
{
    /// <summary>Flags for common E0-prefixed keys; override when a virtual key is ambiguous, such as keypad Enter.</summary>
    public static uint ForVirtualKey(int virtualKey, bool keyUp, bool? extendedOverride = null)
    {
        if (virtualKey is < 1 or > 254) throw new ArgumentOutOfRangeException(nameof(virtualKey));
        bool extended = extendedOverride ?? (virtualKey is 0x03 or >= 0x21 and <= 0x28 or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0xA3 or 0xA5);
        return (extended ? 1u : 0u) | (keyUp ? 2u : 0u);
    }
}
