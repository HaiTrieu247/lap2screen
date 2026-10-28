// Adapted from windows-ble-hid by Abhishek Raj, MIT License, commit b33a004
// (https://github.com/abhishek-raj/windows-ble-hid). See licenses/windows-ble-hid-LICENSE.txt.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MacExtend.Core.Bluetooth;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Security.Cryptography;

namespace MacExtend.Services.Bluetooth
{
    internal sealed class PeripheralCheck
    {
        public PeripheralCheck(PeripheralState state, string detail = null)
        {
            State = state;
            Detail = detail;
        }

        public PeripheralState State { get; }
        /// <summary>What went wrong, for <see cref="PeripheralState.Failed"/>.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// The laptop as a Bluetooth LE keyboard and mouse (HID over GATT) on the in-box Windows stack. Reports go to
    /// <see cref="TargetId"/> only, never to every connected device. Events arrive on thread-pool threads.
    /// </summary>
    internal sealed class HidPeripheral : IDisposable
    {
        /// <summary>Pointer reports any closer than this pile up in the radio and the pointer drifts after the hand stops.</summary>
        private const int MinimumPointerIntervalMs = 10;

        private static readonly Lazy<bool> ConnectionParametersReadable = new Lazy<bool>(() =>
            ApiInformation.IsMethodPresent("Windows.Devices.Bluetooth.BluetoothLEDevice", "GetConnectionParameters"));

        private readonly ConcurrentDictionary<string, BluetoothLEDevice> _devices =
            new ConcurrentDictionary<string, BluetoothLEDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _names = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _intervalsMs = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Radio _radio;
        private GattServiceProvider _provider;
        private GattServiceProvider _battery;
        private GattLocalCharacteristic _keyboardInput;
        private GattLocalCharacteristic _mouseInput;
        // every service, characteristic and descriptor created, released together in Stop()
        private readonly List<object> _owned = new List<object>();
        private TaskCompletionSource<bool> _advertising;
        private bool _everAdvertised;
        private byte[] _lastKeyboard = HidReports.KeyboardRelease();
        private byte[] _lastMouse = new byte[HidReports.MouseReportLength];
        private byte _protocolMode = 0x01; // 0x00 boot, 0x01 report
        private volatile string _targetId;

        /// <summary>A host subscribed, left, connected or disconnected.</summary>
        public event Action HostsChanged;
        /// <summary>Bluetooth was switched on or off in Windows.</summary>
        public event Action RadioChanged;
        public event Action<string> Log;

        /// <summary>The one host reports go to; null sends nothing.</summary>
        public string TargetId
        {
            get => _targetId;
            set => _targetId = value;
        }

        public int PointerIntervalMs
        {
            get
            {
                var target = _targetId;
                var linkMs = target != null && _intervalsMs.TryGetValue(target, out var ms) ? ms : 0;
                return Math.Max(MinimumPointerIntervalMs, linkMs);
            }
        }

        /// <summary>Whether this laptop can be a keyboard right now, without advertising anything.</summary>
        public async Task<PeripheralCheck> CheckAsync()
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter == null) return new PeripheralCheck(PeripheralState.NoAdapter);
            if (!adapter.IsLowEnergySupported || !adapter.IsPeripheralRoleSupported) return new PeripheralCheck(PeripheralState.NoPeripheral);

            if (_radio == null)
            {
                _radio = await adapter.GetRadioAsync();
                if (_radio != null) _radio.StateChanged += OnRadioStateChanged;
            }
            return _radio != null && _radio.State != RadioState.On
                ? new PeripheralCheck(PeripheralState.RadioOff)
                : new PeripheralCheck(PeripheralState.Idle);
        }

