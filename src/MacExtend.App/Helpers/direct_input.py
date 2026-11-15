# SPDX-License-Identifier: GPL-3.0-or-later
#
# MacExtend direct input: touches, keys and buttons for one iPhone or iPad over CoreDevice.
# Copyright (C) 2026 Thinh Hai Trieu
#
# This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
# Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
# option) any later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
# without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General
# Public License for more details: licenses/GPL-3.0.txt next to MacExtend.exe, or <https://www.gnu.org/licenses/>.
#
# It uses pymobiledevice3 (GPL-3.0-or-later) and runs as a process of its own; MacExtend talks to it only over
# HTTP on 127.0.0.1.
"""
iOS takes CoreDevice touches only while a screen-mirroring media stream is open (dtuhidd's authentication gate,
see pymobiledevice3's hid_service.py; keys pass without it, as an external keyboard's would), and that stream
keeps the device's camera and microphone from every app. The device also tears the stream down after 20 s without
RTCP receiver reports (RTCPTimeoutEnabled in its streamConfig), so one goes back every second, as serve-web sends. So the stream here is opened only when input comes (or a warm-up asks for it) and
closed again once nothing has been sent for a while: the rest of the time the camera and microphone are free.

HTTP on 127.0.0.1, the same input API as `serve-web` (one request at a time per connection, keep-alive):

    GET  /           a small page; the app's readiness check looks for its title
    GET  /state      {"stream": "closed"|"opening"|"open", "busy": bool}
    POST /touch      {"type": "contact"|"release"|"tap", "x": 0..65535, "y": 0..65535}
    POST /key        {"usages": [HID keyboard usages held]}
    POST /button     {"name": "home"|"lock"|"volume-up"|"volume-down", "state": "press"}
    POST /clipboard  {"text": "..."}       sets the device pasteboard (no stream needed)
    POST /warm       opens the stream now; answers once it is open (200), refused (409) or failed (503)

Input is answered 200 as soon as it is queued, like serve-web. While the device refused the stream because its
camera or microphone is in use, input is answered 409 "media-in-use" for a few seconds instead, then tried again.
"""

import argparse
import asyncio
import contextlib
import json
import logging
import os
import sys
import time
from typing import Any, Awaitable, Callable, Dict, List, Optional, Tuple

REQUIRED_PYMOBILEDEVICE3 = "11.47.0"
PAGE_TITLE = "<title>MacExtend direct input</title>"
LOGGER_NAME = "macextend.direct_input"

# the stream closes once no input came for this long, and never stays open longer than the hold limit after the last
# input even with a finger or a key still marked down
IDLE_CLOSE_SECONDS = 20.0
HELD_LIMIT_SECONDS = 60.0
# after the device refused the stream (camera or microphone in use), input is refused for this long before trying again
BUSY_RETRY_SECONDS = 3.0
# after any other failure to open, the next open waits this long, so a wedged device is not asked on every move
FAILED_RETRY_SECONDS = 2.0
WARM_ANSWER_SECONDS = 15.0
MAX_BODY = 256 * 1024

# serve-web's own table (screen_stream._NAMED_BUTTONS): HID usage page, usage, how long the button is held
BUTTONS = {
    "home": (0x0C, 0x40, 0.05),
    "lock": (0x0C, 0x30, 0.5),
    "volume-up": (0x0C, 0xE9, 0.05),
    "volume-down": (0x0C, 0xEA, 0.05),
}

log = logging.getLogger(LOGGER_NAME)


class MediaInUse(Exception):
    """The device refused the media stream: an app holds its camera or microphone (iOS error 9022)."""


class Item:
    """One queued request. ``done`` gets what the worker made of it."""

    def __init__(self, kind: str, payload: Any, done: Optional["asyncio.Future[str]"] = None):
        self.kind = kind
        self.payload = payload
        self.done = done


