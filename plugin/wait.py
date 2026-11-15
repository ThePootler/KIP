"""Wait for a desktop task's next check, waking promptly if Ian takes over."""
import math
from pathlib import Path
import sys
import time


def wait(marker, seconds):
    if not math.isfinite(seconds) or seconds < 0:
        raise ValueError("Seconds must be finite and nonnegative.")
    deadline = time.monotonic() + seconds
    while True:
        try:
            marker.stat()
        except FileNotFoundError:
            pass
        except OSError:
            return "KIP could not read mouse permission. Stop desktop work and tell Ian."
        else:
            return "PAUSED: Ian pressed Esc. End computer use and await Ian's explicit permission."
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return "Wait elapsed."
        time.sleep(min(0.1, remaining))


if __name__ == "__main__":
    print(wait(Path(sys.argv[1]), float(sys.argv[2])))
