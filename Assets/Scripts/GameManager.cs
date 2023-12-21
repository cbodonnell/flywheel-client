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
		// Iterates through each player in the payload and passes information to the CreateOrUpdatePlayer method
		foreach (var playerInfo in payload.players)
		{
			CreateOrUpdatePlayer(playerInfo.Key, new Vector2(playerInfo.Value.p.x, playerInfo.Value.p.y));
		}
	}

	private void CreateOrUpdatePlayer(uint playerId, Vector2 position)
	{
		// Check if there is already a GameObject for given playerID
		if (playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
		{
			// If local player, do nothing
			if (playerId == NetworkManager.Instance.ClientID)
			{
				return;
			}
			// If not local player, update position
			playerObject.transform.position = position;
		}
		else
		{
			// Instantiate new GameObject for new players
			GameObject newPlayer = Instantiate(playerPrefab, position, Quaternion.identity);
			newPlayer.name = "Player_" + playerId;
			playerGameObjects.Add(playerId, newPlayer);

			PlayerController controller = newPlayer.GetComponent<PlayerController>();
			if (controller != null)
			{
				// controller.isLocalPlayer set to true if playerID matches clientID
				controller.isLocalPlayer = (playerId == NetworkManager.Instance.ClientID);
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
