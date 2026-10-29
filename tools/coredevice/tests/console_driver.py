"""Windows smoke tests for MacExtend.CoreDeviceProbe against fake_serve_web.py.

Each scenario starts the probe in a hidden console of its own (so a Ctrl+C sent there never reaches the
CI runner), drives its menu through stdin, and checks what the fake recorded: argv, environment,
requests, the number of Ctrl+C signals, cleanup, and that every process of the tree is gone and the
port is free afterwards. Run through test-probe.ps1 with the venv's interpreter (psutil comes from the
lock file).

    python -I console_driver.py --probe PATH --python VENV_PYTHON --fake PATH [--only NAME ...]
    python -I console_driver.py ctrl-c PID     (internal: send one Ctrl+C to PID's console)
"""

import argparse
import ctypes
import http.server
import json
import os
import queue
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
import traceback

UDID = "00008110-000A1234ABCD801E"
CREATE_NEW_CONSOLE = 0x00000010
CREATE_NO_WINDOW = 0x08000000


def send_ctrl_c(pid):
    """Attach to pid's console and send one CTRL_C_EVENT to it, ignoring it here."""
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.FreeConsole()
    if not kernel32.AttachConsole(pid):
        return 10
    kernel32.SetConsoleCtrlHandler(None, True)
    return 0 if kernel32.GenerateConsoleCtrlEvent(0, 0) else 11


class Failure(Exception):
    pass


def check(condition, message):
    if not condition:
        raise Failure(message)


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def listeners(port):
    import psutil
    return [c for c in psutil.net_connections(kind="tcp") if c.status == psutil.CONN_LISTEN and c.laddr and c.laddr.port == port]


