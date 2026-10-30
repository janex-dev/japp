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

using System.Security.Cryptography;

public static class MavenResolver
{
    private const string CentralUrl = "https://repo1.maven.org/maven2";

    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string LocalRepository = Path.Combine(UserProfile, ".m2", "repository");

    private static readonly HttpClient Http = new();

    /// <summary>Resolves a Maven reference to a local jar file. <c>local</c>
    /// reuses the user's default repository — whatever the coordinate yields
    /// is used regardless of provenance — and backfills missing artifacts
    /// into it from Central; <c>central</c> (the default) uses a private
    /// cache under the platform cache directory and never consults the user
    /// repository, so the bytes always match what Central published.
    /// Downloads are verified against the published .sha1.</summary>
    public static Task<string> ResolveAsync(
        string? repository, string group, string artifact, string version, string? classifier,
        CancellationToken cancellationToken = default) =>
        repository switch
        {
            null or "central" => ResolveCentralAsync(group, artifact, version, classifier, cancellationToken),
            "local" => ResolveLocalAsync(group, artifact, version, classifier, cancellationToken),
            _ => throw new ArgumentException("Unknown repo: " + repository),
        };

    private static async Task<string> ResolveLocalAsync(
        string group, string artifact, string version, string? classifier, CancellationToken cancellationToken)
    {
        var file = LayoutPath(LocalRepository, group, artifact, version, classifier);
        if (File.Exists(file))
            return file;

        return await DownloadAsync(LocalRepository, group, artifact, version, classifier, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string> ResolveCentralAsync(
        string group, string artifact, string version, string? classifier, CancellationToken cancellationToken)
    {
        var root = Path.Combine(PlatformCacheDirectory(), "maven", "central");
        var file = LayoutPath(root, group, artifact, version, classifier);
        if (File.Exists(file))
            return file;

        return await DownloadAsync(root, group, artifact, version, classifier, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string LayoutPath(string root, string group, string artifact, string version, string? classifier)
    {
        var fileName = classifier is null
            ? $"{artifact}-{version}.jar"
            : $"{artifact}-{version}-{classifier}.jar";
        return Path.Combine(root, group.Replace('.', Path.DirectorySeparatorChar), artifact, version, fileName);
    }

    private static async Task<string> DownloadAsync(
        string root, string group, string artifact, string version, string? classifier,
        CancellationToken cancellationToken)
    {
        var file = LayoutPath(root, group, artifact, version, classifier);
        var url = $"{CentralUrl}/{group.Replace('.', '/')}/{artifact}/{version}/{Path.GetFileName(file)}";

        var data = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);

        var expectedHash = await TryDownloadSha1Async(url + ".sha1", cancellationToken).ConfigureAwait(false);
        if (expectedHash is not null)
        {
            var actual = SHA1.HashData(data);
            if (!actual.AsSpan().SequenceEqual(expectedHash))
                throw new IOException($"Failed while verifying {url}");
        }

        var directory = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Path.GetRandomFileName());
        File.WriteAllBytes(temp, data);
        File.Move(temp, file, overwrite: true);

        return file;
    }

    private static string PlatformCacheDirectory()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(UserProfile, "Library", "Caches", "japp");

        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "japp", "cache");

        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdgCache) ? Path.Combine(UserProfile, ".cache") : xdgCache, "japp");
    }

    private static async Task<byte[]?> TryDownloadSha1Async(string url, CancellationToken cancellationToken)
    {
        try
        {
            var text = (await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false)).Trim();
            if (text.Length < 40)
                return null;

            return Convert.FromHexString(text[..40]);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // No published checksum: proceed without verification, like the Java side.
            return null;
        }
    }
}
