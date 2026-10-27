using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.Updates;

namespace MacExtend.Services
{
    internal sealed class UpdateCheck
    {
        public UpdateCheck(UpdateManifest manifest, UpdateDecision decision)
        {
            Manifest = manifest;
            Decision = decision;
        }

        /// <summary>null when the repository has no release yet.</summary>
        public UpdateManifest Manifest { get; }
        public UpdateDecision Decision { get; }
    }

    /// <summary>Reads update.ini from the latest GitHub release and downloads the packages a newer version needs.</summary>
    internal static class UpdateService
    {
        private static readonly HttpClient Http = CreateClient();

        public static string Repository =>
            typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? "HaiTrieu247/lap2screen";

        public static Version CurrentVersion => AppVersion.Normalize(typeof(UpdateService).Assembly.GetName().Version);

        public static async Task<UpdateCheck> CheckAsync(CancellationToken cancel)
        {
            // releases/latest/download/<asset> redirects to the newest release; no API call, no rate limit
            var url = "https://github.com/" + Repository + "/releases/latest/download/update.ini";
            using (var response = await Http.GetAsync(url, cancel))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheck(null, UpdateDecision.For(CurrentVersion, null, null));
                response.EnsureSuccessStatusCode();
                var manifest = UpdateManifest.Parse(await response.Content.ReadAsStringAsync());
                if (manifest == null) throw new InvalidDataException("update.ini của bản phát hành không hợp lệ.");
                return new UpdateCheck(manifest, UpdateDecision.For(CurrentVersion, InstalledRuntimeId(), manifest));
            }
        }

        /// <summary>Downloads, verifies and extracts into a staging folder that the updater helper applies.</summary>
        /// <param name="progress">Fraction 0..1 of the bytes to download.</param>
        /// <returns>The staging folder.</returns>
        public static async Task<string> DownloadAsync(UpdateCheck check, IProgress<double> progress, CancellationToken cancel)
        {
            var manifest = check.Manifest;
            var staging = Path.Combine(AppPaths.UpdatesDir, manifest.Version.ToString(3));
            Directory.CreateDirectory(staging);
            RemoveOtherStagingFolders(staging);

            var total = Math.Max(1, check.Decision.DownloadBytes);
            long done = 0;
            var counter = new Progress<long>(bytes => progress.Report(Math.Min(1.0, (done + bytes) / (double)total)));

            await FetchAsync(manifest, manifest.App, staging, "app", counter, cancel);
            done += manifest.App.Size;
            var runtimeDir = Path.Combine(staging, "runtime");
            if (check.Decision.NeedsRuntime)
            {
                await FetchAsync(manifest, manifest.Runtime, staging, "runtime", counter, cancel);
            }
            else if (Directory.Exists(runtimeDir))
            {
                Directory.Delete(runtimeDir, true);
            }

            File.WriteAllText(Path.Combine(staging, "update.ini"), UpdateManifest.Serialize(manifest));
            progress.Report(1.0);
            return staging;
        }

        /// <summary>
        /// Old staging folders and the helper copies in %TEMP% (which cannot delete themselves)
        /// are useless once the app runs the new version.
        /// </summary>
        public static void CleanupStaging()
        {
            var folders = Directory.GetDirectories(Path.GetTempPath(), "MacExtend-update-*").ToList();
            if (Directory.Exists(AppPaths.UpdatesDir)) folders.Add(AppPaths.UpdatesDir);
            foreach (var dir in folders)
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // a helper still exiting holds its exe; the next update removes it
                }
            }
        }

        private static async Task FetchAsync(UpdateManifest manifest, UpdatePackage package, string staging, string folder,
                                             IProgress<long> progress, CancellationToken cancel)
        {
            var zip = Path.Combine(staging, package.File);
            if (!File.Exists(zip) || FileHash.Sha256(zip) != package.Sha256)
            {
                var part = zip + ".part";
                var url = "https://github.com/" + Repository + "/releases/download/v" + manifest.Version.ToString(3) + "/" + package.File;
                using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
                {
                    response.EnsureSuccessStatusCode();
                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = File.Create(part))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancel)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, cancel);
                            received += read;
                            progress.Report(received);
                        }
                    }
                }
                if (FileHash.Sha256(part) != package.Sha256)
                {
                    File.Delete(part);
                    throw new InvalidDataException("Tệp " + package.File + " tải về bị hỏng (sai mã SHA-256).");
                }
                if (File.Exists(zip)) File.Delete(zip);
                File.Move(part, zip);
            }
            UpdateStager.Extract(zip, Path.Combine(staging, folder));
        }

        private static void RemoveOtherStagingFolders(string keep)
        {
            foreach (var dir in Directory.GetDirectories(AppPaths.UpdatesDir))
            {
                if (string.Equals(dir.TrimEnd('\\'), keep.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                }
            }
        }

        private static string InstalledRuntimeId()
        {
            try
            {
                return File.Exists(AppPaths.RuntimeIdFile) ? File.ReadAllText(AppPaths.RuntimeIdFile).Trim() : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MacExtend/" + CurrentVersion.ToString(3));
            return client;
        }
    }
}
