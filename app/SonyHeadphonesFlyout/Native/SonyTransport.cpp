// SPDX-License-Identifier: GPL-3.0-or-later

#include <winsock2.h>
#include <ws2bth.h>
#include <bluetoothapis.h>

#include "Upstream/CommandSerializer.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cwchar>
#include <memory>

namespace {

using Clock = std::chrono::steady_clock;
using namespace std::chrono_literals;

constexpr GUID sonyService{0x96cc203e, 0x5068, 0x46ad, {0xb3, 0x2d, 0xe3, 0x16, 0xf5, 0xe0, 0x69, 0xba}};

struct DeviceInfo {
    uint64_t address;
    int connected;
    wchar_t name[BLUETOOTH_MAX_NAME_SIZE];
};

static_assert(BLUETOOTH_MAX_NAME_SIZE == 248 && sizeof(DeviceInfo) == 512);

template <auto Close>
struct HandleDeleter {
    void operator()(void* handle) const noexcept { Close(handle); }
};

template <auto Close>
using Handle = std::unique_ptr<void, HandleDeleter<Close>>;

struct Winsock {
    WSADATA data{};
    int error = WSAStartup(MAKEWORD(2, 2), &data);
    ~Winsock() { if (error == 0) WSACleanup(); }
};

struct Session {
    Winsock winsock;
    SOCKET socket = INVALID_SOCKET;
    unsigned char sequence = 0;
    bool readingFrame = false;
    Buffer escapedFrame;

    ~Session() { if (socket != INVALID_SOCKET) closesocket(socket); }
};

int waitForSocket(SOCKET socket, bool writing, Clock::time_point deadline) {
    const auto remaining = std::chrono::duration_cast<std::chrono::microseconds>(deadline - Clock::now());
    if (remaining.count() <= 0) return WSAETIMEDOUT;

    timeval time{static_cast<long>(remaining.count() / 1000000),
                 static_cast<long>(remaining.count() % 1000000)};
    fd_set ready;
    fd_set errors;
    FD_ZERO(&ready);
    FD_ZERO(&errors);
    FD_SET(socket, &ready);
    FD_SET(socket, &errors);
    const int result = select(0, writing ? nullptr : &ready, writing ? &ready : nullptr, &errors, &time);
    if (result == SOCKET_ERROR) return WSAGetLastError();
    if (result == 0) return WSAETIMEDOUT;
    if (!FD_ISSET(socket, &errors)) return 0;

    int error = 0;
    int size = sizeof(error);
    if (getsockopt(socket, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&error), &size) == SOCKET_ERROR) {
        return WSAGetLastError();
    }
    return error == 0 ? WSAECONNABORTED : error;
}

ULONG findSonyChannel(SOCKADDR_BTH peer) {
    wchar_t context[64]{};
    DWORD contextLength = static_cast<DWORD>(std::size(context));
    if (WSAAddressToStringW(reinterpret_cast<sockaddr*>(&peer), sizeof(peer), nullptr, context, &contextLength)) {
        return 0;
    }

    GUID service = sonyService;
    WSAQUERYSETW query{};
    query.dwSize = sizeof(query);
    query.dwNameSpace = NS_BTH;
    query.lpszContext = context;
    query.lpServiceClassId = &service;
    HANDLE rawLookup = nullptr;
    if (WSALookupServiceBeginW(&query, LUP_FLUSHCACHE, &rawLookup)) return 0;
    Handle<WSALookupServiceEnd> lookup(rawLookup);

    alignas(WSAQUERYSETW) std::array<char, 8192> buffer{};
    for (;;) {
        auto* result = reinterpret_cast<WSAQUERYSETW*>(buffer.data());
        *result = {};
        result->dwSize = sizeof(*result);
        DWORD size = static_cast<DWORD>(buffer.size());
        if (WSALookupServiceNextW(lookup.get(), LUP_RETURN_ADDR, &size, result)) return 0;
        for (DWORD i = 0; i < result->dwNumberOfCsAddrs; ++i) {
            const auto& entry = result->lpcsaBuffer[i];
            if (entry.iProtocol != BTHPROTO_RFCOMM || !entry.RemoteAddr.lpSockaddr ||
                entry.RemoteAddr.iSockaddrLength < sizeof(SOCKADDR_BTH)) continue;
            const auto& target = *reinterpret_cast<const SOCKADDR_BTH*>(entry.RemoteAddr.lpSockaddr);
            if (target.addressFamily == AF_BTH && target.btAddr == peer.btAddr && target.port >= 1 && target.port <= 30) {
                return target.port;
            }
        }
    }
}

