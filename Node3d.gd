extends Node3D
@onready var box_mesh: MeshInstance3D = %BoxMesh


func _process(delta: float) -> void:
	box_mesh.global_rotate(Vector3.UP,-1.0 * delta) 