class Worker:
    """
    The one owner of the stream: every request goes through its queue in order. ``device`` does the device side
    (open_stream, close_stream, touch, keys, button), ``clock`` is monotonic seconds. Between requests it closes an
    idle stream. Nothing here imports pymobiledevice3, so it is tested with a fake device.
    """

    def __init__(self, device: Any, clock: Callable[[], float] = time.monotonic):
        self.device = device
        self.clock = clock
        self.queue: "asyncio.Queue[Item]" = asyncio.Queue()
        self.state = "closed"
        self.last_input = clock()
        self.opened_at = 0.0
        self.held_touch: Optional[Tuple[int, int]] = None
        self.held_keys: List[int] = []
        self.busy_since: Optional[float] = None
        self.failed_at: Optional[float] = None

    # ---- the HTTP side -------------------------------------------------------------------

    def busy(self) -> bool:
        """The device refused the stream a moment ago and none is open: input is refused, not queued."""
        return self.state != "open" and self.busy_since is not None and self.clock() - self.busy_since < BUSY_RETRY_SECONDS

    def submit(self, kind: str, payload: Any) -> str:
        """Queues input: "queued", or "busy" while the camera or microphone holds the device."""
        if self.busy():
            return "busy"
        self.queue.put_nowait(Item(kind, payload))
        return "queued"

    async def warm(self) -> str:
        """Opens the stream through the queue: "open", "busy" or "failed"."""
        done: "asyncio.Future[str]" = asyncio.get_running_loop().create_future()
        self.queue.put_nowait(Item("warm", None, done))
        return await done

    # ---- the worker ----------------------------------------------------------------------

    async def run(self) -> None:
        while True:
            try:
                item = await asyncio.wait_for(self.queue.get(), timeout=1.0)
            except asyncio.TimeoutError:
                await self.on_idle()
                continue
            await self.handle(item)

    async def handle(self, item: Item) -> None:
        result = "done"
        try:
            if item.kind == "warm":
                result = await self.ensure_open()
                if result == "open":
                    # a warm-up counts as use: the stream it opens lives its idle time
                    self.last_input = self.clock()
                return
            if not self.needs_stream(item):
                return
            self.last_input = self.clock()
            opened = await self.ensure_open()
            if opened != "open":
                result = opened
                # what was queued behind this one would only fail the same way
                self.drop_queued_input()
                return
            await self.send(item)
        except Exception as e:  # the device went away mid-request, a service broke: the next input reopens
            log.warning("input failed (%s); the stream is closed and opens again on the next input", type(e).__name__)
            await self.close("failed")
            result = "failed"
        finally:
            if item.done is not None and not item.done.done():
                item.done.set_result(result)

    def needs_stream(self, item: Item) -> bool:
        """A release of nothing held never opens the stream: the app's own clean-up must not block the camera."""
        if self.state == "open":
            return True
        if item.kind == "touch" and item.payload[0] == "release":
            return self.held_touch is not None
        if item.kind == "keys":
            return bool(item.payload) or bool(self.held_keys)
        return True

    async def ensure_open(self) -> str:
        if self.state == "open":
            return "open"
        now = self.clock()
        if self.busy():
            return "busy"
        if self.failed_at is not None and now - self.failed_at < FAILED_RETRY_SECONDS:
            return "failed"
        self.state = "opening"
        started = now
        try:
            await self.device.open_stream()
        except MediaInUse:
            self.state = "closed"
            self.busy_since = self.clock()
            log.info("stream refused: camera or microphone in use")
            return "busy"
        except Exception as e:
            self.state = "closed"
            self.failed_at = self.clock()
            log.warning("stream did not open (%s)", type(e).__name__)
            return "failed"
        self.state = "open"
        self.busy_since = None
        self.failed_at = None
        self.opened_at = self.clock()
        log.info("stream open after %.2f s", self.opened_at - started)
        return "open"

    async def send(self, item: Item) -> None:
        if item.kind == "touch":
            kind, x, y = item.payload
            await self.device.touch(kind, x, y)
            self.held_touch = (x, y) if kind == "contact" else None
        elif item.kind == "keys":
            await self.device.keys(item.payload)
            self.held_keys = list(item.payload)
        elif item.kind == "button":
            await self.device.button(item.payload)

    def drop_queued_input(self) -> None:
        kept = []
        while not self.queue.empty():
            queued = self.queue.get_nowait()
            if queued.kind == "warm":
                kept.append(queued)
        for queued in kept:
            self.queue.put_nowait(queued)

    async def on_idle(self) -> None:
        """Closes the stream once nothing came for a while; a finger or a key held keeps it, up to the hold limit."""
        if self.state != "open":
            return
        quiet = self.clock() - self.last_input
        held = self.held_touch is not None or bool(self.held_keys)
        if quiet >= HELD_LIMIT_SECONDS or (quiet >= IDLE_CLOSE_SECONDS and not held):
            await self.close("idle")

    async def close(self, why: str) -> None:
        """Lifts what is held, then closes the stream; best effort, the device frees the camera when the stream stops."""
        if self.state != "open":
            self.state = "closed"
            return
        if self.held_touch is not None:
            with contextlib.suppress(Exception):
                await self.device.touch("release", *self.held_touch)
        if self.held_keys:
            with contextlib.suppress(Exception):
                await self.device.keys([])
        self.held_touch = None
        self.held_keys = []
        self.state = "closed"
        with contextlib.suppress(Exception):
            await self.device.close_stream()
        log.info("stream closed (%s) after %.0f s", why, self.clock() - self.opened_at)


