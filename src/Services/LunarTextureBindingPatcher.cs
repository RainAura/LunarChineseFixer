using System.Text;

namespace RainAura.LunarFontFixer.Services;

// Lunar 的文字显示列表会在 GlStateManager 记录的纹理状态与 OpenGL 实际状态不一致时绑定错误的 Unicode 页。
// 在每个缓存渲染段绑定字体纹理前先绑定 0，可强制下一次 bindTexture 真正提交到 OpenGL。
internal static class LunarTextureBindingPatcher
{
    private const string BridgeBindName = "bridge$bindTexture";
    private const string GlStateManagerName = "net/minecraft/client/renderer/GlStateManager";
    private const string BindTextureName = "bindTexture";
    private const string BindTextureDescriptor = "(I)V";

    public static JavaPatchState Inspect(byte[] input)
    {
        _ = TryPatch(input, out _, out var state);
        return state;
    }

    public static IReadOnlyList<string> GetReferencedClassNames(byte[] input)
    {
        try
        {
            var info = ParseConstantPool(input);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 1; i < info.Tags.Length; i++)
            {
                if (info.Tags[i] != 7) continue;
                var name = Utf8At(info, info.First[i]);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
            }
            return names.ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool TryPatch(byte[] input, out byte[] output, out JavaPatchState state)
    {
        output = input;
        try
        {
            var pool = ParseConstantPool(input);
            var target = FindBridgeBindCall(input, pool);
            if (target is null)
            {
                state = JavaPatchState.Unsupported;
                return false;
            }

            if (HasForcedBind(input, pool, target.CodeStart + target.CallOffset))
            {
                state = JavaPatchState.AlreadyPatched;
                return true;
            }

            var additions = new List<byte>();
            var addedEntries = 0;
            int AddEntry(Action<List<byte>> writer)
            {
                var index = pool.Count + addedEntries++;
                writer(additions);
                return index;
            }

            int EnsureUtf8(string value)
            {
                var existing = FindUtf8(pool, value);
                if (existing != 0) return existing;
                return AddEntry(bytes =>
                {
                    var encoded = Encoding.UTF8.GetBytes(value);
                    bytes.Add(1);
                    AddU2(bytes, encoded.Length);
                    bytes.AddRange(encoded);
                });
            }

            int EnsureClass(string name)
            {
                var nameIndex = EnsureUtf8(name);
                for (var i = 1; i < pool.Tags.Length; i++)
                    if (pool.Tags[i] == 7 && pool.First[i] == nameIndex) return i;
                return AddEntry(bytes =>
                {
                    bytes.Add(7);
                    AddU2(bytes, nameIndex);
                });
            }

            int EnsureNameAndType(string name, string descriptor)
            {
                var nameIndex = EnsureUtf8(name);
                var descriptorIndex = EnsureUtf8(descriptor);
                for (var i = 1; i < pool.Tags.Length; i++)
                    if (pool.Tags[i] == 12 && pool.First[i] == nameIndex && pool.Second[i] == descriptorIndex)
                        return i;
                return AddEntry(bytes =>
                {
                    bytes.Add(12);
                    AddU2(bytes, nameIndex);
                    AddU2(bytes, descriptorIndex);
                });
            }

            var classIndex = EnsureClass(GlStateManagerName);
            var nameAndTypeIndex = EnsureNameAndType(BindTextureName, BindTextureDescriptor);
            var methodReference = 0;
            for (var i = 1; i < pool.Tags.Length; i++)
                if (pool.Tags[i] == 10 && pool.First[i] == classIndex && pool.Second[i] == nameAndTypeIndex)
                    methodReference = i;
            if (methodReference == 0)
                methodReference = AddEntry(bytes =>
                {
                    bytes.Add(10);
                    AddU2(bytes, classIndex);
                    AddU2(bytes, nameAndTypeIndex);
                });

            var bytes = new List<byte>(input);
            if (additions.Count > 0)
            {
                bytes.InsertRange(pool.End, additions);
                WriteU2(bytes, 8, pool.Count + addedEntries);
            }

            var shift = additions.Count;
            var insertion = target.CallOffset;
            PatchCodeMetadata(bytes, target, shift, insertion, 4);
            WriteU2(bytes, target.MaxStackOffset + shift,
                Math.Min(ushort.MaxValue, ReadU2(input, target.MaxStackOffset) + 1));
            WriteU4(bytes, target.CodeLengthOffset + shift, target.CodeLength + 4);
            WriteU4(bytes, target.CodeAttributeLengthOffset + shift, target.CodeAttributeLength + 4);
            bytes.InsertRange(target.CodeStart + shift + insertion, new byte[]
            {
                0x03, // iconst_0
                0xB8, (byte)(methodReference >> 8), (byte)methodReference // invokestatic GlStateManager.bindTexture(0)
            });

            output = bytes.ToArray();
            state = JavaPatchState.Patchable;
            return true;
        }
        catch
        {
            state = JavaPatchState.Unsupported;
            return false;
        }
    }

    private static MethodTarget? FindBridgeBindCall(byte[] data, ConstantPoolInfo pool)
    {
        var cursor = pool.End + 6;
        var interfaces = ReadU2(data, cursor);
        cursor += 2 + interfaces * 2;
        cursor = SkipMembers(data, cursor);
        var methodCount = ReadU2(data, cursor);
        cursor += 2;
        for (var method = 0; method < methodCount; method++)
        {
            cursor += 6;
            var attributeCount = ReadU2(data, cursor);
            cursor += 2;
            for (var attribute = 0; attribute < attributeCount; attribute++)
            {
                var attributeStart = cursor;
                var attributeName = ReadU2(data, cursor);
                var attributeLength = checked((int)ReadU4(data, cursor + 2));
                if (Utf8At(pool, attributeName) == "Code")
                {
                    var codeLength = checked((int)ReadU4(data, cursor + 10));
                    var codeStart = cursor + 14;
                    foreach (var instruction in EnumerateInstructions(data, codeStart, codeLength))
                    {
                        if (data[codeStart + instruction] != 0xB9) continue;
                        var reference = ReadU2(data, codeStart + instruction + 1);
                        if (!IsMethod(pool, reference, BridgeBindName, null)) continue;
                        return ParseMethodTarget(data, pool, cursor, attributeLength, codeStart, codeLength, instruction);
                    }
                }
                cursor = attributeStart + 6 + attributeLength;
            }
        }
        return null;
    }

    private static MethodTarget ParseMethodTarget(byte[] data, ConstantPoolInfo pool, int codeAttribute,
        int attributeLength, int codeStart, int codeLength, int callOffset)
    {
        var exceptionCountOffset = codeStart + codeLength;
        var exceptionCount = ReadU2(data, exceptionCountOffset);
        var exceptionTableOffset = exceptionCountOffset + 2;
        var nestedCountOffset = exceptionTableOffset + exceptionCount * 8;
        var nestedCount = ReadU2(data, nestedCountOffset);
        var nested = new List<NestedAttribute>();
        var cursor = nestedCountOffset + 2;
        for (var i = 0; i < nestedCount; i++)
        {
            var length = checked((int)ReadU4(data, cursor + 2));
            nested.Add(new NestedAttribute(Utf8At(pool, ReadU2(data, cursor)), cursor + 6, length));
            cursor += 6 + length;
        }
        return new MethodTarget(codeAttribute + 6, codeAttribute + 10, codeAttribute + 2, attributeLength,
            codeStart, codeLength, callOffset, exceptionCountOffset, exceptionTableOffset, exceptionCount, nested);
    }

    private static void PatchCodeMetadata(List<byte> bytes, MethodTarget target, int shift, int insertion, int amount)
    {
        var original = bytes.ToArray();
        var codeStart = target.CodeStart + shift;
        foreach (var offset in EnumerateInstructions(original, codeStart, target.CodeLength))
        {
            var opcode = original[codeStart + offset];
            if ((opcode >= 0x99 && opcode <= 0xA8) || opcode is 0xC6 or 0xC7)
            {
                var oldDelta = ReadS2(original, codeStart + offset + 1);
                var newDelta = TransformPc(offset + oldDelta, insertion, amount) - TransformPc(offset, insertion, amount);
                WriteS2(bytes, codeStart + offset + 1, newDelta);
            }
            else if (opcode is 0xC8 or 0xC9)
            {
                var oldDelta = ReadS4(original, codeStart + offset + 1);
                var newDelta = TransformPc(offset + oldDelta, insertion, amount) - TransformPc(offset, insertion, amount);
                WriteS4(bytes, codeStart + offset + 1, newDelta);
            }
            else if (opcode is 0xAA or 0xAB)
            {
                PatchSwitch(bytes, original, codeStart, offset, insertion, amount, opcode == 0xAA);
            }
        }

        for (var i = 0; i < target.ExceptionCount; i++)
        {
            var entry = target.ExceptionTableOffset + shift + i * 8;
            for (var field = 0; field < 3; field++)
            {
                var value = ReadU2(original, entry + field * 2);
                WriteU2(bytes, entry + field * 2, TransformPc(value, insertion, amount));
            }
        }

        foreach (var attribute in target.NestedAttributes)
        {
            var dataOffset = attribute.DataOffset + shift;
            switch (attribute.Name)
            {
                case "LineNumberTable":
                    var lineCount = ReadU2(original, dataOffset);
                    for (var i = 0; i < lineCount; i++)
                    {
                        var position = dataOffset + 2 + i * 4;
                        WriteU2(bytes, position, TransformPc(ReadU2(original, position), insertion, amount));
                    }
                    break;
                case "LocalVariableTable":
                case "LocalVariableTypeTable":
                    var localCount = ReadU2(original, dataOffset);
                    for (var i = 0; i < localCount; i++)
                    {
                        var position = dataOffset + 2 + i * 10;
                        var start = ReadU2(original, position);
                        var end = start + ReadU2(original, position + 2);
                        var newStart = TransformPc(start, insertion, amount);
                        var newEnd = TransformPc(end, insertion, amount);
                        WriteU2(bytes, position, newStart);
                        WriteU2(bytes, position + 2, newEnd - newStart);
                    }
                    break;
                case "StackMapTable":
                    PatchStackMapTable(bytes, original, dataOffset, insertion, amount);
                    break;
            }
        }
    }

    private static void PatchStackMapTable(List<byte> bytes, byte[] original, int offset, int insertion, int amount)
    {
        var count = ReadU2(original, offset);
        var cursor = offset + 2;
        var previousOld = -1;
        var previousNew = -1;
        for (var i = 0; i < count; i++)
        {
            var framePosition = cursor;
            var type = original[cursor++];
            int delta;
            int deltaPosition = -1;
            var compactBase = -1;
            if (type <= 63) { delta = type; compactBase = 0; }
            else if (type <= 127) { delta = type - 64; compactBase = 64; cursor = SkipVerification(original, cursor); }
            else if (type == 247)
            {
                deltaPosition = cursor; delta = ReadU2(original, cursor); cursor += 2;
                cursor = SkipVerification(original, cursor);
            }
            else if (type is >= 248 and <= 251)
            {
                deltaPosition = cursor; delta = ReadU2(original, cursor); cursor += 2;
            }
            else if (type is >= 252 and <= 254)
            {
                deltaPosition = cursor; delta = ReadU2(original, cursor); cursor += 2;
                for (var local = 0; local < type - 251; local++) cursor = SkipVerification(original, cursor);
            }
            else if (type == 255)
            {
                deltaPosition = cursor; delta = ReadU2(original, cursor); cursor += 2;
                var locals = ReadU2(original, cursor); cursor += 2;
                for (var local = 0; local < locals; local++) cursor = SkipVerification(original, cursor);
                var stack = ReadU2(original, cursor); cursor += 2;
                for (var item = 0; item < stack; item++) cursor = SkipVerification(original, cursor);
            }
            else throw new InvalidDataException("不支持的 StackMap 帧类型。");

            var oldPc = previousOld + delta + 1;
            var newPc = TransformPc(oldPc, insertion, amount);
            var newDelta = newPc - previousNew - 1;
            if (compactBase >= 0)
            {
                if (newDelta is < 0 or > 63) throw new InvalidDataException("StackMap 紧凑帧无法安全扩展。");
                bytes[framePosition] = (byte)(compactBase + newDelta);
            }
            else WriteU2(bytes, deltaPosition, newDelta);
            previousOld = oldPc;
            previousNew = newPc;
        }
    }

    private static void PatchSwitch(List<byte> bytes, byte[] original, int codeStart, int offset,
        int insertion, int amount, bool table)
    {
        var cursor = codeStart + offset + 1;
        while ((cursor - codeStart) % 4 != 0) cursor++;
        void PatchOffset(int position)
        {
            var oldDelta = ReadS4(original, position);
            var newDelta = TransformPc(offset + oldDelta, insertion, amount) - TransformPc(offset, insertion, amount);
            WriteS4(bytes, position, newDelta);
        }
        PatchOffset(cursor);
        if (table)
        {
            var low = ReadS4(original, cursor + 4);
            var high = ReadS4(original, cursor + 8);
            for (var i = 0; i <= high - low; i++) PatchOffset(cursor + 12 + i * 4);
        }
        else
        {
            var pairs = ReadS4(original, cursor + 4);
            for (var i = 0; i < pairs; i++) PatchOffset(cursor + 12 + i * 8);
        }
    }

    private static bool HasForcedBind(byte[] data, ConstantPoolInfo pool, int call)
    {
        if (call < 4 || data[call - 4] != 0x03 || data[call - 3] != 0xB8) return false;
        return IsMethod(pool, ReadU2(data, call - 2), BindTextureName, BindTextureDescriptor, GlStateManagerName);
    }

    private static bool IsMethod(ConstantPoolInfo pool, int reference, string name, string? descriptor,
        string? owner = null)
    {
        if (reference <= 0 || reference >= pool.Tags.Length || pool.Tags[reference] is not (10 or 11)) return false;
        var classIndex = pool.First[reference];
        var nameAndType = pool.Second[reference];
        if (nameAndType <= 0 || pool.Tags[nameAndType] != 12) return false;
        if (Utf8At(pool, pool.First[nameAndType]) != name) return false;
        if (descriptor is not null && Utf8At(pool, pool.Second[nameAndType]) != descriptor) return false;
        return owner is null || Utf8At(pool, pool.First[classIndex]) == owner;
    }

    private static ConstantPoolInfo ParseConstantPool(byte[] data)
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
                    var length = ReadU2(data, offset); offset += 2;
                    utf8[index] = Encoding.UTF8.GetString(data, offset, length); offset += length;
                    break;
                case 3: case 4: offset += 4; break;
                case 5: case 6: offset += 8; index++; break;
                case 7: case 8: case 16: case 19: case 20:
                    first[index] = ReadU2(data, offset); offset += 2; break;
                case 9: case 10: case 11: case 12: case 17: case 18:
                    first[index] = ReadU2(data, offset); second[index] = ReadU2(data, offset + 2); offset += 4; break;
                case 15: offset += 3; break;
                default: throw new InvalidDataException("不支持的常量池类型。");
            }
        }
        return new ConstantPoolInfo(count, offset, tags, first, second, utf8);
    }

    private static IEnumerable<int> EnumerateInstructions(byte[] data, int codeStart, int codeLength)
    {
        var offset = 0;
        while (offset < codeLength)
        {
            yield return offset;
            var length = InstructionLength(data, codeStart, offset);
            if (length <= 0 || offset + length > codeLength) throw new InvalidDataException("Java 指令长度无效。");
            offset += length;
        }
    }

    private static int InstructionLength(byte[] data, int start, int offset)
    {
        var opcode = data[start + offset];
        if (opcode == 0xAA)
        {
            var cursor = offset + 1; while (cursor % 4 != 0) cursor++;
            var low = ReadS4(data, start + cursor + 4); var high = ReadS4(data, start + cursor + 8);
            return cursor - offset + 12 + checked((high - low + 1) * 4);
        }
        if (opcode == 0xAB)
        {
            var cursor = offset + 1; while (cursor % 4 != 0) cursor++;
            var pairs = ReadS4(data, start + cursor + 4);
            return cursor - offset + 8 + checked(pairs * 8);
        }
        if (opcode == 0xC4) return data[start + offset + 1] == 0x84 ? 6 : 4;
        if (opcode is 0xB9 or 0xBA or 0xC8 or 0xC9) return 5;
        if (opcode == 0xC5) return 4;
        if (opcode is 0x11 or 0x13 or 0x14 or 0x84 ||
            opcode is >= 0x99 and <= 0xA8 || opcode is >= 0xB2 and <= 0xB8 ||
            opcode is 0xBB or 0xBD or 0xC0 or 0xC1 or 0xC6 or 0xC7) return 3;
        if (opcode is 0x10 or 0x12 or 0xBC or 0xA9 ||
            opcode is >= 0x15 and <= 0x19 || opcode is >= 0x36 and <= 0x3A) return 2;
        return 1;
    }

    private static int SkipMembers(byte[] data, int cursor)
    {
        var count = ReadU2(data, cursor); cursor += 2;
        for (var member = 0; member < count; member++)
        {
            var attributes = ReadU2(data, cursor + 6); cursor += 8;
            for (var attribute = 0; attribute < attributes; attribute++)
                cursor += 6 + checked((int)ReadU4(data, cursor + 2));
        }
        return cursor;
    }

    private static int SkipVerification(byte[] data, int cursor) => data[cursor] is 7 or 8 ? cursor + 3 : cursor + 1;
    private static int TransformPc(int pc, int insertion, int amount) => pc >= insertion ? pc + amount : pc;
    private static string? Utf8At(ConstantPoolInfo info, int index) =>
        index > 0 && index < info.Utf8.Length ? info.Utf8[index] : null;
    private static int FindUtf8(ConstantPoolInfo info, string value)
    {
        for (var i = 1; i < info.Utf8.Length; i++) if (info.Utf8[i] == value) return i;
        return 0;
    }
    private static int ReadU2(IReadOnlyList<byte> data, int offset) => (data[offset] << 8) | data[offset + 1];
    private static short ReadS2(IReadOnlyList<byte> data, int offset) => unchecked((short)ReadU2(data, offset));
    private static uint ReadU4(IReadOnlyList<byte> data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    private static int ReadS4(IReadOnlyList<byte> data, int offset) => unchecked((int)ReadU4(data, offset));
    private static void AddU2(ICollection<byte> data, int value) { data.Add((byte)(value >> 8)); data.Add((byte)value); }
    private static void WriteU2(IList<byte> data, int offset, int value) { data[offset] = (byte)(value >> 8); data[offset + 1] = (byte)value; }
    private static void WriteU4(IList<byte> data, int offset, int value)
    { data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16); data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value; }
    private static void WriteS2(IList<byte> data, int offset, int value) => WriteU2(data, offset, value);
    private static void WriteS4(IList<byte> data, int offset, int value) => WriteU4(data, offset, value);

    private sealed class ConstantPoolInfo
    {
        public ConstantPoolInfo(int count, int end, byte[] tags, int[] first, int[] second, string?[] utf8)
        { Count = count; End = end; Tags = tags; First = first; Second = second; Utf8 = utf8; }
        public int Count { get; }
        public int End { get; }
        public byte[] Tags { get; }
        public int[] First { get; }
        public int[] Second { get; }
        public string?[] Utf8 { get; }
    }

    private sealed class NestedAttribute
    {
        public NestedAttribute(string? name, int dataOffset, int length)
        { Name = name; DataOffset = dataOffset; Length = length; }
        public string? Name { get; }
        public int DataOffset { get; }
        public int Length { get; }
    }

    private sealed class MethodTarget
    {
        public MethodTarget(int maxStackOffset, int codeLengthOffset, int codeAttributeLengthOffset,
            int codeAttributeLength, int codeStart, int codeLength, int callOffset, int exceptionCountOffset,
            int exceptionTableOffset, int exceptionCount, IReadOnlyList<NestedAttribute> nestedAttributes)
        {
            MaxStackOffset = maxStackOffset; CodeLengthOffset = codeLengthOffset;
            CodeAttributeLengthOffset = codeAttributeLengthOffset; CodeAttributeLength = codeAttributeLength;
            CodeStart = codeStart; CodeLength = codeLength; CallOffset = callOffset;
            ExceptionCountOffset = exceptionCountOffset; ExceptionTableOffset = exceptionTableOffset;
            ExceptionCount = exceptionCount; NestedAttributes = nestedAttributes;
        }
        public int MaxStackOffset { get; }
        public int CodeLengthOffset { get; }
        public int CodeAttributeLengthOffset { get; }
        public int CodeAttributeLength { get; }
        public int CodeStart { get; }
        public int CodeLength { get; }
        public int CallOffset { get; }
        public int ExceptionCountOffset { get; }
        public int ExceptionTableOffset { get; }
        public int ExceptionCount { get; }
        public IReadOnlyList<NestedAttribute> NestedAttributes { get; }
    }
}
