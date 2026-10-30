/*
 * Copyright (C) 2026 ghostflyby
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace JApp.Launcher;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

/// <summary>In-process JVM launch on Windows — the counterpart of the Unix
/// exec path: the process keeps its pid, exit code and console-ctrl
/// semantics. jvm.dll is loaded directly, the VM is created on a dedicated
/// thread whose stack honors -Xss (mirroring java.exe's
/// ContinueInNewThread), and the boot module's main is invoked through JNI.
/// The java.exe launcher behavior this bypasses is emulated:
/// JDK_JAVA_OPTIONS is read and reported, --module-path is passed as the
/// long-form VM option, sun.java.command and sun.java.launcher are set, and
/// --module is replaced by our own FindClass. Any failure before main is
/// invoked returns null and the caller falls back to spawning
/// java.exe.</summary>
internal static class JniLauncher
{
    private const int JniVersion18 = 0x00010008;
    private const string BootMainClass = "org/glavo/japp/boot/JAppBootLauncher";

    internal sealed record LaunchPlan(
        IReadOnlyList<string> VmOptions,
        string ModulePath,
        string Module,
        IReadOnlyList<string> AppArgs);

    /// <summary>Splits the java command line into the JNI launch shape:
    /// everything up to `--module` is a VM option except the `--module-path`
    /// pair (whose value becomes the module path), and everything after the
    /// `--module` pair is an application argument. Null when the command is
    /// not the shape the launcher generates.</summary>
    internal static LaunchPlan? TryTranslate(IReadOnlyList<string> commandArgs)
    {
        var vmOptions = new List<string>();
        List<string>? appArgs = null;
        string? modulePath = null;
        string? module = null;

        for (var i = 1; i < commandArgs.Count; i++)
        {
            if (module is not null)
            {
                appArgs ??= [];
                appArgs.Add(commandArgs[i]);
                continue;
            }

            switch (commandArgs[i])
            {
                case "--module-path":
                    if (i + 1 >= commandArgs.Count)
                        return null;
                    modulePath = commandArgs[++i];
                    break;
                case "--module":
                    if (i + 1 >= commandArgs.Count)
                        return null;
                    module = commandArgs[++i];
                    break;
                default:
                    vmOptions.Add(commandArgs[i]);
                    break;
            }
        }

        return modulePath is null || module is null
            ? null
            : new LaunchPlan(vmOptions, modulePath, module, (IReadOnlyList<string>?)appArgs ?? []);
    }

    /// <summary>Adds the emulated launcher behavior: JDK_JAVA_OPTIONS is
    /// reported and prepended (java.exe semantics), --module-path becomes the
    /// jdk.module.path property the module bootstrap reads, and the launcher
    /// identity properties apps expect (Spring Boot reads sun.java.command)
    /// are set.</summary>
    internal static IReadOnlyList<string> PrepareVmOptions(LaunchPlan plan)
    {
        var options = new List<string>();

        var fromEnvironment = Environment.GetEnvironmentVariable("JDK_JAVA_OPTIONS");
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            Console.Error.WriteLine("NOTE: Picked up JDK_JAVA_OPTIONS: " + fromEnvironment);
            options.AddRange(SplitTokens(fromEnvironment));
        }

        options.AddRange(plan.VmOptions);
        // The VM recognizes the long-form module option and derives the
        // internal jdk.module.path property itself; a -Djdk.module.* system
        // property is reserved and silently ignored.
        options.Add("--module-path=" + plan.ModulePath);
        options.Add("-Dsun.java.launcher=SUN_STANDARD");
        options.Add("-Dsun.java.command=" + string.Join(' ', plan.AppArgs));

        return options;
    }

    /// <summary>Whitespace split honoring double quotes, like the launcher's
    /// environment-variable tokenization.</summary>
    internal static IReadOnlyList<string> SplitTokens(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var ch in value)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(ch);
                hasToken = true;
            }
        }

        if (hasToken)
            tokens.Add(current.ToString());

        return tokens;
    }

    /// <summary>The stack size of the main thread from `-Xss&lt;size&gt;`
    /// (attached form), or 0 for the platform default.</summary>
    internal static long TryParseStackSize(IReadOnlyList<string> options)
    {
        foreach (var option in options)
        {
            if (!option.StartsWith("-Xss", StringComparison.Ordinal) || option.Length == 4)
                continue;

            var number = option[4..];
            long multiplier = 1;
            switch (char.ToLowerInvariant(number[^1]))
            {
                case 'k':
                    multiplier = 1024;
                    number = number[..^1];
                    break;
                case 'm':
                    multiplier = 1024 * 1024;
                    number = number[..^1];
                    break;
                case 'g':
                    multiplier = 1024L * 1024 * 1024;
                    number = number[..^1];
                    break;
            }

            if (long.TryParse(number, out var size) && size > 0)
                return size * multiplier;
        }

        return 0;
    }

    [SupportedOSPlatform("windows")]
    public static int? TryRun(IReadOnlyList<string> commandArgs)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var plan = TryTranslate(commandArgs);
        if (plan is null)
            return null;

        var javaHome = Path.GetDirectoryName(Path.GetDirectoryName(commandArgs[0]));
        var jvmDll = FindJvmLibrary(javaHome);
        if (jvmDll is null)
            return null;

        var options = PrepareVmOptions(plan);
        var stackSize = (int)Math.Clamp(TryParseStackSize(options), 0, int.MaxValue);

        int? result = null;
        var thread = new Thread(() => result = RunInProcess(jvmDll, options, plan.AppArgs), stackSize);
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>Locates the JVM shared library of a java home. Windows loads
    /// jvm.dll (bin\server; JDK 8 runtimes live under jre\bin); Unix JVMs put
    /// libjvm under lib/server. The JNI function table layout is
    /// platform-independent, so the Unix paths double as the verification
    /// channel for the JNIEnv indices the Windows production path uses.</summary>
    internal static string? FindJvmLibrary(string? javaHome)
    {
        if (string.IsNullOrEmpty(javaHome))
            return null;

        string[] candidates =
        [
            Path.Combine(javaHome, "bin", "server", "jvm.dll"),
            Path.Combine(javaHome, "bin", "client", "jvm.dll"),
            Path.Combine(javaHome, "jre", "bin", "server", "jvm.dll"),
            Path.Combine(javaHome, "jre", "bin", "client", "jvm.dll"),
            Path.Combine(javaHome, "lib", "server", "libjvm.dylib"),
            Path.Combine(javaHome, "lib", "server", "libjvm.so"),
            Path.Combine(javaHome, "lib", "client", "libjvm.dylib"),
            Path.Combine(javaHome, "jre", "lib", "server", "libjvm.dylib"),
            Path.Combine(javaHome, "jre", "lib", "client", "libjvm.dylib"),
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Best-effort for the Windows production path: any failure
    /// before main is invoked falls back to spawning java.exe, so nothing
    /// here may escape. The integration test drives the same code on Unix to
    /// verify the function table layout against a real JVM. The VM is never
    /// destroyed — DestroyJavaVM crashes under .NET hosts (its teardown is
    /// only reliable when the host is java.exe's simple process); japp
    /// applications terminate via System.exit, and when main returns the
    /// launcher process simply exits. Divergence from java.exe: a normal main
    /// return does not wait for the application's remaining non-daemon
    /// threads. <c>JAPP_JNI_DEBUG=1</c> prints stage markers.</summary>
    internal static unsafe int? RunInProcess(string jvmLibrary, IReadOnlyList<string> options, IReadOnlyList<string> appArgs)
    {
        var debug = Environment.GetEnvironmentVariable("JAPP_JNI_DEBUG") is { Length: > 0 };
        void Stage(string name)
        {
            if (debug)
                Console.Error.WriteLine("jni: " + name);
        }

        try
        {
            // Must happen on this thread: the VM-creating thread becomes the
            // "main" Java thread, exactly like java.exe's JavaMain.
            Stage("loading " + jvmLibrary);
            var library = NativeLibrary.Load(jvmLibrary);
            var createJavaVm = (delegate* unmanaged<IntPtr*, IntPtr*, JavaVmInitArgs*, int>)
                NativeLibrary.GetExport(library, "JNI_CreateJavaVM").ToPointer();

            var optionPointers = options.Select(option => Marshal.StringToCoTaskMemUTF8(option)).ToArray();
            var pointerSize = IntPtr.Size;
            var optionsBlock = Marshal.AllocHGlobal(pointerSize * 2 * optionPointers.Length);
            for (var i = 0; i < optionPointers.Length; i++)
            {
                Marshal.WriteIntPtr(optionsBlock, pointerSize * 2 * i, optionPointers[i]);
                Marshal.WriteIntPtr(optionsBlock, pointerSize * 2 * i + pointerSize, IntPtr.Zero);
            }

            var args = new JavaVmInitArgs
            {
                Version = JniVersion18,
                NOptions = optionPointers.Length,
                Options = optionsBlock,
                IgnoreUnrecognized = 0,
            };

            Stage("creating vm with " + optionPointers.Length + " options");
            IntPtr vm = IntPtr.Zero, env = IntPtr.Zero;
            if (createJavaVm(&vm, &env, &args) != 0)
            {
                Stage("vm creation failed");
                return null;
            }

            var functions = (JniNativeInterface*)Marshal.ReadIntPtr(env);

            fixed (byte* bootName = BootMainClassBytes)
            fixed (byte* mainName = MainNameBytes)
            fixed (byte* mainSignature = MainSignatureBytes)
            fixed (byte* stringClassName = StringClassNameBytes)
            {
                Stage("FindClass(boot)");
                var bootClass = functions->FindClass(env, bootName);

                Stage("GetStaticMethodID(main)");
                var main = bootClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : functions->GetStaticMethodID(env, bootClass, mainName, mainSignature);
                if (main == IntPtr.Zero)
                {
                    Stage("boot main not found");
                    functions->ExceptionDescribe(env);
                    return null;
                }

                Stage("FindClass(String)");
                var stringClass = functions->FindClass(env, stringClassName);
                Stage("NewObjectArray");
                var arguments = functions->NewObjectArray(env, appArgs.Count, stringClass, IntPtr.Zero);
                for (var i = 0; i < appArgs.Count; i++)
                {
                    var bytes = NullTerminated(appArgs[i]);
                    fixed (byte* value = bytes)
                    {
                        Stage("NewStringUTF/SetObjectArrayElement " + i);
                        functions->SetObjectArrayElement(env, arguments, i, functions->NewStringUTF(env, value));
                    }
                }

                // One jvalue (pointer-sized union member) carrying the array.
                var jvalue = Marshal.AllocHGlobal(pointerSize);
                Marshal.WriteIntPtr(jvalue, arguments);

                Stage("invoking main");
                functions->CallStaticVoidMethodA(env, bootClass, main, jvalue);

                if (functions->ExceptionOccurred(env) != IntPtr.Zero)
                {
                    Stage("main threw");
                    functions->ExceptionDescribe(env);
                    return 1;
                }

                Stage("main finished");
                return 0;
            }
        }
        catch (Exception)
        {
            Stage("failed with an exception");
            return null;
        }
    }

    private static byte[] NullTerminated(string value)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1];
        Encoding.UTF8.GetBytes(value, bytes);
        return bytes;
    }

    private static readonly byte[] BootMainClassBytes = NullTerminated(BootMainClass);
    private static readonly byte[] MainNameBytes = NullTerminated("main");
    private static readonly byte[] MainSignatureBytes = NullTerminated("([Ljava/lang/String;)V");
    private static readonly byte[] StringClassNameBytes = NullTerminated("java/lang/String");

    [StructLayout(LayoutKind.Sequential)]
    private struct JavaVmInitArgs
    {
        public int Version;
        public int NOptions;
        public IntPtr Options;
        public byte IgnoreUnrecognized;
    }

    /// <summary>The JNINativeInterface_ table from jni.h, declared up to the
    /// highest slot used; the header's declaration order determines every
    /// offset. Comments cite the slot numbers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct JniNativeInterface
    {
        private fixed long _reserved[4];                                                        // 0..3
        private nint _getVersion;                                                               // 4
        private nint _defineClass;                                                              // 5
        public delegate* unmanaged<IntPtr, byte*, IntPtr> FindClass;                            // 6
        private fixed long _7_to_14[8];                                                         // 7..14
        public delegate* unmanaged<IntPtr, IntPtr> ExceptionOccurred;                           // 15
        public delegate* unmanaged<IntPtr, void> ExceptionDescribe;                             // 16
        private fixed long _17_to_112[96];                                                      // 17..112
        public delegate* unmanaged<IntPtr, IntPtr, byte*, byte*, IntPtr> GetStaticMethodID;     // 113
        private fixed long _114_to_142[29];                                                     // 114..142
        public delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void> CallStaticVoidMethodA; // 143
        private nint _getStaticFieldId;                                                         // 144
        private fixed long _145_to_162[18];                                                     // 145..162
        private fixed long _163_to_166[4];                                                      // 163..166
        public delegate* unmanaged<IntPtr, byte*, IntPtr> NewStringUTF;                         // 167

        // 168..171: GetStringUTFLength, GetStringUTFChars, ReleaseStringUTFChars, GetArrayLength
        private fixed long _168_to_171[4];
        public delegate* unmanaged<IntPtr, nint, IntPtr, IntPtr, IntPtr> NewObjectArray;      // 172
        private nint _getObjectArrayElement;                                                  // 173
        public delegate* unmanaged<IntPtr, IntPtr, nint, IntPtr, void> SetObjectArrayElement; // 174
    }

    /// <summary>The JavaVM table from jni.h (JNIInvokeInterface_).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct JniInvokeInterface
    {
        private fixed long _reserved[3];                             // 0..2
        public delegate* unmanaged<IntPtr, int> DestroyJavaVM;       // 3
        private nint _attachCurrentThread;                           // 4
        public delegate* unmanaged<IntPtr, int> DetachCurrentThread; // 5
    }
}