class ReceiverReport:
    """
    The RTCP keep-alive of one media stream (RFC 3550 6.4.2): a receiver report with an empty SDES CNAME, byte for byte
    what serve-web and Xcode's mirror send. Without one every second the device drops the stream after its
    RTCPTimeoutInterval (20 s), and with it the authentication that lets touches through.
    """

    def __init__(self, port: int, ours: int, device: int):
        self.port = port
        self.ours = ours & 0xFFFFFFFF
        self.device = device & 0xFFFFFFFF
        self.received = 0
        # the extended highest sequence number received: wrap cycles in the high 16 bits
        self.highest = 0

    @staticmethod
    def from_answer(answer: Any) -> Optional["ReceiverReport"]:
        """From start_video_stream's answer, whose streamConfig names are the device's view (RemoteSSRC is ours)."""
        try:
            config = answer["connection"]["streamConfig"]
            port, ours, device = int(config["SourcePort"]), int(config["RemoteSSRC"]), int(config["LocalSSRC"])
        except (KeyError, TypeError, ValueError):
            return None
        if not (1 <= port <= 65535) or not ours or not device:
            return None
        return ReceiverReport(port, ours, device)

    def saw(self, packet: bytes) -> None:
        """Counts one RTP packet; the device's own RTCP on the same socket (payload type 64..95 masked) is skipped."""
        if len(packet) < 12 or 64 <= (packet[1] & 0x7F) <= 95:
            return
        seq = int.from_bytes(packet[2:4], "big")
        if self.received == 0:
            self.highest = seq
        else:
            # only a step forward of less than half the sequence space moves it; a late or repeated packet does not
            last = self.highest & 0xFFFF
            step = (seq - last) & 0xFFFF
            if 0 < step < 0x8000:
                cycles = self.highest >> 16
                if seq < last:
                    cycles = (cycles + 1) & 0xFFFF
                self.highest = (cycles << 16) | seq
        self.received += 1

    def packet(self) -> bytes:
        import struct

        rr = struct.pack("!BBHII BBBB IIII", 0x81, 0xC9, 7, self.ours, self.device, 0, 0, 0, 0, self.highest & 0xFFFFFFFF, 0, 0, 0)
        sdes = struct.pack("!BBHI BBBB", 0x81, 0xCA, 2, self.ours, 0x01, 0x00, 0x00, 0x00)
        return rr + sdes


async def receive(transport: Any, report: Optional[ReceiverReport]) -> None:
    """Takes the stream's packets off the socket; the video itself is thrown away."""
    try:
        while True:
            packet = await transport.recv(65535)
            if report is not None:
                report.saw(packet)
    except (asyncio.CancelledError, OSError):
        pass


async def keep_alive(transport: Any, report: ReceiverReport, device_ip: str, interval: float = 1.0) -> None:
    """A receiver report every ``interval`` seconds once the stream has delivered, as serve-web's _rtcp_send_loop."""
    try:
        while True:
            await asyncio.sleep(interval)
            if report.received == 0:
                continue
            await transport.sendto(report.packet(), device_ip, report.port)
    except (asyncio.CancelledError, OSError):
        pass


