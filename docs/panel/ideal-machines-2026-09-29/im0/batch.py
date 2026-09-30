"""IM-0: measurements on the fixed build, no code. Arms alternate (A B A B) because this machine
runs at two speeds minute to minute. Played worlds run on a fresh copy each, deleted after."""
import os, shutil, subprocess, sys, time, datetime

repo = r"C:\Users\Admin\Desktop\-=Programming=-\MinerCraft"
exe = os.path.join(repo, r"..\GAME\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe")
worlds = os.path.join(os.environ["APPDATA"], r"Godot\app_userdata\Overburden\worlds")
here = os.path.dirname(os.path.abspath(__file__))
log = open(os.path.join(here, "batch-log.txt"), "a", encoding="utf-8")

PLAYED = ["--lightbudget=16", "--adaptive=1", "--fluidnear=2", "--fluidjob=1"]
BITE = ["--profile", "--speed=6", "--radius=4", "--vradius=2", "--mine", "--nogate", "--measure=20"]
INJECT = ["--profile", "--speed=0", "--fluidnear=2", "--fluidjob=1", "--measure=60"]

runs = []
for i in (1, 2):
    runs += [
        (f"A-pool-{i}", "retired-pool-2026-09-22", ["--quit-after", "5400"], PLAYED),
        (f"A-spread-{i}", "retired-spread-2026-09-22", ["--quit-after", "5400"], PLAYED),
        (f"A-taj-{i}", "retired-TajMahal-2026-09-29", ["--quit-after", "5400"], PLAYED + ["--at=-268,-5,1284"]),
    ]
for i in (1, 2):
    runs += [
        (f"B-cube-dry-{i}", None, [], BITE + ["--entangle"]),
        (f"B-tunnel-dry-{i}", None, [], BITE + ["--entangletunnel"]),
        (f"B-cube-wet-{i}", None, [], BITE + ["--entangle", "--fillbasin=2000000"]),
        (f"B-tunnel-wet-{i}", None, [], BITE + ["--entangletunnel", "--fillbasin=2000000"]),
    ]
for i in (1, 2):
    runs += [
        (f"C-inject-1ML-{i}", None, [], INJECT + ["--injectlitres=1000000"]),
        (f"C-inject-10ML-{i}", None, [], INJECT + ["--injectlitres=10000000"]),
    ]

only = set(sys.argv[1:])

def godot_running():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Godot*"], capture_output=True, text=True).stdout
    return "Godot" in out

def say(msg):
    line = f"{datetime.datetime.now():%H:%M:%S} {msg}"
    print(line, flush=True)
    log.write(line + "\n"); log.flush()

for name, source, engine_args, game_args in runs:
    if only and name not in only:
        continue
    if godot_running():
        say(f"ABORT before {name}: a Godot process is running (his game?)")
        break
    args = [exe, "--path", "Overburden.Game", "--headless"] + engine_args + ["--"] + game_args
    probe = None
    if source:
        probe = f"probe-im0-{name}"
        dst = os.path.join(worlds, probe)
        if os.path.exists(dst):
            shutil.rmtree(dst)
        shutil.copytree(os.path.join(worlds, source), dst)
        args.append(f"--world={probe}")
    t0 = time.time()
    with open(os.path.join(here, f"{name}.txt"), "w", encoding="utf-8", errors="replace") as f:
        try:
            p = subprocess.run(args, cwd=repo, stdout=f, stderr=subprocess.STDOUT, timeout=1500)
            code = p.returncode
        except subprocess.TimeoutExpired:
            code = "timeout"
    say(f"{name}: exit {code} in {time.time() - t0:.0f} s")
    if probe:
        shutil.rmtree(os.path.join(worlds, probe), ignore_errors=True)
        if os.path.exists(os.path.join(worlds, probe)):
            say(f"  could not delete {probe}")
say("done")
