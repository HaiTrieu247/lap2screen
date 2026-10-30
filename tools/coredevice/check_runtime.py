"""Checks a built CoreDevice runtime from the inside, the way `pip check` would, without pip in the runtime:
every installed distribution's requirements (with their markers) are installed at a matching version, and
pymobiledevice3 is exactly the version MacExtend was checked against."""
import importlib.metadata as md
import sys

from packaging.requirements import Requirement
from packaging.utils import canonicalize_name

REQUIRED = "11.47.0"


def main():
    installed = {canonicalize_name(d.metadata["Name"]): d.version for d in md.distributions()}
    problems = []
    for dist in md.distributions():
        for spec in dist.requires or []:
            req = Requirement(spec)
            if req.marker is not None and not req.marker.evaluate({"extra": ""}):
                continue
            have = installed.get(canonicalize_name(req.name))
            if have is None:
                problems.append(f"{dist.metadata['Name']} needs {req}, which is not installed")
            elif req.specifier and not req.specifier.contains(have, prereleases=True):
                problems.append(f"{dist.metadata['Name']} needs {req}, found {have}")
    if installed.get("pymobiledevice3") != REQUIRED:
        problems.append(f"pymobiledevice3 is {installed.get('pymobiledevice3')}, MacExtend needs {REQUIRED}")
    for problem in problems:
        print(problem)
    print("ok" if not problems else f"{len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
