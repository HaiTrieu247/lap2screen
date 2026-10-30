"""Stand-in for `pymobiledevice3 developer core-device display serve-web` in the probe's smoke tests.

It takes the same arguments, logs the same lines and answers the same HTTP requests the probe uses,
without a device. MACEXTEND_FAKE_SCENARIO picks the behaviour; everything it sees goes to
MACEXTEND_FAKE_ARTIFACTS/events.jsonl, a temporary test directory. The request bodies there are the
smoke test's own synthetic input.
"""

import http.server
import json
import os
import signal
import subprocess
import sys
import threading
import time

SCENARIO = os.environ.get("MACEXTEND_FAKE_SCENARIO", "normal")
ARTIFACTS = os.environ["MACEXTEND_FAKE_ARTIFACTS"]
CLEANUP_SECONDS = float(os.environ.get("MACEXTEND_FAKE_CLEANUP_SECONDS", "1"))
# how late each /touch contact (finger down or move) is answered; releases are always answered at once
CONTACT_DELAY = float(os.environ.get("MACEXTEND_FAKE_CONTACT_DELAY_MS", "0")) / 1000
VIEWER = b"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>pymobiledevice3 screen</title></head><body></body></html>"

# reentrant: the SIGINT handler runs on the main thread, maybe while it holds the lock
_events_lock = threading.RLock()
_sigints = 0
_stop = threading.Event()


def event(name, **fields):
    fields["event"] = name
    fields["t"] = time.monotonic()
    with _events_lock, open(os.path.join(ARTIFACTS, "events.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps(fields) + "\n")


def log(logger, level, message):
    # the ansilogs format serve-web's lines use: <time> <host> <logger>[<pid>] <LEVEL> <message>
    stamp = time.strftime("%Y-%m-%d %H:%M:%S")
    sys.stderr.write(f"{stamp} fakehost {logger}[{os.getpid()}] {level} {message}\n")
    sys.stderr.flush()


def stream_log(level, message):
    log("pymobiledevice3.remote.core_device.screen_stream", level, message)


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):
        pass

    def handle_expect_100(self):
        # serve-web never answers 100 Continue; a client that waits for one stalls, so refuse it loudly
        event("request", path=self.path, method=self.command, expect=self.headers.get("Expect"), body=None)
        self.send_response(417)
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "close")
        self.end_headers()
        return False

    def _reply(self, code, body, content_type="text/plain"):
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/":
            self._reply(200, VIEWER, "text/html; charset=utf-8")
        else:
            self._reply(404, b"")

    def do_POST(self):
        if self.headers.get("Transfer-Encoding"):
            # serve-web reads Content-Length only
            event("request", path=self.path, method="POST", expect=None, body=None, chunked=True)
            self._reply(411, b"")
            return
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length > 0 else b""
        try:
            body = json.loads(raw)
        except ValueError:
            body = {"unparsed": raw.decode("utf-8", "replace")}
        event("request", path=self.path, method="POST", expect=self.headers.get("Expect"), body=body,
              content_type=self.headers.get("Content-Type"))
        if self.path not in ("/touch", "/key", "/button"):
            self._reply(404, b"")
            return
        if CONTACT_DELAY and self.path == "/touch" and isinstance(body, dict) and body.get("type") == "contact":
            time.sleep(CONTACT_DELAY)
        try:
            self._reply(200, b"ok")
        except (BrokenPipeError, ConnectionAbortedError, ConnectionResetError):
            # the probe gave up on this request (a gesture cut at its deadline) and closed the connection
            event("reply_dropped", path=self.path)


def on_sigint(signum, frame):
    global _sigints
    _sigints += 1
    event("sigint", count=_sigints)
    _stop.set()


def wait_for_stop():
    # time.sleep is interruptible by Ctrl+C on Windows; Event.wait is not
    while not _stop.is_set():
        time.sleep(0.05)


def cleanup(server, descendant, step_timeout=False):
    event("cleanup_begin")
    if SCENARIO == "hang_cleanup":
        while True:
            time.sleep(1)
    deadline = time.monotonic() + CLEANUP_SECONDS
    while time.monotonic() < deadline:
        time.sleep(0.1)
    if server is not None:
        server.shutdown()
        server.server_close()
    if descendant is not None:
        descendant.kill()
        descendant.wait()
    if step_timeout:
        stream_log("WARNING", "shutdown: stop_all_streams timed out after 6.0s")
    stream_log("INFO", "shutdown complete")
    event("cleanup_done")


def main():
    signal.signal(signal.SIGINT, on_sigint)
    args = sys.argv[1:]
    env = {k: v for k, v in os.environ.items() if k.upper().startswith("PYMOBILEDEVICE3_")}
    event("started", pid=os.getpid(), argv=args, env=env, no_color=os.environ.get("NO_COLOR"),
          isolated=sys.flags.isolated, utf8_mode=sys.flags.utf8_mode)

    prefix = ["developer", "core-device", "display", "serve-web", "--bind", "127.0.0.1", "--http-port"]
    if args[:len(prefix)] != prefix:
        event("bad_argv")
        return 2
    port = int(args[len(prefix)])

    # a grandchild that ignores Ctrl+C: only this script's cleanup or the probe's job can end it. The base
    # interpreter, not the venv launcher, so the PID recorded here is the process that runs.
    descendant = subprocess.Popen(
        [getattr(sys, "_base_executable", sys.executable), "-I", "-c", "import signal, time\nsignal.signal(signal.SIGINT, signal.SIG_IGN)\nwhile True: time.sleep(1)"],
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    event("descendant", pid=descendant.pid)

    if SCENARIO == "early_exit_media":
        log("__main__", "ERROR",
            "The device's camera or microphone is in use by another app (e.g. Camera or Voice Memos). "
            "Quit that app on the device, then retry")
        descendant.kill()
        return 1

    server = None
    if SCENARIO == "never_ready":
        pass
    elif SCENARIO == "foreign_listener":
        # the probe's port check has passed; wait until the driver has taken the port, then claim it
        flag = os.path.join(ARTIFACTS, "go")
        for _ in range(600):
            if os.path.exists(flag):
                break
            time.sleep(0.05)
        stream_log("INFO", f"Open http://127.0.0.1:{port}/ in Safari/Chrome. Ctrl-C to stop.")
    else:
        server = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
        server.daemon_threads = True
        threading.Thread(target=server.serve_forever, daemon=True).start()
        event("listening", port=port)
        stream_log("INFO", "WebCodecs codec string: hvc1.1.6.L153.B0")
        stream_log("INFO", "hid worker started")
        stream_log("INFO", f"Open http://127.0.0.1:{port}/ in Safari/Chrome. Ctrl-C to stop.")

    if SCENARIO == "exit_after_ready":
        time.sleep(3)
        server.shutdown()
        server.server_close()
        descendant.kill()
        event("exited_on_its_own")
        return 1

    wait_for_stop()
    cleanup(server, descendant, step_timeout=SCENARIO == "media_step_timeout")
    return 130


if __name__ == "__main__":
    sys.exit(main())