class PmdDevice:
    """The device side over pymobiledevice3, through an RSD connection (the cable's userspace tunnel or tunneld's)."""

    def __init__(self, rsd: Any, refresh: Optional[Callable[[], Awaitable[Any]]] = None):
        self.rsd = rsd
        # over Wi-Fi, a new connection from tunneld after its tunnel was rebuilt; None over the cable
        self.refresh = refresh
        self.display = None
        self.transport = None
        self.drain = None
        self.keep = None
        self.uhs = None
        self.indigo = None
        self.keyboard = None

    async def open_stream(self) -> None:
        try:
            await self._open_stream()
        except MediaInUse:
            raise
        except Exception as e:
            if self.refresh is None:
                raise
            # tunneld rebuilds its tunnel after the network drops: the old connection is dead, a new one may work
            log.info("stream did not open (%s); asking tunneld for the device again", type(e).__name__)
            fresh = await self.refresh()
            if fresh is None:
                raise
            with contextlib.suppress(Exception):
                await self.rsd.close()
            self.rsd = fresh
            await self._open_stream()

    async def _open_stream(self) -> None:
        from pymobiledevice3.remote.core_device.display_service import DisplayService, is_media_in_use_error
        from pymobiledevice3.remote.core_device.hid_service import TOUCH_AUTH_SETTLE_SECONDS, IndigoHIDService, UniversalHIDServiceService
        from pymobiledevice3.remote.core_device.screen_stream import open_media_receiver

        # the steps of pymobiledevice3's touch_session (a video stream whose packets are thrown away, then the HID
        # services, which iOS now treats as authenticated), plus serve-web's receiver reports, which keep the stream
        # past 20 s; touch_session sends none because its gestures are over by then
        display = DisplayService(self.rsd)
        await display.__aenter__()
        transport = drain = keep = None
        try:
            transport, receiver_ip = open_media_receiver(display, (1 * 1024 * 1024,))
            device_ip = self.rsd.service.address[0]
            try:
                answer = await asyncio.wait_for(
                    display.start_video_stream(
                        receiver_ip=receiver_ip,
                        receiver_port=transport.port,
                        sender_ip=device_ip,
                        display_id=1,
                    ),
                    timeout=10.0,
                )
            except Exception as e:
                if is_media_in_use_error(e):
                    raise MediaInUse() from None
                raise
            report = ReceiverReport.from_answer(answer)
            if report is None:
                # the stream opens, but the device will drop it after 20 s: the log says why touches stop then
                log.warning("input failed (NoRtcpFields); the device will stop the stream after 20 s")
            drain = asyncio.ensure_future(receive(transport, report))
            if report is not None:
                keep = asyncio.ensure_future(keep_alive(transport, report, device_ip))
            await asyncio.sleep(TOUCH_AUTH_SETTLE_SECONDS)
            uhs = UniversalHIDServiceService(self.rsd)
            await uhs.connect()
            indigo = IndigoHIDService(self.rsd)
            await indigo.connect()
        except BaseException:
            for task in (keep, drain):
                if task is not None:
                    task.cancel()
            with contextlib.suppress(BaseException):
                await display.stop_media_stream(stop_all=True)
            with contextlib.suppress(BaseException):
                await display.__aexit__(None, None, None)
            if transport is not None:
                with contextlib.suppress(BaseException):
                    transport.close()
            raise
        self.display, self.transport, self.drain, self.keep, self.uhs, self.indigo = display, transport, drain, keep, uhs, indigo
        # a keyboard is registered against the stream it was made under
        self.keyboard = None

    async def close_stream(self) -> None:
        display, transport, drain, keep, uhs, indigo = self.display, self.transport, self.drain, self.keep, self.uhs, self.indigo
        self.display = self.transport = self.drain = self.keep = self.uhs = self.indigo = self.keyboard = None
        if keep is not None:
            keep.cancel()
            with contextlib.suppress(BaseException):
                await keep
        for service in (uhs, indigo):
            if service is not None:
                with contextlib.suppress(Exception):
                    await asyncio.wait_for(service.close(), timeout=3.0)
        if drain is not None:
            drain.cancel()
            with contextlib.suppress(BaseException):
                await drain
        if display is not None:
            # this stop is what gives the camera and microphone back
            with contextlib.suppress(Exception):
                await asyncio.wait_for(display.stop_media_stream(stop_all=True), timeout=6.0)
            with contextlib.suppress(Exception):
                await display.__aexit__(None, None, None)
        if transport is not None:
            with contextlib.suppress(Exception):
                transport.close()

    async def touch(self, kind: str, x: int, y: int) -> None:
        from pymobiledevice3.remote.core_device.hid_service import (
            DIGITIZER_SURFACE_MAIN_TOUCHSCREEN,
            TOUCHSCREEN_STATE_CONTACT,
            TOUCHSCREEN_STATE_RELEASE,
        )

        surface = DIGITIZER_SURFACE_MAIN_TOUCHSCREEN
        if kind == "contact":
            await self.uhs.send_touchscreen(TOUCHSCREEN_STATE_CONTACT, x, y, service_id=surface)
        elif kind == "release":
            await self.uhs.send_touchscreen(TOUCHSCREEN_STATE_RELEASE, x, y, service_id=surface)
        else:
            await self.uhs.send_touchscreen(TOUCHSCREEN_STATE_CONTACT, x, y, service_id=surface)
            await asyncio.sleep(0.05)
            await self.uhs.send_touchscreen(TOUCHSCREEN_STATE_RELEASE, x, y, service_id=surface)

    async def keys(self, usages: List[int]) -> None:
        if self.keyboard is None:
            self.keyboard = await self.uhs.create_keyboard_service()
        await self.uhs.send_keyboard(self.keyboard, usages)

    async def button(self, name: str) -> None:
        from pymobiledevice3.remote.core_device.hid_service import HID_BUTTON_STATE_DOWN, HID_BUTTON_STATE_UP

        page, usage, hold = BUTTONS[name]
        await self.indigo.send_button(page, usage, HID_BUTTON_STATE_DOWN)
        await asyncio.sleep(hold)
        await self.indigo.send_button(page, usage, HID_BUTTON_STATE_UP)

    async def set_clipboard(self, text: str) -> None:
        from pymobiledevice3.remote.core_device.pasteboard_service import PasteboardService

        async with PasteboardService(self.rsd) as pasteboard:
            await pasteboard.set_text(text)


