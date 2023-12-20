using UnityEngine;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System;
using System.Net;

public class NetworkManager : MonoBehaviour
{
    [SerializeField]
    // private string serverHostname = "10.8.0.1"
    private string serverHostname = "127.0.0.1";
    [SerializeField]
    private int serverTcpPort = 8888;
    [SerializeField]
    private int serverUdpPort = 8889;

    private TcpClient tcpClient;
    private Thread tcpReceiveThread;
    private const int tcpReceiveBufferSize = 1024;
    private UdpClient udpClient;
    private Thread udpReceiveThread;

    enum DisconnectReasons
    {
        OnDestroy,
        UserRequestedDisconnect,
        ServerClosedConnection
    }

    private bool isConnected = false;
    public bool IsConnected
    {
        get { return isConnected; }
    }

    private bool hasClientID = false;
    public bool HasClientID
    {
        get { return hasClientID; }
    }

    private uint clientID;
    public uint ClientID
    {
        get { return clientID; }
    }

    // Singleton pattern
    private static NetworkManager _Instance;
    public static NetworkManager Instance
    {
        get
        {
            if (!_Instance)
            {
                _Instance = new GameObject().AddComponent<NetworkManager>();
                // name it for easy recognition
                _Instance.name = _Instance.GetType().ToString();
                // mark root as DontDestroyOnLoad();
                DontDestroyOnLoad(_Instance.gameObject);
            }
            return _Instance;
        }
    }

    public void OnConnect()
    {
        try
        {
            ConnectToServer();
        }
        catch (Exception ex)
        {
            Debug.Log($"Error connecting to server: {ex.Message}");
        }
    }

    private void ConnectToServer()
    {
        tcpClient = new TcpClient();
        tcpClient.Connect(serverHostname, serverTcpPort);

        // Start a thread for receiving messages
        tcpReceiveThread = new Thread(ReceiveTcpMessages);
        tcpReceiveThread.Start();

        udpClient = new UdpClient();
        udpClient.Connect(serverHostname, serverUdpPort);

        // Start a thread for receiving messages
        udpReceiveThread = new Thread(ReceiveUdpMessages);
        udpReceiveThread.Start();

        isConnected = true;
    }

    public void OnDisconnect()
    {
        try
        {
            DisconnectFromServer(DisconnectReasons.UserRequestedDisconnect);
        }
        catch (Exception ex)
        {
            Debug.Log($"Error disconnecting from server: {ex.Message}");
        }
    }

    private void DisconnectFromServer(DisconnectReasons reason)
    {
        Debug.Log("Disconnecting from server");

        if (tcpClient != null)
        {
            tcpClient.Close();
            tcpClient = null;
        }

        if (tcpReceiveThread != null)
        {
            if (reason != DisconnectReasons.ServerClosedConnection)
                // if the server closed the connection, we don't want to abort the thread
                // since the disconnect originated from the receive thread
                // TODO: there's probably a better way to handle this   
                tcpReceiveThread.Abort();
            tcpReceiveThread = null;
        }

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }

        if (udpReceiveThread != null)
        {
            udpReceiveThread.Abort();
            udpReceiveThread = null;
        }

