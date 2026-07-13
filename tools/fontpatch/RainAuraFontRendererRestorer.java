package rainaura.fontpatch;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.FieldInsnNode;
import org.objectweb.asm.tree.FieldNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.IntInsnNode;
import org.objectweb.asm.tree.JumpInsnNode;
import org.objectweb.asm.tree.LabelNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.AbstractInsnNode;
import org.objectweb.asm.tree.VarInsnNode;
import org.objectweb.asm.tree.InsnNode;

/**
 * 将经过验证的 1.8.9 即时字体绘制方法移植到 Lunar FontRenderer。
 *
 * <p>只替换绘制方法体，保留 Lunar 已注入的字段、接口和其他功能。</p>
 *
 * @author RainAura
 */
public final class RainAuraFontRendererRestorer {
    private static final String RENDER_CHAR = "renderChar(CZ)F";
    private static final String RENDER_DEFAULT = "renderDefaultChar(IZ)F";
    private static final String LOAD_GLYPH = "loadGlyphTexture(I)V";
    private static final String RENDER_UNICODE = "renderUnicodeChar(CZ)F";
    private static final String RENDER_STRING = "renderStringAtPos(Ljava/lang/String;Z)V";
    private static final String RENDER_STRING_CACHED = "renderStringAtPos(Ljava/lang/String;ZI)V";
    private static final String TICK = "tick()V";
    private static final String STARTUP_REFRESH_FIELD = "rainAura$startupFontRefreshDone";
    private static final String WORLD_TICKS_FIELD = "rainAura$worldFontRefreshTicks";
    private static final String STARTUP_REFRESH_METHOD = "rainAura$refreshUnicodeTextures";

