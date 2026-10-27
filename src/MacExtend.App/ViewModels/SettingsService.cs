using System;
using System.IO;
using System.Windows.Threading;
using MacExtend.Core;

namespace MacExtend.ViewModels
{
    /// <summary>The one live <see cref="AppSettings"/> instance, saved shortly after each change.</summary>
    internal sealed class SettingsService
    {
        private readonly string _path;
        private readonly DispatcherTimer _saveTimer;

        public SettingsService(string path)
        {
            _path = path;
            Current = SettingsStore.Load(path);
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, __) => Flush();
            if (!Pin.IsValid(Current.Pin))
            {
                Current.Pin = Pin.Generate(new Random());
                Save();
            }
        }

        public AppSettings Current { get; }

        public void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void Flush()
        {
            if (!_saveTimer.IsEnabled) return;
            _saveTimer.Stop();
            Save();
        }

        private void Save()
        {
            try
            {
                SettingsStore.Save(_path, Current);
            }
            catch (IOException)
            {
                // next change retries; nothing the user can act on
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
