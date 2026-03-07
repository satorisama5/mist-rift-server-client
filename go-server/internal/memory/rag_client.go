package memory

import (
	"bytes"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"time"
)

const (
	ragBaseURL   = "http://localhost:8001"
	ragTimeout   = 10 * time.Second
)

// SaveDecision 调用 Python memory-rag 的 POST /save_decision，失败返回 error。
func SaveDecision(sessionID, scene, action, result, roomType string) error {
	body := map[string]string{
		"session_id": sessionID,
		"scene":      scene,
		"action":     action,
		"result":     result,
		"room_type":  roomType,
	}
	raw, err := json.Marshal(body)
	if err != nil {
		return err
	}
	req, err := http.NewRequest(http.MethodPost, ragBaseURL+"/save_decision", bytes.NewReader(raw))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")
	client := &http.Client{Timeout: ragTimeout}
	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("save_decision: %s", resp.Status)
	}
	return nil
}

// SearchSimilar 调用 Python memory-rag 的 POST /search_similar，返回相似决策描述列表；失败时返回 nil 不报错。
func SearchSimilar(scene string) []string {
	log.Printf("[rag] SearchSimilar start url=%s/search_similar", ragBaseURL)
	body := map[string]interface{}{
		"scene": scene,
		"top_k": 5,
	}
	raw, err := json.Marshal(body)
	if err != nil {
		log.Printf("[rag] SearchSimilar marshal err=%v", err)
		return nil
	}
	req, err := http.NewRequest(http.MethodPost, ragBaseURL+"/search_similar", bytes.NewReader(raw))
	if err != nil {
		log.Printf("[rag] SearchSimilar newrequest err=%v", err)
		return nil
	}
	req.Header.Set("Content-Type", "application/json")
	client := &http.Client{Timeout: ragTimeout}
	resp, err := client.Do(req)
	if err != nil {
		log.Printf("[rag] SearchSimilar do err=%v (memory-rag 未启动或超时?)", err)
		return nil
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		log.Printf("[rag] SearchSimilar status=%s", resp.Status)
		return nil
	}
	var out struct {
		Decisions []string `json:"decisions"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		log.Printf("[rag] SearchSimilar decode err=%v", err)
		return nil
	}
	log.Printf("[rag] SearchSimilar done len=%d", len(out.Decisions))
	return out.Decisions
}

// GetPlayerStyle 调用 Python memory-rag 的 POST /get_player_style，返回该 session 的玩家风格描述；失败返回空字符串。
func GetPlayerStyle(sessionID string) string {
	body := map[string]string{"session_id": sessionID}
	raw, err := json.Marshal(body)
	if err != nil {
		return ""
	}
	req, err := http.NewRequest(http.MethodPost, ragBaseURL+"/get_player_style", bytes.NewReader(raw))
	if err != nil {
		return ""
	}
	req.Header.Set("Content-Type", "application/json")
	client := &http.Client{Timeout: ragTimeout}
	resp, err := client.Do(req)
	if err != nil {
		return ""
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return ""
	}
	var out struct {
		Style string `json:"style"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return ""
	}
	return out.Style
}
