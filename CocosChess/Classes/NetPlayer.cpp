// NetPlayer.cpp — blocking BSD sockets wrapped in one worker thread.
// Wire format: 13-byte frames [u32 magic][u8 type][i32 a][i32 b], host byte
// order (LAN x86-to-x86; the engine guide documents this as a desktop-LAN demo,
// not a production protocol).
#include "NetPlayer.h"
#include <cstring>

#ifdef _WIN32
    #include <winsock2.h>
    #include <ws2tcpip.h>
    #pragma comment(lib, "ws2_32.lib")
    using sock_t = SOCKET;
    static constexpr sock_t kBadSocket = INVALID_SOCKET;
    static void closeSock(sock_t s) { closesocket(s); }
    static int sockErr() { return WSAGetLastError(); }
#else
    #include <sys/socket.h>
    #include <sys/select.h>
    #include <netinet/in.h>
    #include <arpa/inet.h>
    #include <unistd.h>
    #include <fcntl.h>
    #include <errno.h>
    using sock_t = int;
    static constexpr sock_t kBadSocket = -1;
    static void closeSock(sock_t s) { ::close(s); }
    static int sockErr() { return errno; }
#endif

namespace {
constexpr uint32_t kMagic = 0x58514749u; // "XQGI"
constexpr int kPortDefault = 5005;

// Godot reference frame types (RpcMove / RpcRestart / RpcOpponentLeft)
constexpr uint8_t kTypeMove = 1;
constexpr uint8_t kTypeRestart = 2;
constexpr uint8_t kTypeLeave = 3;

bool sendAll(sock_t s, const void* data, int len) {
    const char* p = (const char*)data;
    int sent = 0;
    while (sent < len) {
        int n = (int)::send(s, p + sent, len - sent, 0);
        if (n <= 0) return false;
        sent += n;
    }
    return true;
}

void ensureWsa() {
#ifdef _WIN32
    static bool done = false;
    if (!done) {
        WSADATA d;
        WSAStartup(MAKEWORD(2, 2), &d);
        done = true;
    }
#endif
}
} // namespace

bool NetPlayer::host(int port) {
    if (role_ != Role::None || worker_.joinable()) return false;
    ensureWsa();
    role_ = Role::Host;
    stop_ = false;
    worker_ = std::thread(&NetPlayer::runHost, this, port);
    return true;
}

bool NetPlayer::join(const std::string& ip, int port) {
    if (role_ != Role::None || worker_.joinable()) return false;
    if (ip.empty()) return false;
    ensureWsa();
    role_ = Role::Guest;
    stop_ = false;
    worker_ = std::thread(&NetPlayer::runJoin, this, ip, port);
    return true;
}

void NetPlayer::runHost(int port) {
    sock_t ls = ::socket(AF_INET, SOCK_STREAM, 0);
    if (ls == kBadSocket) { push({Message::ConnectFailed, 0, 0}); return; }
    listenSock_ = (void*)ls;

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    addr.sin_port = htons((uint16_t)port);
    if (::bind(ls, (sockaddr*)&addr, sizeof(addr)) != 0 || ::listen(ls, 1) != 0) {
        closeSock(ls);
        listenSock_ = nullptr;
        push({Message::ConnectFailed, 0, 0});
        return;
    }

    sock_t c = ::accept(ls, nullptr, nullptr); // unblocked by close() closing ls
    closeSock(ls);
    listenSock_ = nullptr;
    if (c == kBadSocket || stop_.load()) { push({Message::ConnectFailed, 0, 0}); return; }

    sock_ = (void*)c;
    connected_ = true;
    push({Message::Connected, 0, 0});
    recvLoop();
}