# ---- HTTP ------------------------------------------------------------------------------------


def parse_input(path: str, body: bytes) -> Tuple[str, Any]:
    """The worker item of an input request; ValueError when the body is not one."""
    data = json.loads(body.decode("utf-8") or "null")
    if not isinstance(data, dict):
        raise ValueError("not an object")
    if path == "/touch":
        kind = data.get("type")
        if kind not in ("contact", "release", "tap"):
            raise ValueError("unknown touch type")
        x, y = int(data["x"]), int(data["y"])
        if not (0 <= x <= 65535 and 0 <= y <= 65535):
            raise ValueError("coordinates out of range")
        return "touch", (kind, x, y)
    if path == "/key":
        usages = [int(u) for u in data.get("usages", [])]
        if any(u < 0x04 or u > 0xE7 for u in usages) or len(set(usages)) != len(usages):
            raise ValueError("bad usages")
        return "keys", sorted(usages)
    if path == "/button":
        name = data.get("name")
        if name not in BUTTONS or data.get("state", "press") != "press":
            raise ValueError("unknown button")
        return "button", name
    raise ValueError("unknown path")


class Server:
    def __init__(self, worker: Worker, device: Any):
        self.worker = worker
        self.device = device

    async def handle(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        try:
            while True:
                request = await _read_request(reader)
                if request is None:
                    return
                method, path, body = request
                status, content_type, reply = await self.route(method, path, body)
                _write_response(writer, status, content_type, reply)
                await writer.drain()
        except (ConnectionError, asyncio.IncompleteReadError, ValueError):
            pass
        finally:
            with contextlib.suppress(Exception):
                writer.close()

    async def route(self, method: str, path: str, body: bytes) -> Tuple[str, str, bytes]:
        if method == "GET" and path == "/":
            page = "<!doctype html><html><head><meta charset=\"utf-8\">" + PAGE_TITLE + "</head><body></body></html>"
            return "200 OK", "text/html; charset=utf-8", page.encode()
        if method == "GET" and path == "/state":
            state = {"stream": self.worker.state, "busy": self.worker.busy()}
            return "200 OK", "application/json", json.dumps(state).encode()
        if method == "POST" and path in ("/touch", "/key", "/button"):
            try:
                kind, payload = parse_input(path, body)
            except (ValueError, KeyError, TypeError) as e:
                return "400 Bad Request", "text/plain", ("invalid request: " + type(e).__name__).encode()
            if self.worker.submit(kind, payload) == "busy":
                return "409 Conflict", "text/plain", b"media-in-use"
            return "200 OK", "text/plain", b"ok"
        if method == "POST" and path == "/warm":
            try:
                result = await asyncio.wait_for(self.worker.warm(), timeout=WARM_ANSWER_SECONDS)
            except asyncio.TimeoutError:
                result = "failed"
            if result == "open":
                return "200 OK", "application/json", b'{"stream":"open"}'
            if result == "busy":
                return "409 Conflict", "text/plain", b"media-in-use"
            return "503 Service Unavailable", "text/plain", b"stream-failed"
        if method == "POST" and path == "/clipboard":
            try:
                text = str(json.loads(body.decode("utf-8"))["text"])
            except (ValueError, KeyError, TypeError):
                return "400 Bad Request", "text/plain", b"invalid request"
            try:
                await asyncio.wait_for(self.device.set_clipboard(text), timeout=12.0)
            except Exception as e:
                log.warning("clipboard failed (%s)", type(e).__name__)
                return "500 Internal Server Error", "text/plain", b"clipboard-failed"
            return "200 OK", "application/json", b'{"ok":true}'
        return "404 Not Found", "text/plain", b"not found"


async def _read_request(reader: asyncio.StreamReader) -> Optional[Tuple[str, str, bytes]]:
    line = await reader.readline()
    if not line:
        return None
    parts = line.decode("latin-1").split()
    if len(parts) < 2:
        raise ValueError("bad request line")
    method, path = parts[0].upper(), parts[1].split("?", 1)[0]
    length = 0
    while True:
        header = await reader.readline()
        if header in (b"\r\n", b"\n", b""):
            break
        name, _, value = header.decode("latin-1").partition(":")
        if name.strip().lower() == "content-length":
            length = int(value.strip())
    if length < 0 or length > MAX_BODY:
        raise ValueError("body too large")
    body = await reader.readexactly(length) if length else b""
    return method, path, body


def _write_response(writer: asyncio.StreamWriter, status: str, content_type: str, body: bytes) -> None:
    head = (
        "HTTP/1.1 " + status + "\r\n"
        "Content-Type: " + content_type + "\r\n"
        "Content-Length: " + str(len(body)) + "\r\n"
        "Cache-Control: no-store\r\n"
        "Connection: keep-alive\r\n\r\n"
    )
    writer.write(head.encode("latin-1") + body)


# ---- the process -----------------------------------------------------------------------------


def configure_logging() -> None:
    """One line per record, shaped like pymobiledevice3's own ("date time host logger[pid] LEVEL message") so the app
    reads both the same way; the host field is a fixed word, never the machine's name."""
    handler = logging.StreamHandler(sys.stderr)
    handler.setFormatter(logging.Formatter("%(asctime)s localhost %(name)s[%(process)d] %(levelname)s %(message)s", "%Y-%m-%d %H:%M:%S"))
    root = logging.getLogger()
    root.handlers[:] = [handler]
    root.setLevel(logging.WARNING)
    log.setLevel(logging.INFO)


def check() -> int:
    """--check: the installed pymobiledevice3 is the one this file was written against and has every name it uses."""
    import importlib.metadata as metadata

    version = metadata.version("pymobiledevice3")
    if version != REQUIRED_PYMOBILEDEVICE3:
        print("pymobiledevice3 is " + version + ", direct_input.py needs " + REQUIRED_PYMOBILEDEVICE3)
        return 1
    from pymobiledevice3.remote.core_device.display_service import DisplayService, is_media_in_use_error  # noqa: F401
    from pymobiledevice3.remote.core_device.hid_service import (  # noqa: F401
        DIGITIZER_SURFACE_MAIN_TOUCHSCREEN,
        HID_BUTTON_STATE_DOWN,
        HID_BUTTON_STATE_UP,
        TOUCH_AUTH_SETTLE_SECONDS,
        TOUCHSCREEN_STATE_CONTACT,
        TOUCHSCREEN_STATE_RELEASE,
        IndigoHIDService,
        UniversalHIDServiceService,
    )
    from pymobiledevice3.remote.core_device.pasteboard_service import PasteboardService  # noqa: F401
    from pymobiledevice3.remote.core_device.screen_stream import open_media_receiver  # noqa: F401
    from pymobiledevice3.remote.userspace_tunnel import UserspaceRsdTunnel  # noqa: F401
    from pymobiledevice3.tunneld.api import get_tunneld_device_by_udid  # noqa: F401

    for name in ("start_video_stream", "stop_media_stream"):
        if not hasattr(DisplayService, name):
            print("DisplayService has no " + name)
            return 1
    for name in ("send_touchscreen", "send_keyboard", "create_keyboard_service"):
        if not hasattr(UniversalHIDServiceService, name):
            print("UniversalHIDServiceService has no " + name)
            return 1
    print("ok")
    return 0


async def serve(args: argparse.Namespace) -> int:
    import importlib.metadata as metadata

    version = metadata.version("pymobiledevice3")
    if version != REQUIRED_PYMOBILEDEVICE3:
        log.error("pymobiledevice3 is %s, direct_input.py needs %s", version, REQUIRED_PYMOBILEDEVICE3)
        return 2
    udid = args.tunnel or os.environ.get("PYMOBILEDEVICE3_UDID") or None
    tunnel = None
    rsd = None
    try:
        if args.tunnel:
            from pymobiledevice3.tunneld.api import get_tunneld_device_by_udid

            rsd = await get_tunneld_device_by_udid(args.tunnel)
            if rsd is None:
                log.error("Device not found: tunneld serves no tunnel for this device")
                return 3
        else:
            from pymobiledevice3.remote.userspace_tunnel import UserspaceRsdTunnel

            tunnel = UserspaceRsdTunnel(serial=udid, remotepairing_fallback=False)
            rsd = await tunnel.aopen()
    except Exception as e:
        log.error("%s: the device could not be reached", type(e).__name__)
        return 3

    refresh = None
    if args.tunnel:
        from pymobiledevice3.tunneld.api import get_tunneld_device_by_udid

        async def refresh() -> Any:
            return await get_tunneld_device_by_udid(args.tunnel)

    device = PmdDevice(rsd, refresh)
    worker = Worker(device)
    server = Server(worker, device)
    worker_task = asyncio.ensure_future(worker.run())
    http = await asyncio.start_server(server.handle, args.bind, args.http_port)
    log.info("listening on http://%s:%d/", args.bind, args.http_port)
    try:
        await asyncio.Event().wait()
    finally:
        # Ctrl+C from the app: give the camera back first, then let go of the device
        log.info("shutting down")
        http.close()
        worker_task.cancel()
        with contextlib.suppress(BaseException):
            await worker_task
        with contextlib.suppress(BaseException):
            await asyncio.wait_for(worker.close("shutdown"), timeout=10.0)
        with contextlib.suppress(BaseException):
            if tunnel is not None:
                await asyncio.wait_for(tunnel.aclose(), timeout=5.0)
            else:
                await asyncio.wait_for(device.rsd.close(), timeout=5.0)
        log.info("shutdown complete")
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="MacExtend direct input for one iOS device")
    parser.add_argument("--check", action="store_true", help="check the installed pymobiledevice3 and exit")
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--http-port", type=int, default=0)
    transport = parser.add_mutually_exclusive_group()
    transport.add_argument("--userspace", action="store_true", help="the cable's in-process tunnel; the device from PYMOBILEDEVICE3_UDID")
    transport.add_argument("--tunnel", metavar="UDID", help="tunneld's tunnel to this device (Wi-Fi)")
    args = parser.parse_args(argv)
    if args.check:
        return check()
    configure_logging()
    if args.bind != "127.0.0.1" or not (1 <= args.http_port <= 65535) or not (args.userspace or args.tunnel):
        log.error("usage: --bind 127.0.0.1 --http-port PORT (--userspace | --tunnel UDID)")
        return 2
    try:
        return asyncio.run(serve(args))
    except KeyboardInterrupt:
        # a second Ctrl+C, or one before the loop ran: the app's job object ends what is left
        return 130


if __name__ == "__main__":
    sys.exit(main())