        clientID = 0;
        hasClientID = false;
        isConnected = false;
    }

    public void PingUDP()
    {
        if (!isConnected || !hasClientID)
            return;

        ClientPingMessage pingMessage = new ClientPingMessage
        {
            clientID = clientID,
            type = MessageTypes.ClientPing,
        };

        string jsonMessage = JsonUtility.ToJson(pingMessage);
        byte[] data = Encoding.UTF8.GetBytes(jsonMessage);
        udpClient.Send(data, data.Length);
    }

    void OnDestroy()
    {
        DisconnectFromServer(DisconnectReasons.OnDestroy);
    }

    private void ReceiveTcpMessages()
    {
        if (tcpClient == null)
            return;

        try
        {
            NetworkStream stream = tcpClient.GetStream();
            byte[] buffer = new byte[tcpReceiveBufferSize];

            while (true)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                    break;

                string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Debug.Log($"Received TCP message: {receivedMessage}");

                Message message = JsonUtility.FromJson<Message>(receivedMessage);
                Debug.Log($"Received TCP message of type: {message.type}");

                switch (message.type)
                {
                    case MessageTypes.ServerAssignID:
                        AssignIDMessage assignIDMessage = JsonUtility.FromJson<AssignIDMessage>(receivedMessage);
                        clientID = assignIDMessage.payload.clientID;
                        hasClientID = true;
                        // Send a UDP ping to the server once we have our client ID
                        // this shouldn't be necessary once real identity is implemented
                        PingUDP();
                        break;
                    default:
                        Debug.Log($"Unknown message type: {message.type}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Log($"Error receiving TCP messages: {ex.Message}");
        }

        // If we get here, assume the connection has been closed
        DisconnectFromServer(DisconnectReasons.ServerClosedConnection);
    }

    private void ReceiveUdpMessages()
    {
        if (udpClient == null)
            return;

        try
        {
            IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                byte[] receivedData = udpClient.Receive(ref remoteEndpoint);
                string receivedMessage = Encoding.UTF8.GetString(receivedData);

                Console.WriteLine($"Received UDP message: {receivedMessage}");

                Message message = JsonUtility.FromJson<Message>(receivedMessage);
                Debug.Log($"Received UDP message of type: {message.type}");

                switch (message.type)
                {
                    case MessageTypes.ServerPong:
                        ServerPongMessage pongMessage = JsonUtility.FromJson<ServerPongMessage>(receivedMessage);
                        Debug.Log($"Received UDP Pong from server");
                        break;

                    case MessageTypes.ServerGameUpdate:
                        ServerGameUpdateMessage gameUpdateMessage = JsonUtility.FromJson<ServerGameUpdateMessage>(receivedMessage);
                        Debug.Log($"Received UDP GameUpdate from server");
                        Debug.Log(gameUpdateMessage.payload);

                        // Now you can access gameUpdateMessage.payload.timestamp, gameUpdateMessage.payload.players, etc.
                        HandleGameUpdate(gameUpdateMessage.payload);
                        break;

                    default:
                        Debug.Log($"Unknown message type: {message.type}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving UDP messages: {ex.Message}");
        }
    }
    public void SendClientPlayerUpdate(Vector2 position)
    {
        // Create the payload for the player update message
        ClientPlayerUpdatePayload updatePayload = new ClientPlayerUpdatePayload
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            playerState = new PlayerState
            {
                p = new Position { x = position.x, y = position.y }
            }
        };

        // Create the player update message with the payload
        ClientPlayerUpdateMessage playerUpdateMessage = new ClientPlayerUpdateMessage
        {
            clientID = clientID,
            type = MessageTypes.ClientPlayerUpdate,
            payload = updatePayload
        };

        string jsonMessage = JsonUtility.ToJson(playerUpdateMessage);
        // Debug.Log(jsonMessage);
        byte[] data = Encoding.UTF8.GetBytes(jsonMessage);

        try
        {
            udpClient.Send(data, data.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError("Error sending player update via UDP: " + ex.Message);
        }
    }

    private void HandleGameUpdate(ServerGameUpdatePayload payload)
    {
        // Process the game update payload
        Debug.Log($"Received Game Update at timestamp: {payload.timestamp}");
        foreach (var playerEntry in payload.players)
        {
            Debug.Log($"Player ID: {playerEntry.Key}, Position: {playerEntry.Value.p.x}, {playerEntry.Value.p.y}");

            // Update player positions or other game state based on this data
            // Example: Find the player GameObject and update its position
            UpdatePlayerPosition(playerEntry.Key, playerEntry.Value.p.x, playerEntry.Value.p.y);
        }
    }

    // Example method to update player position in the game
    private void UpdatePlayerPosition(uint playerId, float x, float y)
    {
        // Implement logic to find the player GameObject and update its position
        // For example, if you have a dictionary of player GameObjects indexed by playerId:
        // if (playerGameObjects.ContainsKey(playerId))
        // {
        //     playerGameObjects[playerId].transform.position = new Vector2(x, y);
        // }
    }

}