class Probe:
    def __init__(self, ctx, scenario, extra_args=(), env_extra=None, port=None):
        self.ctx = ctx
        self.artifacts = tempfile.mkdtemp(prefix="coredevice-probe-")
        self.port = port or free_port()
        env = dict(os.environ)
        # inherited device selection the probe has to drop before serve-web sees it
        env["PYMOBILEDEVICE3_TUNNEL"] = "0000000000000000000000000000000000000000"
        env["pymobiledevice3_userspace"] = "1"
        env["PYMOBILEDEVICE3_DEFAULT_FALLBACK"] = "tunneld"
        env["MACEXTEND_FAKE_SCENARIO"] = scenario
        env["MACEXTEND_FAKE_ARTIFACTS"] = self.artifacts
        env.update(env_extra or {})
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
        args = [ctx.probe, "--python", ctx.python, "--udid", UDID, "--port", str(self.port),
                "--test-serve-script", ctx.fake, *extra_args]
        self.proc = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     creationflags=CREATE_NEW_CONSOLE, startupinfo=startup, env=env,
                                     text=True, encoding="utf-8", errors="replace", bufsize=1)
        self.lines = []
        self._queue = queue.Queue()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        for line in self.proc.stdout:
            line = line.rstrip("\r\n")
            self.lines.append(line)
            self._queue.put(line)
        self._queue.put(None)

    def wait_for(self, pattern, timeout):
        regex = re.compile(pattern)
        for line in self.lines:
            match = regex.search(line)
            if match:
                return match
        deadline = time.monotonic() + timeout
        while True:
            left = deadline - time.monotonic()
            check(left > 0, f"no line matching {pattern!r} within {timeout} s")
            try:
                line = self._queue.get(timeout=left)
            except queue.Empty:
                continue
            check(line is not None, f"probe output ended before a line matching {pattern!r}")
            match = regex.search(line)
            if match:
                return match

    def saw(self, pattern):
        return any(re.search(pattern, line) for line in self.lines)

    def send(self, line):
        self.proc.stdin.write(line + "\n")
        self.proc.stdin.flush()

    def ctrl_c(self):
        base = getattr(sys, "_base_executable", sys.executable)
        rc = subprocess.run([base, "-I", os.path.abspath(__file__), "ctrl-c", str(self.proc.pid)],
                            creationflags=CREATE_NO_WINDOW, timeout=15).returncode
        check(rc == 0, f"could not send Ctrl+C to the probe console (helper exit {rc})")

    def close_stdin(self):
        self.proc.stdin.close()

    def exit_code(self, timeout):
        try:
            return self.proc.wait(timeout)
        except subprocess.TimeoutExpired:
            raise Failure(f"probe still running after {timeout} s")

    def ready(self, timeout=90):
        return self.wait_for(r"\[probe\] ready url=", timeout)

    def events(self):
        path = os.path.join(self.artifacts, "events.jsonl")
        if not os.path.exists(path):
            return []
        with open(path, encoding="utf-8") as f:
            return [json.loads(line) for line in f if line.strip()]

    def of(self, name):
        return [e for e in self.events() if e["event"] == name]

    def requests(self):
        return [e for e in self.of("request")]

    def tree(self):
        """psutil handles for the launcher (child pid), the fake interpreter and its descendant."""
        import psutil
        pids = set()
        match = re.search(r"\[probe\] child pid=(\d+)", "\n".join(self.lines))
        if match:
            pids.add(int(match.group(1)))
        for e in self.of("started") + self.of("descendant"):
            pids.add(e["pid"])
        handles = []
        for pid in pids:
            try:
                handles.append(psutil.Process(pid))
            except psutil.NoSuchProcess:
                pass
        return handles

    def status_pids(self):
        self.send("status")
        match = self.wait_for(r"\[probe\] status .*job_pids=(\S+)", 15)
        return set() if match.group(1) == "none" else {int(p) for p in match.group(1).split(",")}

    def check_gone(self, handles, timeout=15):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            alive = [p.pid for p in handles if p.is_running()]
            if not alive and not listeners(self.port):
                return
            time.sleep(0.2)
        alive = [p.pid for p in handles if p.is_running()]
        raise Failure(f"left behind: processes {alive}, listeners {[c.pid for c in listeners(self.port)]}")

    def check_launch(self, transport_args=("--userspace",)):
        started = self.of("started")
        check(len(started) == 1, f"fake started {len(started)} times")
        s = started[0]
        expected = ["developer", "core-device", "display", "serve-web", "--bind", "127.0.0.1",
                    "--http-port", str(self.port), "--no-audio", *transport_args]
        check(s["argv"] == expected, f"argv {s['argv']} != {expected}")
        check(s["env"] == {"PYMOBILEDEVICE3_UDID": UDID}, f"PYMOBILEDEVICE3_* seen by serve-web: {s['env']}")
        check(s["no_color"] == "1", "NO_COLOR not set")
        check(s["isolated"] == 1 and s["utf8_mode"] == 1, "interpreter not started with -I -X utf8")

    def check_tree_in_job(self):
        job = self.status_pids()
        tree = {p.pid for p in self.tree()}
        check(tree and tree <= job, f"processes {sorted(tree - job)} are outside the job {sorted(job)}")

    def check_one_sigint(self):
        sigints = self.of("sigint")
        check(len(sigints) == 1, f"serve-web saw {len(sigints)} Ctrl+C signals, expected exactly 1")

    def check_cleanup_done(self):
        check(self.of("cleanup_done"), "serve-web cleanup did not finish")

    def close(self):
        if self.proc.poll() is None:
            self.proc.kill()
        if self.ctx.keep:
            print(f"    artifacts: {self.artifacts}")
        else:
            shutil.rmtree(self.artifacts, ignore_errors=True)


def body_of(request):
    return json.dumps(request["body"], separators=(",", ":"), sort_keys=False)


