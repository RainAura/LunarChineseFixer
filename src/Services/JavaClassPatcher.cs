using System.Text;

namespace RainAura.LunarFontFixer.Services;

internal enum JavaPatchState
{
    Patchable,
    AlreadyPatched,
    Unsupported
}

internal static class JavaClassPatcher
{
    private const string FinalMarker = "rainAura$worldFontRefreshTicks";

    public static JavaPatchState Inspect(byte[] input)
    {
        if (input.Length < 16 || ReadU4(input, 0) != 0xCAFEBABE)
            return JavaPatchState.Unsupported;
        if (ContainsUtf8(input, FinalMarker))
            return JavaPatchState.AlreadyPatched;

        var required = new[]
        {
            "renderChar",
            "renderDefaultChar",
            "loadGlyphTexture",
            "renderUnicodeChar",
            "renderStringAtPos",
            "tick",
            "clearCaches"
        };
        return required.All(name => ContainsUtf8(input, name))
            ? JavaPatchState.Patchable
            : JavaPatchState.Unsupported;
    }

    private static bool ContainsUtf8(byte[] input, string value)
    {
        var needle = Encoding.UTF8.GetBytes(value);
        for (var offset = 0; offset <= input.Length - needle.Length; offset++)
        {
            var match = true;
            for (var index = 0; index < needle.Length; index++)
                if (input[offset + index] != needle[index]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }

    private static uint ReadU4(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];
}
