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
    private const string ReloadName = "onResourceManagerReload";
    private const string ReloadDescriptor = "(Lnet/minecraft/client/resources/IResourceManager;)V";
    private const string ClearName = "clearCaches";
    private const string VoidDescriptor = "()V";
    private const string RenderName = "renderStringAtPos";
    private const string RenderDescriptor = "(Ljava/lang/String;ZI)V";

    public static JavaPatchState Inspect(byte[] input)
    {
        _ = TryPatch(input, out _, out var state);
        return state;
    }

    public static bool TryPatch(byte[] input, out byte[] output, out JavaPatchState state)
    {
        output = input;
        try
        {
            var info = Parse(input);
            if (info.TargetCodeStart < 0 || info.TargetCodeLength < 1 ||
                input[info.TargetCodeStart + info.TargetCodeLength - 1] != 0xB1)
            {
                state = JavaPatchState.Unsupported;
                return false;
            }

            var reloadPatched = ContainsClearCachesCall(input, info);
            var fallbackPatched = ContainsFallbackBypass(input, info);
            if (reloadPatched && fallbackPatched)
            {
                state = JavaPatchState.AlreadyPatched;
                return true;
            }

            var working = input;
            if (reloadPatched)
            {
                working = PatchFallbackBypass(working, info);
                output = working;
                state = JavaPatchState.Patchable;
                return true;
            }

            var additions = new List<byte>();
            var addedEntries = 0;
            var nameAndTypeIndex = FindNameAndType(info, ClearName, VoidDescriptor);
            if (nameAndTypeIndex == 0)
            {
                var nameIndex = FindUtf8(info, ClearName);
                var descriptorIndex = FindUtf8(info, VoidDescriptor);
                if (nameIndex == 0 || descriptorIndex == 0)
                {
                    state = JavaPatchState.Unsupported;
                    return false;
                }

                nameAndTypeIndex = info.ConstantPoolCount + addedEntries++;
                additions.Add(12);
                AddU2(additions, nameIndex);
                AddU2(additions, descriptorIndex);
            }

            var methodReference = FindMethodReference(info, info.ThisClassIndex, nameAndTypeIndex);
            if (methodReference == 0)
            {
                methodReference = info.ConstantPoolCount + addedEntries++;
                additions.Add(10);
                AddU2(additions, info.ThisClassIndex);
                AddU2(additions, nameAndTypeIndex);
            }

            var bytes = new List<byte>(input.Length + additions.Count + 4);
            bytes.AddRange(input);
            if (additions.Count > 0)
            {
                bytes.InsertRange(info.ConstantPoolEnd, additions);
                WriteU2(bytes, 8, info.ConstantPoolCount + addedEntries);
            }

            var shift = additions.Count;
            WriteU4(bytes, info.CodeAttributeLengthOffset + shift, info.CodeAttributeLength + 4);
            WriteU4(bytes, info.CodeLengthOffset + shift, info.TargetCodeLength + 4);
            var injectionOffset = info.TargetCodeStart + shift + info.TargetCodeLength - 1;
            bytes.InsertRange(injectionOffset, new byte[]
            {
                0x2A,
                0xB6, (byte)(methodReference >> 8), (byte)methodReference
            });

            working = bytes.ToArray();
            var patchedInfo = Parse(working);
            if (!ContainsFallbackBypass(working, patchedInfo))
                working = PatchFallbackBypass(working, patchedInfo);
            output = working;
            state = JavaPatchState.Patchable;
            return true;
        }
        catch
        {
            state = JavaPatchState.Unsupported;
            return false;
        }
    }

    private static byte[] PatchFallbackBypass(byte[] input, ClassInfo info)
    {
        if (info.RenderCodeStart < 0 || info.RenderCodeLength < 12)
            throw new InvalidDataException("未找到 Lunar 字体显示列表缓存入口。");

        var methodReference = FindMethodReference(info, info.ThisClassIndex,
            FindNameAndType(info, ClearName, VoidDescriptor));
        if (methodReference == 0)
            throw new InvalidDataException("未找到 Lunar 字体缓存清理调用。");

        var offset = info.RenderCodeStart;
        if (input[offset] != 0x2A || input[offset + 1] != 0xB4 ||
            input[offset + 4] != 0x2B || input[offset + 5] != 0x1D ||
            input[offset + 6] != 0x1C || input[offset + 7] != 0xB6 ||
            input[offset + 10] != 0x3A || input[offset + 11] != 0x04)
            throw new InvalidDataException("Lunar 字体缓存入口结构不受支持。");

        var bytes = (byte[])input.Clone();
        var replacement = new byte[]
        {
            0x2A,
            0xB6, (byte)(methodReference >> 8), (byte)methodReference,
            0x01,
            0x3A, 0x04,
            0x00, 0x00, 0x00, 0x00, 0x00
        };
        Buffer.BlockCopy(replacement, 0, bytes, offset, replacement.Length);
        return bytes;
    }

    private static bool ContainsFallbackBypass(byte[] data, ClassInfo info)
    {
        if (info.RenderCodeStart < 0 || info.RenderCodeLength < 7) return false;
        var offset = info.RenderCodeStart;
        if (data[offset] != 0x2A || data[offset + 1] != 0xB6 ||
            data[offset + 4] != 0x01 || data[offset + 5] != 0x3A || data[offset + 6] != 0x04)
            return false;
        var reference = ReadU2(data, offset + 2);
        return reference > 0 && reference < info.Tags.Length && info.Tags[reference] == 10 &&
               info.First[reference] == info.ThisClassIndex &&
               IsNameAndType(info, info.Second[reference], ClearName, VoidDescriptor);
    }

    private static bool ContainsClearCachesCall(byte[] data, ClassInfo info)
    {
        var end = info.TargetCodeStart + info.TargetCodeLength - 3;
        for (var offset = info.TargetCodeStart; offset < end; offset++)
        {
            if (data[offset] != 0x2A || data[offset + 1] != 0xB6) continue;
            var index = ReadU2(data, offset + 2);
            if (index <= 0 || index >= info.Tags.Length || info.Tags[index] != 10) continue;
            var classIndex = info.First[index];
            var nameAndType = info.Second[index];
            if (classIndex == info.ThisClassIndex && IsNameAndType(info, nameAndType, ClearName, VoidDescriptor))
                return true;
        }
        return false;
    }

    private static ClassInfo Parse(byte[] data)
    {
        if (data.Length < 16 || ReadU4(data, 0) != 0xCAFEBABE)
            throw new InvalidDataException("不是有效的 Java 类文件。");

        var count = ReadU2(data, 8);
        var tags = new byte[count];
        var first = new int[count];
        var second = new int[count];
        var utf8 = new string?[count];
        var offset = 10;
        for (var index = 1; index < count; index++)
        {
            var tag = data[offset++];
            tags[index] = tag;
            switch (tag)
            {
                case 1:
                    var length = ReadU2(data, offset);
                    offset += 2;
                    utf8[index] = Encoding.UTF8.GetString(data, offset, length);
                    offset += length;
                    break;
                case 3:
                case 4:
                    offset += 4;
                    break;
                case 5:
                case 6:
                    offset += 8;
                    index++;
                    break;
                case 7:
                case 8:
                case 16:
                case 19:
                case 20:
                    first[index] = ReadU2(data, offset);
                    offset += 2;
                    break;
                case 9:
                case 10:
                case 11:
                case 12:
                case 17:
                case 18:
                    first[index] = ReadU2(data, offset);
                    second[index] = ReadU2(data, offset + 2);
                    offset += 4;
                    break;
                case 15:
                    offset += 3;
                    break;
                default:
                    throw new InvalidDataException("不支持的常量池类型。");
            }
        }

        var info = new ClassInfo(count, offset, tags, first, second, utf8);
        var cursor = offset + 2;
        info.ThisClassIndex = ReadU2(data, cursor);
        cursor += 4;
        var interfaceCount = ReadU2(data, cursor);
        cursor += 2 + interfaceCount * 2;
        cursor = SkipMembers(data, cursor);

        var methodCount = ReadU2(data, cursor);
        cursor += 2;
        for (var method = 0; method < methodCount; method++)
        {
            cursor += 2;
            var nameIndex = ReadU2(data, cursor);
            var descriptorIndex = ReadU2(data, cursor + 2);
            var attributeCount = ReadU2(data, cursor + 4);
            cursor += 6;
            var isTarget = utf8[nameIndex] == ReloadName && utf8[descriptorIndex] == ReloadDescriptor;
            var isRenderTarget = utf8[nameIndex] == RenderName && utf8[descriptorIndex] == RenderDescriptor;
            for (var attribute = 0; attribute < attributeCount; attribute++)
            {
                var attributeStart = cursor;
                var attributeName = ReadU2(data, cursor);
                var attributeLength = checked((int)ReadU4(data, cursor + 2));
                if (isTarget && utf8[attributeName] == "Code")
                {
                    info.CodeAttributeLengthOffset = cursor + 2;
                    info.CodeAttributeLength = attributeLength;
                    info.CodeLengthOffset = cursor + 10;
                    info.TargetCodeLength = checked((int)ReadU4(data, info.CodeLengthOffset));
                    info.TargetCodeStart = cursor + 14;
                }
                if (isRenderTarget && utf8[attributeName] == "Code")
                {
                    info.RenderCodeLength = checked((int)ReadU4(data, cursor + 10));
                    info.RenderCodeStart = cursor + 14;
                }
                cursor = attributeStart + 6 + attributeLength;
            }
        }
        return info;
    }

    private static int SkipMembers(byte[] data, int cursor)
    {
        var count = ReadU2(data, cursor);
        cursor += 2;
        for (var member = 0; member < count; member++)
        {
            var attributes = ReadU2(data, cursor + 6);
            cursor += 8;
            for (var attribute = 0; attribute < attributes; attribute++)
                cursor += 6 + checked((int)ReadU4(data, cursor + 2));
        }
        return cursor;
    }

    private static int FindUtf8(ClassInfo info, string value)
    {
        for (var i = 1; i < info.Utf8.Length; i++)
            if (info.Utf8[i] == value) return i;
        return 0;
    }

    private static int FindNameAndType(ClassInfo info, string name, string descriptor)
    {
        for (var i = 1; i < info.Tags.Length; i++)
            if (info.Tags[i] == 12 && IsNameAndType(info, i, name, descriptor)) return i;
        return 0;
    }

    private static bool IsNameAndType(ClassInfo info, int index, string name, string descriptor) =>
        index > 0 && index < info.Tags.Length && info.Tags[index] == 12 &&
        info.Utf8[info.First[index]] == name && info.Utf8[info.Second[index]] == descriptor;

    private static int FindMethodReference(ClassInfo info, int classIndex, int nameAndTypeIndex)
    {
        for (var i = 1; i < info.Tags.Length; i++)
            if (info.Tags[i] == 10 && info.First[i] == classIndex && info.Second[i] == nameAndTypeIndex) return i;
        return 0;
    }

    private static int ReadU2(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];
    private static uint ReadU4(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static void AddU2(ICollection<byte> data, int value)
    {
        data.Add((byte)(value >> 8));
        data.Add((byte)value);
    }

    private static void WriteU2(IList<byte> data, int offset, int value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private static void WriteU4(IList<byte> data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private sealed class ClassInfo
    {
        public ClassInfo(int count, int end, byte[] tags, int[] first, int[] second, string?[] utf8)
        {
            ConstantPoolCount = count;
            ConstantPoolEnd = end;
            Tags = tags;
            First = first;
            Second = second;
            Utf8 = utf8;
        }

        public int ConstantPoolCount { get; }
        public int ConstantPoolEnd { get; }
        public byte[] Tags { get; }
        public int[] First { get; }
        public int[] Second { get; }
        public string?[] Utf8 { get; }
        public int ThisClassIndex { get; set; }
        public int CodeAttributeLengthOffset { get; set; } = -1;
        public int CodeAttributeLength { get; set; }
        public int CodeLengthOffset { get; set; } = -1;
        public int TargetCodeStart { get; set; } = -1;
        public int TargetCodeLength { get; set; }
        public int RenderCodeStart { get; set; } = -1;
        public int RenderCodeLength { get; set; }
    }
}
