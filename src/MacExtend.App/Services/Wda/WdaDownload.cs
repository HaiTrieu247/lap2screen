using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.Wda;

namespace MacExtend.Services.Wda
{
    /// <summary>
    /// "Tải WDA": fetches the pinned WebDriverAgent runner, checks it is exactly that file, repackages it as
    /// WebDriverAgentRunner.ipa in the user's Downloads folder (through a temporary file) and shows it in Explorer.
    /// </summary>
    internal static class WdaDownload
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

        /// <returns>The .ipa's path.</returns>
        /// <exception cref="HttpRequestException">No connection or an HTTP error.</exception>
        /// <exception cref="InvalidDataException">Not the pinned file, or not a runner zip.</exception>
        public static async Task<string> FetchAsync(CancellationToken token)
        {
            byte[] data;
            using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MacExtend");
                using (var response = await http.GetAsync(WdaPackage.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var length = response.Content.Headers.ContentLength;
                    if (length > WdaPackage.MaxDownload) throw new InvalidDataException("the download is larger than WebDriverAgent");
                    data = await ReadBoundedAsync(response, token).ConfigureAwait(false);
                }
            }
            if (!WdaPackage.IsPinned(data)) throw new InvalidDataException("the download is not the pinned WebDriverAgent " + WdaPackage.Version);

            var folder = DownloadsFolder();
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, WdaPackage.IpaName);
            var temp = path + ".part";
            try
            {
                using (var zip = new MemoryStream(data, writable: false))
                using (var ipa = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    IpaPackage.FromRunnerZip(zip, ipa);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
            return path;
        }

        /// <summary>Explorer with the file selected; nothing happens when Explorer cannot be started.</summary>
        public static void Reveal(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = false })?.Dispose();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                // the path is in the status line anyway
            }
        }

        /// <summary>The known Downloads folder (it may be moved), else %USERPROFILE%\Downloads.</summary>
        public static string DownloadsFolder()
        {
            var known = KnownFolders.Downloads();
            return !string.IsNullOrEmpty(known) ? known : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken token)
        {
            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > WdaPackage.MaxDownload) throw new InvalidDataException("the download is larger than WebDriverAgent");
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // a leftover .part file is harmless
            }
        }

        private static class KnownFolders
        {
            private static readonly Guid DownloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");

            [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            private static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id,
                                                           uint flags, IntPtr token, out IntPtr path);

            public static string Downloads()
            {
                try
                {
                    if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) != 0) return null;
                    try
                    {
                        return System.Runtime.InteropServices.Marshal.PtrToStringUni(p);
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p);
                    }
                }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
                {
                    return null;
                }
            }
        }
    }
}
