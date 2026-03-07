package main

import (
	"context"
	"log"
	"net"
	"os"

	"go-server/internal/protocol"
	"go-server/internal/services"
	riftofog "go-server/pkg/protocol"

	"google.golang.org/grpc"
	"google.golang.org/grpc/reflection"
)

const defaultAddr = ":50053"

type server struct {
	riftofog.UnimplementedResourceServiceServer
}

func (s *server) ExecuteAction(ctx context.Context, in *riftofog.ActionRequest) (*riftofog.ActionResult, error) {
	req := toProtocolRequest(in)
	log.Printf("[resource] ExecuteAction action_key=%s", req.ActionKey)
	out := services.ExecuteResourceAction(req)
	if out != nil {
		log.Printf("[resource] result success=%v result_text=%q", out.Success, out.ResultText)
	}
	return toProtoResult(out), nil
}

func toProtocolRequest(in *riftofog.ActionRequest) *protocol.ActionRequest {
	if in == nil {
		return &protocol.ActionRequest{}
	}
	return &protocol.ActionRequest{
		ActionKey:   in.GetActionKey(),
		Str:         in.GetStr(),
		IntVal:      in.GetIntVal(),
		Lck:         in.GetLck(),
		Stealth:     in.GetStealth(),
		Hp:          in.GetHp(),
		Ap:          in.GetAp(),
		Status:      in.GetStatus(),
		EnemyType:   in.GetEnemyType(),
		RoomContent: in.GetRoomContent(),
	}
}

func toProtoResult(r *protocol.ActionResult) *riftofog.ActionResult {
	if r == nil {
		return &riftofog.ActionResult{}
	}
	return &riftofog.ActionResult{
		Success:     r.Success,
		ResultText:  r.ResultText,
		HpDelta:     r.HpDelta,
		ApDelta:     r.ApDelta,
		NewStatus:   r.NewStatus,
		ClearStatus: r.ClearStatus,
		Reward:      r.Reward,
	}
}

func main() {
	addr := os.Getenv("RESOURCE_GRPC_ADDR")
	if addr == "" {
		addr = defaultAddr
	}
	lis, err := net.Listen("tcp", addr)
	if err != nil {
		log.Fatalf("resource listen: %v", err)
	}
	srv := grpc.NewServer()
	riftofog.RegisterResourceServiceServer(srv, &server{})
	reflection.Register(srv)
	log.Printf("[resource] gRPC listening on %s", addr)
	if err := srv.Serve(lis); err != nil {
		log.Fatal(err)
	}
}