void NetPlayer::runJoin(const std::string& ip, int port) {
    sock_t c = ::socket(AF_INET, SOCK_STREAM, 0);
    if (c == kBadSocket) { push({Message::ConnectFailed, 0, 0}); return; }

    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_port = htons((uint16_t)port);
#ifdef _WIN32
    if (inet_pton(AF_INET, ip.c_str(), &addr.sin_addr) != 1) {
        closeSock(c);
        push({Message::ConnectFailed, 0, 0});
        return;
    }
#else
    if (inet_pton(AF_INET, ip.c_str(), &addr.sin_addr) != 1) {
        closeSock(c);
        push({Message::ConnectFailed, 0, 0});
        return;
    }
#endif

    // non-blocking connect + 5s select so a bad IP fails fast instead of
    // hanging the UI for the OS default timeout
#ifdef _WIN32
    u_long nb = 1;
    ioctlsocket(c, FIONBIO, &nb);
#else
    fcntl(c, F_SETFL, O_NONBLOCK);
#endif
    int rc = ::connect(c, (sockaddr*)&addr, sizeof(addr));
    if (rc != 0 && sockErr() != 0
#ifdef _WIN32
        && sockErr() != WSAEWOULDBLOCK
#else
        && sockErr() != EINPROGRESS
#endif
    ) {
        closeSock(c);
        push({Message::ConnectFailed, 0, 0});
        return;
    }
    fd_set wset;
    FD_ZERO(&wset);
    FD_SET(c, &wset);
    timeval tv{ 5, 0 };
    if (::select((int)c + 1, nullptr, &wset, nullptr, &tv) <= 0) {
        closeSock(c);
        push({Message::ConnectFailed, 0, 0});
        return;
    }
    int soerr = 0;
    socklen_t slen = sizeof(soerr);
    getsockopt(c, SOL_SOCKET, SO_ERROR, (char*)&soerr, &slen);
    if (soerr != 0) {
        closeSock(c);
        push({Message::ConnectFailed, 0, 0});
        return;
    }
#ifdef _WIN32
    u_long blk = 0;
    ioctlsocket(c, FIONBIO, &blk);
#else
    int fl = fcntl(c, F_GETFL, 0);
    fcntl(c, F_SETFL, fl & ~O_NONBLOCK);
#endif

    sock_ = (void*)c;
    connected_ = true;
    push({Message::Connected, 0, 0});
    recvLoop();
}

void NetPlayer::recvLoop() {
    sock_t s = (sock_t)sock_;
    unsigned char buf[13];
    int got = 0;
    while (!stop_.load()) {
        int n = (int)::recv(s, (char*)buf + got, 13 - got, 0);
        if (n <= 0) {
            // peer gone (recv 0) or error; suppress if we initiated the close
            if (!stop_.load()) push({Message::Disconnected, 0, 0});
            break;
        }
        got += n;
        if (got < 13) continue;
        got = 0;
        uint32_t magic;
        std::memcpy(&magic, buf, 4);
        if (magic != kMagic) continue; // resync: drop garbage frames
        int32_t a, b;
        std::memcpy(&a, buf + 5, 4);
        std::memcpy(&b, buf + 9, 4);
        switch (buf[4]) {
            case kTypeMove:    push({Message::Move, a, b}); break;
            case kTypeRestart: push({Message::Restart, 0, 0}); break;
            case kTypeLeave:   push({Message::Leave, 0, 0}); break;
            default: break;
        }
    }
    connected_ = false;
}

bool NetPlayer::sendFrame(uint8_t type, int32_t a, int32_t b) {
    if (!connected_.load()) return false;
    unsigned char frame[13];
    std::memcpy(frame, &kMagic, 4);
    frame[4] = type;
    std::memcpy(frame + 5, &a, 4);
    std::memcpy(frame + 9, &b, 4);
    return sendAll((sock_t)sock_, frame, 13);
}

bool NetPlayer::sendMove(int from, int to) {
    return sendFrame(kTypeMove, (int32_t)from, (int32_t)to);
}

void NetPlayer::sendRestart() {
    sendFrame(kTypeRestart, 0, 0);
}

void NetPlayer::close() {
    if (role_ == Role::None && !worker_.joinable()) return;
    stop_ = true;
    if (connected_.load()) sendFrame(kTypeLeave, 0, 0); // best-effort goodbye
    if (sock_) {
        sock_t s = (sock_t)sock_;
#ifdef _WIN32
        shutdown(s, SD_BOTH);
#else
        shutdown(s, SHUT_RDWR);
#endif
    }
    if (listenSock_) {
        closeSock((sock_t)listenSock_); // unblocks a pending accept()
        listenSock_ = nullptr;
    }
    if (worker_.joinable()) worker_.join();
    if (sock_) { closeSock((sock_t)sock_); sock_ = nullptr; }
    connected_ = false;
    role_ = Role::None;
}

void NetPlayer::push(const Message& m) {
    std::lock_guard<std::mutex> lock(mtx_);
    queue_.push_back(m);
}

bool NetPlayer::hasMessages() {
    std::lock_guard<std::mutex> lock(mtx_);
    return !queue_.empty();
}

NetPlayer::Message NetPlayer::popMessage() {
    std::lock_guard<std::mutex> lock(mtx_);
    Message m = queue_.front();
    queue_.pop_front();
    return m;
}
