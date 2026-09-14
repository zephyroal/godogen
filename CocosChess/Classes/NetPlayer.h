// NetPlayer.h — minimal TCP peer for LAN two-player Xiangqi.
// Mirrors the Godot reference's ENet flow: host listens on 5005 and accepts
// exactly one guest; guest connects to the host IP. Messages are drained on
// the game thread via hasMessages()/popMessage() — the socket thread never
// touches game state.
#pragma once
#include <string>
#include <deque>
#include <mutex>
#include <thread>
#include <atomic>
#include <cstdint>

class NetPlayer {
public:
    enum class Role { None, Host, Guest };

    struct Message {
        enum Type { Connected, ConnectFailed, Move, Restart, Leave, Disconnected } type;
        int a = 0, b = 0;
    };

    // async: listen → accept exactly one guest; messages follow on the queue
    bool host(int port = 5005);
    // async connect with a 5s timeout
    bool join(const std::string& ip, int port = 5005);
    // graceful shutdown: send Leave, close sockets, join the worker thread.
    // Safe to call repeatedly; call it before tearing the scene down.
    void close();

    bool connected() const { return connected_.load(); }
    Role role() const { return role_; }

    bool sendMove(int from, int to);
    void sendRestart();

    bool hasMessages();
    Message popMessage(); // caller must check hasMessages() first

private:
    void push(const Message& m);
    bool sendFrame(uint8_t type, int32_t a, int32_t b);
    void runHost(int port);
    void runJoin(const std::string& ip, int port);
    void recvLoop();

    // socket handles kept opaque so this header needs no platform includes
    void* sock_ = nullptr;
    void* listenSock_ = nullptr;
    std::atomic<bool> stop_{ false };
    std::atomic<bool> connected_{ false };
    Role role_ = Role::None;
    std::thread worker_;
    std::mutex mtx_;
    std::deque<Message> queue_;
};
