extends Control
@onready var background: TextureRect = %Background


func _on_bg_button_toggled(toggled_on: bool) -> void:
	background.visible = toggled_on
