#ifndef UDPCLIENT_H
#define UDPCLIENT_H

#include <string>
#include <winsock2.h>
#include <Ws2tcpip.h>

class UDPClient {
private:
    int recvSocket;
    int sendSocket;
    struct sockaddr_in recvAddr;
    struct sockaddr_in sendAddr;

public:
    UDPClient(const char* sendIPAddress, int sendPort, int recvPort);
    void initSendSocket(const char* ipAddress, int port);
    void initRecvSocket(int port);
    char* recvMessage();
    void sendMessage(const char* data, size_t dataSize);
    ~UDPClient();
};

#endif