package repository

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"go-server/internal/protocol"

	"github.com/redis/go-redis/v9"
)

const (
	exchangeListKey   = "rift:ws:exchanges"
	maxListLen        = 10000
	sessionKeyPrefix  = "session:"
	sessionExpireSec  = 24 * 60 * 60 // 24h
)

// Repository 用于将 Unity 与网关的通信内容写入 Redis。
type Repository struct {
	cli *redis.Client
}

// New 创建 Redis 仓库。addr 例 "localhost:6379"，password 可为空。
func New(addr, password string, db int) (*Repository, error) {
	cli := redis.NewClient(&redis.Options{
		Addr:     addr,
		Password: password,
		DB:       db,
	})
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	if err := cli.Ping(ctx).Err(); err != nil {
		return nil, fmt.Errorf("redis ping: %w", err)
	}
	return &Repository{cli: cli}, nil
}

// Exchange 单次通信记录（请求+响应），便于序列化存入 Redis。
type Exchange struct {
	At       time.Time               `json:"at"`
	Request  *protocol.UnityRequest   `json:"request"`
	Response *protocol.ServerResponse `json:"response"`
	Session  string                  `json:"session,omitempty"`
}

// SessionCache 会话摘要，缓存到 session:{session_id}，过期 24h。
type SessionCache struct {
	SessionID string    `json:"session_id"`
	RoomType  string    `json:"room_type"`
	Action    string    `json:"action"`
	Result    string    `json:"result"`
	UpdatedAt time.Time `json:"updated_at"`
}

// SaveExchange 将一次 Unity 请求与服务器响应写入 Redis（列表尾部），便于审计与后续分析。
func (r *Repository) SaveExchange(ctx context.Context, session string, req *protocol.UnityRequest, resp *protocol.ServerResponse) error {
	if req == nil {
		req = &protocol.UnityRequest{}
	}
	if resp == nil {
		resp = &protocol.ServerResponse{}
	}
	ex := Exchange{
		At:       time.Now().UTC(),
		Request:  req,
		Response: resp,
		Session:  session,
	}
	raw, err := json.Marshal(ex)
	if err != nil {
		return fmt.Errorf("marshal exchange: %w", err)
	}
	pipe := r.cli.Pipeline()
	pipe.RPush(ctx, exchangeListKey, raw)
	pipe.LTrim(ctx, exchangeListKey, -maxListLen, -1)
	_, err = pipe.Exec(ctx)
	if err != nil {
		return err
	}

	// 将会话数据缓存到 session:{session_id}，过期 24 小时
	resultText := ""
	if resp.Result != "" {
		resultText = resp.Result
	}
	cache := SessionCache{
		SessionID: session,
		RoomType:  req.RoomType,
		Action:    req.ActionKey,
		Result:    resultText,
		UpdatedAt: time.Now().UTC(),
	}
	cacheRaw, _ := json.Marshal(cache)
	key := sessionKeyPrefix + session
	_ = r.cli.Set(ctx, key, cacheRaw, sessionExpireSec*time.Second)
	return nil
}

// Close 关闭 Redis 连接。
func (r *Repository) Close() error {
	return r.cli.Close()
}
