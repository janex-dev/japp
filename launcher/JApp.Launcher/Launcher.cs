/*
 * Copyright (C) 2023 Glavo
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

/// <summary>Ported from src/main/java/org/glavo/japp/launcher/Launcher.java:
/// read the launcher metadata, select a JVM, resolve the config, encode the
/// boot args and hand off to java running org.glavo.japp.boot.</summary>
internal static class Launcher
{
    private const string BootLauncherModule = "org.glavo.japp.boot";

    public static async Task<int> RunAsync(string jappFile, IReadOnlyList<string> jvmOptions, IReadOnlyList<string> args)
    {
        var config = JAppLauncherMetadata.ReadFile(jappFile);
        var group = config.Group;

        var found = await JavaFinder.SearchAsync(group).ConfigureAwait(false);
        if (found is null)
        {
            await Console.Error.WriteLineAsync("Error: Unable to find suitable Java");
            await Console.Error.WriteLineAsync("Condition: " + (group.Condition is null
                ? "(none)"
                : ConditionParser.Parse(group.Condition).ToString()));
            await Console.Error.WriteLineAsync("Java:");

            foreach (var (jvm, jvmContext) in await JavaFinder.LocateAllAsync().ConfigureAwait(false))
                await Console.Error.WriteLineAsync($"  - {jvm.Executable.Path} ({jvmContext})");

            return 1;
        }

        var (java, context) = found.Value;

        group.Resolve(context);

        var command = await BuildCommandAsync(
            java.Executable.Path, context, group, jappFile, config, jvmOptions, args,
            BootJar.ResolveModulePath()).ConfigureAwait(false);

        return await JavaCommand.RunAsync(command).ConfigureAwait(false);
    }

    /// <summary>Assembles the java command line in the Java launcher's exact
    /// order; the boot args blob is built alongside because the
    /// enable-native-access handling feeds both.</summary>
    internal static async Task<List<string>> BuildCommandAsync(
        string javaPath, JvmContext context, JAppConfigGroup group, string jappFile, JAppLauncherMetadata config,
        IReadOnlyList<string> jvmOptions, IReadOnlyList<string> appArgs, string bootModulePath)
    {
        var command = new List<string> { javaPath };

        var release = context.MajorVersion;
        var enablePreview = false;

        foreach (var property in group.JvmProperties)
            command.Add("-D" + property);

        command.AddRange(group.ExtraJvmOptions);

        var bootArgs = new BootArgs.Builder();
        bootArgs.WriteHeader(jappFile, config.BaseOffset, config.BootMetadataOffset, config.BootMetadataSize);

        bootArgs.WriteStringListField(BootArgs.Field.AddReads, group.AddReads);
        bootArgs.WriteStringListField(BootArgs.Field.AddOpens, group.AddOpens);
        bootArgs.WriteStringListField(BootArgs.Field.AddExports, group.AddExports);

        if (group.EnableNativeAccess.Count > 0)
        {
            if (release == 16)
            {
                command.Add("-Dforeign.restricted=permit");
            }
            else if (release >= 17)
            {
                command.Add("--enable-native-access=" + BootLauncherModule);

                List<string> list;
                if (group.EnableNativeAccess.Contains("ALL-UNNAMED"))
                {
                    command.Add("--enable-native-access=ALL-UNNAMED");
                    list = group.EnableNativeAccess
                        .Where(module => !module.Equals("ALL-UNNAMED"))
                        .ToList();
                }
                else
                {
                    list = group.EnableNativeAccess;
                }

                bootArgs.WriteStringListField(BootArgs.Field.EnableNativeAccess, list);
            }

            if (release <= 21)
                enablePreview = true;
        }

        await bootArgs.WriteClassOrModulePathAsync(BootArgs.Field.ModulePath, release, group.ModulePath)
            .ConfigureAwait(false);
        await bootArgs.WriteClassOrModulePathAsync(BootArgs.Field.ClassPath, release, group.ClassPath)
            .ConfigureAwait(false);

        bootArgs.WriteMainClass(group.MainClass);
        bootArgs.WriteMainModule(group.MainModule);
        bootArgs.WriteEnd();

        command.Add("-Dorg.glavo.japp.boot.args=" + Convert.ToBase64String(bootArgs.ToArray()));

        if (enablePreview)
            command.Add("--enable-preview");

        command.AddRange(jvmOptions);

        command.Add("--module-path");
        command.Add(bootModulePath);
        command.Add("--add-exports=java.base/jdk.internal.loader=" + BootLauncherModule);
        command.Add("--add-exports=java.base/jdk.internal.module=" + BootLauncherModule);
        command.Add("--add-exports=java.base/jdk.internal.misc=" + BootLauncherModule);
        command.Add("--add-opens=java.base/jdk.internal.loader=" + BootLauncherModule);
        command.Add("--add-opens=java.base/java.lang=" + BootLauncherModule);
        command.Add("--module");
        command.Add(BootLauncherModule);

        command.AddRange(appArgs);

        return command;
    }
}
