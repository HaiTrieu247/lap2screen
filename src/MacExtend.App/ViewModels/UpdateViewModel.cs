using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using MacExtend.Services;

namespace MacExtend.ViewModels
{
    internal enum UpdatePhase
    {
        Idle,
        Checking,
        UpToDate,
        NoRelease,
        Available,
        Downloading,
        Installing,
    }

    /// <summary>Check-for-update state shown in the banner, the footer and the tray.</summary>
    internal sealed class UpdateViewModel : ObservableObject
    {
        private static readonly CultureInfo Vietnamese = new CultureInfo("vi-VN");
        private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

        private readonly Func<bool> _autoCheck;
        private readonly Func<bool> _macConnected;
        private readonly Action<string> _exitForUpdate;
        private readonly DispatcherTimer _timer;
        private UpdateCheck _check;
        private UpdatePhase _phase;
        private double _progress;
        private string _message;
        private string _announced;
        private bool _busy;

        public UpdateViewModel(Func<bool> autoCheck, Func<bool> macConnected, Action<string> exitForUpdate)
        {
            _autoCheck = autoCheck;
            _macConnected = macConnected;
            _exitForUpdate = exitForUpdate;
            CheckCommand = new RelayCommand(() => _ = CheckAsync(manual: true), () => !_busy);
            InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => _phase == UpdatePhase.Available && !_busy);

            _timer = new DispatcherTimer { Interval = FirstCheck };
            _timer.Tick += async (_, __) =>
            {
                _timer.Interval = Interval;
                if (_autoCheck()) await CheckAsync(manual: false);
            };
            _timer.Start();
        }

        /// <summary>A version not announced before was found (once per version), for the tray balloon.</summary>
        public event Action<string> NewVersionFound;

        public ICommand CheckCommand { get; }
        public ICommand InstallCommand { get; }

        private string NewVersion => _check?.Manifest?.Version.ToString(3) ?? "";

        public bool ShowBanner => _phase == UpdatePhase.Available || _phase == UpdatePhase.Downloading || _phase == UpdatePhase.Installing;
        public bool ShowInstallButton => _phase == UpdatePhase.Available;
        public bool ShowProgress => _phase == UpdatePhase.Downloading;
        public double Progress => _progress;

        public string BannerTitle
        {
            get
            {
                switch (_phase)
                {
                    case UpdatePhase.Downloading: return "Đang tải bản " + NewVersion;
                    case UpdatePhase.Installing: return "Đang cài bản " + NewVersion;
                    default: return "Có bản mới " + NewVersion;
                }
            }
        }

        public string BannerDetail
        {
            get
            {
                switch (_phase)
                {
                    case UpdatePhase.Downloading:
                        return Math.Floor(_progress).ToString(CultureInfo.InvariantCulture) + "%";
                    case UpdatePhase.Installing:
                        return "MacExtend sẽ đóng rồi tự mở lại sau khi cài xong.";
                    default:
                        var text = "Tải về khoảng " + Size(_check?.Decision.DownloadBytes ?? 0) + ". MacExtend sẽ đóng rồi tự mở lại";
                        text += UpdateHelper.NeedsElevation(AppPaths.InstallDir) ? "; Windows sẽ hỏi quyền quản trị một lần." : ".";
                        if (_macConnected()) text += " Mac đang kết nối sẽ bị ngắt.";
                        return text;
                }
            }
        }