def scenario_normal_stop(ctx):
    p = Probe(ctx, "normal")
    try:
        p.ready()
        p.check_launch()
        p.check_tree_in_job()
        tree = p.tree()
        for command, done in [("tap 100 200", "sent tap"), ("hold 300 400 200", "sent hold"),
                              ("drag 0 0 65535 65535 100", "sent drag"), ("key 4 0xE1", r"sent key press \(2 usages\)"),
                              ("home", "sent home"), ("release", "release: released")]:
            p.send(command)
            p.wait_for(r"\[probe\] " + done, 20)
        check(not p.saw(r"\[probe\] error"), "an input command failed: " + "; ".join(l for l in p.lines if "error" in l))

        requests = p.requests()
        check(all(r.get("expect") is None for r in requests), "a request carried an Expect header")
        check(all(r.get("content_type", "").startswith("application/json") for r in requests), "a request was not JSON")
        bodies = [(r["path"], body_of(r)) for r in requests]
        differ = "requests differ from the commands (order, payload or a retry):\n  got %s" % bodies
        check(bodies[:3] == [("/touch", '{"type":"tap","x":100,"y":200}'),
                             ("/touch", '{"type":"contact","x":300,"y":400}'),
                             ("/touch", '{"type":"release","x":300,"y":400}')], differ)
        # the drag moves on the clock, one move per 16 ms frame at most, so only its shape is fixed
        drag_release = ("/touch", '{"type":"release","x":65535,"y":65535}')
        check(drag_release in bodies[3:], differ)
        end = bodies.index(drag_release, 3)
        check(bodies[3] == ("/touch", '{"type":"contact","x":0,"y":0}'), differ)
        moves = [r["body"] for r in requests[4:end]]
        check(moves and moves[-1] == {"type": "contact", "x": 65535, "y": 65535}, differ)
        check(all(m["type"] == "contact" and m["x"] == m["y"] for m in moves), differ)
        check([m["x"] for m in moves] == sorted(m["x"] for m in moves), "the drag went backwards: %s" % moves)
        check(len(moves) <= 100 // 16 + 1, f"{len(moves)} moves for a 100 ms drag: more than one per frame")
        check(bodies[end + 1:] == [("/key", '{"usages":[4,225]}'), ("/key", '{"usages":[]}'),
                                   ("/button", '{"name":"home","state":"press"}'),
                                   drag_release, ("/key", '{"usages":[]}')], differ)
        check(not p.saw(r"sent key press .*\d{2,}"), "the probe echoed key usages")

        p.send("stop")
        p.wait_for(r"\[probe\] stopped exit=graceful cleanup=reported-complete port=released", 60)
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(tree)
        p.send("stop")
        p.send("quit")
        check(p.exit_code(30) == 0, "exit code is not 0")
        p.check_one_sigint()
    finally:
        p.close()


def scenario_ctrl_c_over_tunnel(ctx):
    p = Probe(ctx, "normal", extra_args=["--transport", "tunnel"])
    try:
        p.ready()
        p.check_launch(transport_args=("--tunnel", UDID))
        tree = p.tree()
        p.ctrl_c()
        p.wait_for(r"\[probe\] stopped exit=graceful", 60)
        check(p.exit_code(30) == 0, "exit code is not 0")
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(tree)
    finally:
        p.close()


def scenario_repeated_ctrl_c(ctx):
    p = Probe(ctx, "normal", env_extra={"MACEXTEND_FAKE_CLEANUP_SECONDS": "6"})
    try:
        p.ready()
        tree = p.tree()
        p.ctrl_c()
        p.wait_for(r"\[probe\] sent one Ctrl\+C to serve-web", 30)
        for _ in range(3):
            time.sleep(0.7)
            p.ctrl_c()
        p.wait_for(r"\[probe\] stopped exit=graceful", 60)
        check(p.saw(r"\[probe\] already stopping"), "a repeated Ctrl+C was not reported")
        check(p.exit_code(30) == 0, "exit code is not 0")
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(tree)
    finally:
        p.close()


def scenario_repeated_stop(ctx):
    # a stop typed while one runs waits its turn and changes nothing; the menu stays usable afterwards
    p = Probe(ctx, "normal", env_extra={"MACEXTEND_FAKE_CLEANUP_SECONDS": "4"})
    try:
        p.ready()
        tree = p.tree()
        p.send("stop")
        p.wait_for(r"\[probe\] sent one Ctrl\+C to serve-web", 30)
        p.send("stop")
        p.send("status")
        p.wait_for(r"\[probe\] stopped exit=graceful cleanup=reported-complete", 60)
        p.wait_for(r"\[probe\] status state=Stopped", 15)
        p.send("stop")
        p.send("quit")
        check(p.exit_code(30) == 0, "exit code is not 0")
        check(sum(1 for line in p.lines if re.search(r"\[probe\] stopping: ", line)) == 1, "the stop ran more than once")
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(tree)
    finally:
        p.close()


def scenario_ctrl_c_during_stop(ctx):
    # Ctrl+C while a stop command runs joins that stop, sends serve-web no second signal, and exits the probe
    # once the stop is done, without a quit
    p = Probe(ctx, "normal", env_extra={"MACEXTEND_FAKE_CLEANUP_SECONDS": "4"})
    try:
        p.ready()
        tree = p.tree()
        p.send("stop")
        p.wait_for(r"\[probe\] sent one Ctrl\+C to serve-web", 30)
        p.ctrl_c()
        p.wait_for(r"\[probe\] already stopping", 15)
        p.wait_for(r"\[probe\] stopped exit=graceful cleanup=reported-complete", 60)
        check(p.exit_code(30) == 0, "the probe did not exit with 0 after Ctrl+C")
        check(sum(1 for line in p.lines if re.search(r"\[probe\] stopping: ", line)) == 1, "the stop ran more than once")
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(tree)
    finally:
        p.close()


def scenario_eof(ctx):
    p = Probe(ctx, "normal")
    try:
        p.ready()
        tree = p.tree()
        p.close_stdin()
        p.wait_for(r"\[probe\] stopped exit=graceful", 60)
        check(p.exit_code(30) == 0, "exit code is not 0")
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(tree)
    finally:
        p.close()


def scenario_long_cleanup(ctx):
    p = Probe(ctx, "normal", env_extra={"MACEXTEND_FAKE_CLEANUP_SECONDS": "28"})
    try:
        p.ready()
        tree = p.tree()
        p.send("stop")
        p.wait_for(r"\[probe\] stopped exit=graceful cleanup=reported-complete", 60)
        for second in (5, 10, 15, 20, 25):
            check(p.saw(rf"waiting for serve-web cleanup: {second} s of 40 s"), f"no progress report at {second} s")
        check(not p.saw("terminating"), "the probe terminated serve-web during a 28 s cleanup")
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(tree)
        p.send("quit")
        check(p.exit_code(30) == 0, "exit code is not 0")
    finally:
        p.close()


def scenario_grace_expires(ctx):
    p = Probe(ctx, "hang_cleanup", extra_args=["--test-grace", "4"])
    try:
        p.ready()
        tree = p.tree()
        p.send("stop")
        p.wait_for(r"\[probe\] stopped exit=killed cleanup=unconfirmed", 60)
        check(p.of("cleanup_begin") and not p.of("cleanup_done"), "the hanging cleanup should have been cut")
        p.check_one_sigint()
        p.check_gone(tree)
        p.send("quit")
        check(p.exit_code(30) == 3, "exit code is not 3 (killed)")
    finally:
        p.close()


def scenario_media_step_timeout(ctx):
    p = Probe(ctx, "media_step_timeout")
    try:
        p.ready()
        tree = p.tree()
        p.send("stop")
        p.wait_for(r"\[probe\] stopped exit=graceful cleanup=incomplete \(stop_all_streams", 60)
        check(p.saw("this is the step that frees camera and microphone"), "the failed media step was not explained")
        p.check_gone(tree)
        p.send("quit")
        p.exit_code(30)
    finally:
        p.close()


def scenario_early_exit(ctx):
    p = Probe(ctx, "early_exit_media")
    try:
        p.wait_for(r"\[probe\] error: serve-web exited during startup: the device's camera or microphone is in use", 60)
        check(p.exit_code(60) == 2, "exit code is not 2")
        check(not p.of("listening"), "fake listened")
        p.check_gone(p.tree())
    finally:
        p.close()


def scenario_startup_timeout(ctx):
    p = Probe(ctx, "never_ready", extra_args=["--test-startup-timeout", "5"])
    try:
        p.wait_for(r"\[probe\] error: serve-web was not ready within 5 s", 60)
        p.wait_for(r"\[probe\] stopped exit=graceful", 60)
        check(p.exit_code(30) == 2, "exit code is not 2")
        p.check_one_sigint()
        p.check_cleanup_done()
        p.check_gone(p.tree())
    finally:
        p.close()


def scenario_port_occupied(ctx):
    blocker = socket.socket()
    try:
        blocker.bind(("127.0.0.1", 0))
        blocker.listen()
        port = blocker.getsockname()[1]
        p = Probe(ctx, "normal", port=port)
        try:
            p.wait_for(rf"\[probe\] error: port {port} is already in use", 90)
            check(p.exit_code(30) == 2, "exit code is not 2")
            check(not p.events(), "serve-web was started on a busy port")
            check(not p.saw(r"\[probe\] child pid="), "a child was started on a busy port")
        finally:
            p.close()
    finally:
        blocker.close()


class ForeignViewer(http.server.BaseHTTPRequestHandler):
    posts = []

    def log_message(self, format, *args):
        pass

    def do_GET(self):
        body = b"<title>pymobiledevice3 screen</title>"
        self.send_response(200)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        ForeignViewer.posts.append(self.path)
        self.send_response(200)
        self.send_header("Content-Length", "0")
        self.end_headers()


def scenario_port_race(ctx):
    p = Probe(ctx, "foreign_listener", extra_args=["--test-startup-timeout", "30"])
    server = None
    try:
        p.wait_for(r"\[probe\] child pid=\d+", 60)
        # the probe has checked the port; another process takes it before serve-web "listens"
        server = http.server.ThreadingHTTPServer(("127.0.0.1", p.port), ForeignViewer)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        open(os.path.join(p.artifacts, "go"), "w").close()
        p.wait_for(r"a process outside serve-web's job listens on port", 60)
        check(p.exit_code(90) == 2, "exit code is not 2")
        check(not p.saw(r"\[probe\] ready"), "the probe accepted a server it did not start")
        check(not ForeignViewer.posts, "the probe sent input to a server it did not start")
        p.check_one_sigint()
    finally:
        if server is not None:
            server.shutdown()
            server.server_close()
        p.close()


def gone(pid):
    import psutil
    try:
        return not psutil.Process(pid).is_running()
    except psutil.NoSuchProcess:
        return True


def scenario_assign_failure(ctx):
    p = Probe(ctx, "normal", extra_args=["--test-fail-assign", "serve-web"])
    try:
        pid = int(p.wait_for(r"\[probe\] child pid=(\d+)", 60).group(1))
        p.wait_for(r"job assignment refused", 30)
        check(p.exit_code(30) == 2, "exit code is not 2")
        check(gone(pid), f"child {pid} survived a failed job assignment")
        check(not p.events(), "the child ran although it was never in the job")
    finally:
        p.close()


def scenario_assign_failure_version_check(ctx):
    # the version check's interpreter is launched like serve-web; refused by its job, it must not stay suspended
    p = Probe(ctx, "normal", extra_args=["--test-fail-assign", "version-check"])
    try:
        pid = int(p.wait_for(r"\[probe\] version check pid=(\d+)", 60).group(1))
        p.wait_for(r"\[probe\] error: version check failed: job assignment refused", 30)
        check(p.exit_code(30) == 1, "exit code is not 1")
        check(gone(pid), f"version check child {pid} survived a failed job assignment")
        check(not p.saw(r"\[probe\] child pid="), "serve-web was started after the version check failed")
        check(not p.events(), "serve-web ran although the version check failed")
    finally:
        p.close()


def touches(p):
    return [r for r in p.requests() if r["path"] == "/touch"]


def scenario_slow_answers(ctx):
    # every contact is answered 50 ms late; hold and drag still end on the clock instead of stretching
    p = Probe(ctx, "normal", env_extra={"MACEXTEND_FAKE_CONTACT_DELAY_MS": "50"})
    try:
        p.ready()
        p.send("drag 0 0 65535 65535 2000")
        p.wait_for(r"\[probe\] sent drag", 30)
        p.send("hold 100 100 1000")
        p.wait_for(r"\[probe\] sent hold", 30)
        sent = touches(p)
        releases = [i for i, r in enumerate(sent) if r["body"]["type"] == "release"]
        check(len(releases) == 2, f"expected two releases, got {[r['body'] for r in sent]}")
        drag, hold = sent[:releases[0] + 1], sent[releases[0] + 1:releases[1] + 1]
        span = drag[-1]["t"] - drag[0]["t"]
        # paced per step (16 ms, then the 50 ms answer, 125 steps) it would hold the finger down for over 8 s
        check(1.9 <= span <= 2.8, f"a 2 s drag held the contact for {span:.2f} s")
        check(len(drag) - 2 <= 2000 // 16 + 1, f"{len(drag) - 2} moves: more than one per frame")
        check(drag[-2]["body"] == {"type": "contact", "x": 65535, "y": 65535}, "the drag did not end at its end point")
        span = hold[-1]["t"] - hold[0]["t"]
        check(len(hold) == 2 and 0.9 <= span <= 1.5, f"a 1 s hold held the contact for {span:.2f} s ({len(hold)} requests)")
        check(not p.saw("cut at its deadline"), "a gesture on 50 ms answers was cut")
        p.send("quit")
        check(p.exit_code(60) == 0, "exit code is not 0")
    finally:
        p.close()


def scenario_gesture_cut(ctx):
    # contacts are answered after 1.5 s: inside the client's 2 s request timeout, past a 200 ms drag's 1.2 s deadline
    p = Probe(ctx, "normal", env_extra={"MACEXTEND_FAKE_CONTACT_DELAY_MS": "1500"})
    try:
        p.ready()
        tree = p.tree()
        p.send("drag 100 200 300 400 200")
        p.wait_for(r"\[probe\] drag cut at its deadline .*release: released\. No retry", 30)
        sent = touches(p)
        check([r["body"] for r in sent] == [{"type": "contact", "x": 100, "y": 200}, {"type": "release", "x": 100, "y": 200}],
              f"a cut drag sent {[r['body'] for r in sent]}")
        span = sent[1]["t"] - sent[0]["t"]
        check(1.1 <= span <= 1.9, f"the cut drag held the contact for {span:.2f} s; its deadline is 1.2 s")
        p.send("tap 1 1")
        p.wait_for(r"\[probe\] error: the input session is closed", 15)
        check(len(p.requests()) == 2, "input was sent after the cut")
        p.send("stop")
        p.wait_for(r"\[probe\] stopped exit=graceful", 60)
        p.check_one_sigint()
        p.check_gone(tree)
        p.send("quit")
        check(p.exit_code(30) == 0, "exit code is not 0")
    finally:
        p.close()


def scenario_parent_killed(ctx):
    p = Probe(ctx, "normal")
    try:
        p.ready()
        p.check_tree_in_job()
        tree = p.tree()
        check(len(tree) >= 2, f"expected the interpreter and its descendant (and the venv launcher), found {[t.pid for t in tree]}")
        p.proc.kill()
        p.proc.wait(15)
        p.check_gone(tree)
        check(not p.of("sigint") and not p.of("cleanup_done"), "a killed parent cannot have run the cleanup")
    finally:
        p.close()


def scenario_exits_on_its_own(ctx):
    p = Probe(ctx, "exit_after_ready")
    try:
        p.ready()
        tree = p.tree()
        p.wait_for(r"\[probe\] serve-web exited on its own", 30)
        p.wait_for(r"\[probe\] stopped exit=alreadyexited", 30)
        p.send("tap 1 1")
        p.wait_for(r"\[probe\] error: input is only accepted while serve-web is ready", 15)
        p.send("quit")
        check(p.exit_code(30) == 4, "exit code is not 4")
        check(not p.of("sigint"), "an exited serve-web was signalled")
        p.check_gone(tree)
    finally:
        p.close()


SCENARIOS = [
    scenario_normal_stop,
    scenario_ctrl_c_over_tunnel,
    scenario_repeated_ctrl_c,
    scenario_repeated_stop,
    scenario_ctrl_c_during_stop,
    scenario_eof,
    scenario_long_cleanup,
    scenario_grace_expires,
    scenario_media_step_timeout,
    scenario_early_exit,
    scenario_startup_timeout,
    scenario_port_occupied,
    scenario_port_race,
    scenario_assign_failure,
    scenario_assign_failure_version_check,
    scenario_slow_answers,
    scenario_gesture_cut,
    scenario_parent_killed,
    scenario_exits_on_its_own,
]


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "ctrl-c":
        return send_ctrl_c(int(sys.argv[2]))
    if sys.platform != "win32":
        print("console_driver.py runs on Windows only")
        return 2
    parser = argparse.ArgumentParser()
    parser.add_argument("--probe", required=True)
    parser.add_argument("--python", required=True)
    parser.add_argument("--fake", required=True)
    parser.add_argument("--only", nargs="*")
    parser.add_argument("--keep", action="store_true", help="keep each scenario's artifact directory")
    ctx = parser.parse_args()
    ctx.probe, ctx.python, ctx.fake = (os.path.abspath(x) for x in (ctx.probe, ctx.python, ctx.fake))

    failed = []
    for scenario in SCENARIOS:
        name = scenario.__name__[len("scenario_"):]
        if ctx.only and name not in ctx.only:
            continue
        start = time.monotonic()
        try:
            scenario(ctx)
            print(f"PASS {name} ({time.monotonic() - start:.1f} s)", flush=True)
        except Exception as e:
            failed.append(name)
            print(f"FAIL {name}: {e}", flush=True)
            if not isinstance(e, Failure):
                traceback.print_exc()
    print(f"{len(failed)} failed" if failed else "all scenarios passed", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
