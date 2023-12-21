using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
	/*
	GameManager:
	- Manages player GameObjects, including creation, destruction, and position updates
	- Subscribes to network updates such as OnGameUpdateReceived event and handles player GameObjects accordingly
	*/

	// Reference to the player prefab
	public GameObject playerPrefab;

	// Dictionary to keep track of player GameObjects by their IDs
	private readonly Dictionary<uint, GameObject> playerGameObjects = new Dictionary<uint, GameObject>();

	// Define a hashset to keep track of active players for each GameUpdate
	private HashSet<uint> activePlayerIDs = new HashSet<uint>();

	// Unity lifecycle method that is called when script instance is being loaded, executed before the game starts and before the Start() method:
	// Commonly used for initialization tasks that need to be performed once and are independent of other objects (ex: setting initial values or conditions)
	private void Awake()
	{
		// Ensure this script persists across scene loads (if needed)
		DontDestroyOnLoad(gameObject);
	}

	// Unity lifecycle method that is called once before the first frame update but after Awake() method:
	// Commonly used for intialization that depend on other objects having been initialized
	private void Start()
	{
		// Subscribes the HandleGameUpdate method to NetworkManager's OnGameUpdateReceived event
		NetworkManager.Instance.OnGameUpdateReceived += HandleGameUpdate;
	}

	// Unity lifecycle method called when GameObject attached to this script is destroyed
	private void OnDestroy()
	{
		// Unsubscribes the HandleGameUpdate method to NetworkManager's OnGameUpdateReceived event
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.OnGameUpdateReceived -= HandleGameUpdate;
		}
	}

	// When subscribed, method is called whenever a game update is received
	public void HandleGameUpdate(ServerGameUpdatePayload payload)
	{
		HashSet<uint> updatedPlayerIDs = new HashSet<uint>();

		// Add/Update players based on the payload
		foreach (var playerInfo in payload.players)
		{
			uint playerId = playerInfo.Key;
			updatedPlayerIDs.Add(playerId);
			CreateOrUpdatePlayer(playerId, new Vector2(playerInfo.Value.p.x, playerInfo.Value.p.y));
		}

		// Check for disconnected players
		var disconnectedPlayers = new HashSet<uint>(activePlayerIDs);
		disconnectedPlayers.ExceptWith(updatedPlayerIDs);
		foreach (var playerId in disconnectedPlayers)
		{
			HandlePlayerDisconnection(playerId);
		}

		// Update the active player list
		activePlayerIDs = updatedPlayerIDs;
	}

	void HandlePlayerDisconnection(uint playerId)
	{
		if (playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
		{
			Destroy(playerObject);
			playerGameObjects.Remove(playerId);
		}
	}

	private void CreateOrUpdatePlayer(uint playerId, Vector2 position)
	{
		if (playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
		{
			// Update position for remote players
			if (playerId != NetworkManager.Instance.ClientID)
			{
				playerObject.transform.position = position;
			}
		}
		else
		{
			// Instantiate new GameObject for new players
			GameObject newPlayer = Instantiate(playerPrefab, position, Quaternion.identity);
			newPlayer.name = "Player_" + playerId;
			playerGameObjects.Add(playerId, newPlayer);

			// Add PlayerController only if it's the local player
			if (playerId == NetworkManager.Instance.ClientID)
			{
				var controller = newPlayer.AddComponent<PlayerController>();
				controller.isLocalPlayer = true;
			}
		}
	}

	// UI for connecting and disconnecting from the server
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
