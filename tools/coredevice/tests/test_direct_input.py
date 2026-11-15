# SPDX-License-Identifier: GPL-3.0-or-later
"""Tests of direct_input.py's stream handling and HTTP surface with a fake device: no device, no pymobiledevice3.

Run: python -I tools/coredevice/tests/test_direct_input.py
(-I keeps the user's site-packages out; the file finds the helper by its path).
"""

import asyncio
import importlib.util
import json
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
HELPER = os.path.join(HERE, "..", "..", "..", "src", "MacExtend.App", "Helpers", "direct_input.py")

spec = importlib.util.spec_from_file_location("direct_input", HELPER)
di = importlib.util.module_from_spec(spec)
spec.loader.exec_module(di)


class FakeClock:
    def __init__(self):
        self.now = 1000.0

    def __call__(self):
        return self.now


class FakeDevice:
    """Records what reaches the device. ``refuse`` makes the next opens fail: "busy" (camera in use) or "broken"."""

    def __init__(self):
        self.events = []
        self.refuse = None
        self.fail_touch = False

    async def open_stream(self):
        if self.refuse == "busy":
            raise di.MediaInUse()
        if self.refuse == "broken":
            raise RuntimeError("wedged")
        self.events.append("open")

    async def close_stream(self):
        self.events.append("close")

    async def touch(self, kind, x, y):
        if self.fail_touch:
            raise ConnectionError("gone")
        self.events.append(("touch", kind, x, y))

    async def keys(self, usages):
        self.events.append(("keys", list(usages)))

    async def button(self, name):
        self.events.append(("button", name))

    async def set_clipboard(self, text):
        self.events.append(("clipboard", text))


class WorkerTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.clock = FakeClock()
        self.device = FakeDevice()
        self.worker = di.Worker(self.device, self.clock)

    async def drain(self):
        while not self.worker.queue.empty():
            await self.worker.handle(self.worker.queue.get_nowait())

    async def test_the_first_input_opens_the_stream_once_then_everything_goes_in_order(self):
        self.worker.submit("touch", ("contact", 1, 2))
        self.worker.submit("touch", ("contact", 3, 4))
        self.worker.submit("touch", ("release", 3, 4))
        await self.drain()

        self.assertEqual(["open", ("touch", "contact", 1, 2), ("touch", "contact", 3, 4), ("touch", "release", 3, 4)], self.device.events)
        self.assertEqual("open", self.worker.state)

    async def test_the_stream_closes_after_twenty_quiet_seconds_and_gives_the_camera_back(self):
        self.worker.submit("touch", ("tap", 5, 5))
        await self.drain()

        self.clock.now += di.IDLE_CLOSE_SECONDS - 1
        await self.worker.on_idle()
        self.assertEqual("open", self.worker.state)
        self.clock.now += 1
        await self.worker.on_idle()

        self.assertEqual("closed", self.worker.state)
        self.assertEqual("close", self.device.events[-1])

    async def test_a_finger_held_down_keeps_the_stream_until_the_hold_limit_then_is_lifted(self):
        self.worker.submit("touch", ("contact", 7, 8))
        await self.drain()

        self.clock.now += di.IDLE_CLOSE_SECONDS + 5
        await self.worker.on_idle()
        self.assertEqual("open", self.worker.state)

        self.clock.now = 1000.0 + di.HELD_LIMIT_SECONDS
        await self.worker.on_idle()
        self.assertEqual("closed", self.worker.state)
        self.assertEqual([("touch", "release", 7, 8), "close"], self.device.events[-2:])

    async def test_keys_held_keep_the_stream_and_are_let_go_on_close(self):
        self.worker.submit("keys", [0xE1])
        await self.drain()
        self.clock.now += di.HELD_LIMIT_SECONDS
        await self.worker.on_idle()

        self.assertEqual([("keys", []), "close"], self.device.events[-2:])

    async def test_releasing_nothing_never_opens_the_stream(self):
        # the app lifts what may be held before it stops the helper: that must not block the camera
        self.worker.submit("touch", ("release", 1, 1))
        self.worker.submit("keys", [])
        await self.drain()

        self.assertEqual([], self.device.events)
        self.assertEqual("closed", self.worker.state)

    async def test_a_busy_camera_refuses_input_for_a_moment_then_it_is_tried_again(self):
        self.device.refuse = "busy"
        self.worker.submit("touch", ("contact", 1, 1))
        self.worker.submit("touch", ("release", 1, 1))
        await self.drain()

        self.assertEqual([], self.device.events)
        self.assertTrue(self.worker.busy())
        self.assertEqual("busy", self.worker.submit("touch", ("tap", 1, 1)))

        self.clock.now += di.BUSY_RETRY_SECONDS
        self.device.refuse = None
        self.assertFalse(self.worker.busy())
        self.assertEqual("queued", self.worker.submit("touch", ("tap", 1, 1)))
        await self.drain()
        self.assertEqual(["open", ("touch", "tap", 1, 1)], self.device.events)

    async def test_a_failed_open_is_not_retried_on_every_move(self):
        self.device.refuse = "broken"
        self.worker.submit("touch", ("contact", 1, 1))
        await self.drain()
        self.device.refuse = None
        self.worker.submit("touch", ("contact", 2, 2))
        await self.drain()
        self.assertEqual([], self.device.events)

        self.clock.now += di.FAILED_RETRY_SECONDS
        self.worker.submit("touch", ("contact", 3, 3))
        await self.drain()
        self.assertEqual(["open", ("touch", "contact", 3, 3)], self.device.events)

    async def test_a_warm_up_opens_without_sending_anything_and_says_what_happened(self):
        warm = asyncio.ensure_future(self.worker.warm())
        await asyncio.sleep(0)
        await self.drain()
        self.assertEqual("open", await warm)
        self.assertEqual(["open"], self.device.events)

        self.device.refuse = "busy"
        await self.worker.close("test")
        warm = asyncio.ensure_future(self.worker.warm())
        await asyncio.sleep(0)
        await self.drain()
        self.assertEqual("busy", await warm)

    async def test_a_device_error_closes_the_stream_and_the_next_input_opens_it_again(self):
        self.worker.submit("touch", ("contact", 1, 1))
        await self.drain()
        self.device.fail_touch = True
        self.worker.submit("touch", ("contact", 2, 2))
        await self.drain()
        self.assertEqual("closed", self.worker.state)

        self.device.fail_touch = False
        self.worker.submit("touch", ("contact", 3, 3))
        await self.drain()
        self.assertEqual(["open", ("touch", "contact", 3, 3)], self.device.events[-2:])

    async def test_buttons_go_through_the_stream_too(self):
        self.worker.submit("button", "home")
        await self.drain()

        self.assertEqual(["open", ("button", "home")], self.device.events)


