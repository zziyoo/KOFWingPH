extends Node2D

# Standalone inspection tool; does not create a playable fighter or combat data.
const ACTIONS := [0, 20, 200, 10, 11, 12, 40, 41, 42, 120, 5000, 5120]
var action_choices: Array = ACTIONS.duplicate()
var directory := "res://data/imported-characters/terry98/"
var manifest: Dictionary
var textures: Dictionary = {}
var choice := 0
var tick := 0
var total_ticks := 0
var shot_after := 120
var labels: Label
var left_sprite: Sprite2D
var right_sprite: Sprite2D

func _ready() -> void:
	RenderingServer.set_default_clear_color(Color(0.11, 0.13, 0.17))
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--mugen-dir="):
			directory = arg.trim_prefix("--mugen-dir=").trim_suffix("/") + "/"
		elif arg.begins_with("--preview-action="):
			var requested := int(arg.trim_prefix("--preview-action="))
			if not action_choices.has(requested): action_choices.append(requested)
			choice = action_choices.find(requested)
		elif arg.begins_with("--preview-shot-after="):
			shot_after = maxi(1, int(arg.trim_prefix("--preview-shot-after=")))
	labels = Label.new()
	labels.position = Vector2(25, 20)
	labels.add_theme_font_size_override("font_size", 22)
	add_child(labels)
	left_sprite = _make_sprite(Vector2(390, 570), 1.0)
	right_sprite = _make_sprite(Vector2(890, 570), -1.0)
	var path := directory + "manifest.json"
	if not FileAccess.file_exists(path):
		labels.text = "MUGEN manifest missing: " + path
		return
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not parsed is Dictionary:
		labels.text = "Invalid manifest"
		return
	manifest = parsed
	var factor: float = 960.0 / float(manifest.localcoord)
	left_sprite.get_parent().scale = Vector2(factor, factor)
	right_sprite.get_parent().scale = Vector2(-factor, factor)
	_show_frame()

func _make_sprite(at: Vector2, facing: float) -> Sprite2D:
	var anchor := Node2D.new()
	anchor.position = at
	anchor.scale = Vector2(3.0 * facing, 3.0)
	add_child(anchor)
	var sprite := Sprite2D.new()
	sprite.centered = false
	sprite.region_enabled = true
	sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	anchor.add_child(sprite)
	return sprite

func _physics_process(_delta: float) -> void:
	if manifest.is_empty(): return
	tick += 1
	total_ticks += 1
	_show_frame()
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--preview-shot=") and total_ticks == shot_after:
			get_viewport().get_texture().get_image().save_png(arg.trim_prefix("--preview-shot="))
			get_tree().quit()

func _unhandled_key_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_RIGHT:
			choice = (choice + 1) % action_choices.size()
			tick = 0
		elif event.keycode == KEY_LEFT:
			choice = (choice + action_choices.size() - 1) % action_choices.size()
			tick = 0

func _show_frame() -> void:
	var action: Dictionary = {}
	for candidate in manifest.actions:
		if candidate.action == action_choices[choice]:
			action = candidate
			break
	if action.is_empty() or action.frames.is_empty():
		labels.text = "Action %d missing" % action_choices[choice]
		left_sprite.hide(); right_sprite.hide()
		return
	var frames: Array = action.frames
	var loop_start: int = action.loop_start if action.loop_start != null else 0
	var position := tick
	var duration := 0
	var loop_duration := 0
	var has_infinite := false
	for i in frames.size():
		if frames[i].time == -1:
			has_infinite = true
			break
		var d: int = maxi(0, frames[i].time)
		duration += d
		if i >= loop_start: loop_duration += d
	if not has_infinite and loop_duration > 0 and position >= duration:
		position = duration - loop_duration + (position - duration) % loop_duration
	var selected: Dictionary = frames.back()
	var frame_index := frames.size() - 1
	for i in frames.size():
		var f: Dictionary = frames[i]
		if f.time == -1 or position < f.time:
			selected = f; frame_index = i; break
		position -= maxi(0, f.time)
	labels.text = "%s visual preview  Action %d  frame %d/%d  time %d\nLeft / Right: action   |   Original MUGEN visuals only" % [manifest.character, action.action, frame_index + 1, frames.size(), selected.time]
	if selected.sprite_index == null:
		left_sprite.hide(); right_sprite.hide()
		return
	var record: Dictionary = manifest.sprites[selected.sprite_index]
	var region: Dictionary = record.region
	var page: int = region.page
	if not textures.has(page):
		var img := Image.new()
		if img.load(ProjectSettings.globalize_path(directory + "atlas-%03d.png" % page)) != OK:
			left_sprite.hide(); right_sprite.hide(); return
		textures[page] = ImageTexture.create_from_image(img)
	var horizontal: bool = "H" in selected.flip
	var vertical: bool = "V" in selected.flip
	for sprite in [left_sprite, right_sprite]:
		sprite.texture = textures[page]
		sprite.region_rect = Rect2(region.x, region.y, region.w, region.h)
		sprite.scale = Vector2(-1 if horizontal else 1, -1 if vertical else 1)
		sprite.position = Vector2(selected.x + record.origin_x if horizontal else selected.x - record.origin_x,
			selected.y + record.origin_y if vertical else selected.y - record.origin_y)
		sprite.show()