        /// <summary>Footer line: result of the last check or update, empty when there is nothing to say.</summary>
        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(_message)) return _message;
                switch (_phase)
                {
                    case UpdatePhase.Checking: return "Đang kiểm tra cập nhật";
                    case UpdatePhase.UpToDate: return "Bạn đang dùng bản mới nhất.";
                    case UpdatePhase.NoRelease: return "Chưa có bản phát hành nào trên GitHub.";
                    default: return "";
                }
            }
        }

        /// <summary>Message after MacExtend was restarted by the updater.</summary>
        public void ShowResultOfLastUpdate(string arg, string version)
        {
            if (arg == UpdateHelper.AfterUpdateArg) _message = "Đã cập nhật lên bản " + version + ".";
            else if (arg == UpdateHelper.CancelledArg) _message = "Cập nhật đã bị hủy. Bản hiện tại vẫn chạy bình thường.";
            else if (arg == UpdateHelper.FailedArg) _message = "Cập nhật không thành công nên bản cũ được giữ nguyên. Chi tiết trong update.log ở thư mục nhật ký.";
            RaiseAll();
        }

        /// <summary>The helper could not be started; stay on this version and say why.</summary>
        public void ReportLaunchFailure(string reason)
        {
            _message = "Không chạy được trình cập nhật: " + reason;
            SetPhase(UpdatePhase.Available);
        }

        /// <summary>Connection state changed: the banner mentions the Mac being disconnected.</summary>
        public void Refresh() => Raise(nameof(BannerDetail));

        private async Task CheckAsync(bool manual)
        {
            if (_busy || _phase == UpdatePhase.Downloading || _phase == UpdatePhase.Installing) return;
            _busy = true;
            _message = null;
            if (manual) SetPhase(UpdatePhase.Checking);
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    _check = await UpdateService.CheckAsync(timeout.Token);
                }
                if (_check.Manifest == null)
                {
                    SetPhase(manual ? UpdatePhase.NoRelease : UpdatePhase.Idle);
                }
                else if (_check.Decision.Available)
                {
                    SetPhase(UpdatePhase.Available);
                    if (_announced != NewVersion)
                    {
                        _announced = NewVersion;
                        NewVersionFound?.Invoke(NewVersion);
                    }
                }
                else
                {
                    SetPhase(manual ? UpdatePhase.UpToDate : UpdatePhase.Idle);
                }
            }
            catch (Exception e) when (IsExpected(e))
            {
                // automatic checks stay quiet when offline; a manual check explains
                if (manual) _message = "Không kiểm tra được cập nhật: " + Describe(e);
                SetPhase(UpdatePhase.Idle);
            }
            finally
            {
                _busy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task InstallAsync()
        {
            if (_check?.Manifest == null) return;
            _busy = true;
            _message = null;
            _progress = 0;
            SetPhase(UpdatePhase.Downloading);
            try
            {
                var progress = new Progress<double>(fraction =>
                {
                    _progress = fraction * 100;
                    Raise(nameof(Progress), nameof(BannerDetail));
                });
                var staging = await UpdateService.DownloadAsync(_check, progress, CancellationToken.None);
                SetPhase(UpdatePhase.Installing);
                _exitForUpdate(staging);
            }
            catch (Exception e) when (IsExpected(e))
            {
                _message = "Không tải được bản cập nhật: " + Describe(e);
                SetPhase(UpdatePhase.Available);
            }
            finally
            {
                _busy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void SetPhase(UpdatePhase phase)
        {
            _phase = phase;
            RaiseAll();
        }

        private void RaiseAll() => Raise(nameof(ShowBanner), nameof(ShowInstallButton), nameof(ShowProgress), nameof(Progress),
                                         nameof(BannerTitle), nameof(BannerDetail), nameof(StatusText));

        private static bool IsExpected(Exception e) =>
            e is HttpRequestException || e is TaskCanceledException || e is InvalidDataException ||
            e is IOException || e is UnauthorizedAccessException;

        private static string Describe(Exception e)
        {
            if (e is TaskCanceledException) return "GitHub không phản hồi kịp.";
            if (e is HttpRequestException) return "không kết nối được GitHub.";
            return e.Message;
        }

        private static string Size(long bytes)
        {
            if (bytes < 1024 * 1024) return Math.Max(1, bytes / 1024).ToString(Vietnamese) + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0", Vietnamese) + " MB";
        }
    }
}