def rtp(seq, pt=96):
    """A minimal RTP packet: version 2, the payload type, the sequence number, a timestamp and an SSRC."""
    return bytes([0x80, pt & 0x7F]) + seq.to_bytes(2, "big") + b"\x00\x00\x00\x01" + b"\xAA\xBB\xCC\xDD" + b"payload"


class ReceiverReportTests(unittest.TestCase):
    # what start_video_stream answers; the names are the device's view, so RemoteSSRC is ours
    ANSWER = {"connection": {"streamConfig": {"SourcePort": 52000, "LocalSSRC": 0x11223344, "RemoteSSRC": 0x55667788}}}

    def test_the_answer_gives_the_ssrcs_and_where_reports_go(self):
        report = di.ReceiverReport.from_answer(self.ANSWER)

        self.assertEqual(52000, report.port)
        self.assertEqual(0x55667788, report.ours)
        self.assertEqual(0x11223344, report.device)

    def test_an_answer_without_those_fields_gives_no_report(self):
        self.assertIsNone(di.ReceiverReport.from_answer({}))
        self.assertIsNone(di.ReceiverReport.from_answer({"connection": {"streamConfig": {"SourcePort": 1, "LocalSSRC": 0, "RemoteSSRC": 2}}}))
        self.assertIsNone(di.ReceiverReport.from_answer(None))

    def test_the_packet_is_the_rr_and_sdes_serve_web_and_xcode_send(self):
        report = di.ReceiverReport.from_answer(self.ANSWER)
        report.saw(rtp(1000))

        packet = report.packet()

        rr = bytes.fromhex("81c90007" "55667788" "11223344" "00000000" "000003e8" "00000000" "00000000" "00000000")
        sdes = bytes.fromhex("81ca0002" "55667788" "01000000")
        self.assertEqual(rr + sdes, packet)

    def test_the_highest_sequence_number_follows_wraps_and_ignores_late_packets(self):
        report = di.ReceiverReport(1, 2, 3)
        for seq in (65534, 65535, 0, 1):
            report.saw(rtp(seq))
        report.saw(rtp(65533))

        self.assertEqual((1 << 16) | 1, report.highest)
        self.assertEqual(5, report.received)

    def test_rtcp_and_short_packets_are_not_counted(self):
        report = di.ReceiverReport(1, 2, 3)
        report.saw(bytes([0x80, 200]) + b"\x00" * 26)
        report.saw(b"\x80\x60")

        self.assertEqual(0, report.received)


class FakeTransport:
    def __init__(self, packets):
        self.packets = list(packets)
        self.sent = []

    async def recv(self, bufsize=65535):
        if self.packets:
            return self.packets.pop(0)
        await asyncio.sleep(3600)

    async def sendto(self, data, ip, port):
        self.sent.append((data, ip, port))


