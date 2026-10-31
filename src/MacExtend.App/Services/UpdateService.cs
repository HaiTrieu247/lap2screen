using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.CoreDevice;
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
                .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? "HaiTrieu247/MacExtend";

        public static Version CurrentVersion => AppVersion.Normalize(typeof(UpdateService).Assembly.GetName().Version);

        /// <summary>
        /// The release label this build came from: "1.0.8-beta.1" for a pre-release tag, else the 3-part version.
        /// Release tags are "v" + this.
        /// </summary>
        public static string CurrentLabel => AppVersion.ReleaseLabel(
            typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, CurrentVersion);

        /// <summary>A file attached to the release tagged "v" + <paramref name="label"/>.</summary>
        public static string ReleaseAssetUrl(string label, string file) =>
            "https://github.com/" + Repository + "/releases/download/v" + label + "/" + file;

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
                return new UpdateCheck(manifest, UpdateDecision.For(CurrentVersion, InstalledRuntimeId(), manifest, CoreDevice.CoreDeviceRuntime.InstalledId));
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

            done += check.Decision.NeedsRuntime ? manifest.Runtime.Size : 0;
            var coreDeviceDir = Path.Combine(staging, CoreDeviceUpdate.StagingFolder);
            if (check.Decision.NeedsCoreDevice)
            {
                await FetchAsync(manifest, manifest.CoreDevice, staging, CoreDeviceUpdate.StagingFolder, counter, cancel);
                CheckStagedCoreDevice(manifest, coreDeviceDir);
            }
            else if (Directory.Exists(coreDeviceDir))
            {
                Directory.Delete(coreDeviceDir, true);
            }

            File.WriteAllText(Path.Combine(staging, "update.ini"), UpdateManifest.Serialize(manifest));
            progress.Report(1.0);
            return staging;
        }

        /// <summary>Only a complete runtime with the manifest's RUNTIME_ID stays staged; its zip's SHA-256 was checked on download.</summary>
        private static void CheckStagedCoreDevice(UpdateManifest manifest, string dir)
        {
            try
            {
                CoreDeviceUpdate.CheckStaged(dir, manifest.CoreDeviceId);
            }
            catch (RuntimePackageException e)
            {
                Directory.Delete(dir, true);
                throw new InvalidDataException("Gói runtime Điều khiển trực tiếp trong bản phát hành không hợp lệ: " + e.Message);
            }
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
            await DownloadVerifiedAsync(ReleaseAssetUrl(manifest.Version.ToString(3), package.File), zip, package.Sha256, progress, cancel);
            UpdateStager.Extract(zip, Path.Combine(staging, folder));
        }

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="zip"/> unless that file already has <paramref name="sha256"/>:
        /// into a .part file, checked, then renamed. The updater's packages and the first CoreDevice runtime both come this way.
        /// </summary>
        /// <param name="progress">Bytes received so far.</param>
        /// <param name="notFoundMessage">When given, a 404 throws <see cref="FileNotFoundException"/> with it instead of an HTTP error.</param>
        public static async Task DownloadVerifiedAsync(string url, string zip, string sha256, IProgress<long> progress, CancellationToken cancel,
                                                       string notFoundMessage = null)
        {
            if (File.Exists(zip) && FileHash.Sha256(zip) == sha256) return;
            var part = zip + ".part";
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                if (notFoundMessage != null && response.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException(notFoundMessage);
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
                        progress?.Report(received);
                    }
                }
            }
            if (FileHash.Sha256(part) != sha256)
            {
                File.Delete(part);
                throw new InvalidDataException("Tệp " + Path.GetFileName(zip) + " tải về bị hỏng (sai mã SHA-256).");
            }
            if (File.Exists(zip)) File.Delete(zip);
            File.Move(part, zip);
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
