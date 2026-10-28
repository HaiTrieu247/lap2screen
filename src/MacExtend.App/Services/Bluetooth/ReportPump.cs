using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MacExtend.Core.Bluetooth;

namespace MacExtend.Services.Bluetooth
{
    /// <summary>
    /// Sends what the hooks queued. Keys and button changes go in order, as fast as the link takes them; pointer
    /// motion is merged and paced to the connection interval, because NotifyValueAsync returns once a report is
    /// queued and an overfull radio shows up as the pointer drifting on after the hand has stopped.
    /// Idea and pacing from windows-ble-hid (MIT), CaptureSession.
    /// </summary>
    internal sealed class ReportPump : IDisposable
    {
        private readonly HidPeripheral _peripheral;
        private readonly ReportQueue _queue;
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0, 1);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Task _loop;

        public ReportPump(HidPeripheral peripheral, ReportQueue queue)
        {
            _peripheral = peripheral;
            _queue = queue;
            _loop = Task.Run(RunAsync);
        }

        /// <summary>A report could not be sent; raised on a thread-pool thread.</summary>
        public event Action<Exception> Failed;

        /// <summary>Something was queued. Safe from any thread, including a hook callback.</summary>
        public void Wake()
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // already awake
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // cancelled
            }
        }

        private async Task RunAsync()
        {
            var clock = Stopwatch.StartNew();
            long lastMouse = -1000;
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(_stop.Token).ConfigureAwait(false);
                    while (_queue.TryDequeueOrdered(out var report))
                    {
                        if (report.IsKeyboard)
                        {
                            await _peripheral.SendKeyboardAsync(report.Keyboard).ConfigureAwait(false);
                            continue;
                        }
                        await _peripheral.SendMouseAsync(report.Buttons, report.Dx, report.Dy, report.Wheel).ConfigureAwait(false);
                        lastMouse = clock.ElapsedMilliseconds;
                    }
                    if (!_queue.HasMouse) continue;

                    var wait = _peripheral.PointerIntervalMs - (clock.ElapsedMilliseconds - lastMouse);
                    if (wait > 0) await Task.Delay((int)wait, _stop.Token).ConfigureAwait(false);
                    // a key queued meanwhile goes first; the motion waits for the next round
                    if (!_queue.TryTakeMouse(out var mouse))
                    {
                        Wake();
                        continue;
                    }
                    await _peripheral.SendMouseAsync(mouse.Buttons, mouse.Dx, mouse.Dy, mouse.Wheel).ConfigureAwait(false);
                    lastMouse = clock.ElapsedMilliseconds;
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    // WinRT reports a dropped link as COMException, a withdrawn service as cancelled or worse;
                    // the pump itself keeps going for the next session
                    Failed?.Invoke(e);
                }
            }
        }
    }
}
