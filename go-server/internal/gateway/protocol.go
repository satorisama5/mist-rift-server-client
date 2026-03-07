package gateway

import "go-server/internal/protocol"

// 与 proto 一致的 Go 结构体，复用于 WebSocket JSON 与 Redis 序列化。
// 定义见 internal/protocol，此处仅做类型别名便于 gateway 包内使用。
type (
	StateChanges   = protocol.StateChanges
	UnityRequest   = protocol.UnityRequest
	ServerResponse = protocol.ServerResponse
)
