using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MacExtend.Core.CoreDevice
{
    /// <summary>
    /// usbmuxd's plist protocol, as much as the cable presence check needs: one ListDevices request and its reply, as
    /// the Apple Mobile Device Service speaks it on 127.0.0.1:27015 (checked against pymobiledevice3 11.47.0's usbmux.py).
    /// A packet is a 16-byte little-endian header (total length including the header, version 1, message type 8 "plist",
    /// tag) followed by an XML plist. Anything that is not exactly such a reply is unknown (null), never "no device".
    /// </summary>
    public static class UsbmuxProtocol
    {
        public const int HeaderSize = 16;

        /// <summary>The largest reply read; a device list is a few kilobytes.</summary>
        public const int MaxReplySize = 64 * 1024;

        private const uint PlistVersion = 1;
        private const uint PlistMessage = 8;

        // what pymobiledevice3 and libusbmuxd send, with this app's names; kLibUSBMuxVersion as they set it
        private const string ListDevicesPlist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + "<plist version=\"1.0\">\n<dict>\n"
            + "\t<key>ClientVersionString</key>\n\t<string>MacExtend</string>\n"
            + "\t<key>MessageType</key>\n\t<string>ListDevices</string>\n"
            + "\t<key>ProgName</key>\n\t<string>MacExtend</string>\n"
            + "\t<key>kLibUSBMuxVersion</key>\n\t<integer>3</integer>\n"
            + "</dict>\n</plist>\n";

        public static byte[] ListDevicesRequest(uint tag)
        {
            var payload = Encoding.UTF8.GetBytes(ListDevicesPlist);
            var packet = new byte[HeaderSize + payload.Length];
            WriteUInt32(packet, 0, (uint)packet.Length);
            WriteUInt32(packet, 4, PlistVersion);
            WriteUInt32(packet, 8, PlistMessage);
            WriteUInt32(packet, 12, tag);
            Buffer.BlockCopy(payload, 0, packet, HeaderSize, payload.Length);
            return packet;
        }

        /// <summary>The whole packet size a reply's header declares, or -1 when no reply this check reads can have it.</summary>
        public static int DeclaredSize(byte[] header)
        {
            if (header == null || header.Length < 4) return -1;
            var size = ReadUInt32(header, 0);
            return size < HeaderSize || size > MaxReplySize ? -1 : (int)size;
        }

        /// <summary>
        /// The serial numbers (UDIDs, as usbmuxd writes them) of the devices attached over USB, from a whole ListDevices
        /// reply to the request tagged <paramref name="tag"/>. Empty when it lists none; null when it is not such a reply.
        /// A device usbmuxd only sees on the network is not on the cable.
        /// </summary>
        public static IReadOnlyList<string> UsbDevices(byte[] packet, uint tag)
        {
            if (packet == null || packet.Length < HeaderSize || DeclaredSize(packet) != packet.Length) return null;
            if (ReadUInt32(packet, 4) != PlistVersion || ReadUInt32(packet, 8) != PlistMessage || ReadUInt32(packet, 12) != tag) return null;

            var end = packet.Length;
            // tolerate a C string's terminator after the document
            while (end > HeaderSize && packet[end - 1] == 0) end--;
            var root = Parse(packet, HeaderSize, end - HeaderSize);
            if (root == null || root.Name.LocalName != "plist") return null;
            var top = Dict(root.Elements().SingleOrDefault());
            if (top == null || !top.TryGetValue("DeviceList", out var list) || list.Name.LocalName != "array") return null;

            var usb = new List<string>();
            foreach (var item in list.Elements())
            {
                var entry = Dict(item);
                if (entry == null) return null;
                if (entry.TryGetValue("MessageType", out var type) && !IsString(type, "Attached")) continue;
                if (!entry.TryGetValue("Properties", out var propertiesValue)) continue;
                var properties = Dict(propertiesValue);
                if (properties == null) return null;
                if (!properties.TryGetValue("ConnectionType", out var connection) || !IsString(connection, "USB")) continue;
                if (!properties.TryGetValue("SerialNumber", out var serial) || serial.Name.LocalName != "string" || serial.Value.Length == 0) continue;
                usb.Add(serial.Value);
            }
            return usb;
        }

        /// <summary>The document's root, or null. No DTD is processed (no entities, nothing fetched) and the size is bounded.</summary>
        private static XElement Parse(byte[] buffer, int offset, int count)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = MaxReplySize,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            };
            try
            {
                using (var stream = new MemoryStream(buffer, offset, count, writable: false))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    return XDocument.Load(reader).Root;
                }
            }
            catch (Exception e) when (e is XmlException || e is DecoderFallbackException)
            {
                // not well-formed, or not in the encoding it declares
                return null;
            }
        }

        /// <summary>A plist &lt;dict&gt; as key to value element, or null when it is not a well-formed one.</summary>
        private static Dictionary<string, XElement> Dict(XElement element)
        {
            if (element == null || element.Name.LocalName != "dict") return null;
            var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
            var children = element.Elements().ToList();
            if (children.Count % 2 != 0) return null;
            for (var i = 0; i < children.Count; i += 2)
            {
                if (children[i].Name.LocalName != "key" || children[i + 1].Name.LocalName == "key") return null;
                result[children[i].Value] = children[i + 1];
            }
            return result;
        }

        private static bool IsString(XElement value, string expected) =>
            value.Name.LocalName == "string" && string.Equals(value.Value, expected, StringComparison.Ordinal);

        private static uint ReadUInt32(byte[] buffer, int offset) =>
            (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
