import os, re, sys
here = os.path.dirname(os.path.abspath(__file__))
WANT = ("swing ms", "mining", "remesh", "clones", "seep", "frame ms", "fluidfield", "boundary",
        "fluidtick", "fluidjob", "fluid ms", "fluid ", "update ms")
for name in sys.argv[1:]:
    path = os.path.join(here, f"{name}.txt")
    if not os.path.exists(path):
        print(f"=== {name}: no output"); continue
    lines = open(path, encoding="utf-8", errors="replace").read().splitlines()
    errors = sum(1 for l in lines if "ERROR" in l)
    print(f"=== {name}: {errors} ERROR line(s)")
    for l in lines:
        for w in WANT:
            if l.startswith(w):
                print("  " + l[:330])
                break
