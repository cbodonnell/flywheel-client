using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    // Reference to the player prefab
    public GameObject playerPrefab;

    // Dictionary to keep track of player GameObjects by their IDs
    private readonly Dictionary<uint, GameObject> playerGameObjects = new Dictionary<uint, GameObject>();

    private void Awake()
    {
        // Ensure this script persists across scene loads (if needed)
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Subscribe to the network manager's event
        NetworkManager.Instance.OnGameUpdateReceived += HandleGameUpdate;
    }

    private void OnDestroy()
    {
        // Unsubscribe from the network manager's event to avoid memory leaks
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnGameUpdateReceived -= HandleGameUpdate;
        }
    }

    public void HandleGameUpdate(ServerGameUpdatePayload payload)
    {
        // Update or create players based on the payload
        foreach (var playerInfo in payload.players)
        {
            CreateOrUpdatePlayer(playerInfo.Key, new Vector2(playerInfo.Value.p.x, playerInfo.Value.p.y));
        }
    }

    private void CreateOrUpdatePlayer(uint playerId, Vector2 position)
    {
        // Check if the player already exists
        if (playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
        {
            // Update the existing player's position
            playerObject.transform.position = position;
        }
        else
        {
            // If the player doesn't exist, create a new one
            GameObject newPlayer = Instantiate(playerPrefab, position, Quaternion.identity);
            newPlayer.name = "Player_" + playerId;
            playerGameObjects.Add(playerId, newPlayer);
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 300));
        if (!NetworkManager.Instance.IsConnected)
        {
            if (GUILayout.Button("Connect"))
                NetworkManager.Instance.OnConnect();
        }
        else
        {
            if (!NetworkManager.Instance.HasClientID)
            {
                GUILayout.Label("Waiting to be assigned an ID...");
            }
            else
            {
                GUILayout.Label($"Connected as client: {NetworkManager.Instance.ClientID}");

                // Optionally, create or update the local player object
                CreateOrUpdatePlayer(NetworkManager.Instance.ClientID, Vector2.zero); // Replace Vector2.zero with actual position
            }

            if (GUILayout.Button("Disconnect"))
                NetworkManager.Instance.OnDisconnect();
        }
        GUILayout.EndArea();
    }
}