        /// <summary>Publishes the HID service and starts advertising, so paired devices reconnect and new ones can pair.</summary>
        public async Task<PeripheralCheck> StartAsync()
        {
            var check = await CheckAsync();
            if (check.State != PeripheralState.Idle) return check;

            var serviceResult = await GattServiceProvider.CreateAsync(HidDescriptors.HidService);
            if (serviceResult.Error != BluetoothError.Success) return FromError("HID service", serviceResult.Error);
            _provider = serviceResult.ServiceProvider;
            var service = Own(_provider.Service);

            await CreateReadableAsync(service, "HID Information", HidDescriptors.HidInformation, HidDescriptors.HidInformationValue);
            await CreateReadableAsync(service, "Report Map", HidDescriptors.ReportMap, HidDescriptors.ReportMapValue);
            await CreateControlPointAsync(service);
            await CreateProtocolModeAsync(service);
            _keyboardInput = await CreateInputReportAsync(service, "keyboard", HidDescriptors.KeyboardReportId, () => _lastKeyboard);
            // hosts write their lock lamps here; a keyboard without it confuses some of them
            await CreateOutputReportAsync(service, HidDescriptors.KeyboardReportId);
            _mouseInput = await CreateInputReportAsync(service, "mouse", HidDescriptors.MouseReportId, () => _lastMouse);
            if (_keyboardInput == null || _mouseInput == null)
            {
                Stop();
                return new PeripheralCheck(PeripheralState.Failed, "không tạo được report bàn phím/chuột");
            }
            await CreateBatteryServiceAsync();

            _everAdvertised = false;
            _advertising = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _provider.AdvertisementStatusChanged += OnAdvertisementStatusChanged;
            _provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });

            // the provider reports Aborted until the radio really advertises, so wait for Started
            var advertising = _advertising;
            var finished = await Task.WhenAny(advertising.Task, Task.Delay(TimeSpan.FromSeconds(10))) == advertising.Task;
            var provider = _provider;
            if (provider == null) return new PeripheralCheck(PeripheralState.Failed, "đã dừng trong lúc khởi động");
            // like the reference: a slow radio may still start, so keep the service and say so
            Log?.Invoke(finished && advertising.Task.Result
                ? "advertising as a Bluetooth keyboard and mouse"
                : "advertising has not started after 10 s (" + provider.AdvertisementStatus + "); waiting on");
            return new PeripheralCheck(PeripheralState.Ready);
        }

        /// <summary>Withdraws the service, so connected devices lose the keyboard and get their on-screen one back.</summary>
        public void Stop()
        {
            var provider = _provider;
            var battery = _battery;
            _provider = null;
            _battery = null;
            _keyboardInput = null;
            _mouseInput = null;
            _advertising?.TrySetResult(false);
            if (provider != null)
            {
                provider.AdvertisementStatusChanged -= OnAdvertisementStatusChanged;
                try
                {
                    if (provider.AdvertisementStatus == GattServiceProviderAdvertisementStatus.Started) provider.StopAdvertising();
                }
                catch (COMException e)
                {
                    Log?.Invoke("stop advertising: " + e.Message);
                }
            }
            // the service stays published until its last reference goes; do not wait for a garbage collection
            var owned = _owned.ToList();
            _owned.Clear();
            foreach (var item in owned) Release(item);
            Release(provider);
            Release(battery);

            // out of the dictionary before disposing, so a concurrent Hosts() never reads a disposed device
            var devices = _devices.Values.ToList();
            _devices.Clear();
            _intervalsMs.Clear();
            foreach (var device in devices)
            {
                device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                if (ConnectionParametersReadable.Value) UnwatchParameters(device);
                device.Dispose();
            }
            _lastKeyboard = HidReports.KeyboardRelease();
            _lastMouse = new byte[HidReports.MouseReportLength];
            HostsChanged?.Invoke();
        }

        /// <summary>Hosts subscribed to the keyboard or mouse reports and still connected.</summary>
        public IReadOnlyList<PairedHost> Hosts()
        {
            var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var characteristic in new[] { _keyboardInput, _mouseInput })
            {
                if (characteristic == null) continue;
                foreach (var client in characteristic.SubscribedClients) ids.Add(client.Session.DeviceId.Id);
            }
            return ids.Where(IsLive)
                      .Select(id => new PairedHost(id, _names.TryGetValue(id, out var name) ? name : ""))
                      .ToList();
        }

        public Task SendKeyboardAsync(byte[] report)
        {
            _lastKeyboard = report;
            return NotifyAsync(_keyboardInput, report);
        }

        public Task SendMouseAsync(MouseButtons buttons, int dx, int dy, int wheel)
        {
            var report = HidReports.Mouse(buttons, dx, dy, wheel);
            _lastMouse = report;
            return NotifyAsync(_mouseInput, report);
        }

        public void Dispose()
        {
            Stop();
            if (_radio != null) _radio.StateChanged -= OnRadioStateChanged;
            _radio = null;
        }

        // ---- service -----------------------------------------------------------------------

        private PeripheralCheck FromError(string step, BluetoothError error)
        {
            Log?.Invoke(step + ": " + error);
            switch (error)
            {
                case BluetoothError.RadioNotAvailable: return new PeripheralCheck(PeripheralState.RadioOff);
                case BluetoothError.DisabledByPolicy:
                case BluetoothError.DisabledByUser:
                case BluetoothError.ConsentRequired:
                    return new PeripheralCheck(PeripheralState.Blocked);
                case BluetoothError.NotSupported:
                case BluetoothError.TransportNotSupported:
                    return new PeripheralCheck(PeripheralState.NoPeripheral);
                default: return new PeripheralCheck(PeripheralState.Failed, error.ToString());
            }
        }

        private void OnAdvertisementStatusChanged(GattServiceProvider sender, GattServiceProviderAdvertisementStatusChangedEventArgs args)
        {
            // Aborted is reported once on the way up on a healthy radio; only after Started does it mean trouble
            if (args.Status == GattServiceProviderAdvertisementStatus.Started)
            {
                _everAdvertised = true;
                _advertising?.TrySetResult(true);
            }
            else if (args.Status == GattServiceProviderAdvertisementStatus.Aborted && _everAdvertised)
            {
                Log?.Invoke("advertising stopped (" + args.Error + "); devices can no longer find this laptop");
            }
        }

        private async Task CreateReadableAsync(GattLocalService service, string name, Guid uuid, byte[] value)
        {
            var result = await service.CreateCharacteristicAsync(uuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read,
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            if (Created(name, result.Error)) ServeReads(Own(result.Characteristic), () => value);
        }

        private async Task CreateControlPointAsync(GattLocalService service)
        {
            var result = await service.CreateCharacteristicAsync(HidDescriptors.HidControlPoint, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            // suspend / exit suspend: nothing to do for a keyboard that is always on
            if (Created("HID Control Point", result.Error)) AcceptWrites(Own(result.Characteristic), null);
        }

        private async Task CreateProtocolModeAsync(GattLocalService service)
        {
            var result = await service.CreateCharacteristicAsync(HidDescriptors.ProtocolMode, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.WriteWithoutResponse,
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            if (!Created("Protocol Mode", result.Error)) return;
            ServeReads(Own(result.Characteristic), () => new[] { _protocolMode });
            AcceptWrites(result.Characteristic, value =>
            {
                if (value.Length > 0) _protocolMode = value[0];
            });
        }

        private async Task CreateOutputReportAsync(GattLocalService service, byte reportId)
        {
            var result = await service.CreateCharacteristicAsync(HidDescriptors.Report, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Write |
                                           GattCharacteristicProperties.WriteWithoutResponse,
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                WriteProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            if (!Created("keyboard LED report", result.Error)) return;
            // the lamps are the device's business; it only needs somewhere to write them
            ServeReads(Own(result.Characteristic), () => new byte[] { 0 });
            AcceptWrites(result.Characteristic, null);
            await CreateReportReferenceAsync(result.Characteristic, reportId, HidDescriptors.ReportTypeOutput);
        }

        private async Task<GattLocalCharacteristic> CreateInputReportAsync(GattLocalService service, string name, byte reportId,
                                                                           Func<byte[]> currentValue)
        {
            var result = await service.CreateCharacteristicAsync(HidDescriptors.Report, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            });
            if (!Created(name + " report", result.Error)) return null;
            var characteristic = Own(result.Characteristic);
            ServeReads(characteristic, currentValue);
            characteristic.SubscribedClientsChanged += (sender, args) => _ = TrackHostsAsync();
            return await CreateReportReferenceAsync(characteristic, reportId, HidDescriptors.ReportTypeInput) ? characteristic : null;
        }

        private async Task<bool> CreateReportReferenceAsync(GattLocalCharacteristic characteristic, byte reportId, byte reportType)
        {
            var result = await characteristic.CreateDescriptorAsync(HidDescriptors.ReportReference, new GattLocalDescriptorParameters
            {
                ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
                StaticValue = CryptographicBuffer.CreateFromByteArray(new[] { reportId, reportType }),
            });
            if (result.Error == BluetoothError.Success) Own(result.Descriptor);
            return Created("Report Reference " + reportId + "/" + reportType, result.Error);
        }

        private async Task CreateBatteryServiceAsync()
        {
            var result = await GattServiceProvider.CreateAsync(HidDescriptors.BatteryService);
            if (!Created("Battery service", result.Error)) return;
            _battery = result.ServiceProvider;
            var level = await Own(_battery.Service).CreateCharacteristicAsync(HidDescriptors.BatteryLevel, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Notify,
                ReadProtectionLevel = GattProtectionLevel.Plain,
            });
            if (Created("Battery Level", level.Error)) ServeReads(Own(level.Characteristic), () => new byte[] { 100 });
        }

        /// <summary>Answers each read with the value at that moment. A device dropping mid-request is logged, not thrown.</summary>
        private void ServeReads(GattLocalCharacteristic characteristic, Func<byte[]> value)
        {
            characteristic.ReadRequested += async (sender, args) =>
            {
                Deferral deferral = null;
                try
                {
                    deferral = args.GetDeferral();
                    var request = await args.GetRequestAsync();
                    request?.RespondWithValue(CryptographicBuffer.CreateFromByteArray(value()));
                }
                catch (Exception e)
                {
                    Log?.Invoke("read request: " + e.Message);
                }
                finally
                {
                    Complete(deferral);
                }
            };
        }

        /// <param name="received">Gets the bytes written; null to only acknowledge.</param>
        private void AcceptWrites(GattLocalCharacteristic characteristic, Action<byte[]> received)
        {
            characteristic.WriteRequested += async (sender, args) =>
            {
                Deferral deferral = null;
                try
                {
                    deferral = args.GetDeferral();
                    var request = await args.GetRequestAsync();
                    if (request == null) return;
                    CryptographicBuffer.CopyToByteArray(request.Value, out var value);
                    received?.Invoke(value ?? new byte[0]);
                    if (request.Option == GattWriteOption.WriteWithResponse) request.Respond();
                }
                catch (Exception e)
                {
                    Log?.Invoke("write request: " + e.Message);
                }
                finally
                {
                    Complete(deferral);
                }
            };
        }

        private void Complete(Deferral deferral)
        {
            try
            {
                deferral?.Complete();
            }
            catch (Exception e)
            {
                Log?.Invoke("request: " + e.Message);
            }
        }

        private bool Created(string name, BluetoothError error)
        {
            if (error == BluetoothError.Success) return true;
            Log?.Invoke(name + ": " + error);
            return false;
        }

        private T Own<T>(T winRtObject)
        {
            _owned.Add(winRtObject);
            return winRtObject;
        }

        private static void Release(object winRtObject)
        {
            if (winRtObject == null || !Marshal.IsComObject(winRtObject)) return;
            try
            {
                Marshal.FinalReleaseComObject(winRtObject);
            }
            catch (ArgumentException)
            {
                // already released
            }
        }

        // ---- hosts -------------------------------------------------------------------------

        /// <summary>
        /// Windows keeps a bonded host in SubscribedClients after it disconnects, so a host only counts while the
        /// device it belongs to has not been seen disconnecting.
        /// </summary>
        private bool IsLive(string id) =>
            !_devices.TryGetValue(id, out var device) || device.ConnectionStatus == BluetoothConnectionStatus.Connected;

        private async Task TrackHostsAsync()
        {
            try
            {
                foreach (var host in Hosts())
                {
                    if (_devices.ContainsKey(host.Id)) continue;
                    var device = await BluetoothLEDevice.FromIdAsync(host.Id);
                    if (device == null) continue;
                    if (!_devices.TryAdd(host.Id, device))
                    {
                        device.Dispose();
                        continue;
                    }
                    _names[host.Id] = device.Name ?? "";
                    device.ConnectionStatusChanged += OnConnectionStatusChanged;
                    if (ConnectionParametersReadable.Value) WatchParameters(device);
                    Log?.Invoke("device subscribed: " + new PairedHost(host.Id, device.Name).Display);
                }
            }
            catch (Exception e)
            {
                Log?.Invoke("could not read device details: " + e.Message);
            }
            HostsChanged?.Invoke();
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            try
            {
                Log?.Invoke(new PairedHost(sender.DeviceId, sender.Name).Display + " " +
                            (sender.ConnectionStatus == BluetoothConnectionStatus.Connected ? "connected" : "disconnected"));
            }
            catch (Exception e)
            {
                // disposed by Stop() while the event was on its way
                Log?.Invoke("connection status: " + e.Message);
            }
            HostsChanged?.Invoke();
        }

        private void OnRadioStateChanged(Radio sender, object args) => RadioChanged?.Invoke();

        // GetConnectionParameters exists from Windows 11; these only run where it does

        private void WatchParameters(BluetoothLEDevice device)
        {
            device.ConnectionParametersChanged += OnConnectionParametersChanged;
            ReadInterval(device);
        }

        private void UnwatchParameters(BluetoothLEDevice device) => device.ConnectionParametersChanged -= OnConnectionParametersChanged;

        private void OnConnectionParametersChanged(BluetoothLEDevice sender, object args) => ReadInterval(sender);

        private void ReadInterval(BluetoothLEDevice device)
        {
            try
            {
                var interval = device.GetConnectionParameters().ConnectionInterval;
                // in units of 1.25 ms
                if (interval > 0) _intervalsMs[device.DeviceId] = (int)Math.Ceiling(interval * 1.25);
            }
            catch (Exception)
            {
                // not connected right now, or disposed by Stop() meanwhile
            }
        }

        // ---- reports -----------------------------------------------------------------------

        private async Task NotifyAsync(GattLocalCharacteristic characteristic, byte[] payload)
        {
            var target = _targetId;
            if (characteristic == null || target == null) return;
            foreach (var client in characteristic.SubscribedClients)
            {
                if (!string.Equals(client.Session.DeviceId.Id, target, StringComparison.OrdinalIgnoreCase)) continue;
                await characteristic.NotifyValueAsync(CryptographicBuffer.CreateFromByteArray(payload), client);
                return;
            }
        }
    }
}
