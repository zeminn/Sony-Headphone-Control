// SPDX-License-Identifier: GPL-3.0-or-later

#include "../app/SonyHeadphonesFlyout/Native/SonyTransport.cpp"

#include <array>
#include <future>
#include <iostream>
#include <string_view>
#include <thread>

namespace {

void require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

Buffer fromHex(std::string_view hex) {
    Buffer bytes;
    for (size_t i = 0; i < hex.size(); i += 2) {
        bytes.push_back(static_cast<char>(std::stoi(std::string(hex.substr(i, 2)), nullptr, 16)));
    }
    return bytes;
}

Buffer readBytes(SOCKET socket, size_t size) {
    const auto deadline = Clock::now() + 2s;
    Buffer bytes(size);
    size_t offset = 0;
    while (offset < size) {
        require(waitForSocket(socket, false, deadline) == 0, "Peer did not receive a frame in time");
        int count = recv(socket, bytes.data() + offset, static_cast<int>(size - offset), 0);
        require(count > 0, "Peer lost the connection");
        offset += count;
    }
    return bytes;
}

struct Loopback {
    Session client;
    Session peer;

    Loopback() {
        Session listener;
        listener.socket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        require(listener.socket != INVALID_SOCKET, "Could not open listener");
        sockaddr_in address{};
        address.sin_family = AF_INET;
        address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        auto* endpoint = reinterpret_cast<sockaddr*>(&address);
        require(bind(listener.socket, endpoint, sizeof(address)) == 0 && listen(listener.socket, 1) == 0,
                "Could not listen on loopback");
        int size = sizeof(address);
        require(getsockname(listener.socket, endpoint, &size) == 0, "Could not resolve loopback address");
        client.socket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        require(client.socket != INVALID_SOCKET && connect(client.socket, endpoint, size) == 0,
                "Could not connect client to loopback listener");
        peer.socket = accept(listener.socket, nullptr, nullptr);
        require(peer.socket != INVALID_SOCKET, "Could not accept client");
    }
};

void checkFraming() {
    auto frame = fromHex("0C3D2C000000033D2C3D2D3D2E02");
    DATA_TYPE type{};
    unsigned char sequence = 0;
    size_t length = 0;
    require(decodeFrame(frame, type, sequence, length), "Valid escaped frame was rejected");
    require(type == DATA_TYPE::DATA_MDR && sequence == 0x3c && length == 3, "Decoded contents differ");
    frame.back() ^= 1;
    require(!decodeFrame(frame, type, sequence, length), "Bad checksum was accepted");
    auto malformed = fromHex("01010000000103");
    require(!decodeFrame(malformed, type, sequence, length), "Bad length was accepted");
}

void checkCommands() {
    Loopback connection;
    auto answer = std::async(std::launch::async, [&] {
        const auto deadline = Clock::now() + 2s;
        const auto concertHall = fromHex("3E0C00000000034801035B3C");
        require(readBytes(connection.peer.socket, concertHall.size()) == concertHall, "VPT frame differs from v1.3.2");
        auto invalidThenNotification = fromHex("3E010000000000013C3E010100000000003C3E0C00000000013D2E4B3C");
        require(writeAll(connection.peer.socket, invalidThenNotification, deadline) == 0, "Could not send test responses");
        const auto hostAck = fromHex("3E010100000000023C");
        require(readBytes(connection.peer.socket, hostAck.size()) == hostAck, "Notification was not acknowledged");
        require(writeAll(connection.peer.socket, fromHex("3E0101"), deadline) == 0, "Could not send first ACK fragment");
        std::this_thread::sleep_for(30ms);
        require(writeAll(connection.peer.socket, fromHex("00000000023C"), deadline) == 0, "Could not send second ACK fragment");

        const auto ambience = fromHex("3E0C01000000086802110100010113A63C");
        require(readBytes(connection.peer.socket, ambience.size()) == ambience, "Ambient frame or next sequence differs");
        require(writeAll(connection.peer.socket, fromHex("3E010000000000013C"), deadline) == 0, "Could not ACK the ambient frame");

        // Level 0 and 1 must preserve Sony's DUAL/SINGLE fields. Level 2 must suppress voice focus.
        constexpr std::array<std::string_view, 3> lowerLevels = {
            "3E0C00000000086802110102010000933C",
            "3E0C01000000086802110101010001943C",
            "3E0C00000000086802110100010002933C"
        };
        for (size_t level = 0; level < lowerLevels.size(); ++level) {
            const auto expected = fromHex(lowerLevels[level]);
            require(readBytes(connection.peer.socket, expected.size()) == expected, "Low ambient level differs from v1.3.2");
            const auto ack = fromHex(level % 2 == 0 ? "3E010100000000023C" : "3E010000000000013C");
            require(writeAll(connection.peer.socket, ack, deadline) == 0, "Could not ACK the low ambient level");
        }
    });
    require(SonySetVpt(&connection.client, 3) == 0 && connection.client.sequence == 1,
            "VPT did not accept the fragmented, matching ACK");
    require(SonySetAmbient(&connection.client, 1, 19, 1) == 0 && connection.client.sequence == 0,
            "Ambient command did not advance the sequence back to zero");
    for (int level = 0; level <= 2; ++level) {
        require(SonySetAmbient(&connection.client, 1, level, 1) == 0, "A low ambient level was rejected");
    }
    answer.get();
    shutdown(connection.peer.socket, SD_BOTH);
    require(SonySetVpt(&connection.client, 0) != 0, "Closed peer looked like a successful command");
    require(SonySetVpt(&connection.client, 5) == ERROR_INVALID_PARAMETER, "Invalid VPT preset was not rejected");
    require(SonySetAmbient(&connection.client, 1, 20, 0) == ERROR_INVALID_PARAMETER, "Invalid ambient level was not rejected");

    Loopback silent;
    require(SonySetVpt(&silent.client, 0) == WSAETIMEDOUT, "Silent peer did not time out");
}

}  // namespace

int main() {
    try {
        Winsock winsock;
        require(winsock.error == 0, "Winsock startup failed");
        checkFraming();
        checkCommands();
        std::cout << "Verified v1.3.2 command bytes including ambient levels 0, 1, 2 and 19, escaping, fragmented ACK, sequence, EOF, and timeout.\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
