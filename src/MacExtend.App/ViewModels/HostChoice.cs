using System;
using MacExtend.Core.Bluetooth;

namespace MacExtend.ViewModels
{
    /// <summary>One device in the "which device to control" picker.</summary>
    internal sealed class HostChoice : ObservableObject
    {
        private readonly Action<string> _select;
        private bool _selected;

        public HostChoice(PairedHost host, bool selected, Action<string> select)
        {
            Id = host.Id;
            Display = host.Display;
            _selected = selected;
            _select = select;
        }

        public string Id { get; }
        public string Display { get; }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value) || !value) return;
                _select(Id);
            }
        }
    }
}
