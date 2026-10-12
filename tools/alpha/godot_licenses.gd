extends SceneTree
# Writes Godot's own licence, the copyright of every third-party component bundled in the engine, and the
# full text of every licence those components use, straight from the running engine, into the folder named
# after "--". The exported game is this engine, so these are the notices the alpha zip must carry
# (ALPHA-PLAN decision 23). Run again only when the pinned Godot version changes:
#
#   <godot console exe> --headless --path Overburden.Game --script <repo>/tools/alpha/godot_licenses.gd -- <repo>/tools/alpha/licenses


func _init() -> void:
	var out: String = OS.get_cmdline_user_args()[0]
	DirAccess.make_dir_recursive_absolute(out)

	var engine := FileAccess.open(out.path_join("godot-engine.txt"), FileAccess.WRITE)
	engine.store_string("Godot Engine " + str(Engine.get_version_info()["string"]) + "\n\n")
	engine.store_string(Engine.get_license_text() + "\n\n")
	engine.store_string("Third-party components bundled in the engine, with their licences (full texts in license-*.txt):\n\n")
	for component in Engine.get_copyright_info():
		engine.store_string(str(component["name"]) + "\n")
		for part in component["parts"]:
			for line in part["copyright"]:
				engine.store_string("    Copyright " + str(line) + "\n")
			engine.store_string("    License: " + str(part["license"]) + "\n")
		engine.store_string("\n")
	engine.close()

	var licenses: Dictionary = Engine.get_license_info()
	for name in licenses.keys():
		var text := FileAccess.open(out.path_join("license-" + str(name).replace("/", "_").replace(" ", "_") + ".txt"), FileAccess.WRITE)
		text.store_string(str(licenses[name]))
		text.close()

	print("godot_licenses: wrote godot-engine.txt and ", licenses.size(), " licence text(s) to ", out)
	quit()
