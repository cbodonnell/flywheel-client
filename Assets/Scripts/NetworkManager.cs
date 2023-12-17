using UnityEngine;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System;
using System.Net;

public class NetworkManager : MonoBehaviour
{


    [SerializeField]
    private string serverHostname = "10.8.0.1";
    [SerializeField]
    private int serverTcpPort = 8888;
    [SerializeField]
    private int serverUdpPort = 8889;

    private TcpClient tcpClient;
    private Thread tcpReceiveThread;
    private UdpClient udpClient;
    private Thread udpReceiveThread;

    private bool isConnected = false;
    public bool IsConnected
    {
        get { return isConnected; }
    }

    private bool hasClientID = false;
    public bool IsLoggedIn
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

    public void Connect()
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

    public void Login()
    {
        if (!isConnected)
            return;

        // Send a login message to the server to identify this client and retrieve the client ID
        Message loginMessage = new Message()
        {
            type = "cli",
        };
        string loginMessageJson = JsonUtility.ToJson(loginMessage);
        Debug.Log($"Sending TCP message: {loginMessageJson}");
        byte[] loginMessageBytes = Encoding.UTF8.GetBytes(loginMessageJson);
        tcpClient.GetStream().Write(loginMessageBytes, 0, loginMessageBytes.Length);
    }

    void OnDestroy()
    {
        if (tcpClient!= null)
        {
            // Close the TCP client and stop the receive thread when done
            tcpClient.Close();
            tcpReceiveThread.Join();
        }

        if (udpClient != null)
        {
            // Close the UDP client and stop the receive thread when done
            udpClient.Close();
            udpReceiveThread.Join();
        }
    }

    private void ReceiveTcpMessages()
    {
        if (tcpClient == null)
            return;

        try
        {
            NetworkStream stream = tcpClient.GetStream();
            byte[] buffer = new byte[1024];

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
                    case "aid":
                        AssignIDMessage assignIDMessage = JsonUtility.FromJson<AssignIDMessage>(receivedMessage);
                        clientID = assignIDMessage.payload.clientID;
                        hasClientID = true;
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

                // TODO: parse and handle messages
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving UDP messages: {ex.Message}");
        }
    }
}
