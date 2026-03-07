package main

import (
	"bufio"
	"log"
	"net/http"
	"os"
	"strings"

	"go-server/internal/gateway"
	"go-server/internal/repository"
	riftofog "go-server/pkg/protocol"

	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
)

func main() {
	loadEnvFile(".env")

	redisAddr := getEnv("REDIS_ADDR", "localhost:6379")
	redisPass := getEnv("REDIS_PASSWORD", "")
	httpAddr := getEnv("HTTP_ADDR", ":8080")
	combatAddr := getEnv("COMBAT_GRPC_ADDR", "localhost:50051")
	eventAddr := getEnv("EVENT_GRPC_ADDR", "localhost:50052")
	resourceAddr := getEnv("RESOURCE_GRPC_ADDR", "localhost:50053")

	var redisRepo *repository.Repository
	if r, err := repository.New(redisAddr, redisPass, 0); err != nil {
		log.Printf("[warn] redis not available: %v (communication will not be stored)", err)
	} else {
		redisRepo = r
		defer redisRepo.Close()
	}

	var combatClient riftofog.CombatServiceClient  //websocket请求，所以被当作客户端
	var eventClient riftofog.EventServiceClient
	var resourceClient riftofog.ResourceServiceClient
	if cc, err := grpc.NewClient(combatAddr, grpc.WithTransportCredentials(insecure.NewCredentials())); err != nil {
		log.Printf("[warn] combat gRPC not available: %v", err)
	} else {
		defer cc.Close()
		combatClient = riftofog.NewCombatServiceClient(cc)
	}
	if cc, err := grpc.NewClient(eventAddr, grpc.WithTransportCredentials(insecure.NewCredentials())); err != nil {
		log.Printf("[warn] event gRPC not available: %v", err)
	} else {
		defer cc.Close()
		eventClient = riftofog.NewEventServiceClient(cc)
	}
	if cc, err := grpc.NewClient(resourceAddr, grpc.WithTransportCredentials(insecure.NewCredentials())); err != nil {
		log.Printf("[warn] resource gRPC not available: %v", err)
	} else {
		defer cc.Close()
		resourceClient = riftofog.NewResourceServiceClient(cc)
	}

	wsHandler := &gateway.Handler{
		Redis:    redisRepo,
		Combat:   combatClient,
		Event:    eventClient,
		Resource: resourceClient,
	}
	handler := gateway.Route(wsHandler)  //注册服务路由 将http连接升级

	log.Printf("[server] listening on %s", httpAddr)
	if err := http.ListenAndServe(httpAddr, handler); err != nil {
		log.Fatal(err)
	}
}

func getEnv(key, defaultVal string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return defaultVal
}

// loadEnvFile 从 .env 读取 KEY=VALUE 并设置到环境变量（忽略空行和 # 注释）。
func loadEnvFile(path string) {
	f, err := os.Open(path)  
	if err != nil {
		return
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		if i := strings.Index(line, "="); i > 0 {
			key := strings.TrimSpace(line[:i])
			val := strings.TrimSpace(line[i+1:])
			if key != "" && os.Getenv(key) == "" {
				_ = os.Setenv(key, val)
			}
		}
	}
}
