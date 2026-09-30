import os, re, sys
here = os.path.dirname(os.path.abspath(__file__))
for name in sys.argv[1:]:
    text = open(os.path.join(here, f"{name}.txt"), encoding="utf-8", errors="replace").read()
    lines = text.splitlines()
    errors = sum(1 for l in lines if "ERROR" in l)
    print(f"=== {name}: {errors} ERROR line(s)")
    m10 = [l for l in lines if l.startswith("M10:") and "tick:" in l]
    if m10:
        last = m10[-1]
        m = re.search(r"(\d[\d,]*) tick\(s\), ([\d.]+) ms mean[^|;]*", last)
        print("  M10 tick timing: " + (m.group(0) if m else "(not found)"))
        rows = re.match(r"M10: (\d+) machine", last)
        print("  rows: " + (rows.group(1) if rows else "?"))
    worst = [l for l in lines if l.startswith("M8b:") and "boundary worst" in l]
    if worst:
        last = worst[-1]
        i = last.find("boundary worst")
        print("  boundary worst (run): " + last[i:i + 300])
        hz = re.search(r"([\d.]+) tick\(s\)/s|([\d.]+) Hz", last)
        m = re.search(r"at tick (\d+)", last)
        print("  at tick: " + (m.group(1) if m else "?") + ";  hz: " + (hz.group(0) if hz else "?"))
    loaded = [l for l in lines if l.startswith("M8b:") and "loaded" in l]
    if loaded:
        m = re.search(r"loaded [^,;|]*", loaded[0])
        print("  first M8b loaded: " + (m.group(0) if m else loaded[0][:160]))