    private RainAuraFontRendererRestorer() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length != 3) {
            throw new IllegalArgumentException("用法：<Lunar FontRenderer.class> <原版方法模板.class> <输出.class>");
        }

        Path targetPath = Path.of(args[0]);
        Path donorPath = Path.of(args[1]);
        Path outputPath = Path.of(args[2]);
        byte[] result = restore(Files.readAllBytes(targetPath), Files.readAllBytes(donorPath));
        Files.createDirectories(outputPath.toAbsolutePath().getParent());
        Files.write(outputPath, result);
        System.out.println("已恢复 1.8.9 即时字体绘制方法：" + outputPath);
    }

    public static byte[] restore(byte[] targetBytes, byte[] donorBytes) {
        ClassNode target = read(targetBytes);
        ClassNode donor = read(donorBytes);
        if (!target.name.equals(donor.name)) {
            throw new IllegalArgumentException("模板类与目标类名称不一致。");
        }

        Map<String, MethodNode> donorMethods = new HashMap<>();
        for (MethodNode method : donor.methods) {
            donorMethods.put(key(method), method);
        }

        List<String> wanted = List.of(
                RENDER_CHAR, RENDER_DEFAULT, LOAD_GLYPH, RENDER_UNICODE, RENDER_STRING);
        List<String> replaced = new ArrayList<>();
        for (int index = 0; index < target.methods.size(); index++) {
            MethodNode current = target.methods.get(index);
            String key = key(current);
            if (wanted.contains(key)) {
                MethodNode source = donorMethods.get(key);
                if (source == null) {
                    throw new IllegalArgumentException("模板缺少方法：" + key);
                }
                target.methods.set(index, transplant(current, source));
                replaced.add(key);
            } else if (RENDER_STRING_CACHED.equals(key)) {
                target.methods.set(index, createImmediateDelegate(current, target.name));
                replaced.add(key);
            }
        }

        List<String> missing = new ArrayList<>(wanted);
        missing.add(RENDER_STRING_CACHED);
        missing.removeAll(replaced);
        if (!missing.isEmpty()) {
            throw new IllegalArgumentException("目标类缺少必要方法：" + String.join("，", missing));
        }

        installStartupTextureRefresh(target);

        ClassWriter writer = new SafeClassWriter(ClassWriter.COMPUTE_FRAMES | ClassWriter.COMPUTE_MAXS);
        target.accept(writer);
        return writer.toByteArray();
    }

    private static ClassNode read(byte[] bytes) {
        ClassNode node = new ClassNode(Opcodes.ASM9);
        new ClassReader(bytes).accept(node, ClassReader.EXPAND_FRAMES);
        return node;
    }

    private static MethodNode transplant(MethodNode target, MethodNode source) {
        MethodNode replacement = new MethodNode(
                Opcodes.ASM9,
                target.access,
                target.name,
                target.desc,
                target.signature,
                target.exceptions == null ? null : target.exceptions.toArray(String[]::new));
        source.accept(replacement);
        replacement.access = target.access;
        replacement.name = target.name;
        replacement.desc = target.desc;
        replacement.signature = target.signature;
        replacement.exceptions = target.exceptions == null
                ? new ArrayList<>()
                : new ArrayList<>(target.exceptions);
        restoreDirectOpenGlCalls(replacement);
        return replacement;
    }

    private static void restoreDirectOpenGlCalls(MethodNode method) {
        for (AbstractInsnNode instruction = method.instructions.getFirst();
             instruction != null;
             instruction = instruction.getNext()) {
            if (!(instruction instanceof MethodInsnNode call)) {
                continue;
            }
            if (!"net/minecraft/client/renderer/GlStateManager".equals(call.owner)) {
                continue;
            }
            if (call.name.equals("glBegin") || call.name.equals("glTexCoord2f")
                    || call.name.equals("glVertex3f") || call.name.equals("glEnd")) {
                call.owner = "org/lwjgl/opengl/GL11";
            }
        }
    }

    private static void installStartupTextureRefresh(ClassNode target) {
        boolean hasField = target.fields.stream().anyMatch(field -> STARTUP_REFRESH_FIELD.equals(field.name));
        boolean hasMethod = target.methods.stream().anyMatch(method ->
                STARTUP_REFRESH_METHOD.equals(method.name) && "()V".equals(method.desc));
        if (hasField || hasMethod) {
            throw new IllegalArgumentException("目标类已包含启动字体刷新补丁。");
        }

        target.fields.add(new FieldNode(
                Opcodes.ACC_PRIVATE,
                STARTUP_REFRESH_FIELD,
                "Z",
                null,
                null));
        target.fields.add(new FieldNode(
                Opcodes.ACC_PRIVATE,
                WORLD_TICKS_FIELD,
                "I",
                null,
                null));
        target.methods.add(createStartupRefreshMethod(target.name));

        MethodNode tick = target.methods.stream()
                .filter(method -> TICK.equals(key(method)))
                .findFirst()
                .orElseThrow(() -> new IllegalArgumentException("目标类缺少字体维护方法 tick()。"));
        LabelNode continueTick = new LabelNode();
        InsnList injection = new InsnList();
        injection.add(new VarInsnNode(Opcodes.ALOAD, 0));
        injection.add(new FieldInsnNode(Opcodes.GETFIELD, target.name, STARTUP_REFRESH_FIELD, "Z"));
        injection.add(new JumpInsnNode(Opcodes.IFNE, continueTick));
        injection.add(new VarInsnNode(Opcodes.ALOAD, 0));
        injection.add(new MethodInsnNode(
                Opcodes.INVOKESPECIAL,
                target.name,
                STARTUP_REFRESH_METHOD,
                "()V",
                false));
        injection.add(continueTick);
        tick.instructions.insert(injection);
    }

    private static MethodNode createStartupRefreshMethod(String owner) {
        MethodNode method = new MethodNode(
                Opcodes.ASM9,
                Opcodes.ACC_PRIVATE,
                STARTUP_REFRESH_METHOD,
                "()V",
                null,
                null);
        InsnList code = method.instructions;
        LabelNode worldReady = new LabelNode();
        LabelNode done = new LabelNode();
        code.add(new MethodInsnNode(
                Opcodes.INVOKESTATIC,
                "net/minecraft/client/Minecraft",
                "getMinecraft",
                "()Lnet/minecraft/client/Minecraft;",
                false));
        code.add(new VarInsnNode(Opcodes.ASTORE, 1));
        code.add(new VarInsnNode(Opcodes.ALOAD, 1));
        code.add(new FieldInsnNode(
                Opcodes.GETFIELD,
                "net/minecraft/client/Minecraft",
                "theWorld",
                "Lnet/minecraft/client/multiplayer/WorldClient;"));
        code.add(new JumpInsnNode(Opcodes.IFNONNULL, worldReady));
        code.add(new VarInsnNode(Opcodes.ALOAD, 0));
        code.add(new InsnNode(Opcodes.ICONST_0));
        code.add(new FieldInsnNode(Opcodes.PUTFIELD, owner, WORLD_TICKS_FIELD, "I"));
        code.add(new InsnNode(Opcodes.RETURN));
        code.add(worldReady);
        code.add(new VarInsnNode(Opcodes.ALOAD, 0));
        code.add(new InsnNode(Opcodes.DUP));
        code.add(new FieldInsnNode(Opcodes.GETFIELD, owner, WORLD_TICKS_FIELD, "I"));
        code.add(new InsnNode(Opcodes.ICONST_1));
        code.add(new InsnNode(Opcodes.IADD));
        code.add(new FieldInsnNode(Opcodes.PUTFIELD, owner, WORLD_TICKS_FIELD, "I"));
        code.add(new VarInsnNode(Opcodes.ALOAD, 0));
        code.add(new FieldInsnNode(Opcodes.GETFIELD, owner, WORLD_TICKS_FIELD, "I"));
        code.add(new IntInsnNode(Opcodes.BIPUSH, 100));
        code.add(new JumpInsnNode(Opcodes.IF_ICMPLT, done));
        code.add(new VarInsnNode(Opcodes.ALOAD, 0));
        code.add(new InsnNode(Opcodes.ICONST_1));
        code.add(new FieldInsnNode(Opcodes.PUTFIELD, owner, STARTUP_REFRESH_FIELD, "Z"));
        code.add(new VarInsnNode(Opcodes.ALOAD, 1));
        code.add(new MethodInsnNode(
                Opcodes.INVOKEVIRTUAL,
                "net/minecraft/client/Minecraft",
                "refreshResources",
                "()V",
                false));
        code.add(done);
        code.add(new InsnNode(Opcodes.RETURN));
        return method;
    }

    private static MethodNode createImmediateDelegate(MethodNode target, String owner) {
        MethodNode replacement = new MethodNode(
                Opcodes.ASM9,
                target.access,
                target.name,
                target.desc,
                target.signature,
                target.exceptions == null ? null : target.exceptions.toArray(String[]::new));
        InsnList code = replacement.instructions;
        code.add(new VarInsnNode(Opcodes.ALOAD, 0));
        code.add(new VarInsnNode(Opcodes.ALOAD, 1));
        code.add(new VarInsnNode(Opcodes.ILOAD, 2));
        code.add(new MethodInsnNode(
                Opcodes.INVOKEVIRTUAL,
                owner,
                "renderStringAtPos",
                "(Ljava/lang/String;Z)V",
                false));
        code.add(new InsnNode(Opcodes.RETURN));
        return replacement;
    }

    private static String key(MethodNode method) {
        return method.name + method.desc;
    }

    private static final class SafeClassWriter extends ClassWriter {
        private SafeClassWriter(int flags) {
            super(flags);
        }

        @Override
        protected String getCommonSuperClass(String type1, String type2) {
            return "java/lang/Object";
        }
    }
}