int openSonySocket(Session& session, const SOCKADDR_BTH& target) {
    if (session.socket != INVALID_SOCKET) closesocket(session.socket);
    session.socket = socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM);
    if (session.socket == INVALID_SOCKET) return WSAGetLastError();

    ULONG enabled = TRUE;
    const auto* option = reinterpret_cast<const char*>(&enabled);
    if (setsockopt(session.socket, SOL_RFCOMM, SO_BTH_AUTHENTICATE, option, sizeof(enabled)) == SOCKET_ERROR ||
        setsockopt(session.socket, SOL_RFCOMM, SO_BTH_ENCRYPT, option, sizeof(enabled)) == SOCKET_ERROR ||
        ioctlsocket(session.socket, FIONBIO, &enabled) == SOCKET_ERROR) {
        return WSAGetLastError();
    }

    if (connect(session.socket, reinterpret_cast<const sockaddr*>(&target), sizeof(target)) == 0) return 0;
    const int error = WSAGetLastError();
    if (error != WSAEWOULDBLOCK && error != WSAEINPROGRESS) return error;
    if (const int wait = waitForSocket(session.socket, true, Clock::now() + 6500ms)) return wait;
    int status = 0;
    int size = sizeof(status);
    if (getsockopt(session.socket, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&status), &size) == SOCKET_ERROR) {
        return WSAGetLastError();
    }
    return status;
}

int writeAll(SOCKET socket, const Buffer& frame, Clock::time_point deadline) {
    size_t offset = 0;
    while (offset < frame.size()) {
        if (const int error = waitForSocket(socket, true, deadline)) return error;
        const int count = send(socket, frame.data() + offset, static_cast<int>(frame.size() - offset), 0);
        if (count == SOCKET_ERROR) {
            const int error = WSAGetLastError();
            if (error == WSAEWOULDBLOCK) continue;
            return error;
        }
        if (count == 0) return WSAECONNRESET;
        offset += count;
    }
    return 0;
}

// A frame is accepted only after unescaping and checking the decoded length and checksum.
bool decodeFrame(const Buffer& escaped, DATA_TYPE& type, unsigned char& sequence, size_t& length) {
    Buffer frame;
    try {
        frame = CommandSerializer::_unescapeSpecials(escaped);
    } catch (const std::runtime_error&) {
        return false;
    }
    if (frame.size() < 7) return false;
    length = 0;
    for (size_t index = 2; index < 6; ++index) {
        length = (length << 8) | static_cast<unsigned char>(frame[index]);
    }
    if (length != frame.size() - 7 ||
        CommandSerializer::_sumChecksum(frame.data(), frame.size() - 1) != static_cast<unsigned char>(frame.back())) {
        return false;
    }
    type = static_cast<DATA_TYPE>(frame[0]);
    sequence = static_cast<unsigned char>(frame[1]);
    return true;
}

int sendCommand(Session& session, const Buffer& payload) {
    const auto deadline = Clock::now() + 2500ms;
    const auto command = CommandSerializer::packageDataForBt(payload, DATA_TYPE::DATA_MDR, session.sequence);
    if (const int error = writeAll(session.socket, command, deadline)) return error;

    // The remote ACK carries the next alternating sequence number.
    const auto next = static_cast<unsigned char>(session.sequence ^ 1);
    bool acknowledged = false;
    while (!acknowledged) {
        if (const int error = waitForSocket(session.socket, false, deadline)) return error;
        char received[MAX_BLUETOOTH_MESSAGE_SIZE];
        const int count = recv(session.socket, received, sizeof(received), 0);
        if (count == SOCKET_ERROR) {
            const int error = WSAGetLastError();
            if (error == WSAEWOULDBLOCK) continue;
            return error;
        }
        if (count == 0) return WSAECONNRESET;

        for (int index = 0; index < count; ++index) {
            const char byte = received[index];
            if (byte == START_MARKER) {
                session.readingFrame = true;
                session.escapedFrame.clear();
            } else if (session.readingFrame && byte == END_MARKER) {
                session.readingFrame = false;
                DATA_TYPE type{};
                unsigned char sequence = 0;
                size_t length = 0;
                if (!decodeFrame(session.escapedFrame, type, sequence, length)) continue;
                if (type == DATA_TYPE::ACK) {
                    acknowledged |= length == 0 && sequence == next;
                } else if (type >= DATA_TYPE::DATA && type < DATA_TYPE::SHOT && sequence < 2) {
                    const auto ack = CommandSerializer::packageDataForBt({}, DATA_TYPE::ACK, sequence ^ 1);
                    if (const int error = writeAll(session.socket, ack, deadline)) return error;
                }
            } else if (session.readingFrame) {
                if (session.escapedFrame.size() == MAX_BLUETOOTH_MESSAGE_SIZE) {
                    session.readingFrame = false;
                    session.escapedFrame.clear();
                } else {
                    session.escapedFrame.push_back(byte);
                }
            }
        }
    }
    session.sequence = next;
    return 0;
}

}  // namespace

