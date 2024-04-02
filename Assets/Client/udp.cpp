#include "udp.h"
#include <iostream>
#include <string>
#include <cstring>
#include <winsock2.h>
#include <Ws2tcpip.h>
#pragma comment(lib, "Ws2_32.lib")

// Constructor
UDPClient::UDPClient(const char* sendIPAddress, int sendPort, int recvPort) 
{
    initSendSocket(sendIPAddress, sendPort);
    initRecvSocket(recvPort);
}

void UDPClient::initSendSocket(const char* ipAddress, int port) {
    // Create UDP socket
    sendSocket = socket(AF_INET, SOCK_DGRAM, 0);
    if (sendSocket < 0) {
        std::cerr << "Error in creating socket at" << ipAddress << ":" << port << std::endl;
        exit(EXIT_FAILURE);
    }

    // Bind server address and port
    memset(&sendAddr, 0, sizeof(sendAddr));
    sendAddr.sin_family = AF_INET;
    sendAddr.sin_port = htons(port);
    inet_pton(AF_INET, ipAddress, &sendAddr.sin_addr);
}

void UDPClient::initRecvSocket(int port) {
    // Create UDP socket
    recvSocket = socket(AF_INET, SOCK_DGRAM, 0);
    if (recvSocket < 0) {
        std::cerr << "Error in creating socket at 127.0.0.1:" << port << std::endl;
        exit(EXIT_FAILURE);
    }

    // Bind socket to specified port
    memset(&recvAddr, 0, sizeof(recvAddr));
    recvAddr.sin_family = AF_INET;
    recvAddr.sin_addr.s_addr = INADDR_ANY;
    recvAddr.sin_port = htons(port);

    if (bind(recvSocket, (const struct sockaddr*)&recvAddr, sizeof(recvAddr)) < 0) {
        std::cerr << "Unable to bind socket to 127.0.0.1:" << port << std::endl;
        exit(EXIT_FAILURE);
    }
}

char* UDPClient::recvMessage() {
    char buffer[1024];
    socklen_t len = sizeof(recvAddr);
    SSIZE_T nBytes = recvfrom(recvSocket, buffer, sizeof(buffer), 0, (struct sockaddr*)&recvAddr, &len);
    if ((nBytes < 0) || (nBytes > 1023)) {
        std::cerr << "Receive failed" << std::endl;
        exit(EXIT_FAILURE);
    }

    // Null-terminate the received message
    buffer[nBytes] = '\0';

    // Copy message to allocated memory
    char* message = new char[nBytes + 1];
    strncpy(message, buffer, nBytes + 1);
    return message;
}

void UDPClient::sendMessage(const char* data, size_t dataSize) {
    // Send message to server
    SSIZE_T bytesSent = sendto(sendSocket, data, dataSize, 0, (struct sockaddr*)&sendAddr, sizeof(sendAddr));
    if (bytesSent < 0) {
        std::cerr << "Error in sending message" << std::endl;
        exit(EXIT_FAILURE);
    }
}

UDPClient::~UDPClient() {
    closesocket(recvSocket);
    closesocket(sendSocket);
}