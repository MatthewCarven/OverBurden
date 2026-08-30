extends SceneTree

## Saves a PNG of the real game, from the real spawn, after terrain has streamed in.
##
## Headless has no renderer at all — a shader error does not surface there and nothing is
## drawn — so every look question in this project has needed a person at a window. M5a's
## `--ao=0` A/B is the first one where the two arms had to be compared rather than admired,
## and M5d's are "does a mine read as a mine": both want the same picture twice.
##
## Run it, and pass anything after the output path straight through to the game:
##
##   ...console.exe --path MinerCraft.Game --script res://screenshot.gd -- shot.png --ao=0
##
## GDScript rather than C# because `--script` wants a MainLoop and this needs no build.

const SETTLE_MILLISECONDS := 8000

func _initialize() -> void:
	var arguments := OS.get_cmdline_user_args()
	if arguments.is_empty():
		printerr("screenshot.gd: pass an output path after a bare --")
		quit(1)
		return

	root.add_child(load("res://Main.tscn").instantiate())

	# `process_frame` carries no arguments, so it cannot be accumulated as a delta — the
	# obvious `waited += await process_frame` adds null and spins forever.
	var start := Time.get_ticks_msec()
	while Time.get_ticks_msec() - start < SETTLE_MILLISECONDS:
		await process_frame

	var image: Image = root.get_texture().get_image()
	var error := image.save_png(arguments[0])

	print("screenshot.gd: %s %s -> %s" % [
		"saved" if error == OK else "FAILED (%d)" % error, image.get_size(), arguments[0]])

	quit(OK if error == OK else 1)