extern "C" __declspec(dllexport) int SonyEnumerate(DeviceInfo* entries, int capacity, int* total) noexcept {
    if (!entries || capacity < 1 || !total) return ERROR_INVALID_PARAMETER;
    *total = 0;
    try {
        std::vector<DeviceInfo> devices;
        BLUETOOTH_FIND_RADIO_PARAMS radioParameters{sizeof(radioParameters)};
        HANDLE rawRadio = nullptr;
        Handle<BluetoothFindRadioClose> radios(BluetoothFindFirstRadio(&radioParameters, &rawRadio));
        if (!radios) return static_cast<int>(GetLastError());

        do {
            Handle<CloseHandle> radio(rawRadio);
            // Authenticated devices are paired. A remembered entry alone can be an old scan result.
            BLUETOOTH_DEVICE_SEARCH_PARAMS query{sizeof(query), TRUE, FALSE, FALSE, TRUE, FALSE, 0, radio.get()};
            BLUETOOTH_DEVICE_INFO info{};
            info.dwSize = sizeof(info);
            Handle<BluetoothFindDeviceClose> search(BluetoothFindFirstDevice(&query, &info));
            if (!search) {
                const auto error = GetLastError();
                if (error != ERROR_NO_MORE_ITEMS) return static_cast<int>(error);
                continue;
            }
            do {
                if (!info.fAuthenticated && !info.fConnected) continue;
                const auto address = static_cast<uint64_t>(info.Address.ullLong);
                if (!address || !info.szName[0]) continue;
                auto existing = std::find_if(devices.begin(), devices.end(), [address](const DeviceInfo& entry) {
                    return entry.address == address;
                });
                if (existing != devices.end()) {
                    existing->connected |= info.fConnected != FALSE;
                    continue;
                }
                DeviceInfo entry{};
                entry.address = address;
                entry.connected = info.fConnected != FALSE;
                std::wmemcpy(entry.name, info.szName, BLUETOOTH_MAX_NAME_SIZE - 1);
                devices.push_back(entry);
            } while (BluetoothFindNextDevice(search.get(), &info));
            if (GetLastError() != ERROR_NO_MORE_ITEMS) return static_cast<int>(GetLastError());
        } while (BluetoothFindNextRadio(radios.get(), &rawRadio));
        if (GetLastError() != ERROR_NO_MORE_ITEMS) return static_cast<int>(GetLastError());

        *total = static_cast<int>(devices.size());
        std::copy_n(devices.begin(), std::min(*total, capacity), entries);
        return ERROR_SUCCESS;
    } catch (...) {
        return ERROR_OUTOFMEMORY;
    }
}

extern "C" __declspec(dllexport) int SonyConnect(uint64_t address, Session** connected) noexcept {
    if (!address || !connected) return ERROR_INVALID_PARAMETER;
    *connected = nullptr;
    try {
        auto session = std::make_unique<Session>();
        if (session->winsock.error) return session->winsock.error;
        SOCKADDR_BTH target{};
        target.addressFamily = AF_BTH;
        target.btAddr = address;
        target.serviceClassId = sonyService;
        if (const int error = openSonySocket(*session, target)) {
            if (error != WSAEHOSTDOWN && error != WSAETIMEDOUT && error != WSASERVICE_NOT_FOUND) return error;

            // Resolve the current Sony service directly instead of reusing a potentially stale SDP channel.
            SOCKADDR_BTH peer = target;
            peer.serviceClassId = {};
            const auto channel = findSonyChannel(peer);
            if (channel != 0) {
                target.serviceClassId = {};
                target.port = channel;
            } else if (error == WSAETIMEDOUT) {
                return error;
            }
            if (int retry = openSonySocket(*session, target)) return retry;
        }
        *connected = session.release();
        return ERROR_SUCCESS;
    } catch (...) {
        return ERROR_OUTOFMEMORY;
    }
}

extern "C" __declspec(dllexport) void SonyClose(Session* session) noexcept {
    delete session;
}

extern "C" __declspec(dllexport) int SonySetAmbient(Session* session, int mode, int level, int voice) noexcept {
    if (!session || mode < 0 || mode > 2 || level < 0 || level > 19) return ERROR_INVALID_PARAMETER;
    try {
        const auto effect = mode == 2 ? NC_ASM_EFFECT::OFF : NC_ASM_EFFECT::ADJUSTMENT_COMPLETION;
        const auto ambientLevel = static_cast<char>(mode == 2 ? -1 : mode == 0 ? 0 : level);
        const auto focus = mode == 1 && level > MINIMUM_VOICE_FOCUS_STEP && voice ? ASM_ID::VOICE : ASM_ID::NORMAL;
        return sendCommand(*session, CommandSerializer::serializeNcAndAsmSetting(
            effect, NC_ASM_SETTING_TYPE::LEVEL_ADJUSTMENT, ASM_SETTING_TYPE::LEVEL_ADJUSTMENT, focus, ambientLevel));
    } catch (...) {
        return ERROR_GEN_FAILURE;
    }
}

extern "C" __declspec(dllexport) int SonySetVpt(Session* session, int preset) noexcept {
    if (!session || preset < 0 || preset > 4) return ERROR_INVALID_PARAMETER;
    try {
        return sendCommand(*session, CommandSerializer::serializeVPTSetting(VPT_INQUIRED_TYPE::VPT,
            static_cast<unsigned char>(preset)));
    } catch (...) {
        return ERROR_GEN_FAILURE;
    }
}