class KeepAliveTests(unittest.IsolatedAsyncioTestCase):
    async def test_reports_go_to_the_device_once_packets_arrive(self):
        report = di.ReceiverReport(52000, 7, 9)
        transport = FakeTransport([rtp(5), rtp(6)])
        receive = asyncio.ensure_future(di.receive(transport, report))
        keep = asyncio.ensure_future(di.keep_alive(transport, report, "fd00::1", interval=0.01))
        for _ in range(500):
            if len(transport.sent) >= 2:
                break
            await asyncio.sleep(0.005)
        receive.cancel()
        keep.cancel()

        self.assertGreaterEqual(len(transport.sent), 2)
        data, ip, port = transport.sent[-1]
        self.assertEqual(("fd00::1", 52000), (ip, port))
        self.assertEqual(6, int.from_bytes(data[16:20], "big"))

    async def test_no_report_before_the_first_packet(self):
        report = di.ReceiverReport(52000, 7, 9)
        transport = FakeTransport([])
        keep = asyncio.ensure_future(di.keep_alive(transport, report, "fd00::1", interval=0.01))
        await asyncio.sleep(0.05)
        keep.cancel()

        self.assertEqual([], transport.sent)


class HttpTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.clock = FakeClock()
        self.device = FakeDevice()
        self.worker = di.Worker(self.device, self.clock)
        self.task = asyncio.ensure_future(self.worker.run())
        self.server = await asyncio.start_server(di.Server(self.worker, self.device).handle, "127.0.0.1", 0)
        self.port = self.server.sockets[0].getsockname()[1]

    async def asyncTearDown(self):
        self.task.cancel()
        self.server.close()
        await self.server.wait_closed()

    async def request(self, method, path, body=None):
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        data = json.dumps(body).encode() if body is not None else b""
        writer.write((method + " " + path + " HTTP/1.1\r\nHost: x\r\nContent-Length: " + str(len(data)) + "\r\n\r\n").encode() + data)
        await writer.drain()
        status = (await reader.readline()).decode().split(" ", 1)[1].strip()
        length = 0
        while True:
            line = await reader.readline()
            if line == b"\r\n":
                break
            if line.lower().startswith(b"content-length:"):
                length = int(line.split(b":")[1])
        reply = await reader.readexactly(length)
        writer.close()
        return status, reply

    async def test_the_page_carries_the_title_the_app_waits_for(self):
        status, reply = await self.request("GET", "/")
        self.assertEqual("200 OK", status)
        self.assertIn(di.PAGE_TITLE.encode(), reply)

    async def test_input_is_answered_once_queued_and_reaches_the_device(self):
        status, reply = await self.request("POST", "/touch", {"type": "tap", "x": 32768, "y": 100})
        self.assertEqual(("200 OK", b"ok"), (status, reply))
        # the worker sends it after the answer: wait for it, as long as a busy runner needs, no longer
        for _ in range(1000):
            if len(self.device.events) >= 2:
                break
            await asyncio.sleep(0.005)
        self.assertEqual(["open", ("touch", "tap", 32768, 100)], self.device.events)

    async def test_bad_input_is_refused(self):
        for body in ({"type": "swipe", "x": 1, "y": 1}, {"type": "tap", "x": 70000, "y": 1}, {"type": "tap"}):
            status, _ = await self.request("POST", "/touch", body)
            self.assertEqual("400 Bad Request", status)
        status, _ = await self.request("POST", "/key", {"usages": [4, 4]})
        self.assertEqual("400 Bad Request", status)
        status, _ = await self.request("POST", "/button", {"name": "siri"})
        self.assertEqual("400 Bad Request", status)

    async def test_a_busy_camera_is_a_409(self):
        self.device.refuse = "busy"
        status, _ = await self.request("POST", "/warm")
        self.assertEqual("409 Conflict", status)
        status, reply = await self.request("POST", "/touch", {"type": "tap", "x": 1, "y": 1})
        self.assertEqual(("409 Conflict", b"media-in-use"), (status, reply))

    async def test_the_clipboard_needs_no_stream(self):
        status, _ = await self.request("POST", "/clipboard", {"text": "Thịnh"})
        self.assertEqual("200 OK", status)
        self.assertEqual([("clipboard", "Thịnh")], self.device.events)

    async def test_the_state_says_whether_the_stream_is_open(self):
        _, reply = await self.request("GET", "/state")
        self.assertEqual({"stream": "closed", "busy": False}, json.loads(reply))
        await self.request("POST", "/warm")
        _, reply = await self.request("GET", "/state")
        self.assertEqual("open", json.loads(reply)["stream"])


if __name__ == "__main__":
    unittest.main()
