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

const defaultAddr = ":50051"

type server struct {
	riftofog.UnimplementedCombatServiceServer
}

func (s *server) ExecuteAction(ctx context.Context, in *riftofog.ActionRequest) (*riftofog.ActionResult, error) {
	req := toProtocolRequest(in)
	log.Printf("[combat] ExecuteAction action_key=%s hp=%d ap=%d", req.ActionKey, req.Hp, req.Ap)
	out := services.ExecuteCombatAction(req)
	if out != nil {
		log.Printf("[combat] result success=%v result_text=%q", out.Success, out.ResultText)
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
	addr := os.Getenv("COMBAT_GRPC_ADDR")
	if addr == "" {
		addr = defaultAddr
	}
	lis, err := net.Listen("tcp", addr)
	if err != nil {
		log.Fatalf("combat listen: %v", err)
	}
	srv := grpc.NewServer()
	riftofog.RegisterCombatServiceServer(srv, &server{})
	reflection.Register(srv)
	log.Printf("[combat] gRPC listening on %s", addr)
	if err := srv.Serve(lis); err != nil {
		log.Fatal(err)
	}
}
